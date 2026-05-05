

namespace DIR.Lib.MathLayout;

/// <summary>
/// A rectangular box with a baseline, in the TeX sense:
/// <list type="bullet">
///   <item><c>Width</c>: total horizontal extent in pixels.</item>
///   <item><c>Height</c>: pixels from the box's top edge down to the baseline
///     (i.e. the "ascent" plus everything that sits above the baseline).</item>
///   <item><c>Depth</c>: pixels from the baseline down to the bottom edge
///     (i.e. the "descent" — what hangs below for letters like 'g', or
///     what a denominator drops below the fraction bar).</item>
/// </list>
/// The total visual height is <c>Height + Depth</c>. Boxes always paint
/// themselves relative to a (penX, baselineY) the parent provides; sizing is
/// computed eagerly so parents can lay out children before any rasterization.
/// </summary>
public abstract class Box
{
    /// <summary>Horizontal extent, pixels.</summary>
    public abstract float Width { get; }

    /// <summary>Pixels above the baseline.</summary>
    public abstract float Height { get; }

    /// <summary>Pixels below the baseline.</summary>
    public abstract float Depth { get; }

    /// <summary>Total visual height (= Height + Depth).</summary>
    public float TotalHeight => Height + Depth;

    /// <summary>
    /// Paint this box into <paramref name="renderer"/> with the box's left
    /// edge at <paramref name="penX"/> and the baseline at
    /// <paramref name="baselineY"/>. The box is allowed to occupy the
    /// rectangle [penX, penX+Width] × [baselineY-Height, baselineY+Depth].
    /// </summary>
    public abstract void Draw(RgbaImageRenderer renderer, float penX, float baselineY, BoxStyle style);
}

/// <summary>
/// Rendering parameters threaded through the box layout. Kept as a record so
/// callers can produce variants (smaller font for scripts, for example)
/// without mutating shared state.
/// </summary>
public sealed record BoxStyle(string FontPath, float FontSize, RGBAColor32 Foreground)
{
    public BoxStyle(string fontPath, float fontSize)
        : this(fontPath, fontSize, new RGBAColor32(255, 255, 255, 255))
    { }

    /// <summary>Smaller-em-size variant used for super/subscripts.</summary>
    public BoxStyle Smaller(float scale = 0.7f) => this with { FontSize = FontSize * scale };

    /// <summary>Stroke thickness in pixels for fraction bars, root vinculums, etc.</summary>
    public float RuleThickness => MathF.Max(1f, FontSize / 18f);

    /// <summary>The "ex height" — used for vertical positioning of operators.</summary>
    public float ExHeight => FontSize * 0.5f;
}
