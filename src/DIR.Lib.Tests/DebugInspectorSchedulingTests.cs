#if DEBUG
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using DIR.Lib.Diagnostics;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// The inspector core's SCHEDULING: which commands run in which pump, and who is allowed to overtake whom.
///
/// <para>All of it is ordinary logic, so it is driven through <see cref="DebugInspectorCore.Detached"/> — no
/// TCP listener, no multicast bind. Tying assertions about "one step per pump" to port availability would
/// make them depend on something they are not about.</para>
///
/// <para>These exist because the scheduler is what let the SDL inspector fold onto this core. Every rule
/// below was previously implemented privately in SdlVulkan.Renderer, and each one is load-bearing for a real
/// test workflow — so a regression here breaks driving an app, silently, in a way no compiler catches.</para>
/// </summary>
public class DebugInspectorSchedulingTests
{
    private static readonly JsonElement NoParams = JsonDocument.Parse("{}").RootElement.Clone();

    private static JsonElement Params(string json) => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>A host with no frame-spanning verbs — what a terminal looks like.</summary>
    private class FakeHost : IDebugInspectorHost
    {
        public readonly List<string> Invoked = [];
        public int Pokes;

        public string AppName => "Fake";
        public string SurfaceKind => "test";
        public void Poke() => Pokes++;

        public virtual string? Invoke(string method, JsonElement parameters)
        {
            Invoked.Add(method);
            return method == "nope" ? null : $"\"{method}\"";
        }
    }

    /// <summary>An operation that finishes after a fixed number of advances.</summary>
    private sealed class CountdownOperation(bool exclusive, int advances, string result) : IDebugInspectorOperation
    {
        public int Advances;
        public bool Exclusive => exclusive;
        public TimeSpan Timeout => TimeSpan.FromSeconds(30);

        public string? Advance() => ++Advances >= advances ? result : null;
    }

    /// <summary>A host whose <c>hold</c> spans pumps and whose <c>sweep</c> is exclusive, like a batch.</summary>
    private sealed class SteppedHost(int holdAdvances = 3) : FakeHost, IDebugInspectorSteppedHost
    {
        public int BeginCalls;
        public CountdownOperation? Started;

        public IReadOnlyCollection<string> SteppedMethods { get; } = ["hold", "sweep"];

        public IDebugInspectorOperation Begin(string method, JsonElement parameters)
        {
            BeginCalls++;
            Started = method == "hold"
                ? new CountdownOperation(exclusive: false, holdAdvances, "\"held\"")
                : new CountdownOperation(exclusive: true, holdAdvances, "\"swept\"");
            return Started;
        }
    }

    private sealed class ThrowingHost : FakeHost, IDebugInspectorSteppedHost
    {
        public IReadOnlyCollection<string> SteppedMethods { get; } = ["boom"];
        public IDebugInspectorOperation Begin(string method, JsonElement parameters) => new Thrower();

        private sealed class Thrower : IDebugInspectorOperation
        {
            public bool Exclusive => true;
            public TimeSpan Timeout => TimeSpan.FromSeconds(1);
            public string? Advance() => throw new InvalidOperationException("operation exploded");
        }
    }

    // ---------------------------------------------------------------- instantaneous commands

    [Fact]
    public void AnInstantCommandRunsOnTheNextPump()
    {
        var host = new FakeHost();
        using var core = DebugInspectorCore.Detached(host);

        var pending = core.Submit("state", NoParams);
        pending.IsCompleted.ShouldBeFalse("nothing runs until the host pumps");

        core.Pump();

        pending.IsCompletedSuccessfully.ShouldBeTrue();
        pending.Result.ShouldBe("\"state\"");
    }

    [Fact]
    public void AnUnknownMethodComesBackEmpty_WhichTheTransportReportsAsUnknown()
    {
        var host = new FakeHost();
        using var core = DebugInspectorCore.Detached(host);

        var pending = core.Submit("nope", NoParams);
        core.Pump();

        pending.Result.ShouldBe("");
    }

    [Fact]
    public void PingIsAnsweredByTheCore_SoEveryHostReportsItIdentically()
    {
        var host = new FakeHost();
        using var core = DebugInspectorCore.Detached(host);

        var pending = core.Submit("ping", NoParams);
        core.Pump();

        pending.Result.ShouldContain("\"app\":\"Fake\"");
        host.Invoked.ShouldBeEmpty("the host is not consulted for ping");
    }

    // ---------------------------------------------------------------- batch

