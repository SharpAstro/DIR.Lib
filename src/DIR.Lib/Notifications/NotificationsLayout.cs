using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using DIR.Lib.Layout;

namespace DIR.Lib.Notifications
{
    /// <summary>Which part of the notifications UI a <see cref="NotificationHit"/> names.</summary>
    public enum NotificationPart
    {
        /// <summary>The bell in the strip: toggles the bar.</summary>
        Bell,
        /// <summary>A toast's card. A fading toast declares it as its hover region.</summary>
        Toast,
        /// <summary>A row in the list.</summary>
        Row,
        /// <summary>The button that runs the notification's action.</summary>
        Action,
        /// <summary>The button that dismisses the notification.</summary>
        Dismiss,
        /// <summary>The list's Clear button.</summary>
        Clear,
    }

    /// <summary>What a region of the notifications UI is, so a host can tell hover and clicks apart. <see cref="Id"/> is zero for the parts that name no notification.</summary>
    public sealed record NotificationHit(NotificationPart Part, long Id = 0) : HitResult;

    /// <summary>Which corner of the page the toast stack sits in.</summary>
    public enum ToastCorner
    {
        TopRight,
        BottomRight,
    }

    /// <summary>Open or collapsed, and the list's scroll. One instance per window; the tree carries nothing else of the open state.</summary>
    public sealed class NotificationsState
    {
        /// <summary>True when the side bar covers the right of the page; false when only the strip shows.</summary>
        public bool Open { get; set; }

        /// <summary>Flips <see cref="Open"/>.</summary>
        public void Toggle() => Open = !Open;

        /// <summary>Scrolls the list in the open bar.</summary>
        public ListScrollController ListScroll { get; } = new();
    }

    /// <summary>The colours the notifications UI draws with: the severity marks plus surface and text roles.</summary>
    public sealed record NotificationsColors
    {
        public required RGBAColor32 Info { get; init; }
        public required RGBAColor32 Warn { get; init; }
        public required RGBAColor32 Error { get; init; }
        public required RGBAColor32 Success { get; init; }
        /// <summary>The strip and the bar.</summary>
        public required RGBAColor32 Surface { get; init; }
        /// <summary>A toast or a row.</summary>
        public required RGBAColor32 Card { get; init; }
        public required RGBAColor32 Text { get; init; }
        public required RGBAColor32 Dim { get; init; }
        public required RGBAColor32 Separator { get; init; }
        /// <summary>The bar of a fading toast's countdown.</summary>
        public required RGBAColor32 Track { get; init; }

        /// <summary>Takes every role from a palette, so the theme stays the one source.</summary>
        public static NotificationsColors From(UiPalette p) => new()
        {
            Info = p.Info,
            Warn = p.Warn,
            Error = p.Error,
            Success = p.Success,
            Surface = p.HeaderBg,
            Card = p.PanelBg,
            Text = p.BodyText,
            Dim = p.DimText,
            Separator = p.Separator,
            Track = p.Separator,
        };

        /// <summary>The colour of a severity.</summary>
        public RGBAColor32 Of(NotificationSeverity s) => s switch
        {
            NotificationSeverity.Error => Error,
            NotificationSeverity.Warn => Warn,
            NotificationSeverity.Success => Success,
            _ => Info,
        };
    }

    /// <summary>Sizes of the notifications UI, in design units.</summary>
    public sealed record NotificationsStyle
    {
        /// <summary>Width of the strip at the right edge that always shows the bell.</summary>
        public float StripWidth { get; init; } = 48f;
        /// <summary>Width of the open bar.</summary>
        public float BarWidth { get; init; } = 360f;
        public float ToastWidth { get; init; } = 320f;
        /// <summary>Most toasts drawn at once; separate from <c>NotificationFeed.MaxToasts</c> (the model's cap), same default.</summary>
        public int MaxToasts { get; init; } = 3;
        public float FontSize { get; init; } = 14f;
        public float BellSize { get; init; } = 20f;
        public float Padding { get; init; } = 8f;
        public float Gap { get; init; } = 6f;
        /// <summary>Distance of the toast stack from the content rect's edges.</summary>
        public float Margin { get; init; } = 12f;
        /// <summary>
        /// Where the toast stack sits in the page. Bottom right by default, as most apps do. The stack never changes
        /// corner with the pointer (nothing moves under the pointer): the corner is this setting alone.
        /// </summary>
        public ToastCorner Corner { get; init; } = ToastCorner.BottomRight;
        /// <summary>Height of one toast as the old placement rule assumed it (the real height follows the content).</summary>
        [Obsolete("Only the pointer-dodging placement rule used it, and that is gone: the stack sits at NotificationsStyle.Corner.")]
        public float ToastHeightEstimate { get; init; } = 72f;
        /// <summary>Lines of text a toast or a row shows before an ellipsis (the whole text is on hover).</summary>
        public int MaxTextLines { get; init; } = 3;
        /// <summary>Height of a fading toast's countdown bar.</summary>
        public float CountdownHeight { get; init; } = 3f;
        /// <summary>The key that toggles the bar.</summary>
        public KeyChord ToggleKey { get; init; } = new(InputKey.N, InputModifier.Ctrl);
    }

