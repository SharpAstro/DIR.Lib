using System;

namespace DIR.Lib;

/// <summary>
/// The pixel-surface implementation of <see cref="Layout.IMeasureContext{T}"/>: text intrinsic width comes from
/// <see cref="Renderer{TSurface}.MeasureText"/> and its height is the face's line box (see <see cref="MeasureText"/>),
/// and design-unit scalars (padding, gaps, fixed/font sizes)
/// scale into device pixels. So the arranged rects come back in device pixels; the painter must
/// draw text at <c>fontSize * FontScale</c> to match what was measured — which is why the painter should be
/// handed THIS context (<see cref="PixelWidgetBase{TSurface}"/>'s context-taking layout methods) rather than a
/// scale of its own that has to agree with it by hand.
/// <para>
/// <b>The unit convention belongs to the TREE</b> (see <c>CellMeasureContext</c> in Console.Lib, which
/// states the same rule from the terminal's side). The common case is a pixel-authored tree on a pixel
/// surface — one uniform <paramref name="scaleX"/> = <paramref name="scaleY"/> = DPI scale, the single-scalar
/// constructor. The per-axis constructor exists for a CELL-authored tree (<c>RowH(1)</c> means one terminal
/// row) arranged onto pixels, where one design unit spans a full cell: ~8 pixels across but ~16 down, which
/// one scalar cannot express. <see cref="CellAuthored"/> is that mapping, the exact mirror of
/// <c>CellMeasureContext.PixelAuthored</c> — together they let a tree authored in either convention arrange
/// on either surface.
/// </para>
/// </summary>
/// <param name="renderer">The renderer whose glyph metrics answer <see cref="MeasureText"/>.</param>
/// <param name="fontPath">The font the text will be drawn with, so measure and paint agree on metrics.</param>
/// <param name="scaleX">Device pixels spanned by one design unit horizontally.</param>
/// <param name="scaleY">Device pixels spanned by one design unit vertically.</param>
public sealed class PixelMeasureContext<TSurface>(Renderer<TSurface> renderer, string fontPath, float scaleX, float scaleY)
    : Layout.IMeasureContext<float>
{
    /// <summary>The isotropic mapping: one design unit is <paramref name="dpiScale"/> pixels on both axes —
    /// the convention every pixel-authored tree uses, and the default, so existing callers are unchanged.</summary>
    public PixelMeasureContext(Renderer<TSurface> renderer, string fontPath, float dpiScale = 1f)
        : this(renderer, fontPath, dpiScale, dpiScale)
    {
    }

    /// <summary>
    /// For a tree authored in cell design units (a shared tree that also renders on a terminal), mapped onto
    /// a nominal 8x16 cell by default — pass the terminal's real cell size when it is known. The mirror of
    /// <c>CellMeasureContext.PixelAuthored</c>: a <c>fontSize: 1f</c> text leaf (one cell tall there) is
    /// measured and painted at <paramref name="cellHeight"/> pixels here.
    /// </summary>
    public static PixelMeasureContext<TSurface> CellAuthored(Renderer<TSurface> renderer, string fontPath,
        float cellWidth = 8f, float cellHeight = 16f) => new(renderer, fontPath, cellWidth, cellHeight);

    /// <summary>The font the tree's text is measured against; the painter must draw with the same one.</summary>
    public string FontPath => fontPath;

    /// <summary>
    /// Optional per-codepoint font fallback. When set, a text leaf is measured across its
    /// coverage runs instead of against <see cref="FontPath"/> alone — so a label mixing scripts,
    /// or carrying a symbol the primary face lacks, is sized for the fonts it will actually be
    /// drawn with. The painter reads this same property, which is what stops measure and paint
    /// from disagreeing; set it on the context, not separately on both halves.
    ///
    /// <para>Null (the default) leaves every measurement byte-identical to before.</para>
    /// </summary>
    public FontFallbackResolver? Fallback { get; init; }

    private readonly string? _emojiFontPath;

    /// <summary>
    /// The emoji face, for the callers that draw emoji through a dedicated path rather than by coverage.
    /// <para>
    /// <b>Derived from <see cref="Fallback"/> when one is set</b>, because a resolver built by
    /// <see cref="FontFallbackResolver.FromRoles"/> ALREADY carries the emoji role and already resolves an
    /// emoji codepoint to that face by coverage, exactly as it does any other script. Storing it a second
    /// time would be the same fact in two places, free to disagree -- and the one that disagrees silently
    /// is the one nothing draws from. Setting it explicitly still wins, for a consumer that has an emoji
    /// face but no fallback chain at all.
    /// </para>
    /// </summary>
    public string? EmojiFontPath
    {
        get => _emojiFontPath ?? Fallback?.EmojiFontPath;
        init => _emojiFontPath = value;
    }

    /// <summary>
    /// Device pixels per design unit of FONT size — the vertical scale, because an em is a height. On the
    /// isotropic path this is exactly the DPI scale, so <c>fontSize * FontScale</c> is the same number the
    /// painter always multiplied by; on the cell-authored path it is the cell height, so <c>fontSize: 1f</c>
    /// means one cell of text on both surfaces.
    /// </summary>
    public float FontScale => scaleY;

    /// <summary>
    /// A text leaf's intrinsic size: the run's advance width, and the face's LINE BOX for height
    /// (<see cref="TextBaseline.LineHeight"/> at the scaled size), whatever glyphs the run holds.
    /// <para>
    /// Not the ink. Every renderer's <c>DrawText</c> lays a line out in that box, so a leaf measured to it
    /// and drawn top-aligned sits exactly where a hand-centred row would put it -- which is what lets a
    /// button size itself from its padding alone, with no <c>.RowH(fontSize + gap)</c>. Measuring the ink
    /// made the height depend on which letters were present: "LOG" and "gjpq" at one size came back
    /// different heights, so a row of labels could not agree on how tall it was. The renderer's own
    /// <see cref="Renderer{TSurface}.MeasureText"/> still answers ink; only the layout's question changed.
    /// </para>
    /// </summary>
    public Layout.Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize)
    {
        var px = fontSize * FontScale;
        var lineHeight = TextBaseline.LineHeight(px);

        if (string.IsNullOrEmpty(fontPath) || text.IsEmpty)
        {
            // No font (e.g. headless) or empty run: width 0, but keep a sensible line height for row sizing.
            // The same line box a drawn run gets, so an empty label does not collapse its row.
            return new Layout.Size<float>(0f, lineHeight);
        }

        // Only split when the primary can't draw the whole run — PrimaryCoversAll is
        // allocation-free and true for essentially all chrome, so the common path is unchanged.
        if (Fallback is { } fallback && !fallback.PrimaryCoversAll(text))
        {
            var runs = _runs ??= [];
            fallback.CoverageRuns(text, runs);
            float w = 0f;
            foreach (var (start, length, runFont) in runs)
            {
                w += renderer.MeasureText(text.Slice(start, length), runFont, px).Width;
            }
            return new Layout.Size<float>(w, lineHeight);
        }

        var width = renderer.MeasureText(text, fontPath, px).Width;
        return new Layout.Size<float>(width, lineHeight);
    }

    // Scratch for the run split. A context is per-widget-per-paint and used from the render
    // thread, so one reusable list keeps the fallback path off the allocator too.
    private List<FontFallbackResolver.FontRun>? _runs;

    /// <summary>
    /// The axis-free mapping, used for genuinely axis-free scalars such as a corner radius. Resolved against
    /// the HORIZONTAL scale, mirroring <c>CellMeasureContext</c>'s column choice, so a scalar round-trips the
    /// two contexts to the same extent. Identical to the per-axis calls whenever the mapping is isotropic.
    /// </summary>
    public float ToSurface(float designUnits) => designUnits * scaleX;

    public float ToSurfaceX(float designUnits) => designUnits * scaleX;

    public float ToSurfaceY(float designUnits) => designUnits * scaleY;

    /// <summary>
    /// This context's design→surface mapping on its own, for the pieces that need it but cannot hold a
    /// context: the scroll controller, the gesture recognizer, the tab bar's metrics, the palette's
    /// offsets — all non-generic, none of which can name <typeparamref name="TSurface"/>.
    /// <para>Handing them THIS is the point of it. They used to take a bare <c>float dpiScale</c> and
    /// keep a copy, which is a second source for a number this context already owns — and the copy is
    /// free to disagree the moment a display changes.</para>
    /// </summary>
    public DesignScale Scale => new(scaleX, scaleY);
}
