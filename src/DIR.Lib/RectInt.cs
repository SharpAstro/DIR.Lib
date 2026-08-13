namespace DIR.Lib;

/// <summary>
/// Integer pixel rectangle defined by its lower-right (exclusive) and upper-left (inclusive)
/// corners. Used by <see cref="Renderer{TSurface}.FillRectangle"/>,
/// <see cref="Renderer{TSurface}.DrawRectangle"/>, and <see cref="Renderer{TSurface}.DrawText"/>.
/// <para>
/// Convention: <see cref="UpperLeft"/> is the inclusive top-left corner,
/// <see cref="LowerRight"/> is the exclusive bottom-right corner.
/// A rect from (10, 20) to (50, 40) covers pixels x=10..49, y=20..39.
/// </para>
/// </summary>
public readonly record struct RectInt(PointInt LowerRight, PointInt UpperLeft)
{
    public long Width => Math.Abs(LowerRight.X - UpperLeft.X);

    public long Height => Math.Abs(LowerRight.Y - UpperLeft.Y);

    public readonly bool OverlapsWith(in RectInt other)
        => other.LowerRight.X >= UpperLeft.X && other.LowerRight.Y >= UpperLeft.Y && other.UpperLeft.X <= LowerRight.X && other.UpperLeft.Y <= LowerRight.Y;

    public readonly RectInt Union(RectInt other)
        => new RectInt(
            (Math.Max(other.LowerRight.X, LowerRight.X), Math.Max(other.LowerRight.Y, LowerRight.Y)),
            (Math.Min(other.UpperLeft.X, UpperLeft.X), Math.Min(other.UpperLeft.Y, UpperLeft.Y))
        );

    /// <summary>
    /// This rect with its corners ordered — <see cref="UpperLeft"/> minimal, <see cref="LowerRight"/>
    /// maximal. <see cref="Width"/> and <see cref="Height"/> are absolute differences, so an inverted
    /// rect reports a positive size and reads as valid; anything deriving a REGION from a caller's
    /// rect wants this first.
    /// </summary>
    public readonly RectInt Normalized()
        => new((Math.Max(UpperLeft.X, LowerRight.X), Math.Max(UpperLeft.Y, LowerRight.Y)),
               (Math.Min(UpperLeft.X, LowerRight.X), Math.Min(UpperLeft.Y, LowerRight.Y)));

    /// <summary>
    /// The overlap of two rects, or a zero-area rect at the near corner when they are disjoint.
    /// Assumes both are <see cref="Normalized"/>.
    /// </summary>
    /// <remarks>
    /// Collapses rather than inverting when there is no overlap, for the reason <see cref="Normalized"/>
    /// gives: an inverted result would report a positive width, so a clip nested outside its parent
    /// would WIDEN the region instead of emptying it.
    /// </remarks>
    public readonly RectInt Intersect(in RectInt other)
    {
        var x0 = Math.Max(UpperLeft.X, other.UpperLeft.X);
        var y0 = Math.Max(UpperLeft.Y, other.UpperLeft.Y);
        var x1 = Math.Min(LowerRight.X, other.LowerRight.X);
        var y1 = Math.Min(LowerRight.Y, other.LowerRight.Y);
        return new RectInt((Math.Max(x0, x1), Math.Max(y0, y1)), (x0, y0));
    }

    public readonly bool IsContainedWithin(in RectInt other)
        => LowerRight.X <= other.LowerRight.X && LowerRight.Y <= other.LowerRight.Y && UpperLeft.X >= other.UpperLeft.X && UpperLeft.Y >= other.UpperLeft.Y;

    public readonly RectInt Inflate(int inflate)
        => new RectInt((LowerRight.X + inflate, LowerRight.Y + inflate), (UpperLeft.X - inflate, UpperLeft.Y - inflate));

    public bool Contains(int x, int y) => x <= LowerRight.X && y <= LowerRight.Y && x >= UpperLeft.X && y >= UpperLeft.Y;
}
