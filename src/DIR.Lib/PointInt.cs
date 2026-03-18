namespace DIR.Lib;

public readonly record struct PointInt(int X, int Y)
{
    public static readonly PointInt Origin = new PointInt(0, 0);

    public static implicit operator PointInt((int X, int Y) value) => new PointInt(value.X, value.Y);

    public static implicit operator PointInt((uint X, uint Y) value) => value is { X: <= int.MaxValue, Y: <= int.MaxValue }
        ? new PointInt((int)value.X, (int)value.Y)
        : throw new ArgumentOutOfRangeException(nameof(value), "Point is out of range of int");
}
