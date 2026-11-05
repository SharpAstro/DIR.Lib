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
    public async Task AnInstantCommandRunsOnTheNextPump()
    {
        var host = new FakeHost();
        using var core = DebugInspectorCore.Detached(host);

        var pending = core.Submit("state", NoParams);
        pending.IsCompleted.ShouldBeFalse("nothing runs until the host pumps");

        core.Pump();

        pending.IsCompletedSuccessfully.ShouldBeTrue();
        (await pending).ShouldBe("\"state\"");
    }

    [Fact]
    public async Task AnUnknownMethodComesBackEmpty_WhichTheTransportReportsAsUnknown()
    {
        var host = new FakeHost();
        using var core = DebugInspectorCore.Detached(host);

        var pending = core.Submit("nope", NoParams);
        core.Pump();

        pending.IsCompletedSuccessfully.ShouldBeTrue();
        (await pending).ShouldBe("");
    }

    [Fact]
    public async Task PingIsAnsweredByTheCore_SoEveryHostReportsItIdentically()
    {
        var host = new FakeHost();
        using var core = DebugInspectorCore.Detached(host);

        var pending = core.Submit("ping", NoParams);
        core.Pump();

        pending.IsCompletedSuccessfully.ShouldBeTrue();
        (await pending).ShouldContain("\"app\":\"Fake\"");
        host.Invoked.ShouldBeEmpty("the host is not consulted for ping");
    }

    // ---------------------------------------------------------------- batch

    /// <summary>
    /// One step per pump is the whole point: a batched "zoom then read" must observe the zoom, which only
    /// happens if a real frame renders in between.
    /// </summary>
    [Fact]
    public async Task ABatchRunsOneStepPerPump()
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
        (await pending).ShouldBe("[\"a\",\"b\",\"c\"]");
    }

    [Fact]
    public async Task ABatchStepThatFailsIsRecordedAndTheRestStillRun()
    {
        var host = new FakeHost();
        using var core = DebugInspectorCore.Detached(host);

        var pending = core.Submit("batch",
            Params("""{"steps":[{"method":"a"},{"method":"nope"},{"method":"c"}]}"""));

        for (var i = 0; i < 5; i++) core.Pump();

        pending.IsCompletedSuccessfully.ShouldBeTrue();
        (await pending).ShouldBe("[\"a\",\"error: unknown method 'nope'\",\"c\"]",
            "a 20-step script must say WHICH step broke, not collapse to one error");
    }

    [Fact]
    public async Task AWaitStepBurnsFrames()
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
        (await pending).ShouldBe("[\"a\",\"waited\",\"b\"]");
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
    public async Task AFrameSpanningVerbCannotBeABatchStep_AndIsRefusedWithoutStartingIt()
    {
        var host = new SteppedHost();
        using var core = DebugInspectorCore.Detached(host);

        var pending = core.Submit("batch", Params("""{"steps":[{"method":"hold"}]}"""));
        for (var i = 0; i < 3; i++) core.Pump();

        pending.IsCompletedSuccessfully.ShouldBeTrue();
        (await pending).ShouldContain("spans frames");
        host.BeginCalls.ShouldBe(0, "probing must not have side effects");
    }

    // ---------------------------------------------------------------- exclusive vs background

    /// <summary>An exclusive operation owns the pump: nothing queued behind it may overtake it.</summary>
    [Fact]
    public async Task AnExclusiveOperationBlocksTheQueueUntilItFinishes()
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
        (await sweep).ShouldBe("\"swept\"");

        core.Pump();                       // now the queue drains
        behind.IsCompletedSuccessfully.ShouldBeTrue();
    }

    /// <summary>
    /// A background operation does NOT own the pump. This is the rule that makes a press-and-hold useful:
    /// the point of holding a button is to inspect what the hold put on screen, which needs observe verbs
    /// answered mid-hold.
    /// </summary>
    [Fact]
    public async Task ABackgroundOperationLetsOtherCommandsRunWhileItIsInFlight()
    {
        var host = new SteppedHost(holdAdvances: 4);
        using var core = DebugInspectorCore.Detached(host);

        var hold = core.Submit("hold", NoParams);
        var observe = core.Submit("state", NoParams);

        core.Pump();

        hold.IsCompleted.ShouldBeFalse("still holding");
        observe.IsCompletedSuccessfully.ShouldBeTrue("an observe verb must be answered DURING the hold");
        (await observe).ShouldBe("\"state\"");

        for (var i = 0; i < 4; i++) core.Pump();
        hold.IsCompletedSuccessfully.ShouldBeTrue();
        (await hold).ShouldBe("\"held\"");
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

    // ---------------------------------------------------------------- deadlines (#178)

    private sealed class FakeClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }

    /// <summary>Never finishes; records whether it was told it was cancelled.</summary>
    private sealed class HangingOperation(bool exclusive, TimeSpan timeout) : IDebugInspectorOperation, IDisposable
    {
        public bool Disposed;
        public bool Exclusive => exclusive;
        public TimeSpan Timeout => timeout;
        public string? Advance() => null;
        public void Dispose() => Disposed = true;
    }

    private sealed class HangingHost : FakeHost, IDebugInspectorSteppedHost
    {
        public int Hangs;   // how many "hang"/"shot" calls still hang; later ones finish at once
        public HangingOperation? Last;
        public IReadOnlyCollection<string> SteppedMethods { get; } = ["shot", "sweep"];

        public IDebugInspectorOperation Begin(string method, JsonElement parameters)
        {
            var exclusive = method == "sweep";
            if (Hangs-- > 0)
                return Last = new HangingOperation(exclusive, TimeSpan.FromSeconds(5));
            return new CountdownOperation(exclusive, 1, "\"done\"");
        }
    }

    [Theory]
    [InlineData("shot")]
    [InlineData("sweep")]
    public async Task AnOperationThatNeverFinishesIsFailedAtItsTimeout_AndTheNextOneRuns(string method)
    {
        var host = new HangingHost { Hangs = 1 };
        var clock = new FakeClock();
        using var core = DebugInspectorCore.Detached(host);
        core.TimeProvider = clock;

        var first = core.Submit(method, NoParams);
        core.Pump();
        first.IsCompleted.ShouldBeFalse();

        clock.Advance(TimeSpan.FromSeconds(4));
        core.Pump();
        first.IsCompleted.ShouldBeFalse("still inside its timeout");

        clock.Advance(TimeSpan.FromSeconds(2));
        core.Pump();
        first.IsFaulted.ShouldBeTrue();
        first.Exception!.InnerException!.Message.ShouldBe($"'{method}' did not finish within 5s");
        host.Last!.Disposed.ShouldBeTrue("the operation is told it was cancelled");

        var second = core.Submit(method, NoParams);
        core.Pump();
        second.IsCompletedSuccessfully.ShouldBeTrue("the slot was freed, so no 'already in progress'");
        (await second).ShouldBe("\"done\"");
    }

    [Fact]
    public async Task ARequestTimedOutClientSideLeavesNoSlotHeld()
    {
        var host = new HangingHost { Hangs = 1 };
        using var core = DebugInspectorCore.Detached(host);
        core.TimeProvider = new FakeClock();   // never advances: only the client side can expire
        core.CommandTimeout = TimeSpan.FromMilliseconds(50);
        core.ClientGrace = TimeSpan.FromMilliseconds(50);

        var request = core.HandleAsync("{\"id\":1,\"method\":\"shot\"}");
        core.Pump();   // claims the operation, publishing its 5s timeout
        var (_, response) = await request;
        response.ShouldBe("!'shot' did not finish within 5s");

        var second = core.Submit("shot", NoParams);
        core.Pump();   // reaps the abandoned slot
        core.Pump();   // and starts the new one
        second.IsCompletedSuccessfully.ShouldBeTrue("an abandoned operation must not refuse the next as in progress");
    }

    private sealed class CountingHost : FakeHost, IDebugInspectorSteppedHost
    {
        public int BeginCalls;
        public IReadOnlyCollection<string> SteppedMethods { get; } = ["shot"];

        public IDebugInspectorOperation Begin(string method, JsonElement parameters)
        {
            BeginCalls++;
            return new CountdownOperation(exclusive: false, 1, "\"done\"");
        }
    }

    [Fact]
    public async Task AnAbandonedCommandIsFailedBeforeBegin_SoNothingIsPressedWithNoOneToRelease()
    {
        var host = new CountingHost();
        using var core = DebugInspectorCore.Detached(host);
        core.CommandTimeout = TimeSpan.FromMilliseconds(50);

        // Nobody pumps, so the client gives up and marks the command abandoned while it is still queued.
        var (_, response) = await core.HandleAsync("{\"id\":1,\"method\":\"shot\"}");
        response.ShouldStartWith("!timed out");

        core.Pump();   // the pump now claims the abandoned command

        host.BeginCalls.ShouldBe(0, "Begin acts on the app; an abandoned command must never reach it");

        var next = core.Submit("shot", NoParams);
        core.Pump();
        next.IsCompletedSuccessfully.ShouldBeTrue("no slot was taken by the abandoned command");
        host.BeginCalls.ShouldBe(1);
    }

    private sealed class ThrowingDisposeOperation : IDebugInspectorOperation, IDisposable
    {
        public bool Exclusive => false;
        public TimeSpan Timeout => TimeSpan.FromSeconds(5);
        public string? Advance() => null;
        public void Dispose() => throw new InvalidOperationException("release failed");
    }

    private sealed class ThrowingDisposeHost : FakeHost, IDebugInspectorSteppedHost
    {
        public int Hangs = 1;
        public IReadOnlyCollection<string> SteppedMethods { get; } = ["shot"];

        public IDebugInspectorOperation Begin(string method, JsonElement parameters)
            => Hangs-- > 0 ? new ThrowingDisposeOperation() : new CountdownOperation(false, 1, "\"done\"");
    }

    [Fact]
    public async Task ADisposeThatThrowsDuringCancelStillFailsTheCommandAndFreesTheSlot()
    {
        var host = new ThrowingDisposeHost();
        var clock = new FakeClock();
        using var core = DebugInspectorCore.Detached(host);
        core.TimeProvider = clock;

        var first = core.Submit("shot", NoParams);
        core.Pump();
        clock.Advance(TimeSpan.FromSeconds(6));
        core.Pump();   // cancels; Dispose throws and must be swallowed

        first.IsFaulted.ShouldBeTrue();
        first.Exception!.InnerException!.Message.ShouldBe("'shot' did not finish within 5s");

        var second = core.Submit("shot", NoParams);
        core.Pump();
        second.IsCompletedSuccessfully.ShouldBeTrue("the pump survived the throwing Dispose");
        (await second).ShouldBe("\"done\"");
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
