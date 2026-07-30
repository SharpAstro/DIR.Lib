using System;

namespace DIR.Lib;

/// <summary>
/// The pixel-surface implementation of <see cref="Layout.IMeasureContext{T}"/>: text intrinsic size comes from
/// <see cref="Renderer{TSurface}.MeasureText"/>, and design-unit scalars (padding, gaps, fixed/font sizes)
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
    /// Device pixels per design unit of FONT size — the vertical scale, because an em is a height. On the
    /// isotropic path this is exactly the DPI scale, so <c>fontSize * FontScale</c> is the same number the
    /// painter always multiplied by; on the cell-authored path it is the cell height, so <c>fontSize: 1f</c>
    /// means one cell of text on both surfaces.
    /// </summary>
    public float FontScale => scaleY;

    public Layout.Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize)
    {
        if (string.IsNullOrEmpty(fontPath) || text.IsEmpty)
        {
            // No font (e.g. headless) or empty run: width 0, but keep a sensible line height for row sizing.
            return new Layout.Size<float>(0f, fontSize * FontScale);
        }

        var (width, height) = renderer.MeasureText(text, fontPath, fontSize * FontScale);
        return new Layout.Size<float>(width, height);
    }

    /// <summary>
    /// The axis-free mapping, used for genuinely axis-free scalars such as a corner radius. Resolved against
    /// the HORIZONTAL scale, mirroring <c>CellMeasureContext</c>'s column choice, so a scalar round-trips the
    /// two contexts to the same extent. Identical to the per-axis calls whenever the mapping is isotropic.
    /// </summary>
    public float ToSurface(float designUnits) => designUnits * scaleX;

    public float ToSurfaceX(float designUnits) => designUnits * scaleX;

    public float ToSurfaceY(float designUnits) => designUnits * scaleY;
}
