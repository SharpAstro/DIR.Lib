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
}
