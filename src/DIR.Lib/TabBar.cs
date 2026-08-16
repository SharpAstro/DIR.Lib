namespace DIR.Lib;

/// <summary>
/// The region ids a <see cref="TabBar{TSurface}"/> registers, named once so a host dispatching through
/// <see cref="PixelWidgetBase{TSurface}.HitTest"/> — and the debug inspector reading the region list —
/// can recognise them without repeating a literal.
/// </summary>
/// <remarks>
/// Non-generic, so a consumer writes <c>TabBarRegions.Tabs</c> rather than
/// <c>TabBar&lt;SomeSurface&gt;.Tabs</c> for a value that has nothing to do with the surface.
/// </remarks>
public static class TabBarRegions
{
    /// <summary>A selectable tab's body, indexed by its position in the titles list.</summary>
    public const string Tabs = "tabs";

    /// <summary>
    /// The body of a tab that cannot be selected (<see cref="TabItem{T}.IsEnabled"/> false), at the same
    /// index. A separate id so that everything asking for a pressable tab matches <see cref="Tabs"/> and
    /// skips these without a second test, while the region is still present for the position walk
    /// <see cref="TabBar{TSurface}.SlotAt"/> does.
    /// </summary>
    public const string DisabledTabs = "tabs:disabled";

    /// <summary>The ✕ inside a tab, at the same index. Registered after the body, so it wins the hit.</summary>
    public const string CloseButtons = "tabs:close";

    /// <summary>The + after the last tab (see <see cref="TabBar{TSurface}.ShowNewTabButton"/>).</summary>
    public const string NewTab = "tabs:new";
}

/// <summary>
/// Reusable tab strip: one tab per item, an active highlight + accent, a close button per tab,
/// ellipsis truncation, hover feedback (give it <see cref="Pointer"/>), and drag-to-reorder
/// hit-testing. Backend-agnostic — it draws through its <see cref="Renderer{TSurface}"/> and is told the
/// model (the tabs + which one is active) each frame; the host maps the returned <see cref="TabClick"/> /
/// <see cref="SlotAt"/> to its own actions.
///
/// <para>It attaches to any edge (<see cref="Side"/>) and sizes tabs either to their content or to a
/// square cell (<see cref="Sizing"/>), so one widget is a document strip along the top and a nav rail
/// down the side. The defaults — <see cref="TabStripSide.Top"/> and <see cref="TabSizing.Content"/> —
/// are the layout it has always drawn.</para>
///
/// <para>Titles render through the widget's <see cref="PixelWidgetBase{TSurface}.FontFallback"/>, so
/// non-Latin file names lay out per-script rather than as <c>.notdef</c> boxes.</para>
/// </summary>
/// <remarks>
/// A <see cref="PixelWidgetBase{TSurface}"/> since 8.0, which is what lets a host hand it the window's
/// context (<see cref="PixelWidgetBase{TSurface}.ShareUiContext"/>) instead of pushing a font, a fallback
/// chain and a scale into it one at a time — three channels for values the window already owns. The
/// tabs, their ✕ and the + are registered as they are painted, so a click resolves against the strip
/// that is on screen: the hit rects are the drawn rects, and on a frame the host does not draw the bar
/// they stop answering altogether rather than reporting the last layout they happened to hold.
/// </remarks>
public sealed class TabBar<TSurface>(Renderer<TSurface> renderer) : PixelWidgetBase<TSurface>(renderer)
{
    private const float BaseHeight = 30f;
    private const float BaseFont = 13f;
    private const float BasePad = 10f;       // text inset from the tab's left edge
    private const float BaseCloseBox = 16f;  // clickable size of the ✕ glyph
    private const float BaseMinTabW = 92f;
    private const float BaseMaxTabW = 220f;
    private const float BaseIconBox = 18f;   // width reserved for a TabItem's glyph, when it has one

    /// <summary>Pixel height of the bar — the host reserves this much at the top of the content area.</summary>
    public float Height => BaseHeight * DpiScale;

