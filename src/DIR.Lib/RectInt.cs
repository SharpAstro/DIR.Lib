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

    public readonly bool IsContainedWithin(in RectInt other)
        => LowerRight.X <= other.LowerRight.X && LowerRight.Y <= other.LowerRight.Y && UpperLeft.X >= other.UpperLeft.X && UpperLeft.Y >= other.UpperLeft.Y;

    public readonly RectInt Inflate(int inflate)
        => new RectInt((LowerRight.X + inflate, LowerRight.Y + inflate), (UpperLeft.X - inflate, UpperLeft.Y - inflate));

    public bool Contains(int x, int y) => x <= LowerRight.X && y <= LowerRight.Y && x >= UpperLeft.X && y >= UpperLeft.Y;
}
