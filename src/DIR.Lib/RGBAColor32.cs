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
    /// Mixes <paramref name="b"/> into <paramref name="a"/>: <paramref name="fraction"/> 0 is exactly <paramref name="a"/>,
    /// 1 is exactly <paramref name="b"/>, in between is the per-channel (alpha included) linear blend. The fraction is
    /// clamped to 0..1.
    /// </summary>
    public static RGBAColor32 Mix(RGBAColor32 a, RGBAColor32 b, float fraction)
    {
        if (fraction <= 0f) return a;
        if (fraction >= 1f) return b;
        return Lerp(a, b, fraction);
    }

    /// <summary>Relative luminance (WCAG 2.x) of the colour's RGB, 0 (black) to 1 (white); alpha is ignored.</summary>
    public float RelativeLuminance
    {
        get
        {
            static float Lin(byte c)
            {
                var s = c / 255f;
                return s <= 0.04045f ? s / 12.92f : MathF.Pow((s + 0.055f) / 1.055f, 2.4f);
            }
            return 0.2126f * Lin(Red) + 0.7152f * Lin(Green) + 0.0722f * Lin(Blue);
        }
    }

    /// <summary>The WCAG contrast ratio between two colours (alpha ignored), 1 to 21.</summary>
    public static float ContrastRatio(RGBAColor32 a, RGBAColor32 b)
    {
        var la = a.RelativeLuminance;
        var lb = b.RelativeLuminance;
        return (MathF.Max(la, lb) + 0.05f) / (MathF.Min(la, lb) + 0.05f);
    }

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

    /// <summary>
    /// The same colour as the vector scene's and the font stack's <see cref="SharpAstro.VectorGraphics.Rgba32"/>
    /// (sRGB, 8 bits a channel, alpha not premultiplied: the two differ in nothing but their names). Implicit both
    /// ways, so a colour of the UI can be handed to what reads or draws a scene as it is, a palette colour as
    /// <c>SvgReadOptions.CurrentColor</c> for one, and a colour that comes out of a scene or a font's palette can be
    /// used as a colour of the UI.
    /// </summary>
    public static implicit operator SharpAstro.VectorGraphics.Rgba32(RGBAColor32 color) => new(color.Red, color.Green, color.Blue, color.Alpha);

    /// <inheritdoc cref="op_Implicit(RGBAColor32)"/>
    public static implicit operator RGBAColor32(SharpAstro.VectorGraphics.Rgba32 color) => new(color.R, color.G, color.B, color.A);
}
