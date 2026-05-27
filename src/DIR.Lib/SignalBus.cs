using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Threading.Tasks;

namespace DIR.Lib
{
    /// <summary>
    /// Thread-safe, typed, deferred signal bus. Widgets <see cref="Post{T}"/> signals
    /// during event handling; hosts <see cref="Subscribe{T}(Action{T})"/> handlers
    /// at any time -- including from background threads or lazy-loaded widgets after
    /// the frame loop has started. Signals are delivered when <see cref="ProcessPending"/>
    /// is called (once per frame, after event handling, before render).
    /// </summary>
    public sealed class SignalBus
    {
        // ConcurrentDictionary + ImmutableArray so Subscribe is safe to call from
        // any thread at any time. ProcessPending reads the handler list via
        // TryGetValue and iterates the immutable snapshot lock-free; a handler
        // subscribed mid-ProcessPending lands in a new array that the *next*
        // ProcessPending call will see, not the in-flight one (no reentrancy).
        private readonly ConcurrentQueue<object> _pending = new();
        private readonly ConcurrentDictionary<Type, ImmutableArray<Func<object, Task?>>> _handlers = new();

        /// <summary>
        /// Subscribes a synchronous handler for signals of type <typeparamref name="T"/>.
        /// Thread-safe -- may be called concurrently with other Subscribe calls or with
        /// <see cref="ProcessPending"/>. A handler subscribed during ProcessPending fires
        /// on the next call, not the in-flight one.
        /// </summary>
        public void Subscribe<T>(Action<T> handler) where T : notnull
        {
            AddHandler(typeof(T), signal =>
            {
                handler((T)signal);
                return null;
            });
        }

        /// <summary>
        /// Subscribes an async handler for signals of type <typeparamref name="T"/>.
        /// Thread-safe -- see <see cref="Subscribe{T}(Action{T})"/>. When delivered,
        /// the returned Task is submitted to <see cref="BackgroundTaskTracker"/>
        /// (if provided to <see cref="ProcessPending"/>).
        /// </summary>
        public void Subscribe<T>(Func<T, Task> handler) where T : notnull
        {
            AddHandler(typeof(T), signal => handler((T)signal));
        }

        /// <summary>
        /// Posts a signal for delivery at the next <see cref="ProcessPending"/> call.
        /// Thread-safe — may be called from any thread.
        /// </summary>
        public void Post<T>(T signal) where T : notnull
        {
            _pending.Enqueue(signal);
        }

        /// <summary>
        /// Delivers all pending signals to their registered handlers.
        /// Sync handlers run inline. Async handlers are submitted to the tracker
        /// via <see cref="BackgroundTaskTracker.Run"/>.
        /// Call once per frame, after event handling, before render.
        /// Returns true if any signal was dequeued.
        /// </summary>
        public bool ProcessPending(BackgroundTaskTracker? tracker = null)
        {
            var anyProcessed = false;

            while (_pending.TryDequeue(out var signal))
            {
                anyProcessed = true;

                if (_handlers.TryGetValue(signal.GetType(), out var handlers))
                {
                    // ImmutableArray<T> iteration is via its struct enumerator
                    // (zero-alloc) and the array is immutable, so a concurrent
                    // Subscribe that publishes a new array doesn't disturb us.
                    foreach (var handler in handlers)
                    {
                        var task = handler(signal);
                        if (task is not null)
                        {
                            if (tracker is null)
                            {
                                throw new InvalidOperationException(
                                    $"Async signal handler for {signal.GetType().Name} returned a Task but no BackgroundTaskTracker was provided to ProcessPending.");
                            }
                            tracker.Run(() => task, signal.GetType().Name);
                        }
                    }
                }
            }

            return anyProcessed;
        }

        // AddOrUpdate is atomic on ConcurrentDictionary; ImmutableArray.Add returns
        // a new array, so concurrent subscribers compose via the updateValueFactory
        // delegate. The CD retries updateValueFactory under contention, so it must
        // stay side-effect-free aside from the array-create call.
        private void AddHandler(Type signalType, Func<object, Task?> handler)
        {
            _handlers.AddOrUpdate(
                signalType,
                _ => ImmutableArray.Create(handler),
                (_, existing) => existing.Add(handler));
        }
    }
}