    /// <summary>
    /// The notifications UI as one <see cref="Node"/> declaration, built from a <see cref="NotificationFeed"/> and
    /// arranged alike by a pixel and a cell measure context. It touches no surface and reads no clock.
    /// </summary>
    public static class NotificationsLayout
    {
        /// <summary>
        /// Builds the page docked beside the fixed strip. Open, the bar is an overlay over the right of the page (it
        /// never resizes it); collapsed, the toasts are an overlay over the page. Either way the page is arranged
        /// into the same rect, so the content does not move when the bar opens. The toast stack sits at
        /// <see cref="NotificationsStyle.Corner"/> and nowhere else.
        /// </summary>
        /// <param name="page">The application's content.</param>
        public static Node Build(Node page, NotificationFeed feed, NotificationsState state,
            NotificationsColors colors, NotificationsStyle style)
        {
            ArgumentNullException.ThrowIfNull(page);
            ArgumentNullException.ThrowIfNull(feed);
            ArgumentNullException.ThrowIfNull(state);
            ArgumentNullException.ThrowIfNull(style);

            Node fill;
            if (state.Open)
            {
                fill = Builder.Overlay(page, BarLayer(feed, state, colors, style));
            }
            else
            {
                var toasts = ToastsOf(feed, style);
                fill = toasts.Count == 0
                    ? page
                    : Builder.Overlay(page, ToastLayer(toasts, feed, colors, style, style.Corner));
            }

            return Builder.Dock(fill, Builder.Right(Strip(feed, state, colors, style), style.StripWidth));
        }

        /// <summary>
        /// Kept, with no [Obsolete] so a caller that passes both compiles unchanged: <paramref name="pointer"/> and
        /// <paramref name="contentRect"/> no longer mean anything (the stack never follows the pointer) and are ignored; the
        /// stack sits at <see cref="NotificationsStyle.Corner"/>. Prefer the overload without them.
        /// </summary>
        public static Node Build(Node page, NotificationFeed feed, NotificationsState state,
            NotificationsColors colors, NotificationsStyle style, Vector2? pointer, RectF32 contentRect)
            => Build(page, feed, state, colors, style);

        /// <summary>
        /// Top right, else bottom right when the pointer is over where the top-right stack of
        /// <paramref name="count"/> toasts would sit. Kept as it was for binary compatibility; nothing in the library calls it.
        /// </summary>
        [Obsolete("The stack no longer moves with the pointer; set NotificationsStyle.Corner instead.")]
        public static ToastCorner PlaceToasts(RectF32 content, int count, NotificationsStyle style, Vector2? pointer)
        {
            if (pointer is not { } p || count <= 0 || content.Width <= 0f || content.Height <= 0f)
            {
                return ToastCorner.TopRight;
            }

#pragma warning disable CS0618
            return TopRightRegion(content, count, style).Contains(p) ? ToastCorner.BottomRight : ToastCorner.TopRight;
#pragma warning restore CS0618
        }

        /// <summary>The rect the top-right stack of <paramref name="count"/> toasts was assumed to cover.</summary>
        [Obsolete("The stack no longer moves with the pointer; set NotificationsStyle.Corner instead.")]
        public static RectF32 TopRightRegion(RectF32 content, int count, NotificationsStyle style)
        {
#pragma warning disable CS0618
            var height = count * style.ToastHeightEstimate + Math.Max(0, count - 1) * style.Gap;
#pragma warning restore CS0618
            return new RectF32(content.Right - style.Margin - style.ToastWidth, content.Y + style.Margin,
                style.ToastWidth, height);
        }

        /// <summary>The notification a hit names as a toast, for the host to pass to <see cref="NotificationFeed.PointerOver"/>; null otherwise.</summary>
        public static long? ToastIdOf(HitResult? hit)
            => hit is NotificationHit { Part: NotificationPart.Toast, Id: var id } ? id : null;

