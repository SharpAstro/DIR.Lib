using System;
using System.Diagnostics;
using System.Globalization;

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

    /// <summary>
    /// Where a palette sits, in a form a consumer can store and hand back: the edge it is pinned to
    /// (null while it floats free) and its two offsets, in DESIGN units.
    /// </summary>
    /// <remarks>
    /// <para>The round trip lives here rather than in each consumer because the two halves have to agree
    /// on what <see cref="Across"/> means, and that depends on <see cref="Side"/>: pinned, it is zero and
    /// the edge supplies the missing coordinate; floating, the pair IS the position. A consumer writing
    /// its own format gets one of those two cases right and meets the other a release later.</para>
    /// <para>Invariant-culture on purpose: a settings file written where the decimal separator is a comma
    /// has to be readable where it is a point.</para>
    /// </remarks>
    public readonly record struct PalettePlacement(Layout.DockSide? Side, float Along, float Across)
    {
        /// <summary>The placement as one token, for a settings file: <c>side:along:across</c>.</summary>
        public override string ToString()
            => string.Create(CultureInfo.InvariantCulture, $"{Token(Side)}:{Along}:{Across}");

        /// <summary>
        /// Reads back what <see cref="ToString"/> wrote. False leaves the caller on its own default,
        /// which is the right answer for a missing or damaged setting -- note that an unrecognised side
        /// is a REFUSAL rather than "floating", since reading it as a float would strand the panel at
        /// coordinates that meant something else.
        /// </summary>
        public static bool TryParse(string? text, out PalettePlacement placement)
        {
            placement = default;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var parts = text.Split(':');
            if (parts.Length != 3
                || parts[0] is not ("left" or "right" or "top" or "bottom" or "float")
                || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var along)
                || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var across))
            {
                return false;
            }

            Layout.DockSide? side = parts[0] switch
            {
                "left" => Layout.DockSide.Left,
                "right" => Layout.DockSide.Right,
                "top" => Layout.DockSide.Top,
                "bottom" => Layout.DockSide.Bottom,
                _ => null,
            };

            placement = new PalettePlacement(side, along, across);
            return true;
        }

        private static string Token(Layout.DockSide? side) => side switch
        {
            Layout.DockSide.Left => "left",
            Layout.DockSide.Right => "right",
            Layout.DockSide.Top => "top",
            Layout.DockSide.Bottom => "bottom",
            _ => "float",
        };
    }

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

        /// <summary>
        /// Distance ACROSS the pinned edge, design units. Meaningful only while <see cref="Side"/> is
        /// null: pinned, the edge supplies this coordinate and the value is held at zero.
        /// </summary>
        public float OffsetAcross { get; set; }

        /// <summary>
        /// The edge the panel is pinned to, or null while it floats free of all of them.
        ///
        /// <para>State rather than a <see cref="FloatingPalette.Build"/> argument because a drag CHANGES
        /// it: a panel released near an edge takes that edge (see <see cref="SnapOnRelease"/>). A
        /// consumer that does not want docking simply never calls that and leaves this where it was
        /// set.</para>
        /// </summary>
        public Layout.DockSide? Side { get; set; } = Layout.DockSide.Right;

        /// <summary>
        /// Whether a double press on the grip rolls the panel up. False for a palette with nothing worth
        /// collapsing -- a strip of icon buttons IS its own title bar -- where the gesture would only be
        /// a way to lose the thing.
        /// </summary>
        public bool AllowCollapse { get; set; } = true;

        /// <summary>Whether the panel runs across rather than down, which a top- or bottom-pinned one
        /// does. A consumer laying out a strip needs this to choose its stacking axis.</summary>
        public bool IsHorizontal => FloatingPalette.IsHorizontal(Side);

        /// <summary>
        /// The whole placement as one value, for storing and restoring. Setting it moves the panel
        /// outright, which is what a consumer does once at construction.
        /// </summary>
        public PalettePlacement Placement
        {
            get => new(Side, OffsetAlong, OffsetAcross);
            set => (Side, OffsetAlong, OffsetAcross) = (value.Side, value.Along, value.Across);
        }

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

        private (float PointerX, float PointerY, float Along, float Across)? _drag;
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

        /// <summary>
        /// The same reconciliation for a palette that can dock to ANY edge, or float free of all of
        /// them: it needs the whole rect the panel floats in, because which coordinate is "along"
        /// depends on <see cref="Side"/> and a free-floating panel has both.
        /// </summary>
        /// <remarks>
        /// Prefer this over the <c>contentTop</c> overload wherever the side can change. Written the
        /// clever way first in a consumer -- one ternary on "is it horizontal" -- which is right for
        /// the three pinned states and wrong for floating, where along is X and across is Y, so a
        /// free-floating panel took its Y for both and could only travel the diagonal.
        /// </remarks>
        public void NoteArranged(RectF32 panelRect, RectF32 contentRect, float dpiScale)
        {
            PanelRect = panelRect;
            (OffsetAlong, OffsetAcross) =
                FloatingPalette.DrawnOffsets(panelRect, contentRect, Side, dpiScale);
        }

        /// <summary>
        /// Takes the edge the panel was released near, if any, and returns whether
        /// <see cref="Side"/> changed. Call it from the pointer-up that ends a grip drag.
        /// </summary>
        /// <remarks>
        /// <para><b>Measure the panel where it now IS, not where it was last drawn.</b> A consumer that
        /// tested its own last-arranged rect here settled the panel where the previous frame had put it
        /// rather than where the button came up — felt as the thing resisting, and then not landing
        /// under the cursor once the resistance is overcome. Pass the rect implied by the live drag.</para>
        /// <para>Left and right are tested before top so a panel released into a corner pins to the
        /// side, which is where a tall strip belongs.</para>
        /// </remarks>
        /// <param name="panelRect">Where the panel is now, surface units.</param>
        /// <param name="contentRect">The rect it floats in, surface units.</param>
        /// <param name="snap">How close to an edge still counts, surface units.</param>
        /// <param name="margin">The inset a pinned panel keeps, surface units.</param>
        /// <param name="dpiScale">Design-to-surface scale, so the offsets land in design units.</param>
        public bool SnapOnRelease(RectF32 panelRect, RectF32 contentRect, float snap, float margin,
            float dpiScale = 1f)
        {
            var side = FloatingPalette.SnapSideFor(panelRect, contentRect, snap, margin);
            var changed = side != Side;
            Side = side;

            // ALWAYS re-derived, never carried across, because the offsets mean different axes on
            // either side of the change: floating, along is X; pinned to a side, along is Y. Carrying
            // the number over hands a panel released at the left edge its old X as a distance DOWN
            // that edge, so it docks correctly and then jumps to the top -- which reads as the drop
            // having been ignored. The panel's own rect is the only thing that means the same in both.
            (OffsetAlong, OffsetAcross) =
                FloatingPalette.DrawnOffsets(panelRect, contentRect, side, dpiScale);
            return changed;
        }

        /// <summary>
        /// Lifts the panel off whatever edge it was on, keeping it exactly where it is drawn — what a
        /// grip press does before a drag, so the pointer picks the panel up rather than teleporting it.
        /// </summary>
        /// <remarks>
        /// The mirror of <see cref="SnapOnRelease"/>, and it exists for the same reason: a pinned
        /// panel's <see cref="OffsetAlong"/> is measured down its edge, and free of every edge it is
        /// measured across. Re-deriving both from the rect is the only conversion that holds.
        /// </remarks>
        public void Unpin(RectF32 panelRect, RectF32 contentRect, float dpiScale = 1f)
        {
            Side = null;
            (OffsetAlong, OffsetAcross) =
                FloatingPalette.DrawnOffsets(panelRect, contentRect, null, dpiScale);
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
        public bool PressGrip(float pointerY) => PressGrip(0f, pointerY);

        /// <summary>
        /// A press on the grip, for a palette that can move in both axes. The second of a quick pair
        /// COLLAPSES the panel where <see cref="AllowCollapse"/> permits it; anything else begins a drag.
        /// Returns true when the press toggled <see cref="Collapsed"/>.
        /// </summary>
        public bool PressGrip(float pointerX, float pointerY)
        {
            var now = Stopwatch.GetTimestamp();
            var sinceLast = (float)Stopwatch.GetElapsedTime(_lastGripPressAt).TotalSeconds;
            _lastGripPressAt = now;
            _engagedAt = now;

            if (AllowCollapse && FloatingPalette.IsDoubleClick(sinceLast))
            {
                // The first press of the pair already armed a drag and its release disarmed it; the only
                // thing to undo is the drag THIS press would otherwise start.
                Collapsed = !Collapsed;
                _drag = null;
                return true;
            }

            _drag = (pointerX, pointerY, OffsetAlong, OffsetAcross);
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
            => DragTo(_drag?.PointerX ?? 0f, pointerY, dpiScale);

        /// <summary>
        /// Slides the panel with the pointer in both axes. While <see cref="Side"/> is set only the
        /// along-edge component moves — the edge owns the other one — so a pinned panel slides along
        /// its edge and a free one follows the pointer outright.
        /// </summary>
        /// <remarks>
        /// Design units, so the pointer delta is divided by the DPI scale; without that the panel runs
        /// away from the pointer at exactly the scale factor. Deliberately unclamped, for the reason on
        /// the single-axis overload: <c>Anchored</c> clamps the arranged panel and
        /// <see cref="NoteArranged(RectF32, RectF32, float)"/> feeds that answer straight back.
        /// </remarks>
        public bool DragTo(float pointerX, float pointerY, float dpiScale)
        {
            if (_drag is not { } drag)
            {
                return false;
            }

            var scale = dpiScale <= 0f ? 1f : dpiScale;
            var dx = (pointerX - drag.PointerX) / scale;
            var dy = (pointerY - drag.PointerY) / scale;

            if (Side is { } side)
            {
                OffsetAlong = drag.Along + (FloatingPalette.IsHorizontal(side) ? dx : dy);
                OffsetAcross = 0f;
            }
            else
            {
                // Free of every edge, along is X and across is Y — the pair is simply the position.
                OffsetAlong = drag.Along + dx;
                OffsetAcross = drag.Across + dy;
            }

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

        /// <summary>Released this close to an edge, a panel takes that edge. Design units.</summary>
        public const float SnapDistance = 26f;

        /// <summary>Whether a panel pinned to <paramref name="side"/> runs across rather than down.</summary>
        /// <remarks>
        /// A strip is stacked along the edge it sits on: vertical against a side, horizontal under the
        /// top. Floating free it runs the long way, down, which is what it was before it was dragged off
        /// an edge and the shape a column of rows wants.
        /// </remarks>
        public static bool IsHorizontal(Layout.DockSide? side)
            => side is Layout.DockSide.Top or Layout.DockSide.Bottom;

        /// <summary>
        /// Both offsets that reproduce where a panel was ACTUALLY drawn, in design units. Which
        /// coordinate is which depends on the edge: pinned, one of them is the edge's own and is
        /// reported as zero; floating, the pair is the position.
        /// </summary>
        public static (float Along, float Across) DrawnOffsets(
            RectF32 panelRect, RectF32 contentRect, Layout.DockSide? side, float dpiScale)
        {
            var scale = dpiScale <= 0f ? 1f : dpiScale;
            var x = (panelRect.X - contentRect.X) / scale;
            var y = (panelRect.Y - contentRect.Y) / scale;
            return side switch
            {
                Layout.DockSide.Left or Layout.DockSide.Right => (y, 0f),
                Layout.DockSide.Top or Layout.DockSide.Bottom => (x, 0f),
                _ => (x, y),
            };
        }

        /// <summary>
        /// The edge a panel at <paramref name="panelRect"/> should take, or null to float free. Pure, so
        /// the snap is testable without a pointer.
        /// </summary>
        /// <remarks>
        /// Left and right are tested ahead of top, so a panel released into a corner pins to the side.
        /// Both distances are measured against the panel's own edges rather than its origin, which is
        /// what makes the right-hand test symmetric with the left-hand one for a panel of any width.
        /// </remarks>
        public static Layout.DockSide? SnapSideFor(
            RectF32 panelRect, RectF32 contentRect, float snap, float margin)
        {
            var reach = snap + margin;
            if (panelRect.X - contentRect.X <= reach)
            {
                return Layout.DockSide.Left;
            }

            if (contentRect.X + contentRect.Width - (panelRect.X + panelRect.Width) <= reach)
            {
                return Layout.DockSide.Right;
            }

            if (panelRect.Y - contentRect.Y <= reach)
            {
                return Layout.DockSide.Top;
            }

            return null;
        }

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
        /// <param name="side">Edge to pin to, or null to take the state's own <see cref="FloatingPaletteState.Side"/> — which is what a palette the reader can re-dock needs.</param>
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
            Layout.DockSide? side = null)
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

            // A side named here wins, for a consumer that pins its panel and never lets it move; passing
            // nothing takes the state's own, which is what a dockable palette needs since a drag CHANGES
            // which edge it is on.
            var pinned = side ?? state.Side;
            return Layout.Builder.Anchored(panel, pinned, offsetAlong: state.OffsetAlong,
                offsetAcross: pinned is null ? state.OffsetAcross : 0f, margin: Margin);
        }
    }
}
