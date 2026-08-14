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
    /// <summary>A tab's body, indexed by its position in the titles list.</summary>
    public const string Tabs = "tabs";

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

    /// <summary>Palette, settable by the host like every other presentation value — a theme can change
    /// while the bar is alive, so this is not init-only. Defaults reproduce the bar's original dark
    /// styling.</summary>
    public TabBarColors Colors { get; set; } = new();

    /// <summary>A click that landed on a tab. <see cref="Close"/> = the × button (else the body).</summary>
    public readonly record struct TabClick(int Index, bool Close);

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

    /// <summary>
    /// Where the pointer is, in the coordinates <see cref="Render"/> is given, or null when it is
    /// outside the window (or over something in front of the bar). Hovering the strip needs no other
    /// call: the bar resolves which tab, and whether the ✕ inside it, while it lays the tabs out.
    /// </summary>
    /// <remarks>
    /// A position rather than a hovered index, because the bar owns the tab widths — see
    /// <see cref="ShowNewTabButton"/> for the same argument about the + button's placement. A host
    /// asked to supply the index instead would have to hit-test against the PREVIOUS frame's
    /// geometry, which lags visibly on the frame a tab opens, closes or is dragged past the pointer.
    /// </remarks>
    public (float X, float Y)? Pointer { get; set; }

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
    {
        BeginFrame();

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
        for (var i = 0; i < titles.Count; i++)
        {
            var title = titles[i];
            var active = i == activeIndex;

            var textW = MeasureTitle(title);
            var w = Math.Clamp(textW + Pad * 2 + closeSize, MinTabW, MaxTabW);
            var x0 = x;
            var x1 = x + w;
            var hovered = hoverX is { } hx && hx >= x0 && hx < x1;

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

            // Label, truncated to leave room for the close button. Drawn with per-script fallback.
            var labelRight = (int)(x1 - closeSize - Pad * 0.5f);
            var labelLeft = (int)(x0 + Pad);
            var label = FitTitle(title, labelRight - labelLeft);
            DrawText(label.AsSpan(), FontPath, labelLeft, 0, labelRight - labelLeft, h - (int)(2 * DpiScale),
                Font, lifted ? Colors.ActiveText : Colors.InactiveText, TextAlign.Near, TextAlign.Center);

            // Close button (×) at the right edge — Latin, always covered by the primary font.
            var cx1 = (int)(x1 - Pad * 0.4f);
            var cx0 = (int)(cx1 - closeSize);
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

            // The tab first, then its ✕ over the top: the region list resolves last-registered-wins, so
            // this ordering is what makes the close button a target inside the tab rather than beside it.
            RegisterClickable(x0, 0, w, h, new HitResult.ListItemHit(TabBarRegions.Tabs, i),
                cursor: CursorKind.Pointer);
            RegisterClickable(cx0, 0, cx1 - cx0, h, new HitResult.ListItemHit(TabBarRegions.CloseButtons, i),
                cursor: CursorKind.Pointer);

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
    /// bar, on the + button, or in empty bar space.</summary>
    public TabClick? HandleMouseDown(float x, float y) => HitTest(x, y) switch
    {
        HitResult.ListItemHit { ListId: TabBarRegions.CloseButtons, Index: var i } => new TabClick(i, Close: true),
        HitResult.ListItemHit { ListId: TabBarRegions.Tabs, Index: var i } => new TabClick(i, Close: false),
        _ => null,
    };

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
            if (region.Result is not HitResult.ListItemHit { ListId: TabBarRegions.Tabs })
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
