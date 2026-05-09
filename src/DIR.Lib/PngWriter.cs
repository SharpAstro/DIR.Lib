using System.IO.Compression;

namespace DIR.Lib;

/// <summary>
/// Pure-managed PNG writer for 8-bit RGBA images. Emits a fully-conformant
/// PNG with adaptive per-row filter selection (libpng's "minimum sum of
/// absolute values" heuristic over filters 0/Sub/Up/Average/Paeth) and
/// <see cref="CompressionLevel.Optimal"/> deflate. No interlacing, no
/// palette, no ancillary chunks — just the smallest 8-bit-RGBA PNG that
/// every standard decoder will accept.
///
/// Used by both production code ("save my <see cref="RgbaImage"/> render to
/// disk") and the test suite (committed baselines for golden-image regression
/// tests live as PNGs and are decoded back via <c>StbImageSharp</c> for
/// pixel-equality comparison).
///
/// The filter encoders below are the dual of <see cref="PngPredictor"/>
/// (PDF/TIFF code path's PNG row unfilter): same Sub / Up / Average / Paeth
/// formulas with the signs flipped.
/// </summary>
public static class PngWriter
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Encode an 8-bit RGBA pixel buffer (row-major, no padding) as a PNG.
    /// </summary>
    public static byte[] Encode(ReadOnlySpan<byte> rgba, int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentException("width and height must be positive");
        if (rgba.Length != width * height * 4) throw new ArgumentException("rgba length must equal width*height*4");

        using var ms = new MemoryStream();
        ms.Write(Signature);

        // IHDR: width, height, bit depth (8), color type (6 = RGBA),
        // compression (0 = deflate), filter (0 = adaptive), interlace (0).
        Span<byte> ihdr = stackalloc byte[13];
        WriteBE(ihdr.Slice(0, 4), (uint)width);
        WriteBE(ihdr.Slice(4, 4), (uint)height);
        ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        WriteChunk(ms, "IHDR"u8, ihdr);

        // IDAT: filter each scanline (libpng's "minsum" heuristic over filters
        // 0/Sub/Up/Average/Paeth) and stream the result directly into the
        // outer MemoryStream, ZLibStream-wrapped, so we never materialize
        // either the H×(W*4+1) filtered buffer or a separate compressed
        // buffer. We back-patch the length field once we know the IDAT size,
        // and compute the chunk's CRC over the just-written slice of ms's
        // backing array.
        const int Bpp = 4; // RGBA, 8-bit channels
        var stride = width * Bpp;
        var prevRow = new byte[stride];        // row -1 is all zeros
        var candidateBuf = new byte[5 * stride]; // 5 candidate filters per row
        var sums = new long[5];

        // Reserve the IDAT length field (patched at the end) and write the type.
        long lengthFieldPos = ms.Position;
        Span<byte> placeholder = stackalloc byte[4];
        ms.Write(placeholder);
        long typeAndDataStart = ms.Position;
        ms.Write("IDAT"u8);

        // Scope the ZLibStream so its trailing zlib bytes are flushed before we
        // measure ms.Position to compute the chunk length.
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (int y = 0; y < height; y++)
            {
                var current = rgba.Slice(y * stride, stride);

                // Compute all 5 filter candidates into separate slices of
                // candidateBuf, score each, write out the smallest. Keeping
                // 5 buffers (instead of redoing the chosen filter) avoids
                // ~20% extra filtering work per row at the cost of 4×stride
                // bytes of scratch per call, which is negligible.
                FilterRow(current, prevRow, candidateBuf.AsSpan(0 * stride, stride), 0, Bpp);
                FilterRow(current, prevRow, candidateBuf.AsSpan(1 * stride, stride), 1, Bpp);
                FilterRow(current, prevRow, candidateBuf.AsSpan(2 * stride, stride), 2, Bpp);
                FilterRow(current, prevRow, candidateBuf.AsSpan(3 * stride, stride), 3, Bpp);
                FilterRow(current, prevRow, candidateBuf.AsSpan(4 * stride, stride), 4, Bpp);
                sums[0] = SumAbsSigned(candidateBuf.AsSpan(0 * stride, stride));
                sums[1] = SumAbsSigned(candidateBuf.AsSpan(1 * stride, stride));
                sums[2] = SumAbsSigned(candidateBuf.AsSpan(2 * stride, stride));
                sums[3] = SumAbsSigned(candidateBuf.AsSpan(3 * stride, stride));
                sums[4] = SumAbsSigned(candidateBuf.AsSpan(4 * stride, stride));

                int bestFilter = 0;
                long bestSum = sums[0];
                for (int f = 1; f < 5; f++)
                {
                    if (sums[f] < bestSum) { bestSum = sums[f]; bestFilter = f; }
                }

                z.WriteByte((byte)bestFilter);
                z.Write(candidateBuf, bestFilter * stride, stride);

                // Save the unfiltered current row as next iteration's "previous
                // row" — filter formulas reference the *original* values of the
                // pixel above, not the encoded ones.
                current.CopyTo(prevRow);
            }
        }

        // Patch the IDAT length field now that the deflate stream is closed.
        long idatEnd = ms.Position;
        long idatDataLength = idatEnd - typeAndDataStart - 4; // -4 for "IDAT" type
        Span<byte> lenBuf = stackalloc byte[4];
        WriteBE(lenBuf, (uint)idatDataLength);
        ms.Position = lengthFieldPos;
        ms.Write(lenBuf);
        ms.Position = idatEnd;

        // CRC over [type + data] — read directly from ms's backing buffer; no
        // intermediate copy.
        var crcSpan = ms.GetBuffer().AsSpan((int)typeAndDataStart, (int)(idatEnd - typeAndDataStart));
        Span<byte> crcBuf = stackalloc byte[4];
        WriteBE(crcBuf, Crc32(crcSpan, ReadOnlySpan<byte>.Empty));
        ms.Write(crcBuf);

        // IEND: empty data.
        WriteChunk(ms, "IEND"u8, ReadOnlySpan<byte>.Empty);
        return ms.ToArray();
    }

    /// <summary>
    /// Encode <paramref name="rgba"/> as a PNG and write it to
    /// <paramref name="path"/>.
    /// </summary>
    public static void Save(string path, ReadOnlySpan<byte> rgba, int width, int height)
    {
        var png = Encode(rgba, width, height);
        File.WriteAllBytes(path, png);
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> lenBuf = stackalloc byte[4];
        WriteBE(lenBuf, (uint)data.Length);
        output.Write(lenBuf);
        output.Write(type);
        output.Write(data);

        // CRC32 over type + data, big-endian.
        var crc = Crc32(type, data);
        Span<byte> crcBuf = stackalloc byte[4];
        WriteBE(crcBuf, crc);
        output.Write(crcBuf);
    }

    private static void WriteBE(Span<byte> dst, uint value)
    {
        dst[0] = (byte)(value >> 24);
        dst[1] = (byte)(value >> 16);
        dst[2] = (byte)(value >> 8);
        dst[3] = (byte)value;
    }

    private static byte[] DeflateZlib(ReadOnlySpan<byte> raw)
    {
        // ZLibStream wraps deflate with a 2-byte header + 4-byte Adler32
        // trailer, which is exactly what the PNG IDAT spec asks for. Use
        // Optimal — the encoding cost is dwarfed by the file-size win on
        // anything bigger than a postage stamp.
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(raw);
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Apply a PNG filter to one scanline. <paramref name="filterType"/>:
    /// 0=None, 1=Sub (subtract left neighbour), 2=Up (subtract pixel above),
    /// 3=Average (subtract floor((left+above)/2)), 4=Paeth.
    /// </summary>
    private static void FilterRow(ReadOnlySpan<byte> raw, ReadOnlySpan<byte> prev,
        Span<byte> dst, int filterType, int bpp)
    {
        switch (filterType)
        {
            case 0:
                raw.CopyTo(dst);
                break;
            case 1:
                for (int i = 0; i < bpp; i++) dst[i] = raw[i];
                for (int i = bpp; i < raw.Length; i++) dst[i] = (byte)(raw[i] - raw[i - bpp]);
                break;
            case 2:
                for (int i = 0; i < raw.Length; i++) dst[i] = (byte)(raw[i] - prev[i]);
                break;
            case 3:
                for (int i = 0; i < raw.Length; i++)
                {
                    int left = i >= bpp ? raw[i - bpp] : 0;
                    int above = prev[i];
                    dst[i] = (byte)(raw[i] - (left + above) / 2);
                }
                break;
            case 4:
                for (int i = 0; i < raw.Length; i++)
                {
                    int left = i >= bpp ? raw[i - bpp] : 0;
                    int above = prev[i];
                    int upperLeft = i >= bpp ? prev[i - bpp] : 0;
                    dst[i] = (byte)(raw[i] - PngPredictor.PaethPredictor(left, above, upperLeft));
                }
                break;
        }
    }

    /// <summary>
    /// libpng's "minsum" filter selection score: sum of absolute values of
    /// the bytes interpreted as signed (so 0xFF → 1, 0x80 → 128). Smaller
    /// score correlates with better deflate compression on the row.
    /// </summary>
    private static long SumAbsSigned(ReadOnlySpan<byte> row)
    {
        long sum = 0;
        for (int i = 0; i < row.Length; i++)
        {
            sbyte s = (sbyte)row[i];
            sum += s < 0 ? -s : s;
        }
        return sum;
    }

    /// <summary>
    /// Standard PNG CRC32 (polynomial 0xEDB88320, IEEE 802.3). Computed on
    /// the concatenation of <paramref name="a"/> and <paramref name="b"/>
    /// without materializing either span.
    /// </summary>
    private static uint Crc32(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        uint c = 0xFFFFFFFFu;
        foreach (var x in a) c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        foreach (var x in b) c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (int k = 0; k < 8; k++)
                c = ((c & 1) != 0) ? 0xEDB88320u ^ (c >> 1) : (c >> 1);
            t[n] = c;
        }
        return t;
    }
}
