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
        /// <summary>A toast's card. A fading toast declares it as its hover region. Every card is a surface (11.86): a
        /// staying card's body takes a press and does nothing with it.</summary>
        Toast,
        /// <summary>A row in the list.</summary>
        Row,
        /// <summary>The button that runs the notification's action.</summary>
        Action,
        /// <summary>The button that dismisses the notification.</summary>
        Dismiss,
        /// <summary>The list's Clear button.</summary>
        Clear,
        /// <summary>The cross at the top right of a fading toast: dismisses it. Painted over the card, so its click
        /// does not reach the card's <see cref="Toast"/> region.</summary>
        Close,
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
        private readonly RGBAColor32? _body;

        /// <summary>Ink of a toast's or row's message, below the title. Defaults to <see cref="Text"/>; never <see cref="Dim"/>.</summary>
        public RGBAColor32 Body { get => _body ?? Text; init => _body = value; }

        public required RGBAColor32 Separator { get; init; }
        /// <summary>The bar of a fading toast's countdown.</summary>
        public required RGBAColor32 Track { get; init; }

        private readonly RGBAColor32? _raised;
        private readonly RGBAColor32? _edge;

        /// <summary>A raised surface (a toast). Defaults to <see cref="Card"/>, so an 11.86 initializer is unchanged.</summary>
        public RGBAColor32 Raised { get => _raised ?? Card; init => _raised = value; }

        /// <summary>The edge of a raised surface. Defaults to <see cref="Separator"/>.</summary>
        public RGBAColor32 Edge { get => _edge ?? Separator; init => _edge = value; }

        private readonly RGBAColor32? _page;

        /// <summary>
        /// The page the toasts are drawn over, which the edge must stand out from. Defaults to <see cref="Surface"/>;
        /// <see cref="From(UiPalette)"/> takes <see cref="UiPalette.ContentBg"/>.
        /// </summary>
        public RGBAColor32 Page { get => _page ?? Surface; init => _page = value; }

        /// <summary>
        /// Ink on a filled severity button. Null (the default) derives it per severity as black or white, whichever
        /// contrasts more with the severity colour; see <see cref="InkOn"/>.
        /// </summary>
        public RGBAColor32? OnSeverity { get; init; }

        /// <summary>True when the palette has one hue (<see cref="UiPalette.IsOneHue"/>): severity is then not tinted.</summary>
        public bool OneHue { get; init; }

        /// <summary>Corner radius of a toast, in design units.</summary>
        public float CornerRadius { get; init; } = 6f;

        /// <summary>The shadow a toast carries when the palette has several hues; see <see cref="ToastShadow"/>.</summary>
        public Layout.ShadowStyle Shadow { get; init; } = UiMetrics.DefaultShadow;

        /// <summary>The shadow to paint: none (null) when <see cref="OneHue"/> or when the shadow is fully transparent.</summary>
        public Layout.ShadowStyle? ToastShadow => OneHue || Shadow.Color.Alpha == 0 ? null : Shadow;

        /// <summary>Tinted fill of a severity's toast: the severity mixed 15% into <see cref="Raised"/>; plain <see cref="Raised"/> when one-hue.</summary>
        public RGBAColor32 TintedFill(NotificationSeverity s) => OneHue ? Raised : RGBAColor32.Mix(Raised, Of(s), 0.15f);

        /// <summary>
        /// Tinted edge of a severity's toast: the severity colour itself (a full mix into <see cref="Raised"/>), because the
        /// edge must reach 3:1 against the page on dark and light palettes alike and no weaker mix does on every severity;
        /// a severity colour that still misses 3:1 against <see cref="Page"/> is moved toward <see cref="Text"/> in steps of
        /// a tenth until it reaches it (see <see cref="OnPage"/>); when one-hue, <see cref="EdgeOnPage"/>: with no tint and no shadow the edge is the only thing marking the card.
        /// </summary>
        public RGBAColor32 TintedEdge(NotificationSeverity s) => OneHue ? EdgeOnPage() : OnPage(RGBAColor32.Mix(Raised, Of(s), 1f));

        /// <summary>
        /// The edge to draw over <see cref="Page"/>: <see cref="Edge"/> itself when it reaches 3:1 there, else
        /// <see cref="Edge"/> moved toward <see cref="Text"/> in steps of a tenth until it does.
        /// </summary>
        public RGBAColor32 EdgeOnPage() => OnPage(Edge);

        /// <summary><paramref name="edge"/> itself when it reaches 3:1 against <see cref="Page"/>, else moved toward <see cref="Text"/> until it does.</summary>
        public RGBAColor32 OnPage(RGBAColor32 edge)
        {
            if (RGBAColor32.ContrastRatio(edge, Page) >= 3f) return edge;
            for (var i = 1; i <= 10; i++)
            {
                var moved = RGBAColor32.Mix(edge, Text, i / 10f);
                if (RGBAColor32.ContrastRatio(moved, Page) >= 3f) return moved;
            }

            return Text;
        }

        /// <summary>
        /// The dim ink to draw on <paramref name="fill"/>: <see cref="Dim"/> itself when it reaches 4.5:1 there, else
        /// <see cref="Dim"/> moved toward <see cref="Text"/> in steps of a tenth until it does.
        /// </summary>
        public RGBAColor32 DimOn(RGBAColor32 fill)
        {
            if (RGBAColor32.ContrastRatio(Dim, fill) >= 4.5f) return Dim;
            for (var i = 1; i <= 10; i++)
            {
                var ink = RGBAColor32.Mix(Dim, Text, i / 10f);
                if (RGBAColor32.ContrastRatio(ink, fill) >= 4.5f) return ink;
            }

            return Text;
        }

        /// <summary>Ink for text or an icon drawn on a filled severity button of <paramref name="s"/>.</summary>
        public RGBAColor32 InkOn(NotificationSeverity s)
        {
            if (OnSeverity is { } ink) return ink;
            var fill = Of(s);
            var black = new RGBAColor32(0, 0, 0, 255);
            var white = new RGBAColor32(255, 255, 255, 255);
            return RGBAColor32.ContrastRatio(fill, black) >= RGBAColor32.ContrastRatio(fill, white) ? black : white;
        }

        /// <summary>Takes every role from a theme's palette and metrics in one call.</summary>
        public static NotificationsColors From(UiTheme theme) => From(theme.Palette) with
        {
            CornerRadius = theme.Metrics.CornerRadius,
            Shadow = theme.Metrics.Shadow,
        };

        /// <summary>Takes every role from a palette, so the theme stays the one source.</summary>
        public static NotificationsColors From(UiPalette p) => new()
        {
            Raised = p.RaisedBg,
            Edge = p.Edge,
            Page = p.ContentBg,
            OneHue = p.IsOneHue,
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

        /// <summary>Size of a toast's severity icon, in design units.</summary>
        public const float ToastIconSize = 18f;

        /// <summary>Size of the Close cross's click box, in design units; the cross is drawn inside it.</summary>
        public const float CloseBoxSize = 18f;

        private static IconKind IconOf(NotificationSeverity s) => s switch
        {
            NotificationSeverity.Error => IconKind.Error,
            NotificationSeverity.Warn => IconKind.Warning,
            NotificationSeverity.Success => IconKind.Success,
            _ => IconKind.Info,
        };

        private static Node Toast(ToastView view, NotificationFeed feed, NotificationsColors c, NotificationsStyle s)
        {
            var n = view.Notification;
            var fading = n.Kind == NotificationKind.Fading;
            var parts = new List<Node> { n.Row?.Invoke() ?? DefaultBody(n, c, s, showTime: false, c.TintedFill(n.Severity)) };
            if (!fading)
            {
                parts.Add(Buttons(n, feed, c, s));
            }
            else
            {
                // The countdown sits inside the card's padding, so it stays clear of the corner arcs.
                parts.Add(Builder.Progress((float)view.RemainingFraction, c.Track, c.Of(n.Severity))
                    .RowH(s.CountdownHeight));
            }

            var body = Builder.VStack([.. parts]).WithGap(s.Gap).WStar();
            var icon = Builder.Icon(IconOf(n.Severity), ToastIconSize, c.Of(n.Severity));
            var row = fading
                // Space under the Close cross, so a long title ellipsises before it rather than running under it.
                ? Builder.HStack(icon, body, Builder.Spacer().WFixed(CloseBoxSize))
                : Builder.HStack(icon, body);
            var card = row.WithGap(s.Gap).Pad(s.Padding)
                .Bg(c.TintedFill(n.Severity))
                .Border(c.TintedEdge(n.Severity), 1f, c.CornerRadius)
                .Radius(c.CornerRadius)
                .Shadow(c.ToastShadow)
                .WStar();
            // Every card is a surface (Node.IsSurface): what it covers in the page gets neither the hover, the tooltip, the
            // cursor, a press nor the wheel. A fading toast's region keeps its NotificationHit (the host pauses the
            // countdown under the pointer and clicks it through that hit); a staying card's body takes the press and does
            // nothing with it, while its own buttons, painted over it, act as ever.
            if (!fading)
            {
                return card.AsSurface();
            }

            // The Close cross is its own region painted over the card, so a click on it dismisses and does not reach the card's.
            var close = Builder.HStack(Builder.Icon(IconKind.Close, 12f, c.Text))
                .Pad((CloseBoxSize - 12f) / 2f)
                .CrossCenter()
                .Clickable(new NotificationHit(NotificationPart.Close, n.Id), _ => feed.Dismiss(n.Id), CursorKind.Pointer)
                .WithTooltip("Dismiss")
                .WAuto();
            var closeLayer = Builder.VStack(Builder.HStack(Builder.Spacer().WStar(), close).WStar())
                .Pad(s.Padding)
                // The card's 1-unit edge takes a whole cell on a cell surface, so the layer is bordered alike (invisibly)
                // and the cross sits inside the edge instead of on it.
                .Border(new RGBAColor32(0, 0, 0, 0), 1f, 0f)
                .Stretch();
            return Builder.Overlay(card.Clickable(new NotificationHit(NotificationPart.Toast, n.Id)).AsSurface(), closeLayer).WStar();
        }

        private static Node Buttons(Notification n, NotificationFeed feed, NotificationsColors c, NotificationsStyle s)
        {
            // Order: [Action, filled] [the host's own buttons] [Dismiss]. Dismiss is filled in the severity colour only when
            // it stands alone (no action and no host button); beside any other button it is plain.
            var buttons = new List<Node>();
            var hasOther = false;
            if (n.Action is not null && !string.IsNullOrEmpty(n.ActionLabel))
            {
                buttons.Add(PrimaryButton(n.ActionLabel!, n.Severity, c, s,
                    new NotificationHit(NotificationPart.Action, n.Id), () => feed.Handle(n.Id)));
                hasOther = true;
            }

            var host = n.Buttons?.Invoke();
            if (host is { Count: > 0 })
            {
                buttons.AddRange(host);
                hasOther = true;
            }

            var dismissHit = new NotificationHit(NotificationPart.Dismiss, n.Id);
            buttons.Add(hasOther
                ? PlainButton("Dismiss", c, s, dismissHit, () => feed.Dismiss(n.Id))
                : PrimaryButton("Dismiss", n.Severity, c, s, dismissHit, () => feed.Dismiss(n.Id)));

            // Right-aligned on one row while they fit; past that they wrap onto a second row rather than run past the card. A single
            // row pushed Dismiss past the toast's edge, and past the window, once the host's buttons and Dismiss did not fit (pilot's
            // toasts on a 320-unit card, 2026-11-23). The wrap is sized to its content (its longest row, never more than the card
            // gives it), so the spacer before it keeps a row that fits at the right; every button keeps its whole label.
            return Builder.HStack(Builder.Spacer().WStar(), Builder.WrapH([.. buttons]).WithGap(s.Gap).WithLineGap(s.Gap).WAuto()).WStar();
        }

        /// <summary>Vertical padding of a toast button, in design units; primary and plain share it, so a row of them has one height.</summary>
        public const float ButtonPadY = 4f;

        /// <summary>
        /// A button filled in <paramref name="severity"/>'s colour with <see cref="NotificationsColors.InkOn"/> ink:
        /// the lead button of a row of buttons on a staying card (the action, or Dismiss when it stands alone). A host's own buttons are built by calling this too.
        /// </summary>
        public static Node PrimaryButton(string label, NotificationSeverity severity, NotificationsColors colors,
            NotificationsStyle style, HitResult hit, Action run)
        {
            ArgumentNullException.ThrowIfNull(colors);
            ArgumentNullException.ThrowIfNull(style);
            ArgumentNullException.ThrowIfNull(run);
            return Builder.HStack(Builder.Text(label, style.FontSize, colors.InkOn(severity), TextAlign.Center).WAuto())
                .Pad(style.Padding, ButtonPadY)
                .Bg(colors.Of(severity))
                .Radius(Math.Max(0f, colors.CornerRadius - 2f))
                .Clickable(hit, _ => run(), CursorKind.Pointer)
                .WAuto();
        }

        /// <summary>A button with no fill, in <see cref="NotificationsColors.Text"/>: a button beside another on a staying card (Dismiss beside any other button, and the host's secondary buttons).</summary>
        public static Node PlainButton(string label, NotificationsColors colors, NotificationsStyle style,
            HitResult hit, Action run)
        {
            ArgumentNullException.ThrowIfNull(colors);
            ArgumentNullException.ThrowIfNull(style);
            ArgumentNullException.ThrowIfNull(run);
            return Builder.HStack(Builder.Text(label, style.FontSize, colors.Text, TextAlign.Center).WAuto())
                .Pad(style.Padding, ButtonPadY)
                .Clickable(hit, _ => run(), CursorKind.Pointer)
                .WAuto();
        }

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
                .AsSurface()   // over the page's right: nothing of the page beneath it gets the pointer
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

        private static Node DefaultBody(Notification n, NotificationsColors c, NotificationsStyle s, bool showTime, RGBAColor32? fill = null)
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
                parts.Add(Wrapped(n.Text, s.FontSize, c.Body, s.MaxTextLines));
            }

            if (!string.IsNullOrEmpty(n.Source))
            {
                parts.Add(Builder.Text(n.Source!, s.FontSize, fill is { } f ? c.DimOn(f) : c.Dim).WStar().WithTooltip(n.Source!));
            }

            return Builder.VStack([.. parts]).WStar();
        }

        // A word split where a line may break: after a path separator or punctuation, and before a hump.
        private static List<string> BreakPieces(string word)
        {
            var pieces = new List<string>();
            var start = 0;
            for (var i = 1; i < word.Length; i++)
            {
                var prev = word[i - 1];
                var after = prev is '\\' or '/' or '|' or ',' or ';' or '=' or '&' or '-' or '_' or '.';
                var hump = char.IsLower(prev) && char.IsUpper(word[i]);
                if (after || hump)
                {
                    pieces.Add(word[start..i]);
                    start = i;
                }
            }

            pieces.Add(word[start..]);
            return pieces;
        }

        // Words in a WrapH capped at maxLines: the text wraps, and what does not fit ends in an ellipsis with the whole
        // text as the hover, so nothing is cut silently.
        private static Node Wrapped(string text, float fontSize, RGBAColor32 ink, int maxLines)
        {
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var leaves = new List<Node>(words.Length);
            for (var i = 0; i < words.Length; i++)
            {
                var pieces = BreakPieces(words[i]);
                for (var k = 0; k < pieces.Count; k++)
                {
                    // The trailing space stays on the word's last piece; pieces of one word touch (zero gap).
                    var last = k == pieces.Count - 1;
                    var piece = last && i < words.Length - 1 ? pieces[k] + " " : pieces[k];
                    var leaf = Builder.Text(piece, fontSize, ink).WAuto();
                    leaves.Add(leaf);
                }
            }

            return Builder.WrapH([.. leaves]).WithMaxLines(maxLines, true).WStar().WithTooltip(text);
        }
    }
}
