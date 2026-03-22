using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DIR.Lib
{
    /// <summary>
    /// Thread-safe, typed, deferred signal bus. Widgets <see cref="Post{T}"/> signals
    /// during event handling; hosts <see cref="Subscribe{T}(Action{T})"/> handlers at startup.
    /// Signals are delivered when <see cref="ProcessPending"/> is called (once per frame,
    /// after event handling, before render).
    /// </summary>
    public sealed class SignalBus
    {
        private readonly ConcurrentQueue<object> _pending = new();
        private readonly Dictionary<Type, List<Func<object, Task?>>> _handlers = new();

        /// <summary>
        /// Subscribes a synchronous handler for signals of type <typeparamref name="T"/>.
        /// Must be called before the frame loop starts (not thread-safe for subscription).
        /// </summary>
        public void Subscribe<T>(Action<T> handler) where T : notnull
        {
            GetOrCreateHandlerList(typeof(T)).Add(signal =>
            {
                handler((T)signal);
                return null;
            });
        }

        /// <summary>
        /// Subscribes an async handler for signals of type <typeparamref name="T"/>.
        /// When delivered, the returned Task is submitted to <see cref="BackgroundTaskTracker"/>
        /// (if provided to <see cref="ProcessPending"/>).
        /// </summary>
        public void Subscribe<T>(Func<T, Task> handler) where T : notnull
        {
            GetOrCreateHandlerList(typeof(T)).Add(signal => handler((T)signal));
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

        private List<Func<object, Task?>> GetOrCreateHandlerList(Type signalType)
        {
            if (!_handlers.TryGetValue(signalType, out var list))
            {
                list = new List<Func<object, Task?>>();
                _handlers[signalType] = list;
            }
            return list;
        }
    }
}
