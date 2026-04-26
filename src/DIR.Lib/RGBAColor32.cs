namespace DIR.Lib;

public readonly record struct RGBAColor32(byte Red, byte Green, byte Blue, byte Alpha)
{
    public byte Luminance => (byte)Math.Clamp(Math.Round(0.299f * Red + 0.587f * Green + 0.114f * Blue), 0, 0xff);

    /// <summary>Red channel as a normalised float in 0..1.</summary>
    public float RedF => Red / 255f;

    /// <summary>Green channel as a normalised float in 0..1.</summary>
    public float GreenF => Green / 255f;

    /// <summary>Blue channel as a normalised float in 0..1.</summary>
    public float BlueF => Blue / 255f;

    /// <summary>Alpha channel as a normalised float in 0..1.</summary>
    public float AlphaF => Alpha / 255f;

    /// <summary>
    /// Linearly interpolates between two colors by factor t (0..1).
    /// </summary>
    public static RGBAColor32 Lerp(RGBAColor32 a, RGBAColor32 b, float t) => new(
        (byte)Math.Round(a.Red + (b.Red - a.Red) * t),
        (byte)Math.Round(a.Green + (b.Green - a.Green) * t),
        (byte)Math.Round(a.Blue + (b.Blue - a.Blue) * t),
        (byte)Math.Round(a.Alpha + (b.Alpha - a.Alpha) * t));

    /// <summary>
    /// Creates an <see cref="RGBAColor32"/> from float RGBA components (0..1 each).
    /// </summary>
    public static RGBAColor32 FromFloat(float r, float g, float b, float a) => new(
        (byte)MathF.Min(MathF.FusedMultiplyAdd(r, 255f, 0.5f), 255f),
        (byte)MathF.Min(MathF.FusedMultiplyAdd(g, 255f, 0.5f), 255f),
        (byte)MathF.Min(MathF.FusedMultiplyAdd(b, 255f, 0.5f), 255f),
        (byte)MathF.Min(MathF.FusedMultiplyAdd(a, 255f, 0.5f), 255f));

    /// <summary>
    /// Returns this color with alpha premultiplied by the given mask alpha.
    /// </summary>
    public RGBAColor32 WithAlpha(byte maskAlpha) =>
        new(Red, Green, Blue, (byte)((Alpha * maskAlpha + 127) / 255));
}