    /// <summary>
    /// A tab's text size, inset and border thickness, already scaled — for a host that has to DRAW a tab
    /// somewhere this bar does not.
    /// </summary>
    /// <remarks>
    /// <para>Public for one real case: a torn-out tab carried as its own small window has to paint itself
    /// as a tab, and it is not this bar that paints it. Without these it copies the numbers, which is what
    /// a downstream consumer did — two literals and a comment naming the constants they came from — so a
    /// change to the bar's type size silently stopped matching the thing pretending to be one of its
    /// tabs.</para>
    /// <para>Exposed SCALED, like <see cref="Height"/>, rather than as the base constants: a copier
    /// otherwise has to multiply by a scale of its own, and nothing makes that the same number as
    /// <see cref="PixelWidgetBase{TSurface}.DpiScale"/>. Asking the bar removes the second source.</para>
    /// </remarks>
    public float Font => BaseFont * DpiScale;

    /// <inheritdoc cref="Font"/>
    public float Pad => BasePad * DpiScale;

    /// <inheritdoc cref="Font"/>
    public int Border => Math.Max(1, (int)DpiScale);

    private float CloseBox => BaseCloseBox * DpiScale;
    private float MinTabW => BaseMinTabW * DpiScale;
    private float MaxTabW => BaseMaxTabW * DpiScale;
    private float IconBox => BaseIconBox * DpiScale;

    /// <summary>Palette, settable by the host like every other presentation value — a theme can change
    /// while the bar is alive, so this is not init-only. Defaults reproduce the bar's original dark
    /// styling.</summary>
    public TabBarColors Colors { get; set; } = new();

    /// <summary>A click that landed on a tab. <see cref="Close"/> = the × button (else the body).</summary>
    public readonly record struct TabClick(int Index, bool Close);

    /// <summary>
    /// Index of the tab under <see cref="PixelWidgetBase{TSurface}.Pointer"/> as of the last render, or -1
    /// for none (including when the pointer is over a disabled tab). Resolved while the tabs are laid out,
    /// so it costs the host no hit test of its own.
    /// </summary>
    /// <remarks>
    /// This is how a host draws a tooltip for <see cref="TabItem{T}.Tooltip"/>. The BAR does not draw one,
    /// deliberately: a tooltip is painted outside the strip, over whatever content is adjacent to it, and a
    /// widget that clips to its own bounds — which this one does — cannot put it there. Declaring an overlay
    /// was the alternative and it would move the decision about z-order and placement into a widget that
    /// cannot see what it would cover.
    /// </remarks>
    public int HoveredIndex { get; private set; } = -1;

    /// <summary>
    /// Draw a "+" immediately after the last tab, the way a terminal or a browser does. What it opens is
    /// the host's business: <see cref="HitNewTabButton"/> reports the click and nothing else happens here.
    /// </summary>
    public bool ShowNewTabButton { get; set; }

    /// <summary>
    /// True while whatever the + opens is what the window is showing. It then wears the accent an active
    /// tab wears — a host that puts a real page behind the + (a new-tab page) needs the strip to say so,
    /// or nothing in the bar reads as selected while the marked tab is not the one on screen.
    /// </summary>
    public bool NewTabActive { get; set; }

    /// <summary>Whether the pointer is over the +, set by the host, which is what owns the mouse
    /// position. Affects only its plate, so leaving it false just means no hover feedback.
    /// <para>Superseded by <see cref="Pointer"/>, which covers the tabs as well; the two OR together,
    /// so a host that already sets this keeps working.</para></summary>
    public bool NewTabHovered { get; set; }

    // The bar's own Pointer is now PixelWidgetBase.Pointer: this was the first widget to need one, and
    // the argument it was declared with — a position rather than a hovered index, because the widget owns
    // the geometry and a host would have to hit-test last frame's — is the general one. Hovering the
    // strip still needs no other call: the bar resolves which tab, and whether the ✕ inside it, while it
    // lays the tabs out. Same type and semantics, so a host that sets it reads unchanged.

    /// <summary>
    /// Which edge the strip is attached to. <see cref="TabStripSide.Top"/> (the default) is the layout
    /// the bar has always drawn; orientation, the accent edge and the rule against the content all
    /// derive from this. See <see cref="TabStripSide"/>.
    /// </summary>
    public TabStripSide Side { get; set; } = TabStripSide.Top;

    /// <summary>
    /// How a tab is sized along the axis tabs advance on. <see cref="TabSizing.Content"/> (the default)
    /// is what the bar has always done; <see cref="TabSizing.Uniform"/> is a nav rail's square cell.
    /// </summary>
    public TabSizing Sizing { get; set; } = TabSizing.Content;

