using System;
using System.Collections.Generic;
using System.Linq;

namespace DIR.Lib.Notifications
{
    /// <summary>
    /// The surface-neutral notifications model a host fills and a layout draws. It owns no timer and reads
    /// no wall clock: the host passes the time to <see cref="Post"/> and <see cref="Advance"/>, so a test
    /// steps time by hand. Not thread-safe; post from the render thread (or through a SignalBus).
    /// </summary>
    public sealed class NotificationFeed
    {
        private sealed class Entry(Notification n)
        {
            public Notification N = n;
            /// <summary>Time the toast has been visible and not hovered (fading only).</summary>
            public TimeSpan Shown;
            public bool Open = true;      // staying: still needs the person; fading: still live as a toast
            public bool Handled;
            public DateTimeOffset Closed;
        }

        private readonly List<Entry> _entries = [];   // oldest first
        private long _nextId = 1;
        private long _hovered;
        private DateTimeOffset _now;
        private bool _hasNow;
        private TimeSpan _fadeAfter = TimeSpan.FromSeconds(5);
        private TimeSpan _keepFor = TimeSpan.FromHours(24);

        /// <summary>
        /// Most toasts the MODEL reports as open at once. <c>NotificationsStyle.MaxToasts</c> is the separate cap on
        /// how many a layout draws; they default alike (3) and a host that changes one changes the other.
        /// </summary>
        public int MaxToasts { get; init; } = 3;

        /// <summary>How long a fading toast stays while the pointer is not over it.</summary>
        public TimeSpan FadeAfter
        {
            get => _fadeAfter;
            set { _fadeAfter = value > TimeSpan.Zero ? value : TimeSpan.FromTicks(1); Touch(); }
        }

        /// <summary>How long a closed notification is kept in the history.</summary>
        public TimeSpan KeepFor
        {
            get => _keepFor;
            set { _keepFor = value; Touch(); }
        }

        /// <summary>Rises on every change a view could show; a host repaints when it moved.</summary>
        public long Version { get; private set; }

        /// <summary>Raised after <see cref="Version"/> rises.</summary>
        public event Action? Changed;

        private void Touch()
        {
            Version++;
            Changed?.Invoke();
        }

        /// <summary>Adds a notification, stamping its id and posted time; returns it as stored.</summary>
        public Notification Post(Notification notification, DateTimeOffset now)
        {
            ArgumentNullException.ThrowIfNull(notification);
            Advance(now);
            var stored = notification with { Id = _nextId++, Posted = now };
            _entries.Add(new Entry(stored));
            Touch();
            return stored;
        }

        /// <summary>
        /// Moves the clock to <paramref name="now"/>: runs the fade of visible, un-hovered toasts and prunes the history.
        /// The first call only sets the clock (there is no earlier time to measure from), so nothing fades on it.
        /// </summary>
        public void Advance(DateTimeOffset now)
        {
            if (!_hasNow)
            {
                _now = now;
                _hasNow = true;
                return;
            }
            var elapsed = now - _now;
            if (elapsed <= TimeSpan.Zero)
            {
                return;
            }
            _now = now;
            var changed = false;
            foreach (var e in VisibleEntries())
            {
                if (e.N.Kind == NotificationKind.Fading && e.N.Id != _hovered)
                {
                    e.Shown += elapsed;
                    changed = true;
                }
            }
            foreach (var e in _entries)
            {
                if (e.Open && e.N.Kind == NotificationKind.Fading && e.Shown >= _fadeAfter)
                {
                    Close(e, handled: false);
                    changed = true;
                }
            }
            if (_entries.RemoveAll(e => !e.Open && now - e.Closed > _keepFor) > 0)
            {
                changed = true;
            }
            if (changed)
            {
                Touch();
            }
        }

        /// <summary>Says which toast the pointer is over (null for none); that toast's clock stops meanwhile.</summary>
        public void PointerOver(long? id)
        {
            var v = id ?? 0;
            if (v != _hovered)
            {
                _hovered = v;
                Touch();
            }
        }

        /// <summary>Closes a notification without running its action. Unknown or closed ids do nothing.</summary>
        public void Dismiss(long id)
        {
            if (Find(id) is { Open: true } e)
            {
                Close(e, handled: false);
                Touch();
            }
        }

        /// <summary>Runs the action of an open notification and closes it as handled.</summary>
        public void Handle(long id)
        {
            if (Find(id) is { Open: true } e)
            {
                Close(e, handled: true);
                Touch();
                e.N.Action?.Invoke();
            }
        }

        /// <summary>Empties the history; open staying items and live toasts stay.</summary>
        public void Clear()
        {
            if (_entries.RemoveAll(e => !e.Open) > 0)
            {
                Touch();
            }
        }

        /// <summary>Whether the notification was closed by its action.</summary>
        public bool IsHandled(long id) => Find(id)?.Handled ?? false;

        private Entry? Find(long id) => _entries.Find(e => e.N.Id == id);

        private void Close(Entry e, bool handled)
        {
            e.Open = false;
            e.Handled = handled;
            e.Closed = _now;
            if (_hovered == e.N.Id)
            {
                _hovered = 0;
            }
        }

        private IEnumerable<Entry> VisibleEntries()
            => _entries.Where(e => e.Open).Reverse().Take(MaxToasts);

        /// <summary>Toasts on screen, newest first, at most <see cref="MaxToasts"/>.</summary>
        public IReadOnlyList<ToastView> Toasts
            => [.. VisibleEntries().Select(e => new ToastView(
                e.N,
                e.N.Kind == NotificationKind.Fading
                    ? Math.Clamp(1.0 - e.Shown / _fadeAfter, 0.0, 1.0)
                    : 1.0))];

        /// <summary>Staying notifications still open, newest first.</summary>
        public IReadOnlyList<Notification> NeedsYou
            => [.. _entries.Where(e => e.Open && e.N.Kind == NotificationKind.Staying).Select(e => e.N).Reverse()];

        /// <summary>Closed notifications, newest first (by posting).</summary>
        public IReadOnlyList<Notification> History
            => [.. _entries.Where(e => !e.Open).Select(e => e.N).Reverse()];

        /// <summary>The number the bell shows: how many staying notifications still need the person.</summary>
        public int BellCount => _entries.Count(e => e.Open && e.N.Kind == NotificationKind.Staying);

        /// <summary>Worst severity among the things that need the person; null when none. The look is the layout's job.</summary>
        public NotificationSeverity? WorstSeverity
            => BellCount == 0 ? null : _entries.Where(e => e.Open && e.N.Kind == NotificationKind.Staying).Max(e => e.N.Severity);
    }
}
