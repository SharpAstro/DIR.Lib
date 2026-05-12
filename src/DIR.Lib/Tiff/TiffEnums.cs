namespace DIR.Lib.Tiff;

public enum TiffCompression : ushort
{
    Uncompressed = 1,
    Lzw          = 5,
    Jpeg         = 7,    // New-style JPEG (TIFF Technical Note #2)
    Deflate      = 8,    // Adobe Deflate
    ZlibPkzip    = 32946, // PKZIP / zlib (identical bytes to Deflate=8)
}

public enum TiffPhotometric : ushort
{
    MinIsWhite       = 0,
    MinIsBlack       = 1,
    Rgb              = 2,
    Palette          = 3,
    TransparencyMask = 4,
    Cmyk             = 5,
    YCbCr            = 6,
    CieLab           = 8,
}

public enum TiffExtraSamples : ushort
{
    Unspecified      = 0,
    AssociatedAlpha  = 1,  // pre-multiplied
    UnassociatedAlpha = 2, // straight alpha
}

/// <summary>
/// TIFF SampleFormat tag (339) values per TIFF 6.0 + TIFF Technical Note #3.
/// Tells readers how to interpret the raw sample bits — without this tag, the
/// spec default is <see cref="Uint"/> (1), so 32-bit IEEE float pixels written
/// without an explicit SampleFormat will be silently misread as unsigned ints.
/// </summary>
public enum TiffSampleFormat : ushort
{
    Uint      = 1, // unsigned integer (spec default)
    Int       = 2, // two's-complement signed integer
    IeeeFloat = 3, // IEEE 754 floating point
    Undefined = 4, // void / opaque data
}

public enum TiffLayout
{
    Strip,
    Tiled,
}

internal enum TiffPlanarConfig : ushort
{
    Contig   = 1,
    Separate = 2,
}

internal enum TiffResolutionUnit : ushort
{
    None       = 1,
    Inch       = 2,
    Centimeter = 3,
}
