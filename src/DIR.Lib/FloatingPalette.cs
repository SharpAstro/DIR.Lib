using System;
using System.Diagnostics;

namespace DIR.Lib
{
    /// <summary>
    /// One row of a <see cref="FloatingPalette"/>: what it says, the key it teaches, and whether it is
    /// currently on.
    /// </summary>
    /// <param name="Label">Row text.</param>
    /// <param name="Action">
    /// The <see cref="HitResult.ButtonHit"/> action the row binds. The consumer's own identifier: the
    /// palette only needs it to be unique within the panel.
    /// </param>
    /// <param name="IsOn">Whether the thing this row toggles is currently on. Drives the lit background
    /// and the count a collapsed palette shows.</param>
    /// <param name="KeyHint">
    /// The key that toggles the same thing, printed in its own column so the panel doubles as the
    /// legend. Null for a row no key reaches.
    /// </param>
    /// <param name="IsAvailable">
    /// False for a row that exists but cannot act right now. Such a row is DRAWN and dimmed rather than
    /// dropped: a row that comes and goes moves every row under it, and an absent row says nothing
    /// about why the thing is unavailable.
    /// </param>
    public readonly record struct PaletteItem(
        string Label,
        string Action,
        bool IsOn,
        string? KeyHint = null,
        bool IsAvailable = true);

    /// <summary>Colours for a <see cref="FloatingPalette"/>. Every one is pre-fade; the palette applies
    /// the idle fade itself.</summary>
    public readonly record struct PaletteColors(
        RGBAColor32 PanelBg,
        RGBAColor32 GripBg,
        RGBAColor32 TitleInk,
        RGBAColor32 CountInk,
        RGBAColor32 RowOnBg,
        RGBAColor32 RowOffBg,
        RGBAColor32 RowHoverBg,
        RGBAColor32 KeyChipBg,
        RGBAColor32 OnInk,
        RGBAColor32 OffInk,
        RGBAColor32 DisabledInk);

    /// <summary>
    /// Placement and interaction state for one floating palette, owned by the consumer and mutated by
    /// its pointer events. Separate from the drawing so a host can keep several, and so the whole state
    /// machine is testable without a surface.
    /// </summary>
    public sealed class FloatingPaletteState
    {
        /// <summary>
        /// Distance along the pinned edge, in DESIGN units -- <see cref="Layout.Builder.Anchored"/>'s
        /// <c>offsetAlong</c>, which is consumer-owned state a drag updates.
        /// </summary>
        public float OffsetAlong { get; set; }

        /// <summary>Whether the panel is rolled up to its title bar.</summary>
        public bool Collapsed { get; set; }

        /// <summary>
        /// Where the panel was last ARRANGED, in surface units. The consumer writes this after each
        /// arrange (see <see cref="NoteArranged"/>); the palette needs it to know whether the pointer
        /// is over the panel, which is what holds the fade open.
        /// </summary>
        public RectF32 PanelRect { get; private set; }

        /// <summary>True while a grip drag is in flight.</summary>
        public bool IsDragging => _drag is not null;

        private (float PointerY, float Offset)? _drag;
        private long _engagedAt = Stopwatch.GetTimestamp();
        private long _lastGripPressAt;
        private bool _pointerOver;

        /// <summary>
        /// Records where the panel actually landed, and reconciles <see cref="OffsetAlong"/> with it.
        ///
        /// <para><b>The reconciliation is the load-bearing half.</b> <c>Anchored</c> clamps the panel
        /// into its rect, so a stored offset and the drawn position diverge wherever the clamp bites --
        /// near the far edge, a panel is pulled back to fit while the offset still says where the
        /// pointer left it. That is invisible until the panel's HEIGHT changes: collapse it, the clamp
        /// stops binding, and the title bar jumps to the stale offset. Writing the drawn offset back
        /// every frame is what keeps the two from ever disagreeing.</para>
        /// </summary>
        /// <param name="panelRect">The panel's arranged rect, surface units.</param>
        /// <param name="contentTop">Top of the rect it was arranged into, surface units.</param>
        /// <param name="dpiScale">Design-to-surface scale, so the offset goes back in design units.</param>
        public void NoteArranged(RectF32 panelRect, float contentTop, float dpiScale)
        {
            PanelRect = panelRect;
            OffsetAlong = FloatingPalette.DrawnOffset(panelRect.Y, contentTop, dpiScale);
        }