    /// <summary>
    /// Whether tabs carry a ✕. Default true, which is what the strip has always drawn. False draws none
    /// and, because the box is no longer reserved, makes every tab narrower by it — a strip whose tabs
    /// cannot be closed should not hold a gap where the control would have been.
    /// </summary>
    /// <remarks>
    /// Positive logic, like <see cref="CanReorderTabs"/> and unlike <see cref="ShowNewTabButton"/>: a
    /// property that has to be read as "not not closable" is one more negation than a call site should
    /// have to carry. <see cref="ShowNewTabButton"/> keeps its name because renaming a shipped property
    /// costs consumers more than the inconsistency does.
    /// </remarks>
    public bool CanCloseTabs { get; set; } = true;

    /// <summary>
    /// Whether a drag may reorder the strip. Default true. False makes <see cref="SlotAt"/> report -1
    /// for every position, which is the whole mechanism: the BAR never reorders anything, it only
    /// nominates the slot a host would drop into, so declining to nominate one is how it says no.
    /// </summary>
    public bool CanReorderTabs { get; set; } = true;

    /// <summary>True while the strip runs down an edge rather than across one.</summary>
    private bool Vertical => Side is TabStripSide.Left or TabStripSide.Right;

    /// <summary>
    /// True when the accent belongs at the LOW end of the cross axis (the top of a Top strip, the left
    /// of a Left one) — i.e. on the outer edge, away from the content the strip heads. The bar's own
    /// rule against that content goes on the opposite edge, which is why one flag places both.
    /// </summary>
    private bool OuterAtCrossStart => Side is TabStripSide.Top or TabStripSide.Left;

    /// <summary>
    /// Lays the strip out and paints it, registering each tab body, each ✕ and the + as it goes.
    /// </summary>
    /// <param name="contentStart">Where the tabs begin along the axis they advance on — a host with a
    /// sidebar starts them past it. The strip's x for a horizontal <see cref="Side"/>, its y for a
    /// vertical one.</param>
    /// <param name="viewportEnd">Where they stop. Tabs that do not fit clip off; the + is dropped
    /// rather than drawn under the clip.</param>
    /// <param name="titles">One per tab, in order.</param>
    /// <param name="activeIndex">The tab wearing the accent, or -1 for none (e.g. while the + owns the
    /// window).</param>
    /// <remarks>
    /// Places the strip at 0 on its cross axis, so this overload suits <see cref="TabStripSide.Top"/>
    /// and <see cref="TabStripSide.Left"/>. A <see cref="TabStripSide.Bottom"/> or
    /// <see cref="TabStripSide.Right"/> strip has to be told where the far edge is, which is a viewport
    /// dimension the bar does not know — use the <see cref="Render(RectF32, IReadOnlyList{string}, int)"/>
    /// overload for those.
    /// </remarks>
    public void Render(float contentStart, float viewportEnd, IReadOnlyList<string> titles, int activeIndex)
        => Render(DefaultBounds(contentStart, viewportEnd), titles, activeIndex);

    /// <inheritdoc cref="Render(float, float, IReadOnlyList{string}, int)"/>
    /// <param name="bounds">The whole strip's rectangle. Its thickness across the flow axis is the
    /// strip's, so a nav rail states its width here rather than inheriting <see cref="Height"/>.</param>
    /// <param name="titles">One per tab, in order.</param>
    /// <param name="activeIndex">The tab wearing the accent, or -1 for none.</param>
    public void Render(RectF32 bounds, IReadOnlyList<string> titles, int activeIndex)
        => RenderCore(bounds, new TitleSource(titles), activeIndex);

    /// <summary>
    /// Lays the strip out and paints it from <see cref="TabItem{T}"/>s, so a press comes back as the
    /// VALUE it selects rather than an index the host maps through a switch of its own.
    /// </summary>
    /// <param name="contentStart">Where the tabs begin along the axis they advance on.</param>
    /// <param name="viewportEnd">Where they stop. Tabs that do not fit clip off.</param>
    /// <param name="items">One per tab, in order.</param>
    /// <param name="activeValue">The item wearing the accent, matched by
    /// <see cref="EqualityComparer{T}.Default"/>. A value no item carries leaves the strip with no active
    /// tab, which is what a host showing something other than a tab (a new-tab page) wants.</param>
    /// <inheritdoc cref="Render(float, float, IReadOnlyList{string}, int)" path="/remarks"/>
    public void Render<T>(float contentStart, float viewportEnd, IReadOnlyList<TabItem<T>> items, T activeValue)
        => Render(DefaultBounds(contentStart, viewportEnd), items, activeValue);