    /// <summary>
    /// One step per pump is the whole point: a batched "zoom then read" must observe the zoom, which only
    /// happens if a real frame renders in between.
    /// </summary>
    [Fact]
    public void ABatchRunsOneStepPerPump()
    {
        var host = new FakeHost();
        using var core = DebugInspectorCore.Detached(host);

        var pending = core.Submit("batch",
            Params("""{"steps":[{"method":"a"},{"method":"b"},{"method":"c"}]}"""));

        core.Pump();
        host.Invoked.ShouldBe(["a"]);
        pending.IsCompleted.ShouldBeFalse();

        core.Pump();
        host.Invoked.ShouldBe(["a", "b"]);
        pending.IsCompleted.ShouldBeFalse();

        // The last step and the completion land in the SAME pump — asserted exactly, so an off-by-one-pump
        // regression (a batch that needs one extra idle frame to report) cannot pass.
        core.Pump();
        host.Invoked.ShouldBe(["a", "b", "c"]);
        pending.IsCompletedSuccessfully.ShouldBeTrue();
        pending.Result.ShouldBe("[\"a\",\"b\",\"c\"]");
    }

    [Fact]
    public void ABatchStepThatFailsIsRecordedAndTheRestStillRun()
    {
        var host = new FakeHost();
        using var core = DebugInspectorCore.Detached(host);

        var pending = core.Submit("batch",
            Params("""{"steps":[{"method":"a"},{"method":"nope"},{"method":"c"}]}"""));

        for (var i = 0; i < 5; i++) core.Pump();

        pending.IsCompletedSuccessfully.ShouldBeTrue();
        pending.Result.ShouldBe("[\"a\",\"error: unknown method 'nope'\",\"c\"]",
            "a 20-step script must say WHICH step broke, not collapse to one error");
    }

    [Fact]
    public void AWaitStepBurnsFrames()
    {
        var host = new FakeHost();
        using var core = DebugInspectorCore.Detached(host);

        var pending = core.Submit("batch",
            Params("""{"steps":[{"method":"a"},{"method":"wait","params":{"frames":3}},{"method":"b"}]}"""));

        core.Pump();                       // a
        core.Pump();                       // wait: consumes this frame, 2 left
        core.Pump();                       // wait
        core.Pump();                       // wait
        host.Invoked.ShouldBe(["a"], "b must not run until the wait elapses");

        core.Pump();                       // b, and the batch completes in the same pump
        host.Invoked.ShouldBe(["a", "b"]);
        pending.IsCompletedSuccessfully.ShouldBeTrue();
        pending.Result.ShouldBe("[\"a\",\"waited\",\"b\"]");
    }

    [Theory]
    [InlineData("""{"steps":[]}""", "non-empty")]
    [InlineData("""{"steps":[{"method":"batch"}]}""", "nested")]
    [InlineData("""{}""", "steps")]
    public void AMalformedBatchIsRejected(string json, string expected)
    {
        var host = new FakeHost();
        using var core = DebugInspectorCore.Detached(host);

        var pending = core.Submit("batch", Params(json));
        core.Pump();

        pending.IsFaulted.ShouldBeTrue();
        pending.Exception!.InnerException!.Message.ShouldContain(expected);
    }

    /// <summary>
    /// A frame-spanning verb cannot be a batch step — and crucially, finding that out must NOT call
    /// <c>Begin</c>, which would press the very button being refused.
    /// </summary>
    [Fact]
    public void AFrameSpanningVerbCannotBeABatchStep_AndIsRefusedWithoutStartingIt()
    {
        var host = new SteppedHost();
        using var core = DebugInspectorCore.Detached(host);

        var pending = core.Submit("batch", Params("""{"steps":[{"method":"hold"}]}"""));
        for (var i = 0; i < 3; i++) core.Pump();

        pending.Result.ShouldContain("spans frames");
        host.BeginCalls.ShouldBe(0, "probing must not have side effects");
    }

    // ---------------------------------------------------------------- exclusive vs background

    /// <summary>An exclusive operation owns the pump: nothing queued behind it may overtake it.</summary>
    [Fact]
    public void AnExclusiveOperationBlocksTheQueueUntilItFinishes()
    {
        var host = new SteppedHost(holdAdvances: 3);
        using var core = DebugInspectorCore.Detached(host);

        var sweep = core.Submit("sweep", NoParams);
        var behind = core.Submit("state", NoParams);

        core.Pump();
        core.Pump();
        behind.IsCompleted.ShouldBeFalse("the exclusive operation owns the pump");
        host.Invoked.ShouldBeEmpty();

        core.Pump();                       // third advance finishes the sweep
        sweep.IsCompletedSuccessfully.ShouldBeTrue();
        sweep.Result.ShouldBe("\"swept\"");

        core.Pump();                       // now the queue drains
        behind.IsCompletedSuccessfully.ShouldBeTrue();
    }