        /// <summary>Tells the palette where the pointer is, so hover can hold the fade open.</summary>
        public void NotePointer(float x, float y)
        {
            _pointerOver = PanelRect.Size.X > 0f && PanelRect.Size.Y > 0f
                && x >= PanelRect.X && x < PanelRect.X + PanelRect.Size.X
                && y >= PanelRect.Y && y < PanelRect.Y + PanelRect.Size.Y;
        }

        /// <summary>Holds the panel fully present, as any deliberate use of it should.</summary>
        public void NoteEngaged() => _engagedAt = Stopwatch.GetTimestamp();

        /// <summary>Whether hover or a live drag is currently holding the fade open.</summary>
        public bool IsEngaged => _pointerOver || IsDragging;

        /// <summary>The alpha factor to draw at right now.</summary>
        public float Fade
        {
            get
            {
                if (IsEngaged)
                {
                    _engagedAt = Stopwatch.GetTimestamp();
                }

                return FloatingPalette.FadeFor(
                    (float)Stopwatch.GetElapsedTime(_engagedAt).TotalSeconds, IsEngaged, Collapsed);
            }
        }

        /// <summary>True while the fade is still moving, so the host knows to keep asking for frames.</summary>
        public bool IsFading => Fade > (Collapsed
            ? FloatingPalette.CollapsedIdleAlpha
            : FloatingPalette.IdleAlpha);

        /// <summary>
        /// A press on the grip: the second of a quick pair COLLAPSES the panel, anything else begins a
        /// drag. Returns true when the press toggled <see cref="Collapsed"/>.
        ///
        /// <para><b>Timed here rather than read off a click count</b>, because a host that dispatches a
        /// widget's clickable regions hands their callbacks the modifiers and nothing else -- no
        /// position, no click count -- so successive presses are the only signal available. The pointer
        /// position comes from <see cref="NotePointer"/> for the same reason.</para>
        /// </summary>
        public bool PressGrip(float pointerY)
        {
            var now = Stopwatch.GetTimestamp();
            var sinceLast = (float)Stopwatch.GetElapsedTime(_lastGripPressAt).TotalSeconds;
            _lastGripPressAt = now;
            _engagedAt = now;

            if (FloatingPalette.IsDoubleClick(sinceLast))
            {
                // The first press of the pair already armed a drag and its release disarmed it; the only
                // thing to undo is the drag THIS press would otherwise start.
                Collapsed = !Collapsed;
                _drag = null;
                return true;
            }

            _drag = (pointerY, OffsetAlong);
            return false;
        }

        /// <summary>
        /// Slides the panel with the pointer. Design units, so the pointer delta is divided by the DPI
        /// scale -- without that the panel runs away from the pointer at exactly the scale factor.
        /// Returns whether anything moved.
        /// <para>
        /// Deliberately unclamped: <c>Anchored</c> clamps the arranged panel, which is the one place
        /// that knows both its measured height and the rect it floats in, and
        /// <see cref="NoteArranged"/> feeds that answer straight back.
        /// </para>
        /// </summary>
        public bool DragTo(float pointerY, float dpiScale)
        {
            if (_drag is not { } drag)
            {
                return false;
            }

            var scale = dpiScale <= 0f ? 1f : dpiScale;
            OffsetAlong = drag.Offset + (pointerY - drag.PointerY) / scale;
            _engagedAt = Stopwatch.GetTimestamp();
            return true;
        }

        /// <summary>Ends a grip drag. Returns whether one was in flight, so a host can tell a release
        /// that belongs to the palette from one that belongs to the content behind it.</summary>
        public bool ReleaseGrip()
        {
            if (_drag is null)
            {
                return false;
            }

            _drag = null;
            _engagedAt = Stopwatch.GetTimestamp();
            return true;
        }
    }

