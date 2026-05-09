namespace DIR.Lib.Tiff;

/// <summary>
/// Per-page options for <see cref="TiffWriter.AddPageAsync"/>.
/// </summary>
public sealed record TiffPageOptions
{
    public TiffLayout Layout { get; init; } = TiffLayout.Strip;
    public TiffCompression Compression { get; init; } = TiffCompression.Deflate;
    public int SamplesPerPixel { get; init; } = 3;
    public int BitsPerSample { get; init; } = 8;
    public TiffPhotometric Photometric { get; init; } = TiffPhotometric.Rgb;
    public TiffExtraSamples? ExtraSamples { get; init; }
    public double DpiX { get; init; } = 96.0;
    public double DpiY { get; init; } = 96.0;
    public string? Artist { get; init; }
    public string? Software { get; init; }

    /// <summary>Tile dimensions (must be multiples of 16 per TIFF spec). Only used when Layout=Tiled.</summary>
    public int TileWidth { get; init; } = 256;
    /// <summary>Tile dimensions (must be multiples of 16 per TIFF spec). Only used when Layout=Tiled.</summary>
    public int TileHeight { get; init; } = 256;

    /// <summary>Rows per strip. 0 = entire image as one strip. Only used when Layout=Strip.</summary>
    public int RowsPerStrip { get; init; } = 0;

    /// <summary>Raw ICC profile bytes (tag 34675). Null = no profile embedded.</summary>
    public byte[]? IccProfile { get; init; }

    public static TiffPageOptions Default { get; } = new();
}
