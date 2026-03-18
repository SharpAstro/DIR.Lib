namespace DIR.Lib;

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