    /// <summary>
    /// A floating, grip-dragged, collapsible palette of toggle rows: the tool-palette shape, as a
    /// <see cref="Layout.Node"/> tree plus the pure rules around it.
    ///
    /// <para>Hoisted out of two independent implementations of the same idea -- a PDF viewer's tool
    /// palette and an astronomy app's sky-map layer panel -- which had already diverged on the parts
    /// that are easy to get wrong: the fade, the clamp reconciliation, and what a collapsed panel is
    /// allowed to say.</para>
    ///
    /// <para>Drawing is the consumer's, through <see cref="Layout"/>: this returns a tree and touches no
    /// surface, so a palette's geometry and its click bindings are testable with a stub measure context
    /// and no GPU.</para>
    /// </summary>
    public static class FloatingPalette
    {
        /// <summary>Untouched for this long, the panel starts to recede. Seconds.</summary>
        public const float IdleDelaySeconds = 2.5f;

        /// <summary>How long the recede takes once it starts. Seconds.</summary>
        public const float FadeSeconds = 0.4f;

        /// <summary>
        /// What an expanded panel fades TO, as a factor on every colour's alpha. Not to nothing: a
        /// reader who has forgotten the panel is there should still see that it is, and which rows are
        /// lit, without moving the pointer to find out.
        /// </summary>
        public const float IdleAlpha = 0.40f;

        /// <summary>
        /// The floor for a COLLAPSED panel, which recedes far less. Rolled up, the title bar IS the
        /// panel, so the expanded floor fades the only thing left of it -- measured as barely legible
        /// against a star field, and worse against any busy content.
        /// </summary>
        public const float CollapsedIdleAlpha = 0.85f;

        /// <summary>Two grip presses closer together than this are a double-click.</summary>
        public const float DoubleClickSeconds = 0.4f;

        /// <summary>Row height in design units.</summary>
        public const float RowHeight = 19f;

        /// <summary>Inset kept between the panel and the edge it floats against, design units.</summary>
        public const float Margin = 10f;

        /// <summary>
        /// The fade factor for a panel last engaged <paramref name="idleSeconds"/> ago. Hover or a live
        /// drag holds it fully present. Pure, so the curve is testable without a clock.
        /// </summary>
        public static float FadeFor(float idleSeconds, bool engaged, bool collapsed = false)
        {
            var floor = collapsed ? CollapsedIdleAlpha : IdleAlpha;
            return engaged || idleSeconds <= IdleDelaySeconds ? 1f
                : idleSeconds >= IdleDelaySeconds + FadeSeconds ? floor
                : 1f - (1f - floor) * ((idleSeconds - IdleDelaySeconds) / FadeSeconds);
        }

        /// <summary>Whether a grip press this soon after the last one is the second of a pair.</summary>
        public static bool IsDoubleClick(float secondsSinceLastPress)
            => secondsSinceLastPress <= DoubleClickSeconds;

        /// <summary>
        /// The offset that reproduces where a panel was ACTUALLY drawn, from its arranged top and the
        /// top of the rect it floats in, in design units. See
        /// <see cref="FloatingPaletteState.NoteArranged"/> for why a consumer owes this every frame.
        /// </summary>
        public static float DrawnOffset(float panelTop, float contentTop, float dpiScale)
            => dpiScale <= 0f ? panelTop - contentTop : (panelTop - contentTop) / dpiScale;