    /// <summary>
    /// A background operation does NOT own the pump. This is the rule that makes a press-and-hold useful:
    /// the point of holding a button is to inspect what the hold put on screen, which needs observe verbs
    /// answered mid-hold.
    /// </summary>
    [Fact]
    public void ABackgroundOperationLetsOtherCommandsRunWhileItIsInFlight()
    {
        var host = new SteppedHost(holdAdvances: 4);
        using var core = DebugInspectorCore.Detached(host);

        var hold = core.Submit("hold", NoParams);
        var observe = core.Submit("state", NoParams);

        core.Pump();

        hold.IsCompleted.ShouldBeFalse("still holding");
        observe.IsCompletedSuccessfully.ShouldBeTrue("an observe verb must be answered DURING the hold");
        observe.Result.ShouldBe("\"state\"");

        for (var i = 0; i < 4; i++) core.Pump();
        hold.IsCompletedSuccessfully.ShouldBeTrue();
        hold.Result.ShouldBe("\"held\"");
    }

    [Fact]
    public void ASecondBackgroundOperationIsRefused()
    {
        var host = new SteppedHost(holdAdvances: 10);
        using var core = DebugInspectorCore.Detached(host);

        var first = core.Submit("hold", NoParams);
        core.Pump();

        var second = core.Submit("hold", NoParams);
        core.Pump();

        second.IsFaulted.ShouldBeTrue();
        second.Exception!.InnerException!.Message.ShouldContain("already in progress");
        first.IsCompleted.ShouldBeFalse("the refusal must not disturb the one in flight");
    }

    /// <summary>
    /// Two scripts driving one surface at once would interleave by frame timing, so the result would not be
    /// reproducible. Refused loudly rather than raced.
    /// </summary>
    [Fact]
    public void AnExclusiveOperationIsRefusedWhileABackgroundOneRuns()
    {
        var host = new SteppedHost(holdAdvances: 10);
        using var core = DebugInspectorCore.Detached(host);

        core.Submit("hold", NoParams);
        core.Pump();

        var batch = core.Submit("batch", Params("""{"steps":[{"method":"a"}]}"""));
        core.Pump();

        batch.IsFaulted.ShouldBeTrue();
        batch.Exception!.InnerException!.Message.ShouldContain("while 'hold' is in progress");
        host.Invoked.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------- robustness

    /// <summary>An operation that throws must fail its own request and free the slot, not wedge the pump.</summary>
    [Fact]
    public void AnOperationThatThrowsFailsItsRequestAndReleasesThePump()
    {
        var host = new ThrowingHost();
        using var core = DebugInspectorCore.Detached(host);

        var boom = core.Submit("boom", NoParams);
        var after = core.Submit("state", NoParams);

        core.Pump();
        boom.IsFaulted.ShouldBeTrue();
        boom.Exception!.InnerException!.Message.ShouldBe("operation exploded");

        core.Pump();
        after.IsCompletedSuccessfully.ShouldBeTrue("a broken operation must not block everything after it");
    }

    /// <summary>
    /// The core pokes between advances, so an event-driven host that only renders on demand keeps turning
    /// without every operation having to remember to ask.
    /// </summary>
    [Fact]
    public void TheHostIsPokedWhileAnOperationIsStillRunning()
    {
        var host = new SteppedHost(holdAdvances: 3);
        using var core = DebugInspectorCore.Detached(host);

        core.Submit("hold", NoParams);
        var afterSubmit = host.Pokes;

        core.Pump();

        host.Pokes.ShouldBeGreaterThan(afterSubmit, "an unfinished operation must keep the loop awake");
    }

    /// <summary>
    /// A default interface member, so an existing host picks it up without being edited — which is the whole
    /// reason this addition is source-compatible. Reached through the interface because that is the only place
    /// a default member exists.
    /// </summary>
    [Fact]
    public void DiscoveryExtrasAreOptional_AndAHostWithoutThemIsUnaffected()
        => ((IDebugInspectorHost)new FakeHost()).DiscoveryExtras.ShouldBeNull();
}
#endif
