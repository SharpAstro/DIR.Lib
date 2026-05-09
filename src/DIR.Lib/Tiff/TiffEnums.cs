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
