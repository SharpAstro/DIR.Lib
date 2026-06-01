namespace DIR.Lib;

/// <summary>
/// Reusable horizontal tab strip: one tab per title, an active highlight + accent, a close button
/// per tab, ellipsis truncation, and drag-to-reorder hit-testing. Backend-agnostic — it draws via
/// <see cref="Renderer{TSurface}"/> and is told the model (titles + active index) each frame; the
/// host maps the returned <see cref="TabClick"/> / <see cref="SlotAt"/> to its own actions.
///
/// <para>Titles render through a <see cref="FontFallbackResolver"/>, so non-Latin file names lay
/// out per-script rather than as <c>.notdef</c> boxes.</para>
/// </summary>
public sealed class TabBar
{
    private const float BaseHeight = 30f;
    private const float BaseFont = 13f;
    private const float BasePad = 10f;       // text inset from the tab's left edge
    private const float BaseCloseBox = 16f;  // clickable size of the ✕ glyph
    private const float BaseMinTabW = 92f;
    private const float BaseMaxTabW = 220f;

    /// <summary>HiDPI factor (1.5 = 150% display), set by the host before <see cref="Render"/>.</summary>
    public float Scale { get; set; } = 1f;

    /// <summary>Pixel height of the bar — the host reserves this much at the top of the content area.</summary>
    public float Height => BaseHeight * Scale;

    private float Font => BaseFont * Scale;
    private float Pad => BasePad * Scale;
    private float CloseBox => BaseCloseBox * Scale;
    private float MinTabW => BaseMinTabW * Scale;
    private float MaxTabW => BaseMaxTabW * Scale;
    private int Border => Math.Max(1, (int)Scale);

    private static readonly RGBAColor32 BarBg = new(0x14, 0x14, 0x1c, 0xff);
    private static readonly RGBAColor32 ActiveBg = new(0x2c, 0x2c, 0x3c, 0xff);
    private static readonly RGBAColor32 InactiveBg = new(0x1c, 0x1c, 0x26, 0xff);
    private static readonly RGBAColor32 Separator = new(0x3a, 0x3a, 0x48, 0xff);
    private static readonly RGBAColor32 ActiveAccent = new(0x44, 0x88, 0xff, 0xff);
    private static readonly RGBAColor32 ActiveText = new(0xf0, 0xf0, 0xf0, 0xff);
    private static readonly RGBAColor32 InactiveText = new(0x9a, 0x9a, 0xa6, 0xff);
    private static readonly RGBAColor32 CloseColor = new(0xc0, 0xc0, 0xc8, 0xff);

    /// <summary>A click that landed on a tab. <see cref="Close"/> = the × button (else the body).</summary>
    public readonly record struct TabClick(int Index, bool Close);

    private readonly string _fontPath;
    // Per-script font fallback so a file name in another script renders run-by-run rather than as boxes.
    private readonly FontFallbackResolver _fallback;

    // Per-tab body + close-button bounds, cached each Render for hit-testing.
    private readonly List<(float X0, float X1, float CloseX0, float CloseX1)> _rects = new();
    private float _barBottom;

    public TabBar(string fontPath, FontFallbackResolver fallback)
    {
        _fontPath = fontPath;
        _fallback = fallback;
    }

    public void Render<TSurface>(Renderer<TSurface> renderer, float contentLeft, float viewportW,
        IReadOnlyList<string> titles, int activeIndex)
    {
        _rects.Clear();
        var h = (int)Height;
        _barBottom = h;

        // Bar background spans the full content width; clip the strip to its bounds.
        var barLeft = (int)contentLeft;
        var barRight = (int)viewportW;
        renderer.PushClip(new RectInt((barRight, h), (barLeft, 0)));
        renderer.FillRectangle(new RectInt((barRight, h), (barLeft, 0)), BarBg);

        var x = contentLeft;
        var closeSize = CloseBox;
        for (var i = 0; i < titles.Count; i++)
        {
            var title = titles[i];
            var active = i == activeIndex;

            var textW = _fallback.Measure(renderer, title, Font).Width;
            var w = Math.Clamp(textW + Pad * 2 + closeSize, MinTabW, MaxTabW);
            var x0 = x;
            var x1 = x + w;

            // Tab background + the accent strip / separators that distinguish active from idle.
            var bg = active ? ActiveBg : InactiveBg;
            renderer.FillRectangle(new RectInt(((int)x1, h), ((int)x0, 0)), bg);
            if (active)
                renderer.FillRectangle(new RectInt(((int)x1, Border * 2), ((int)x0, 0)), ActiveAccent);
            // Right-hand separator between tabs.
            renderer.FillRectangle(new RectInt(((int)x1, h), ((int)x1 - Border, 0)), Separator);

            // Label, truncated to leave room for the close button. Drawn with per-script fallback.
            var labelRight = (int)(x1 - closeSize - Pad * 0.5f);
            var labelLeft = (int)(x0 + Pad);
            var label = _fallback.FitEllipsis(renderer, title, Font, labelRight - labelLeft);
            _fallback.Draw(renderer, label, Font, active ? ActiveText : InactiveText,
                new RectInt((labelRight, h - (int)(2 * Scale)), (labelLeft, 0)),
                TextAlign.Near, TextAlign.Center);

            // Close button (×) at the right edge — Latin, always covered by the primary font.
            var cx1 = (int)(x1 - Pad * 0.4f);
            var cx0 = (int)(cx1 - closeSize);
            renderer.DrawText("×".AsSpan(), _fontPath, Font, CloseColor,
                new RectInt((cx1, h), (cx0, 0)), TextAlign.Center, TextAlign.Center);

            _rects.Add((x0, x1, cx0, cx1));
            x = x1;

            if (x >= viewportW) break; // ran out of room — remaining tabs clip off (max-resident keeps this rare)
        }

        // Bottom edge of the whole bar.
        renderer.FillRectangle(new RectInt((barRight, h), (barLeft, h - Border)), Separator);
        renderer.PopClip();
    }

    /// <summary>Maps a click to a tab (and whether the ✕ was hit). Null if the click is below the
    /// bar or in empty bar space.</summary>
    public TabClick? HandleMouseDown(float x, float y)
    {
        if (y >= _barBottom) return null;
        for (var i = 0; i < _rects.Count; i++)
        {
            var r = _rects[i];
            if (x < r.X0 || x >= r.X1) continue;
            var onClose = x >= r.CloseX0 && x <= r.CloseX1;
            return new TabClick(i, onClose);
        }
        return null;
    }

    /// <summary>Maps an x coordinate to the tab slot a dragged tab should occupy, using the tab
    /// midpoints cached by the last <see cref="Render"/>. Returns -1 if no tabs are laid out.</summary>
    public int SlotAt(float x)
    {
        if (_rects.Count == 0) return -1;
        for (var i = 0; i < _rects.Count; i++)
        {
            var r = _rects[i];
            if (x < (r.X0 + r.X1) * 0.5f) return i;
        }
        return _rects.Count - 1;
    }
}
