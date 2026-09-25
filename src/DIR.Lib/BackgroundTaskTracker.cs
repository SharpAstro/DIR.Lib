using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace DIR.Lib
{
    /// <summary>
    /// Tracks background tasks submitted from UI callbacks. Checks for completions
    /// each frame and logs errors. Shared between GUI and TUI.
    /// </summary>
    /// <remarks>
    /// <para><b>Safe to use from any thread.</b> It was written for the render thread alone, and that
    /// stopped being true without anyone deciding it: an async signal handler runs inline only up to
    /// its first <c>await</c> (<see cref="SignalBus.ProcessPending"/>), and a host with no
    /// synchronization context resumes it on the thread pool, so a handler that submits work after an
    /// await submits from a pool thread while the render thread completes work on the same tracker.
    /// Over a plain list and dictionary that lost submissions (so <see cref="HasPending"/> could answer
    /// false with work still running), threw from <see cref="DrainAsync"/>'s enumeration, and corrupted
    /// the keyed slots.</para>
    /// <para>So the whole state is ONE immutable value, replaced by compare-and-swap (the lock-free
    /// shape of the org's <c>CircularBuffer</c>). A swap may run its change more than once under
    /// contention, so a change is pure; everything with an effect (starting work, cancelling or
    /// disposing a token source, logging a fault) happens once, after the swap, done by whichever
    /// caller's swap actually made the change.</para>
    /// </remarks>
    public class BackgroundTaskTracker
    {
        private sealed class Entry(Task task, string description)
        {
            public Task Task { get; } = task;
            public string Description { get; } = description;
        }

        private sealed class Slot
        {
            public required Task Task { get; init; }
            public required CancellationTokenSource Cts { get; init; }
            public required string Description { get; init; }

            // A SUPERSEDED slot has two parties on two threads, in either order: the caller whose swap
            // displaced it, which cancels it, and whoever retires it once its task has ended. Cancel on a
            // disposed source throws, so the source is disposed by whichever of the two settles second.
            private int _settled;

            public void Settle()
            {
                if (Interlocked.Increment(ref _settled) == 2)
                {
                    Cts.Dispose();
                }
            }
        }

        /// <param name="Pending">Anonymous work, in submission order.</param>
        /// <param name="Slots">Keyed work: at most one operation per key, and starting a new one cancels
        /// its predecessor. Separate from the anonymous list because these have an identity the caller can
        /// ask about later ("is a load running?", "give me the enhance result").</param>
        /// <param name="Superseded">Slots that have been cancelled and replaced. They stay until their task
        /// actually ends, because a cancelled task is not a finished one and its token source must outlive
        /// the work still reading the token.</param>
        private sealed record State(
            ImmutableArray<Entry> Pending,
            ImmutableDictionary<string, Slot> Slots,
            ImmutableArray<Slot> Superseded)
        {
            public static readonly State Empty = new State([], ImmutableDictionary<string, Slot>.Empty, []);
        }

        private State _state = State.Empty;

        /// <summary>
        /// Applies <paramref name="change"/> to the state atomically and returns the state it replaced
        /// and the one it installed. The change may run more than once under contention, so it must be
        /// PURE; callers act afterwards, once, on what the returned pair says actually happened.
        /// </summary>
        private (State Before, State After) Swap(Func<State, State> change)
        {
            var before = Volatile.Read(ref _state);
            while (true)
            {
                var after = change(before);
                var seen = Interlocked.CompareExchange(ref _state, after, before);
                if (ReferenceEquals(seen, before))
                {
                    return (before, after);
                }
                before = seen;
            }
        }

        /// <summary>
        /// Submits an async operation to run in the background. Safe from any thread.
        /// </summary>
        public void Run(Func<Task> work, string description)
        {
            // Started once, BEFORE the swap: a swap can retry, and a retry must not start the work again.
            var entry = new Entry(Task.Run(work), description);
            Swap(s => s with { Pending = s.Pending.Add(entry) });
        }

        /// <summary>
        /// Submits <paramref name="work"/> with standard error routing and tracks it (so it is
        /// awaited by <see cref="DrainAsync"/> and counted by <see cref="HasPending"/>): a
        /// <see cref="OperationCanceledException"/> is logged at Information and forwarded to
        /// <paramref name="onCancel"/>; any other exception is logged at Warning and forwarded to
        /// <paramref name="onError"/>; and <paramref name="onFinally"/> always runs. Because the work
        /// is guarded here it completes non-faulted, so <see cref="ProcessCompletions"/> will not also
        /// log it. <paramref name="operation"/> is used both as the tracker description and the log
        /// message subject.
        /// </summary>
        public void RunGuarded(
            Func<CancellationToken, Task> work,
            CancellationToken ct,
            ILogger logger,
            string operation,
            Action<Exception> onError,
            Action? onCancel = null,
            Action? onFinally = null)
            => Run(() => RunGuardedAsync(work, ct, logger, operation, onError, onCancel, onFinally), operation);

        /// <summary>
        /// The error-routing scaffold behind <see cref="RunGuarded"/>, exposed static so it can be
        /// composed or unit-tested without a tracker instance. Runs <paramref name="work"/> and routes
        /// the outcome (see <see cref="RunGuarded"/>); it never rethrows. An
        /// <see cref="OperationCanceledException"/> is logged (Information) rather than swallowed
        /// silently, so a cancellation always leaves a trace.
        /// </summary>
        public static async Task RunGuardedAsync(
            Func<CancellationToken, Task> work,
            CancellationToken ct,
            ILogger logger,
            string operation,
            Action<Exception> onError,
            Action? onCancel = null,
            Action? onFinally = null)
        {
            try
            {
                await work(ct);
            }
            catch (OperationCanceledException ex)
            {
                logger.LogInformation(ex, "{Operation} cancelled", operation);
                onCancel?.Invoke();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "{Operation} failed", operation);
                onError(ex);
            }
            finally
            {
                onFinally?.Invoke();
            }
        }

        /// <summary>
        /// The result-bearing counterpart of <see cref="RunGuardedAsync"/>: routes the outcome exactly
        /// the same way and answers <c>null</c> when the work cancelled or failed.
        /// </summary>
        /// <remarks>
        /// Reference types only, deliberately. For a value type the "nothing to report" answer would be
        /// <c>Nullable&lt;T&gt;</c>, which is a DIFFERENT runtime type from the one the work produced,
        /// so <see cref="TryCollect{TResult}"/> could no longer recognise its own task.
        /// </remarks>
        public static async Task<TResult?> RunGuardedAsync<TResult>(
            Func<CancellationToken, Task<TResult?>> work,
            CancellationToken ct,
            ILogger logger,
            string operation,
            Action<Exception> onError,
            Action? onCancel = null,
            Action? onFinally = null)
            where TResult : class
        {
            try
            {
                return await work(ct);
            }
            catch (OperationCanceledException ex)
            {
                logger.LogInformation(ex, "{Operation} cancelled", operation);
                onCancel?.Invoke();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "{Operation} failed", operation);
                onError(ex);
            }
            finally
            {
                onFinally?.Invoke();
            }

            return null;
        }

        /// <summary>
        /// Runs <paramref name="work"/> under <paramref name="key"/>, cancelling whatever was already
        /// running under that key.
        /// </summary>
        /// <remarks>
        /// <para>For work that is SUPERSEDED rather than queued: opening a second file while the first
        /// is still loading, or re-detecting stars because the image was replaced. The old result is
        /// not merely unwanted, it is about to be wrong, so the point is to stop paying for it.</para>
        /// <para>The token is linked to <paramref name="outer"/>, so app shutdown cancels the work as
        /// well, and a caller never has to compose the two itself.</para>
        /// </remarks>
        public void RunExclusive(
            string key,
            Func<CancellationToken, Task> work,
            CancellationToken outer,
            ILogger logger,
            string operation,
            Action<Exception> onError,
            Action? onCancel = null,
            Action? onFinally = null)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
            Install(key, new Slot
            {
                Task = Task.Run(() => RunGuardedAsync(work, cts.Token, logger, operation, onError, onCancel, onFinally)),
                Cts = cts,
                Description = operation,
            });
        }

        /// <summary>
        /// <see cref="RunExclusive"/> for work that produces a value, collected later by
        /// <see cref="TryCollect{TResult}"/>.
        /// </summary>
        /// <remarks>
        /// The result is handed back through the task and PULLED by the consumer rather than pushed
        /// into a callback, so it is adopted on whichever thread is entitled to adopt it -- for a UI
        /// that is the render thread, on the frame of its choosing. A callback would deliver it on the
        /// pool, which is exactly where a render-thread-owned field must not be written.
        /// </remarks>
        public void RunExclusive<TResult>(
            string key,
            Func<CancellationToken, Task<TResult?>> work,
            CancellationToken outer,
            ILogger logger,
            string operation,
            Action<Exception> onError,
            Action? onCancel = null,
            Action? onFinally = null)
            where TResult : class
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
            Install(key, new Slot
            {
                // Name the generic overload explicitly. Task<TResult> converts to Task, so the
                // non-generic RunGuardedAsync is also applicable here -- and C# PREFERS it, which
                // would hand this slot a bare Task and leave TryCollect unable to recognise its own
                // result for the rest of the run.
                Task = Task.Run(() => RunGuardedAsync<TResult>(work, cts.Token, logger, operation, onError, onCancel, onFinally)),
                Cts = cts,
                Description = operation,
            });
        }

        // Puts `slot` under `key`, superseding whatever was there. The work is already running (started
        // once, before the swap, which may retry), so a predecessor is cancelled a moment after its
        // successor starts rather than before; cancellation is cooperative, so the two overlapped anyway.
        private void Install(string key, Slot slot)
        {
            var (before, _) = Swap(s => s.Slots.TryGetValue(key, out var previous)
                ? s with { Slots = s.Slots.SetItem(key, slot), Superseded = s.Superseded.Add(previous) }
                : s with { Slots = s.Slots.SetItem(key, slot) });
            CancelDisplaced(before, key);
        }

        // Cancels the slot that `before` held under `key`, which the swap that replaced `before` moved to
        // the superseded list: exactly one caller's swap displaces a given slot, so it is cancelled once.
        private static void CancelDisplaced(State before, string key)
        {
            if (before.Slots.TryGetValue(key, out var displaced))
            {
                // Cancel, do NOT dispose. The outgoing work still holds this token, and disposing the
                // source out from under it throws ObjectDisposedException at its next check -- turning
                // an orderly supersede into a fault. It is disposed once its task is seen to end, by
                // whichever of this and the retirement settles second (Slot.Settle).
                displaced.Cts.Cancel();
                displaced.Settle();
            }
        }

        /// <summary>Whether work is currently running under <paramref name="key"/>.</summary>
        public bool IsRunning(string key)
            => Volatile.Read(ref _state).Slots.TryGetValue(key, out var slot) && !slot.Task.IsCompleted;

        /// <summary>Cancels the work under <paramref name="key"/>, if any.</summary>
        public void Cancel(string key)
        {
            var (before, _) = Swap(s => s.Slots.TryGetValue(key, out var previous)
                ? s with { Slots = s.Slots.Remove(key), Superseded = s.Superseded.Add(previous) }
                : s);
            CancelDisplaced(before, key);
        }

        /// <summary>
        /// Takes the result of the completed work under <paramref name="key"/>, if there is one.
        /// </summary>
        /// <remarks>
        /// Retires the slot either way once the task has ended: a run that cancelled or failed has
        /// nothing to hand over, and leaving it in place would wedge the key against its next use.
        /// Returns false while the work is still running.
        /// </remarks>
        public bool TryCollect<TResult>(string key, out TResult? result)
            where TResult : class
        {
            result = null;
            if (!Volatile.Read(ref _state).Slots.TryGetValue(key, out var slot) || !slot.Task.IsCompleted)
            {
                return false;
            }

            // Retired only if it is STILL the slot under the key. A start that raced in has superseded it,
            // and the key then belongs to the newer work; a second collector racing this one finds it gone.
            // Either way only the caller whose swap removed the slot disposes it.
            var (before, _) = Swap(s => s.Slots.TryGetValue(key, out var current) && ReferenceEquals(current, slot)
                ? s with { Slots = s.Slots.Remove(key) }
                : s);
            if (!before.Slots.TryGetValue(key, out var removed) || !ReferenceEquals(removed, slot))
            {
                return false;
            }

            slot.Cts.Dispose();

            if (slot.Task is Task<TResult?> { IsCompletedSuccessfully: true } typed)
            {
                result = typed.Result;
            }

            return result is not null;
        }

        /// <summary>
        /// Checks for completed tasks, logs errors, and removes them from the pending list.
        /// Call once per frame from the render loop. Returns true if any task completed
        /// (caller should trigger a redraw).
        /// </summary>
        public bool ProcessCompletions(ILogger logger)
        {
            // Static and allocation-free when nothing has completed, which is most frames: RemoveAll
            // hands back the same array when it removes nothing, so the state is swapped for itself.
            var (before, after) = Swap(static s =>
            {
                var pending = s.Pending.RemoveAll(static e => e.Task.IsCompleted);
                var superseded = s.Superseded.RemoveAll(static slot => slot.Task.IsCompleted);
                return pending.Length == s.Pending.Length && superseded.Length == s.Superseded.Length
                    ? s
                    : s with { Pending = pending, Superseded = superseded };
            });
            if (ReferenceEquals(before, after))
            {
                return false;
            }

            // Exactly what THIS swap removed: work that finished after it is still in `after`, for the
            // next call, and a concurrent caller's swap removed a disjoint set. `after` is `before` less
            // the removed items with the order kept, so one pass in step finds them.
            var anyCompleted = false;
            var kept = 0;
            foreach (var entry in before.Pending)
            {
                if (kept < after.Pending.Length && ReferenceEquals(after.Pending[kept], entry))
                {
                    kept++;
                    continue;
                }
                if (entry.Task.IsFaulted)
                {
                    logger.LogError(entry.Task.Exception,
                        "Background operation failed: {Description}", entry.Description);
                }
                anyCompleted = true;
            }

            // Retire superseded work once it has actually stopped; its source goes once its displacer has
            // cancelled it too (Slot.Settle), whichever of the two gets there last.
            kept = 0;
            foreach (var slot in before.Superseded)
            {
                if (kept < after.Superseded.Length && ReferenceEquals(after.Superseded[kept], slot))
                {
                    kept++;
                    continue;
                }
                slot.Settle();
                anyCompleted = true;
            }

            // Slots are NOT retired here. A result-bearing one is the caller's to collect, and it
            // cannot be collected once discarded; a plain one is cheap to leave until its key is
            // reused or the tracker drains. Faults are already routed by RunGuardedAsync.
            return anyCompleted;
        }

        /// <summary>Whether any tasks are still pending.</summary>
        public bool HasPending => PendingCount > 0;

        /// <summary>Number of pending tasks.</summary>
        public int PendingCount
        {
            get
            {
                var s = Volatile.Read(ref _state);
                var count = s.Pending.Length;
                foreach (var (_, slot) in s.Slots)
                {
                    if (!slot.Task.IsCompleted)
                    {
                        count++;
                    }
                }
                foreach (var slot in s.Superseded)
                {
                    if (!slot.Task.IsCompleted)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        /// <summary>Descriptions of all currently pending tasks, from one snapshot of the state.</summary>
        public IEnumerable<string> PendingDescriptions
        {
            get
            {
                var s = Volatile.Read(ref _state);
                foreach (var entry in s.Pending)
                {
                    yield return entry.Description;
                }
                foreach (var (_, slot) in s.Slots)
                {
                    if (!slot.Task.IsCompleted)
                    {
                        yield return slot.Description;
                    }
                }
                foreach (var slot in s.Superseded)
                {
                    if (!slot.Task.IsCompleted)
                    {
                        yield return slot.Description;
                    }
                }
            }
        }

        /// <summary>
        /// Awaits all pending tasks (swallowing exceptions), including work submitted while it waits,
        /// until none is left. Call at shutdown.
        /// </summary>
        public async Task DrainAsync()
        {
            // Pass after pass: work that submits more from its own thread while this waits (a
            // completion that queues a follow-up, a handler resuming after its first await) is waited
            // for too. It used to be enumerated from a list that submission then modified.
            while (Volatile.Read(ref _state).Pending is { IsEmpty: false } awaiting)
            {
                foreach (var entry in awaiting)
                {
                    try { await entry.Task; } catch { /* already logged by ProcessCompletions */ }
                }

                // Everything awaited has completed, so dropping every completed entry drops exactly those
                // (and anything submitted meanwhile that already finished); the rest is the next pass.
                Swap(static s => s with { Pending = s.Pending.RemoveAll(static e => e.Task.IsCompleted) });
            }

            // Keyed work is taken over in one swap, so nothing can collect or cancel it meanwhile, then
            // cancelled first so a drain does not sit through a load that nobody is waiting for any more,
            // then awaited so nothing is still touching state as the app tears down -- which is the whole
            // reason this is drained rather than abandoned.
            var (owned, _) = Swap(static s => s.Slots.IsEmpty && s.Superseded.IsEmpty
                ? s
                : s with { Slots = ImmutableDictionary<string, Slot>.Empty, Superseded = [] });

            foreach (var (_, slot) in owned.Slots)
            {
                slot.Cts.Cancel();
            }
            foreach (var slot in owned.Superseded)
            {
                slot.Cts.Cancel();
            }

            foreach (var (_, slot) in owned.Slots)
            {
                try { await slot.Task; } catch { /* already routed by RunGuardedAsync */ }
                slot.Cts.Dispose();
            }
            foreach (var slot in owned.Superseded)
            {
                try { await slot.Task; } catch { /* already routed by RunGuardedAsync */ }
                // Its displacer may not have finished cancelling it yet: the second to settle disposes.
                slot.Settle();
            }
        }
    }
}