        /// <summary>
        /// Builds the palette, floated against <paramref name="side"/> of whatever rect it is arranged
        /// into. Arrange it against the content area: the clamp is the engine's, so a resize or a
        /// sidebar opening under the panel shoves it back into view rather than stranding it.
        /// </summary>
        /// <param name="state">Placement and fade state; read, not written.</param>
        /// <param name="title">Title shown in the grip.</param>
        /// <param name="items">The rows, in the order they are listed.</param>
        /// <param name="colors">Pre-fade colours.</param>
        /// <param name="fontSize">Row text size in DESIGN units.</param>
        /// <param name="gripAction">The <see cref="HitResult.ButtonHit"/> action the grip binds.</param>
        /// <param name="onItem">Invoked with the action of the row a press landed on.</param>
        /// <param name="onGripPress">
        /// Invoked when the grip is pressed. A press, not a release: a host dispatches a widget's
        /// clickable regions from its mouse-DOWN handler, so this is where a drag can begin at all.
        /// Route it to <see cref="FloatingPaletteState.PressGrip"/>.
        /// </param>
        /// <param name="panelWidth">Panel width, design units.</param>
        /// <param name="side">Edge to pin to.</param>
        public static Layout.Node Build(
            FloatingPaletteState state,
            string title,
            ReadOnlySpan<PaletteItem> items,
            in PaletteColors colors,
            float fontSize,
            string gripAction,
            Action<string> onItem,
            Action onGripPress,
            float panelWidth = 124f,
            Layout.DockSide side = Layout.DockSide.Right)
        {
            ArgumentNullException.ThrowIfNull(state);
            ArgumentNullException.ThrowIfNull(onItem);
            ArgumentNullException.ThrowIfNull(onGripPress);

            var fade = state.Fade;

            // WithAlpha PREMULTIPLIES by the mask it is given, so the mask IS the fade. Passing "this
            // colour's alpha times the fade" compiles, looks plausible, and dims the panel at rest.
            RGBAColor32 Faded(RGBAColor32 c) => c.WithAlpha((byte)Math.Clamp(fade * 255f, 0f, 255f));

            var lit = 0;
            foreach (var item in items)
            {
                if (item.IsAvailable && item.IsOn)
                {
                    lit++;
                }
            }

            var rowCount = state.Collapsed ? 0 : items.Length;
            var children = new Layout.Node[rowCount + 1];

            // Title and count are separate runs so the count can carry its own colour: collapsed, the
            // count is the panel's entire state readout and has to win the row. A rolled-up bar that
            // said only its name would have thrown away the one thing it knows.
            children[0] = Layout.Builder.HStack(
                    Layout.Builder.Text(title, fontSize, Faded(colors.TitleInk),
                            TextAlign.Near, TextAlign.Center)
                        .WStar().HStar(),
                    Layout.Builder.Text($"{lit}/{items.Length}", fontSize, Faded(colors.CountInk),
                            TextAlign.Far, TextAlign.Center)
                        .WFixed(30f).HStar())
                .WithGap(4f)
                .RowH(RowHeight * 0.9f)
                .Bg(Faded(colors.GripBg))
                .Clickable(new HitResult.ButtonHit(gripAction), _ => onGripPress(), CursorKind.Move);

            for (var i = 0; i < rowCount; i++)
            {
                // Copied out so the click lambda captures a value rather than an index into a span it
                // could not close over anyway.
                var item = items[i];
                var ink = Faded(!item.IsAvailable ? colors.DisabledInk
                    : item.IsOn ? colors.OnInk : colors.OffInk);

                var keyNode = item.KeyHint is { } key
                    ? Layout.Builder.Text(key, fontSize, ink, TextAlign.Center, TextAlign.Center)
                        .WFixed(13f).HStar().Bg(Faded(colors.KeyChipBg))
                    : Layout.Builder.Spacer().WFixed(13f).HStar();

                var row = Layout.Builder.HStack(
                        keyNode,
                        Layout.Builder.Text(item.Label, fontSize, ink, TextAlign.Near, TextAlign.Center)
                            .WStar().HStar())
                    .WithGap(5f)
                    .RowH(RowHeight)
                    .Bg(Faded(item.IsOn ? colors.RowOnBg : colors.RowOffBg));

                if (item.IsAvailable)
                {
                    var action = item.Action;
                    row = row.BgHover(Faded(colors.RowHoverBg))
                        .Clickable(new HitResult.ButtonHit(action), _ => onItem(action),
                            CursorKind.Pointer);
                }

                children[i + 1] = row;
            }

            var panel = Layout.Builder.VStack(children)
                .WFixed(panelWidth)
                .Bg(Faded(colors.PanelBg))
                .Pad(5f)
                .WithGap(2f);

            return Layout.Builder.Anchored(panel, side, offsetAlong: state.OffsetAlong, margin: Margin);
        }
    }
}