        /// <summary>The bell's colour: the worst severity while something rings (an error shows as an error, anything else as a warning), dim when nothing does.</summary>
        public static RGBAColor32 BellColor(NotificationFeed feed, NotificationsColors colors)
            => feed.WorstSeverity switch
            {
                null => colors.Dim,
                NotificationSeverity.Error => colors.Error,
                _ => colors.Warn,
            };

        private static IReadOnlyList<ToastView> ToastsOf(NotificationFeed feed, NotificationsStyle style)
        {
            var all = feed.Toasts;
            return all.Count <= style.MaxToasts ? all : [.. all.Take(style.MaxToasts)];
        }

        // ---- the strip and the bell ----

        private static Node Strip(NotificationFeed feed, NotificationsState state, NotificationsColors c, NotificationsStyle s)
        {
            var bell = Builder.Icon(IconKind.Bell, s.BellSize, BellColor(feed, c));
            var children = new List<Node> { bell };
            if (feed.BellCount > 0)
            {
                children.Add(Builder.Text(feed.BellCount.ToString(CultureInfo.InvariantCulture), s.FontSize,
                    BellColor(feed, c), TextAlign.Center));
            }

            return Builder.VStack([.. children])
                .CrossCenter()
                .WithGap(2f)
                .Pad(s.Padding)
                .Bg(c.Surface)
                .BorderLeft(c.Separator)
                .Clickable(new NotificationHit(NotificationPart.Bell), _ => state.Toggle(), CursorKind.Pointer)
                .WithShortcut(s.ToggleKey)
                .WithTooltip("Notifications", DockSide.Left);
        }

        // ---- toasts ----

        private static Node ToastLayer(IReadOnlyList<ToastView> toasts, NotificationFeed feed, NotificationsColors c,
            NotificationsStyle s, ToastCorner corner)
        {
            // toasts is newest first. A new toast lands at the corner and does not push older toasts away from it
            // (or under the pointer): at the bottom the newest is the bottom card, at the top it is the top card.
            var cards = new List<Node>(toasts.Count);
            foreach (var t in corner == ToastCorner.BottomRight ? toasts.Reverse() : toasts)
            {
                cards.Add(Toast(t, feed, c, s));
            }

            var column = Builder.VStack([.. cards]).WithGap(s.Gap).WFixed(s.ToastWidth);
            var right = Builder.HStack(Builder.Spacer().WStar(), column).WStar();
            return corner == ToastCorner.TopRight
                ? Builder.VStack(right).Pad(s.Margin).Stretch()
                : Builder.VStack(Builder.Spacer().HStar(), right).Pad(s.Margin).Stretch();
        }

        private static Node Toast(ToastView view, NotificationFeed feed, NotificationsColors c, NotificationsStyle s)
        {
            var n = view.Notification;
            var parts = new List<Node> { n.Row?.Invoke() ?? DefaultBody(n, c, s, showTime: false) };
            if (n.Kind == NotificationKind.Staying)
            {
                parts.Add(Buttons(n, feed, c, s));
            }
            else
            {
                parts.Add(Builder.Progress((float)view.RemainingFraction, c.Track, c.Of(n.Severity))
                    .RowH(s.CountdownHeight));
            }

            var card = Builder.VStack([.. parts]).WithGap(s.Gap).Pad(s.Padding)
                .Bg(c.Card).BorderLeft(c.Of(n.Severity), 3f).WStar();
            // A fading toast declares the hover region; a staying one is plain chrome around its own buttons.
            return n.Kind == NotificationKind.Fading
                ? card.Clickable(new NotificationHit(NotificationPart.Toast, n.Id))
                : card;
        }

        private static Node Buttons(Notification n, NotificationFeed feed, NotificationsColors c, NotificationsStyle s)
        {
            var buttons = new List<Node> { Builder.Spacer().WStar() };
            if (n.Action is not null && !string.IsNullOrEmpty(n.ActionLabel))
            {
                buttons.Add(Button(n.ActionLabel!, c.Of(n.Severity), c, s,
                    new NotificationHit(NotificationPart.Action, n.Id), () => feed.Handle(n.Id)));
            }

            buttons.Add(Button("Dismiss", c.Dim, c, s,
                new NotificationHit(NotificationPart.Dismiss, n.Id), () => feed.Dismiss(n.Id)));
            return Builder.HStack([.. buttons]).WithGap(s.Gap).CrossCenter().WStar();
        }

        private static Node Button(string label, RGBAColor32 ink, NotificationsColors c, NotificationsStyle s,
            HitResult hit, Action run)
            => Builder.HStack(Builder.Text(label, s.FontSize, ink, TextAlign.Center).WAuto())
                .Pad(s.Padding, 2f)
                .Bg(c.Surface)
                .Clickable(hit, _ => run(), CursorKind.Pointer)
                .WAuto();

