using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace DIR.Lib
{
    /// <summary>
    /// Tracks background tasks submitted from UI callbacks. Checks for completions
    /// each frame and logs errors. Shared between GUI and TUI.
    /// </summary>
    public class BackgroundTaskTracker
    {
        private readonly List<(Task Task, string Description)> _pending = [];

        // Keyed work: at most one operation per key, and starting a new one cancels its predecessor.
        // Separate from _pending because these have an identity the caller can ask about later
        // ("is a load running?", "give me the enhance result"), which an anonymous list cannot answer.
        private readonly Dictionary<string, Slot> _slots = [];

        // Slots that have been cancelled and replaced. They stay here until their task actually ends,
        // because a cancelled task is not a finished one and its CancellationTokenSource must outlive
        // the work still reading the token.
        private readonly List<Slot> _superseded = [];

        private sealed class Slot
        {
            public required Task Task { get; init; }
            public required CancellationTokenSource Cts { get; init; }
            public required string Description { get; init; }
        }

        /// <summary>
        /// Submits an async operation to run in the background.
        /// </summary>
        public void Run(Func<Task> work, string description)
        {
            _pending.Add((Task.Run(work), description));
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
            var cts = Supersede(key, outer);
            _slots[key] = new Slot
            {
                Task = Task.Run(() => RunGuardedAsync(work, cts.Token, logger, operation, onError, onCancel, onFinally)),
                Cts = cts,
                Description = operation,
            };
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
            var cts = Supersede(key, outer);
            _slots[key] = new Slot
            {
                // Name the generic overload explicitly. Task<TResult> converts to Task, so the
                // non-generic RunGuardedAsync is also applicable here -- and C# PREFERS it, which
                // would hand this slot a bare Task and leave TryCollect unable to recognise its own
                // result for the rest of the run.
                Task = Task.Run(() => RunGuardedAsync<TResult>(work, cts.Token, logger, operation, onError, onCancel, onFinally)),
                Cts = cts,
                Description = operation,
            };
        }

        private CancellationTokenSource Supersede(string key, CancellationToken outer)
        {
            if (_slots.Remove(key, out var previous))
            {
                // Cancel, do NOT dispose. The outgoing work still holds this token, and disposing the
                // source out from under it throws ObjectDisposedException at its next check -- turning
                // an orderly supersede into a fault. It is disposed once its task is seen to end.
                previous.Cts.Cancel();
                _superseded.Add(previous);
            }

            return CancellationTokenSource.CreateLinkedTokenSource(outer);
        }

        /// <summary>Whether work is currently running under <paramref name="key"/>.</summary>
        public bool IsRunning(string key)
            => _slots.TryGetValue(key, out var slot) && !slot.Task.IsCompleted;

        /// <summary>Cancels the work under <paramref name="key"/>, if any.</summary>
        public void Cancel(string key) => Supersede(key, CancellationToken.None).Dispose();

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
            if (!_slots.TryGetValue(key, out var slot) || !slot.Task.IsCompleted)
            {
                return false;
            }

            _slots.Remove(key);
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
            var anyCompleted = false;
            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].Task.IsCompleted)
                {
                    if (_pending[i].Task.IsFaulted)
                    {
                        logger.LogError(_pending[i].Task.Exception,
                            "Background operation failed: {Description}", _pending[i].Description);
                    }
                    _pending.RemoveAt(i);
                    anyCompleted = true;
                }
            }
            // Retire superseded work once it has actually stopped, and only then dispose its source.
            for (var i = _superseded.Count - 1; i >= 0; i--)
            {
                if (_superseded[i].Task.IsCompleted)
                {
                    _superseded[i].Cts.Dispose();
                    _superseded.RemoveAt(i);
                    anyCompleted = true;
                }
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
                var count = _pending.Count;
                foreach (var (_, slot) in _slots)
                {
                    if (!slot.Task.IsCompleted)
                    {
                        count++;
                    }
                }
                foreach (var slot in _superseded)
                {
                    if (!slot.Task.IsCompleted)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        /// <summary>Descriptions of all currently pending tasks.</summary>
        public IEnumerable<string> PendingDescriptions
        {
            get
            {
                foreach (var (_, desc) in _pending)
                {
                    yield return desc;
                }
                foreach (var (_, slot) in _slots)
                {
                    if (!slot.Task.IsCompleted)
                    {
                        yield return slot.Description;
                    }
                }
                foreach (var slot in _superseded)
                {
                    if (!slot.Task.IsCompleted)
                    {
                        yield return slot.Description;
                    }
                }
            }
        }

        /// <summary>
        /// Awaits all pending tasks (swallowing exceptions). Call at shutdown.
        /// </summary>
        public async Task DrainAsync()
        {
            foreach (var (task, _) in _pending)
            {
                try { await task; } catch { /* already logged by ProcessCompletions */ }
            }
            _pending.Clear();

            // Keyed work is cancelled first so a drain does not sit through a load that nobody is
            // waiting for any more, then awaited so nothing is still touching state as the app tears
            // down -- which is the whole reason this is drained rather than abandoned.
            foreach (var (_, slot) in _slots)
            {
                slot.Cts.Cancel();
            }
            foreach (var slot in _superseded)
            {
                slot.Cts.Cancel();
            }

            foreach (var (_, slot) in _slots)
            {
                try { await slot.Task; } catch { /* already routed by RunGuardedAsync */ }
                slot.Cts.Dispose();
            }
            foreach (var slot in _superseded)
            {
                try { await slot.Task; } catch { /* already routed by RunGuardedAsync */ }
                slot.Cts.Dispose();
            }
            _slots.Clear();
            _superseded.Clear();
        }
    }
}
