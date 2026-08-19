using Microsoft.Extensions.Logging;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DIR.Lib.Tests;

public sealed class BackgroundTaskTrackerTests
{
    /// <summary>Minimal ILogger that records (level, rendered message) so the guarded-run
    /// routing can assert both the callback fired AND the log level (OCE => Information,
    /// other exceptions => Warning).</summary>
    private sealed class RecordingLogger : ILogger
    {
        public readonly List<(LogLevel Level, string Message)> Entries = [];
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    [Fact]
    public async Task RunGuardedAsync_Success_RunsWorkAndFinally_NoLogNoErrorOrCancel()
    {
        var logger = new RecordingLogger();
        bool ran = false, finallyRan = false, errored = false, cancelled = false;

        await BackgroundTaskTracker.RunGuardedAsync(
            _ => { ran = true; return Task.CompletedTask; },
            CancellationToken.None, logger, "Op",
            onError: _ => errored = true,
            onCancel: () => cancelled = true,
            onFinally: () => finallyRan = true);

        ran.ShouldBeTrue();
        finallyRan.ShouldBeTrue();
        errored.ShouldBeFalse();
        cancelled.ShouldBeFalse();
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunGuardedAsync_Exception_LogsWarning_CallsOnError_AndFinally()
    {
        var logger = new RecordingLogger();
        Exception? seen = null;
        bool cancelled = false, finallyRan = false;
        var boom = new InvalidOperationException("boom");

        await BackgroundTaskTracker.RunGuardedAsync(
            _ => throw boom,
            CancellationToken.None, logger, "Widget",
            onError: ex => seen = ex,
            onCancel: () => cancelled = true,
            onFinally: () => finallyRan = true);

        seen.ShouldBe(boom);
        cancelled.ShouldBeFalse();
        finallyRan.ShouldBeTrue();
        logger.Entries.ShouldHaveSingleItem();
        logger.Entries[0].Level.ShouldBe(LogLevel.Warning);
        logger.Entries[0].Message.ShouldContain("Widget");
    }

    [Fact]
    public async Task RunGuardedAsync_Cancellation_LogsInformation_CallsOnCancel_NotOnError()
    {
        var logger = new RecordingLogger();
        bool errored = false, cancelled = false, finallyRan = false;

        await BackgroundTaskTracker.RunGuardedAsync(
            _ => throw new OperationCanceledException(),
            CancellationToken.None, logger, "Slew",
            onError: _ => errored = true,
            onCancel: () => cancelled = true,
            onFinally: () => finallyRan = true);

        cancelled.ShouldBeTrue();
        errored.ShouldBeFalse();
        finallyRan.ShouldBeTrue();
        // OCE is logged (Information), never swallowed silently.
        logger.Entries.ShouldHaveSingleItem();
        logger.Entries[0].Level.ShouldBe(LogLevel.Information);
    }

    [Fact]
    public async Task RunGuardedAsync_Cancellation_NullOnCancel_StillLogsAndRunsFinally()
    {
        var logger = new RecordingLogger();
        var finallyRan = false;

        await BackgroundTaskTracker.RunGuardedAsync(
            _ => throw new OperationCanceledException(),
            CancellationToken.None, logger, "X",
            onError: _ => throw new Exception("onError must not fire on cancellation"),
            onCancel: null,
            onFinally: () => finallyRan = true);

        finallyRan.ShouldBeTrue();
        logger.Entries.ShouldHaveSingleItem();
        logger.Entries[0].Level.ShouldBe(LogLevel.Information);
    }

    [Fact]
    public async Task RunGuarded_TracksWork_AndGuardedSuccessLeavesProcessCompletionsSilent()
    {
        var tracker = new BackgroundTaskTracker();
        var logger = new RecordingLogger();
        var gate = new TaskCompletionSource();

        tracker.RunGuarded(_ => gate.Task, CancellationToken.None, logger, "Job", onError: _ => { });
        tracker.HasPending.ShouldBeTrue();

        gate.SetResult();
        for (var i = 0; i < 200 && tracker.PendingCount > 0; i++)
        {
            tracker.ProcessCompletions(logger);
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        tracker.HasPending.ShouldBeFalse();
        // Guarded work completes non-faulted, so ProcessCompletions logs nothing.
        logger.Entries.ShouldBeEmpty();
    }

    // ---- Keyed, cancel-and-supersede slots ----

    private sealed class Doc { public required string Name { get; init; } }

    /// <summary>
    /// Polls <paramref name="condition"/> until it holds, evaluating it EXACTLY ONCE per attempt.
    /// </summary>
    /// <remarks>
    /// The obvious shape -- a loop guard plus a closing assertion -- asks twice, which is wrong for a
    /// CONSUMING predicate like TryCollect: the guard takes the result and the assertion then finds
    /// the slot already retired and fails a test that had actually passed.
    /// </remarks>
    private static async Task WaitUntil(Func<bool> condition, string what)
    {
        for (var i = 0; i < 500; i++)
        {
            if (condition())
            {
                return;
            }
            await Task.Delay(10);
        }

        throw new Xunit.Sdk.XunitException($"timed out waiting for {what}");
    }

    [Fact]
    public async Task RunExclusive_StartingAgainUnderTheSameKey_CancelsThePredecessor()
    {
        // The point of a keyed slot: the superseded result is not merely unwanted, it is about to be
        // wrong (a second file opened over the first), so the work must actually stop rather than run
        // to completion and be discarded.
        var tracker = new BackgroundTaskTracker();
        var logger = new RecordingLogger();
        var firstCancelled = false;
        var firstEntered = new TaskCompletionSource();

        tracker.RunExclusive("load", async ct =>
        {
            firstEntered.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
        }, CancellationToken.None, logger, "Load one", onError: _ => { }, onCancel: () => firstCancelled = true);

        await firstEntered.Task;

        tracker.RunExclusive("load", _ => Task.CompletedTask,
            CancellationToken.None, logger, "Load two", onError: _ => { });

        await WaitUntil(() => firstCancelled, "the superseded load to observe cancellation");
    }

    [Fact]
    public async Task RunExclusive_ReportsWhetherTheKeyIsBusy()
    {
        var tracker = new BackgroundTaskTracker();
        var logger = new RecordingLogger();
        var release = new TaskCompletionSource();

        tracker.IsRunning("load").ShouldBeFalse();

        tracker.RunExclusive("load", async _ => await release.Task,
            CancellationToken.None, logger, "Load", onError: _ => { });

        await WaitUntil(() => tracker.IsRunning("load"), "the load to start");

        release.SetResult();

        await WaitUntil(() => !tracker.IsRunning("load"), "the load to finish");
    }

    [Fact]
    public async Task TryCollect_HandsTheResultOverOnceAndThenFreesTheKey()
    {
        // The result is PULLED by the consumer rather than pushed into a callback, so it is adopted on
        // whichever thread is entitled to adopt it.
        var tracker = new BackgroundTaskTracker();
        var logger = new RecordingLogger();

        tracker.RunExclusive<Doc>("enhance", _ => Task.FromResult<Doc?>(new Doc { Name = "enhanced" }),
            CancellationToken.None, logger, "Enhance", onError: _ => { });

        await WaitUntil(() => tracker.TryCollect<Doc>("enhance", out _), "the enhance result");

        // Collected already: the slot is retired, so a second ask reports nothing rather than handing
        // the same document out twice.
        tracker.TryCollect<Doc>("enhance", out var again).ShouldBeFalse();
        again.ShouldBeNull();
        tracker.IsRunning("enhance").ShouldBeFalse();
    }

    [Fact]
    public async Task TryCollect_WhileTheWorkIsStillRunning_ReportsNothing()
    {
        var tracker = new BackgroundTaskTracker();
        var logger = new RecordingLogger();
        var release = new TaskCompletionSource();

        tracker.RunExclusive<Doc>("enhance", async _ =>
        {
            await release.Task;
            return new Doc { Name = "late" };
        }, CancellationToken.None, logger, "Enhance", onError: _ => { });

        tracker.TryCollect<Doc>("enhance", out var early).ShouldBeFalse();
        early.ShouldBeNull();

        release.SetResult();
        await WaitUntil(() => tracker.TryCollect<Doc>("enhance", out _), "the enhance result");
    }

    [Fact]
    public async Task AFailedRunLeavesNothingToCollectAndDoesNotWedgeTheKey()
    {
        // A run that threw has no result to hand over, and leaving the slot occupied would block the
        // key against its next use -- the toggle would refuse to start a second attempt forever.
        var tracker = new BackgroundTaskTracker();
        var logger = new RecordingLogger();
        var errored = false;

        tracker.RunExclusive<Doc>("enhance", _ => throw new InvalidOperationException("boom"),
            CancellationToken.None, logger, "Enhance", onError: _ => errored = true);

        await WaitUntil(() => errored, "the failure to be routed");

        tracker.TryCollect<Doc>("enhance", out var none).ShouldBeFalse();
        none.ShouldBeNull();

        var second = false;
        tracker.RunExclusive<Doc>("enhance", _ => { second = true; return Task.FromResult<Doc?>(null); },
            CancellationToken.None, logger, "Enhance again", onError: _ => { });

        await WaitUntil(() => second, "a second attempt under the same key");
    }

    [Fact]
    public async Task DrainAsync_CancelsKeyedWorkRatherThanWaitingForIt()
    {
        // Shutdown must not sit through a load nobody is waiting for, but must not abandon it either:
        // the work has to have STOPPED touching state before the app tears that state down.
        var tracker = new BackgroundTaskTracker();
        var logger = new RecordingLogger();
        var stopped = false;
        var entered = new TaskCompletionSource();

        tracker.RunExclusive("load", async ct =>
        {
            entered.SetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            finally { stopped = true; }
        }, CancellationToken.None, logger, "Load", onError: _ => { });

        await entered.Task;

        await tracker.DrainAsync();

        stopped.ShouldBeTrue();
        tracker.IsRunning("load").ShouldBeFalse();
        tracker.HasPending.ShouldBeFalse();
    }

    [Fact]
    public async Task KeyedWorkCountsAsPendingAndNamesItself()
    {
        var tracker = new BackgroundTaskTracker();
        var logger = new RecordingLogger();
        var release = new TaskCompletionSource();

        tracker.RunExclusive("load", async _ => await release.Task,
            CancellationToken.None, logger, "Loading M31", onError: _ => { });

        await WaitUntil(() => tracker.HasPending, "the load to register as pending");
        tracker.PendingDescriptions.ShouldContain("Loading M31");

        release.SetResult();
        await tracker.DrainAsync();
        tracker.HasPending.ShouldBeFalse();
    }
}