    /// <inheritdoc cref="Render{T}(float, float, IReadOnlyList{TabItem{T}}, T)"/>
    /// <param name="bounds">The whole strip's rectangle.</param>
    /// <param name="items">One per tab, in order.</param>
    /// <param name="activeValue">The item wearing the accent.</param>
    public void Render<T>(RectF32 bounds, IReadOnlyList<TabItem<T>> items, T activeValue)
    {
        var comparer = EqualityComparer<T>.Default;
        var activeIndex = -1;
        for (var i = 0; i < items.Count; i++)
        {
            if (comparer.Equals(items[i].Value, activeValue))
            {
                activeIndex = i;
                break;
            }
        }

        RenderCore(bounds, new ItemSource<T>(items), activeIndex);
    }

    /// <summary>The strip laid along its side at cross-axis 0, <see cref="Height"/> thick.</summary>
    private RectF32 DefaultBounds(float contentStart, float viewportEnd)
        => Vertical
            ? new RectF32(0f, contentStart, Height, viewportEnd - contentStart)
            : new RectF32(contentStart, 0f, viewportEnd - contentStart, Height);

    /// <summary>
    /// What the two <c>Render</c> overloads have in common, over whatever supplies the tabs.
    /// </summary>
    /// <remarks>
    /// Generic over a STRUCT source with a constraint rather than taking an <c>IReadOnlyList&lt;TabItem&gt;</c>
    /// both overloads convert into: the older overload takes titles the caller already holds, and converting
    /// them would allocate a list per frame for a strip that is repainted every frame. The constraint lets
    /// the JIT specialise each source, so the indirection costs nothing and neither call site allocates.
    /// </remarks>
    private void RenderCore<TSource>(RectF32 bounds, TSource source, int activeIndex)
        where TSource : struct, ITabSource
    {
        BeginFrame();
        HoveredIndex = -1;

        // Everything below is expressed on a FLOW axis (the one tabs advance along) and a CROSS axis
        // (the strip's thickness), so one body serves all four sides. Only TabRect maps the pair back
        // to the screen, which is what keeps the side from having to be re-tested per drawn piece.
        var vertical = Vertical;
        var thickness = vertical ? bounds.Width : bounds.Height;
        var flowStart = vertical ? bounds.Y : bounds.X;
        var flowEnd = vertical ? bounds.Bottom : bounds.Right;
        var crossStart = vertical ? bounds.X : bounds.Y;

        PushClip(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        FillRect(bounds.X, bounds.Y, bounds.Width, bounds.Height, Colors.BarBackground);

        // The pointer's FLOW coordinate, but only while its cross coordinate is within the strip's own
        // band — one test here instead of per tab, and null keeps every hover below switched off in one
        // place.
        float? hoverFlow = null;
        if (Pointer is { } p)
        {
            var pointerCross = vertical ? p.X : p.Y;
            if (pointerCross >= crossStart && pointerCross < crossStart + thickness)
            {
                hoverFlow = vertical ? p.Y : p.X;
            }
        }

        var flow = flowStart;
        var uniform = Sizing == TabSizing.Uniform;

        // Zero when the strip cannot close tabs, so the box is not reserved either — the width feeds
        // the extent below, which is what makes a non-closable tab narrower rather than gapped. A
        // uniform cell has no room for one whatever the flag says.
        var closeSize = CanCloseTabs && !uniform ? CloseBox : 0f;
        var trailingInset = closeSize > 0f ? closeSize + Pad * 0.5f : Pad;
        for (var i = 0; i < source.Count; i++)
        {
            var title = source.Label(i);
            var icon = source.Icon(i);
            var enabled = source.Enabled(i);
            var active = i == activeIndex;

            // An icon reserves a fixed box plus a half-pad gap, rather than being measured: a
            // pictograph's advance varies wildly between faces (and a colour emoji's is not the text
            // font's at all), so measuring it would make tab width depend on which fallback happened to
            // resolve. A fixed box also keeps the labels of adjacent tabs aligned with each other.
            var iconW = icon is null ? 0f : IconBox + Pad * 0.5f;

            // A uniform tab is a square cell of the strip's own thickness. That is not a preference on a
            // vertical strip: sizing by content there would set a tab's HEIGHT from the WIDTH of its
            // label, and on an icon-only rail from a label that is never drawn.
            var extent = uniform
                ? thickness
                : Math.Clamp(MeasureTitle(title) + iconW + Pad * 2 + closeSize, MinTabW, MaxTabW);

            var f0 = flow;
            var f1 = flow + extent;
            var rect = TabRect(f0, extent, crossStart, thickness, vertical);

            // A disabled tab is never hovered: its plate must not light up under a pointer that cannot
            // press it, and the host must not tooltip it as though it were live.
            var hovered = enabled && hoverFlow is { } hf && hf >= f0 && hf < f1;
            if (hovered)
            {
                HoveredIndex = i;
            }

            // Tab background + the accent strip / separators that distinguish active from idle. A
            // hovered idle tab takes the ACTIVE plate rather than a tone of its own: it previews what
            // clicking gives you, it is what the + already does, and the palette names no hover
            // surface — inventing one by blending would paint a colour the theme never chose. The
            // accent strip stays exclusive to the active tab, which is what keeps the two apart.
            var lifted = active || hovered;
            var plate = active ? Colors.ActiveBackground
                      : hovered ? Colors.HoverBackground ?? Colors.ActiveBackground
                                : Colors.InactiveBackground;
            FillRect(rect.X, rect.Y, rect.Width, rect.Height, plate);
            if (active)
            {
                var accent = OuterEdge(rect, Border * 2, vertical);
                FillRect(accent.X, accent.Y, accent.Width, accent.Height, Colors.ActiveAccent);
            }

            // Separator along the tab's TRAILING flow edge — its right on a horizontal strip, its
            // bottom on a vertical one.
            var sep = TrailingEdge(rect, Border, vertical);
            FillRect(sep.X, sep.Y, sep.Width, sep.Height, Colors.Separator);

            // One ink for everything the tab says, so a disabled tab greys as a whole rather than
            // greying its label beside a fully-lit glyph.
            var ink = !enabled ? Colors.DisabledText
                    : lifted ? Colors.ActiveText
                             : Colors.InactiveText;

            // Content is laid out across the tab's own rect, which is why it needs no side test: a
            // vertical strip stacks tabs but each tab still reads left-to-right. That is the "upright
            // content" a vertical strip defaults to — rotated text is a renderer capability, not a flag.
            var closeRight = (int)(rect.Right - Pad * 0.4f);
            var closeLeft = (int)(closeRight - closeSize);
            var showClose = enabled && closeSize > 0f;

            if (uniform)
            {
                // No room beside a centred mark for a label or a ✕. With no icon the label takes the
                // centre instead, so a uniform strip is never blank.
                var content = icon ?? FitTitle(title, rect.Width - Pad);
                DrawText(content.AsSpan(), FontPath, rect.X, rect.Y, rect.Width, rect.Height, Font, ink,
                    TextAlign.Center, TextAlign.Center);
            }
            else
            {
                // Glyph, in its reserved box at the tab's leading edge. Drawn through the widget's own
                // DrawText, which splits the run by coverage and sends supplementary-plane codepoints to
                // the emoji face — so a pictograph tab needs nothing of the host but the fonts it set.
                var labelLeft = (int)(rect.X + Pad);
                if (icon is not null)
                {
                    DrawText(icon.AsSpan(), FontPath, labelLeft, rect.Y, IconBox, rect.Height, Font, ink,
                        TextAlign.Center, TextAlign.Center);
                    labelLeft += (int)iconW;
                }

                // Label, truncated to leave room for the close button. Drawn with per-script fallback.
                var labelRight = (int)(rect.Right - trailingInset);
                var label = FitTitle(title, labelRight - labelLeft);
                DrawText(label.AsSpan(), FontPath, labelLeft, rect.Y, labelRight - labelLeft,
                    rect.Height - (int)(2 * DpiScale), Font, ink, TextAlign.Near, TextAlign.Center);
            }

            // Close button (×) — Latin, always covered by the primary font. A disabled tab draws none:
            // a tab that cannot be selected cannot be dismissed either, and a ✕ that answers on a greyed
            // tab is the one live control on something drawn as inert. The width it would have taken
            // stays reserved, so disabling a tab does not resize it.
            if (showClose)
            {
                // Its own plate under the pointer, because the ✕ is a second target inside the tab and a
                // tab-wide hover says nothing about where its edge is. Separator is the plate: it is the
                // one role guaranteed to read against both the panel and the header surface, so this needs
                // no colour of its own in either theme.
                var pointerOnClose = hovered && Pointer is { } q
                    && q.X >= closeLeft && q.X <= closeRight
                    && q.Y >= rect.Y && q.Y <= rect.Bottom;
                if (pointerOnClose)
                {
                    FillRect(closeLeft, rect.Y + (rect.Height - closeSize) * 0.5f, closeRight - closeLeft,
                        closeSize, Colors.Separator, closeSize * 0.25f);
                }

                DrawText("×".AsSpan(), FontPath, closeLeft, rect.Y, closeRight - closeLeft, rect.Height,
                    Font, Colors.CloseMark, TextAlign.Center, TextAlign.Center);
            }

            // A disabled tab registers under its OWN id rather than not registering at all. Both halves
            // matter: every query that means "a tab you can press" (HandleMouseDown, the Pointer cursor)
            // matches TabBarRegions.Tabs and so excludes it for free, with no second copy of the enabled
            // test to keep in step — while the region still being THERE is what keeps SlotAt's walk dense,
            // since a gap would make every tab after a disabled one report the wrong drop slot. It also
            // keeps the strip legible to the debug inspector, which reads this list.
            RegisterClickable(rect.X, rect.Y, rect.Width, rect.Height,
                new HitResult.ListItemHit(enabled ? TabBarRegions.Tabs : TabBarRegions.DisabledTabs, i),
                cursor: enabled ? CursorKind.Pointer : null);

            // Then its ✕ over the top: the region list resolves last-registered-wins, so this ordering is
            // what makes the close button a target inside the tab rather than beside it.
            if (showClose)
            {
                RegisterClickable(closeLeft, rect.Y, closeRight - closeLeft, rect.Height,
                    new HitResult.ListItemHit(TabBarRegions.CloseButtons, i), cursor: CursorKind.Pointer);
            }

            flow = f1;

            if (flow >= flowEnd) break; // out of room — the rest clip off (max-resident keeps this rare)
        }

        // The + goes where the tabs stopped, so it reads as the next slot in the strip rather than as a
        // toolbar button parked at the far end. Skipped when the tabs have already filled the strip:
        // drawing it past the edge would put a control where the clip hides it.
        if (ShowNewTabButton && flow + thickness <= flowEnd)
        {
            var rect = TabRect(flow, thickness, crossStart, thickness, vertical);   // square
            var hovered = NewTabHovered
                || (hoverFlow is { } hf && hf >= flow && hf < flow + thickness);
            var plate = NewTabActive ? Colors.ActiveBackground
                      : hovered ? Colors.HoverBackground ?? Colors.ActiveBackground
                                : Colors.InactiveBackground;
            FillRect(rect.X, rect.Y, rect.Width, rect.Height, plate);
            if (NewTabActive)
            {
                var accent = OuterEdge(rect, Border * 2, vertical);
                FillRect(accent.X, accent.Y, accent.Width, accent.Height, Colors.ActiveAccent);
            }

            var sep = TrailingEdge(rect, Border, vertical);
            FillRect(sep.X, sep.Y, sep.Width, sep.Height, Colors.Separator);

            // Two bars rather than a "+" glyph: the mark has to be there on any face the host happens to
            // be using, and geometry stays crisp at 30 px where a typeset plus does not.
            var cx = rect.X + rect.Width * 0.5f;
            var cy = rect.Y + rect.Height * 0.5f;
            var arm = 5f * DpiScale;
            var t = Math.Max(1f, 1.6f * DpiScale);
            var ink = NewTabActive || hovered ? Colors.ActiveText : Colors.InactiveText;
            FillRect(cx - arm, cy - t * 0.5f, arm * 2f, t, ink);
            FillRect(cx - t * 0.5f, cy - arm, t, arm * 2f, ink);

            RegisterClickable(rect.X, rect.Y, rect.Width, rect.Height,
                new HitResult.ButtonHit(TabBarRegions.NewTab), cursor: CursorKind.Pointer);
        }

        // The bar's rule against the content it heads — the edge OPPOSITE the accent, so one flag
        // places both and they can never end up on the same side.
        var barEdge = InnerEdge(bounds, Border, vertical);
        FillRect(barEdge.X, barEdge.Y, barEdge.Width, barEdge.Height, Colors.Separator);
        PopClip();
    }

    /// <summary>
    /// The screen rect of a tab occupying <paramref name="flowLen"/> from <paramref name="flow"/> along
    /// the flow axis, spanning the strip's thickness across it. The one place the flow/cross pair is
    /// mapped back to x/y, which is what lets everything above be written once for all four sides.
    /// </summary>
    private static RectF32 TabRect(float flow, float flowLen, float cross, float crossLen, bool vertical)
        => vertical
            ? new RectF32(cross, flow, crossLen, flowLen)
            : new RectF32(flow, cross, flowLen, crossLen);

    /// <summary>
    /// A band <paramref name="t"/> thick on <paramref name="rect"/>'s OUTER cross edge — the top of a
    /// Top strip, the left of a Left one. Where the active accent goes.
    /// </summary>
    private RectF32 OuterEdge(RectF32 rect, float t, bool vertical)
        => vertical
            ? new RectF32(OuterAtCrossStart ? rect.X : rect.Right - t, rect.Y, t, rect.Height)
            : new RectF32(rect.X, OuterAtCrossStart ? rect.Y : rect.Bottom - t, rect.Width, t);

    /// <summary>A band <paramref name="t"/> thick on <paramref name="rect"/>'s INNER cross edge, facing
    /// the content. The complement of <see cref="OuterEdge"/>.</summary>
    private RectF32 InnerEdge(RectF32 rect, float t, bool vertical)
        => vertical
            ? new RectF32(OuterAtCrossStart ? rect.Right - t : rect.X, rect.Y, t, rect.Height)
            : new RectF32(rect.X, OuterAtCrossStart ? rect.Bottom - t : rect.Y, rect.Width, t);

    /// <summary>A band <paramref name="t"/> thick on <paramref name="rect"/>'s trailing FLOW edge — its
    /// right on a horizontal strip, its bottom on a vertical one. Where tabs are ruled apart.</summary>
    private static RectF32 TrailingEdge(RectF32 rect, float t, bool vertical)
        => vertical
            ? new RectF32(rect.X, rect.Bottom - t, rect.Width, t)
            : new RectF32(rect.Right - t, rect.Y, t, rect.Height);

    /// <summary>
    /// What <see cref="RenderCore{TSource}"/> needs of whatever is supplying the tabs. Private, and
    /// implemented only by the two structs below: it exists to let one painter serve both public
    /// overloads, not to be a extension point.
    /// </summary>
    private interface ITabSource
    {
        int Count { get; }

        string Label(int index);

        string? Icon(int index);

        bool Enabled(int index);
    }

    /// <summary>The original model: titles, all selectable, none with a glyph.</summary>
    private readonly struct TitleSource(IReadOnlyList<string> titles) : ITabSource
    {
        public int Count => titles.Count;

        public string Label(int index) => titles[index];

        public string? Icon(int index) => null;

        public bool Enabled(int index) => true;
    }

    /// <summary>The item model, where a tab knows its own glyph and whether it can be selected.</summary>
    private readonly struct ItemSource<T>(IReadOnlyList<TabItem<T>> items) : ITabSource
    {
        public int Count => items.Count;

        public string Label(int index) => items[index].Label;

        public string? Icon(int index) => items[index].Icon;

        public bool Enabled(int index) => items[index].IsEnabled;
    }

    /// <summary>
    /// A title's drawn width, through whatever fallback the window set — the same split
    /// <see cref="PixelWidgetBase{TSurface}.DrawText"/> will draw it with, so a tab is sized for the runs
    /// that land in it. Zero on an unresolved font, which lays every tab out at
    /// <see cref="MinTabW"/> rather than throwing (the widget's documented empty-font contract).
    /// </summary>
    private float MeasureTitle(string title)
    {
        if (string.IsNullOrEmpty(FontPath))
        {
            return 0f;
        }

        return FontFallback is { } fallback
            ? fallback.Measure(Renderer, title, Font).Width
            : Renderer.MeasureText(title.AsSpan(), FontPath, Font).Width;
    }

    /// <summary>Truncates a title to <paramref name="maxWidth"/> with a trailing ellipsis, measured the
    /// same way <see cref="MeasureTitle"/> measures.</summary>
    private string FitTitle(string title, float maxWidth)
        => TextFit.ForWidth(Renderer, title, FontPath, FontFallback, Font, maxWidth, TextTrim.End).Text;

    /// <summary>
    /// True if the click landed on the + (see <see cref="ShowNewTabButton"/>). Ask this BEFORE
    /// <see cref="HandleMouseDown"/> — that one reports tabs only and returns null here, so a host that
    /// forgets this call silently swallows the click instead of misrouting it.
    /// </summary>
    public bool HitNewTabButton(float x, float y)
        => HitTest(x, y) is HitResult.ButtonHit { Action: TabBarRegions.NewTab };

    /// <summary>Maps a click to a tab (and whether the ✕ was hit). Null if the click is below the
    /// bar, on the + button, on a disabled tab, or in empty bar space.</summary>
    public TabClick? HandleMouseDown(float x, float y) => HitTest(x, y) switch
    {
        HitResult.ListItemHit { ListId: TabBarRegions.CloseButtons, Index: var i } => new TabClick(i, Close: true),
        HitResult.ListItemHit { ListId: TabBarRegions.Tabs, Index: var i } => new TabClick(i, Close: false),
        _ => null,
    };

    /// <summary>
    /// Maps a click to the ITEM it selects, so the host acts on a value instead of mapping an index back
    /// to meaning. Null on the same misses as <see cref="HandleMouseDown(float, float)"/>.
    /// </summary>
    /// <param name="items">The list that was rendered — the same one, in the same order.</param>
    /// <remarks>
    /// The items come back in rather than being remembered from <see cref="Render{T}"/> because the bar is
    /// generic over its SURFACE, not over the item type: holding the last list would mean storing it as
    /// <c>object</c> and casting on the way out, which turns a caller passing the wrong list from a
    /// compile error into a runtime one. A host builds this list per frame anyway.
    /// <para>
    /// An index the list no longer covers reports null rather than throwing: the strip can outlive its
    /// model by a frame if the host closes a tab between painting and dispatching.
    /// </para>
    /// </remarks>
    public TabClick<T>? HandleMouseDown<T>(float x, float y, IReadOnlyList<TabItem<T>> items)
    {
        if (HandleMouseDown(x, y) is not { } click || click.Index >= items.Count)
        {
            return null;
        }

        return new TabClick<T>(click.Index, items[click.Index].Value, click.Close);
    }

    /// <summary>Maps a coordinate on the FLOW axis — x on a horizontal strip, y on a vertical one — to
    /// the tab slot a dragged tab should occupy, using the midpoints of the tab regions the last
    /// <c>Render</c> registered. Returns -1 if no tabs are laid out, or if
    /// <see cref="CanReorderTabs"/> is false.</summary>
    /// <remarks>
    /// A drop target is not a hit — there is no region for the gap BETWEEN two tabs — so this is the one
    /// thing the bar computes from its layout rather than reporting from it. It still reads the registered
    /// rects, which is what stops a drag from reordering against geometry the strip no longer has.
    /// </remarks>
    public int SlotAt(float flow)
    {
        if (!CanReorderTabs)
        {
            return -1;
        }

        var vertical = Vertical;
        var slot = -1;
        foreach (var region in RegisteredRegions)
        {
            // Disabled tabs count too: they occupy a position in the strip, so a drop past one lands
            // where the pointer is rather than one slot short of it.
            if (region.Result is not HitResult.ListItemHit { ListId: TabBarRegions.Tabs or TabBarRegions.DisabledTabs })
            {
                continue;
            }

            slot++;   // registration order is tab order
            var midpoint = vertical
                ? region.Y + region.Height * 0.5f
                : region.X + region.Width * 0.5f;
            if (flow < midpoint)
            {
                return slot;
            }
        }

        return slot;
    }
}