        // ---- the bar ----

        private static Node BarLayer(NotificationFeed feed, NotificationsState state, NotificationsColors c, NotificationsStyle s)
        {
            var header = Builder.HStack(
                    Builder.Text("Notifications", s.FontSize, c.Text).WStar(),
                    Builder.HStack(Builder.Text("Clear", s.FontSize, feed.History.Count > 0 ? c.Text : c.Dim, TextAlign.Center).WAuto())
                        .Pad(s.Padding, 2f)
                        .Bg(c.Card)
                        .Clickable(new NotificationHit(NotificationPart.Clear), _ => feed.Clear(), CursorKind.Pointer)
                        .WAuto())
                .WithGap(s.Gap).CrossCenter().WStar();

            var rows = new List<Node>();
            var needs = feed.NeedsYou;
            var history = feed.History;
            if (needs.Count > 0)
            {
                rows.Add(Section("Needs you", needs.Count, c, s));
                foreach (var n in needs)
                {
                    rows.Add(Row(n, feed, c, s, actions: true));
                }
            }

            if (history.Count > 0)
            {
                rows.Add(Section("History", history.Count, c, s));
                foreach (var n in history)
                {
                    rows.Add(Row(n, feed, c, s, actions: false));
                }
            }

            if (rows.Count == 0)
            {
                rows.Add(Builder.Text("Nothing here", s.FontSize, c.Dim).WStar());
            }

            var list = Builder.VStack([.. rows]).WithGap(s.Gap).WStar().HStar().WithScroll(state.ListScroll);
            var bar = Builder.VStack(header, list)
                .WithGap(s.Gap).Pad(s.Padding).Bg(c.Surface).BorderLeft(c.Separator)
                .WithCursor(CursorKind.Default)
                .WFixed(s.BarWidth).HStar();
            return Builder.HStack(Builder.Spacer().WStar().HStar(), bar).Stretch();
        }

        private static Node Section(string title, int count, NotificationsColors c, NotificationsStyle s)
            => Builder.Text($"{title} ({count.ToString(CultureInfo.InvariantCulture)})", s.FontSize, c.Dim).WStar();

        private static Node Row(Notification n, NotificationFeed feed, NotificationsColors c, NotificationsStyle s, bool actions)
        {
            var parts = new List<Node> { n.Row?.Invoke() ?? DefaultBody(n, c, s, showTime: true) };
            if (actions && n.Kind == NotificationKind.Staying)
            {
                parts.Add(Buttons(n, feed, c, s));
            }

            return Builder.VStack([.. parts]).WithGap(s.Gap).Pad(s.Padding)
                .Bg(c.Card).BorderLeft(c.Of(n.Severity), 3f).WStar();
        }

        // ---- the default row ----

        private static Node DefaultBody(Notification n, NotificationsColors c, NotificationsStyle s, bool showTime)
        {
            var head = new List<Node>
            {
                // The title keeps one line with an ellipsis; the whole of it is the hover.
                Builder.Text(n.Title, s.FontSize, c.Text).WStar().WithTooltip(n.Title),
            };
            if (showTime)
            {
                head.Add(Builder.Text(n.Posted.ToString("HH:mm", CultureInfo.InvariantCulture), s.FontSize, c.Dim,
                    TextAlign.Far).WAuto());
            }

            var parts = new List<Node> { Builder.HStack([.. head]).WithGap(s.Gap).CrossCenter().WStar() };
            if (n.Text.Length > 0)
            {
                parts.Add(Wrapped(n.Text, s.FontSize, c.Text, s.MaxTextLines));
            }

            if (!string.IsNullOrEmpty(n.Source))
            {
                parts.Add(Builder.Text(n.Source!, s.FontSize, c.Dim).WStar().WithTooltip(n.Source!));
            }

            return Builder.VStack([.. parts]).WStar();
        }

        // Words in a WrapH capped at maxLines: the text wraps, and what does not fit ends in an ellipsis with the whole
        // text as the hover, so nothing is cut silently.
        private static Node Wrapped(string text, float fontSize, RGBAColor32 ink, int maxLines)
        {
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var leaves = new List<Node>(words.Length);
            for (var i = 0; i < words.Length; i++)
            {
                var word = i < words.Length - 1 ? words[i] + " " : words[i];
                leaves.Add(Builder.Text(word, fontSize, ink).WAuto());
            }

            return Builder.WrapH([.. leaves]).WithMaxLines(maxLines, true).WStar().WithTooltip(text);
        }
    }
}
