namespace DIR.Lib;

/// <summary>
/// Integer pixel coordinate. Used for discrete screen positions in
/// <see cref="RectInt"/> and <see cref="Renderer{TSurface}"/> draw calls.
/// Implicit conversions from <c>(int, int)</c> and <c>(uint, uint)</c> tuples.
/// </summary>
public readonly record struct PointInt(int X, int Y)
{
    public static readonly PointInt Origin = new PointInt(0, 0);

    public static implicit operator PointInt((int X, int Y) value) => new PointInt(value.X, value.Y);

    public static implicit operator PointInt((uint X, uint Y) value) => value is { X: <= int.MaxValue, Y: <= int.MaxValue }
        ? new PointInt((int)value.X, (int)value.Y)
        : throw new ArgumentOutOfRangeException(nameof(value), "Point is out of range of int");
}
