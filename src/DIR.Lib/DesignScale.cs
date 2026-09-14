namespace DIR.Lib;

/// <summary>
/// How many surface units one design unit spans, per axis — the design→surface half of what a measure
/// context does, in a form the non-generic widgets can hold.
///
/// <para>This replaces the bare <c>float dpiScale</c> that used to be threaded by hand through
/// <c>SetExtent</c>, <c>Arm</c>, the tab bar's metrics and the palette's offsets. Each of those kept
/// its own copy of a number the measure context already knew, free to disagree with it — and a widget
/// whose chrome is sized from one scale while its text is measured through another is exactly the
/// drift that is invisible until a display changes DPI.</para>
///
/// <para><b>Two axes, not one, and that is the point.</b> A single scalar silently asserts that a
/// design unit is square. It is on a pixel surface; it is not on a terminal, where one cell is about
/// 8 units across and 16 down. The same assumption is what the post-layout ordering rule warns about
/// from the other side — a quarter turn maps X extents onto Y, which is only coherent when the unit
/// IS square — so the pair keeps the two cases distinguishable instead of letting a caller multiply
/// by "the" scale and be right only by luck.</para>
///
/// <para>A scale that should reflow lives here, applied to design units <i>before</i> layout resolves
/// them. A transform applied to finished pixels is
/// <see cref="Renderer{TSurface}.ContentTransform"/>'s job, and it is deliberately not the same
/// thing.</para>
/// </summary>
/// <param name="X">Surface units spanned by one design unit horizontally.</param>
/// <param name="Y">Surface units spanned by one design unit vertically.</param>
public readonly record struct DesignScale(float X, float Y)
{
    /// <summary>One design unit is one surface unit on both axes — an unscaled pixel surface, and what
    /// a caller with nothing better to say should pass.</summary>
    public static readonly DesignScale One = new(1f, 1f);

    /// <summary>The isotropic scale every pixel-authored tree uses: the same factor on both axes.</summary>
    public DesignScale(float uniform) : this(uniform, uniform) { }

    /// <summary>True when a design unit is square, so a single factor describes the whole mapping.</summary>
    public bool IsUniform => X == Y;

    /// <summary>
    /// The factor along the axis a length runs, for the many metrics that are isotropic by nature — a
    /// stroke width, a slop radius, a minimum thumb length. Uses <see cref="X"/>, and is the same as
    /// <see cref="Y"/> whenever <see cref="IsUniform"/>.
    /// </summary>
    public float ToSurface(float designUnits) => designUnits * X;

    /// <summary>A horizontal length in design units, in surface units.</summary>
    public float ToSurfaceX(float designUnits) => designUnits * X;

    /// <summary>A vertical length in design units, in surface units.</summary>
    public float ToSurfaceY(float designUnits) => designUnits * Y;

    /// <summary>A horizontal length in surface units, back in design units — what a drag offset needs
    /// to be stored in, so it survives the display it was made on.</summary>
    public float ToDesignX(float surfaceUnits) => X <= 0f ? surfaceUnits : surfaceUnits / X;

    /// <summary>A vertical length in surface units, back in design units.</summary>
    public float ToDesignY(float surfaceUnits) => Y <= 0f ? surfaceUnits : surfaceUnits / Y;

    /// <summary>
    /// Guards against the zero and negative scales a host can produce mid-resize (a minimized window
    /// reports a zero-size client area), which would otherwise divide a stored offset to infinity.
    /// </summary>
    public DesignScale OrOne() => X > 0f && Y > 0f ? this : One;
}
