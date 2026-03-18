namespace DIR.Lib;

public readonly record struct RGBAColor32(byte Red, byte Green, byte Blue, byte Alpha)
{
    public byte Luminance => (byte)Math.Clamp(Math.Round(0.299f * Red + 0.587f * Green + 0.114f * Blue), 0, 0xff);
}
