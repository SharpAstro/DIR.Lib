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
/// Reusable horizontal tab strip: one tab per title, an active highlight + accent, a close button
/// per tab, ellipsis truncation, hover feedback (give it <see cref="Pointer"/>), and drag-to-reorder
/// hit-testing. Backend-agnostic — it draws through its <see cref="Renderer{TSurface}"/> and is told the
/// model (titles + active index) each frame; the host maps the returned <see cref="TabClick"/> /
/// <see cref="SlotAt"/> to its own actions.
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
    /// Lays the strip out and paints it, registering each tab body, each ✕ and the + as it goes.
    /// </summary>
    /// <param name="contentLeft">Left edge of the strip — a host with a sidebar starts the tabs past it.</param>
    /// <param name="viewportW">Right edge. Tabs that do not fit clip off; the + is dropped rather than
    /// drawn under the clip.</param>
    /// <param name="titles">One per tab, in order.</param>
    /// <param name="activeIndex">The tab wearing the accent, or -1 for none (e.g. while the + owns the
    /// window).</param>
    public void Render(float contentLeft, float viewportW, IReadOnlyList<string> titles, int activeIndex)
        => RenderCore(contentLeft, viewportW, new TitleSource(titles), activeIndex);

    /// <summary>
    /// Lays the strip out and paints it from <see cref="TabItem{T}"/>s, so a press comes back as the
    /// VALUE it selects rather than an index the host maps through a switch of its own.
    /// </summary>
    /// <param name="contentLeft">Left edge of the strip — a host with a sidebar starts the tabs past it.</param>
    /// <param name="viewportW">Right edge. Tabs that do not fit clip off; the + is dropped rather than
    /// drawn under the clip.</param>
    /// <param name="items">One per tab, in order.</param>
    /// <param name="activeValue">The item wearing the accent, matched by
    /// <see cref="EqualityComparer{T}.Default"/>. A value no item carries leaves the strip with no active
    /// tab, which is what a host showing something other than a tab (a new-tab page) wants.</param>
    public void Render<T>(float contentLeft, float viewportW, IReadOnlyList<TabItem<T>> items, T activeValue)
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

        RenderCore(contentLeft, viewportW, new ItemSource<T>(items), activeIndex);
    }

    /// <summary>
    /// What the two <c>Render</c> overloads have in common, over whatever supplies the tabs.
    /// </summary>
    /// <remarks>
    /// Generic over a STRUCT source with a constraint rather than taking an <c>IReadOnlyList&lt;TabItem&gt;</c>
    /// both overloads convert into: the older overload takes titles the caller already holds, and converting
    /// them would allocate a list per frame for a strip that is repainted every frame. The constraint lets
    /// the JIT specialise each source, so the indirection costs nothing and neither call site allocates.
    /// </remarks>
    private void RenderCore<TSource>(float contentLeft, float viewportW, TSource source, int activeIndex)
        where TSource : struct, ITabSource
    {
        BeginFrame();
        HoveredIndex = -1;

        var h = (int)Height;

        // Bar background spans the full content width; clip the strip to its bounds.
        var barLeft = (int)contentLeft;
        var barRight = (int)viewportW;
        PushClip(barLeft, 0, barRight - barLeft, h);
        FillRect(barLeft, 0, barRight - barLeft, h, Colors.BarBackground);

        // The pointer's x, but only while it is within the strip's own band — one test here instead of
        // per tab, and null keeps every hover below switched off in one place.
        var hoverX = Pointer is { } p && p.Y >= 0f && p.Y < h ? p.X : (float?)null;

        var x = contentLeft;
        var closeSize = CloseBox;
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

            var textW = MeasureTitle(title);
            var w = Math.Clamp(textW + iconW + Pad * 2 + closeSize, MinTabW, MaxTabW);
            var x0 = x;
            var x1 = x + w;

            // A disabled tab is never hovered: its plate must not light up under a pointer that cannot
            // press it, and the host must not tooltip it as though it were live.
            var hovered = enabled && hoverX is { } hx && hx >= x0 && hx < x1;
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
            FillRect(x0, 0, w, h, lifted ? Colors.ActiveBackground : Colors.InactiveBackground);
            if (active)
            {
                FillRect(x0, 0, w, Border * 2, Colors.ActiveAccent);
            }

            // Right-hand separator between tabs.
            FillRect(x1 - Border, 0, Border, h, Colors.Separator);

            // One ink for everything the tab says, so a disabled tab greys as a whole rather than
            // greying its label beside a fully-lit glyph.
            var ink = !enabled ? Colors.DisabledText
                    : lifted ? Colors.ActiveText
                             : Colors.InactiveText;

            // Glyph, in its reserved box at the tab's leading edge. Drawn through the widget's own
            // DrawText, which splits the run by coverage and sends supplementary-plane codepoints to the
            // emoji face — so a pictograph tab needs nothing of the host but the fonts it already set.
            var labelLeft = (int)(x0 + Pad);
            if (icon is not null)
            {
                DrawText(icon.AsSpan(), FontPath, labelLeft, 0, IconBox, h, Font, ink,
                    TextAlign.Center, TextAlign.Center);
                labelLeft += (int)iconW;
            }

            // Label, truncated to leave room for the close button. Drawn with per-script fallback.
            var labelRight = (int)(x1 - closeSize - Pad * 0.5f);
            var label = FitTitle(title, labelRight - labelLeft);
            DrawText(label.AsSpan(), FontPath, labelLeft, 0, labelRight - labelLeft, h - (int)(2 * DpiScale),
                Font, ink, TextAlign.Near, TextAlign.Center);

            // Close button (×) at the right edge — Latin, always covered by the primary font. A disabled
            // tab draws none: a tab that cannot be selected cannot be dismissed either, and a ✕ that
            // answers on a greyed tab is the one live control on something drawn as inert. The width it
            // would have taken stays reserved, so disabling a tab does not resize it.
            var cx1 = (int)(x1 - Pad * 0.4f);
            var cx0 = (int)(cx1 - closeSize);
            if (enabled)
            {
                // Its own plate under the pointer, because the ✕ is a second target inside the tab and a
                // tab-wide hover says nothing about where its edge is. Separator is the plate: it is the
                // one role guaranteed to read against both the panel and the header surface, so this needs
                // no colour of its own in either theme.
                if (hovered && hoverX >= cx0 && hoverX <= cx1)
                {
                    FillRect(cx0, (h - closeSize) * 0.5f, cx1 - cx0, closeSize, Colors.Separator, closeSize * 0.25f);
                }

                DrawText("×".AsSpan(), FontPath, cx0, 0, cx1 - cx0, h, Font, Colors.CloseMark,
                    TextAlign.Center, TextAlign.Center);
            }

            // A disabled tab registers under its OWN id rather than not registering at all. Both halves
            // matter: every query that means "a tab you can press" (HandleMouseDown, the Pointer cursor)
            // matches TabBarRegions.Tabs and so excludes it for free, with no second copy of the enabled
            // test to keep in step — while the region still being THERE is what keeps SlotAt's walk dense,
            // since a gap would make every tab after a disabled one report the wrong drop slot. It also
            // keeps the strip legible to the debug inspector, which reads this list.
            RegisterClickable(x0, 0, w, h,
                new HitResult.ListItemHit(enabled ? TabBarRegions.Tabs : TabBarRegions.DisabledTabs, i),
                cursor: enabled ? CursorKind.Pointer : null);

            // Then its ✕ over the top: the region list resolves last-registered-wins, so this ordering is
            // what makes the close button a target inside the tab rather than beside it.
            if (enabled)
            {
                RegisterClickable(cx0, 0, cx1 - cx0, h, new HitResult.ListItemHit(TabBarRegions.CloseButtons, i),
                    cursor: CursorKind.Pointer);
            }

            x = x1;

            if (x >= viewportW) break; // ran out of room — remaining tabs clip off (max-resident keeps this rare)
        }

        // The + goes where the tabs stopped, so it reads as the next slot in the strip rather than as a
        // toolbar button parked at the far end. Skipped when the tabs have already filled the width:
        // drawing it past the edge would put a control where the clip hides it.
        if (ShowNewTabButton && x + h <= viewportW)
        {
            var x0 = x;
            var x1 = x + h;   // square, so it matches the strip's own height
            var hovered = NewTabHovered || (hoverX is { } hx && hx >= x0 && hx < x1);
            FillRect(x0, 0, x1 - x0, h, NewTabActive || hovered ? Colors.ActiveBackground : Colors.InactiveBackground);
            if (NewTabActive)
            {
                FillRect(x0, 0, x1 - x0, Border * 2, Colors.ActiveAccent);
            }

            FillRect(x1 - Border, 0, Border, h, Colors.Separator);

            // Two bars rather than a "+" glyph: the mark has to be there on any face the host happens to
            // be using, and geometry stays crisp at 30 px where a typeset plus does not.
            var cx = (x0 + x1) * 0.5f;
            var cy = h * 0.5f;
            var arm = 5f * DpiScale;
            var t = Math.Max(1f, 1.6f * DpiScale);
            var ink = NewTabActive || hovered ? Colors.ActiveText : Colors.InactiveText;
            FillRect(cx - arm, cy - t * 0.5f, arm * 2f, t, ink);
            FillRect(cx - t * 0.5f, cy - arm, t, arm * 2f, ink);

            RegisterClickable(x0, 0, x1 - x0, h, new HitResult.ButtonHit(TabBarRegions.NewTab),
                cursor: CursorKind.Pointer);
        }

        // Bottom edge of the whole bar.
        FillRect(barLeft, h - Border, barRight - barLeft, Border, Colors.Separator);
        PopClip();
    }

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

    /// <summary>Maps an x coordinate to the tab slot a dragged tab should occupy, using the midpoints of
    /// the tab regions the last <see cref="Render"/> registered. Returns -1 if no tabs are laid out.</summary>
    /// <remarks>
    /// A drop target is not a hit — there is no region for the gap BETWEEN two tabs — so this is the one
    /// thing the bar computes from its layout rather than reporting from it. It still reads the registered
    /// rects, which is what stops a drag from reordering against geometry the strip no longer has.
    /// </remarks>
    public int SlotAt(float x)
    {
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
            if (x < region.X + region.Width * 0.5f)
            {
                return slot;
            }
        }

        return slot;
    }
}
