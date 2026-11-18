using System;

namespace DIR.Lib.Notifications
{
    /// <summary>How serious a notification is. Ordered so the worst of a set is its maximum.</summary>
    public enum NotificationSeverity
    {
        Info = 0,
        Success = 1,
        Warn = 2,
        Error = 3,
    }

    /// <summary>Whether a notification waits for the person or goes by itself.</summary>
    public enum NotificationKind
    {
        /// <summary>Stays among the things that need the person until dismissed or its action runs.</summary>
        Staying,
        /// <summary>Shows as a toast, leaves after the feed's fade time, then lives in the history.</summary>
        Fading,
    }

    /// <summary>
    /// One notification, as a host posts it. All properties are init-only so a later field is additive.
    /// The feed gives it an <see cref="Id"/> and stamps <see cref="Posted"/> on <see cref="NotificationFeed.Post"/>.
    /// </summary>
    public sealed record Notification
    {
        /// <summary>Assigned by the feed; zero before it is posted.</summary>
        public long Id { get; init; }

        public NotificationSeverity Severity { get; init; } = NotificationSeverity.Info;

        public string Title { get; init; } = "";

        public string Text { get; init; } = "";

        /// <summary>When it was posted, as the clock passed to the feed said.</summary>
        public DateTimeOffset Posted { get; init; }

        /// <summary>Where it came from (a node, a device, a subsystem); free text, nothing app-specific here.</summary>
        public string? Source { get; init; }

        public NotificationKind Kind { get; init; } = NotificationKind.Fading;

        /// <summary>Label of the button that runs <see cref="Action"/>; null for no button.</summary>
        public string? ActionLabel { get; init; }

        /// <summary>Run by <see cref="NotificationFeed.Handle"/>.</summary>
        public Action? Action { get; init; }

        /// <summary>
        /// Optional row the host supplies so its own cards keep their look. Null means the layout draws
        /// the default row from the fields above.
        /// </summary>
        public Func<Layout.Node>? Row { get; init; }
    }

    /// <summary>A toast on screen: the notification and, for a fading one, how much of its time is left.</summary>
    /// <param name="Notification">The notification.</param>
    /// <param name="RemainingFraction">1 when it just appeared, falling to 0 as it fades; always 1 for a staying toast.</param>
    public readonly record struct ToastView(Notification Notification, double RemainingFraction);
}
