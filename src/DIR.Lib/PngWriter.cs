using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace DIR.Lib;

/// <summary>
/// Pure-managed PNG writer. Emits a fully-conformant PNG with adaptive
/// per-row filter selection (libpng's "minimum sum of absolute values"
/// heuristic over filters 0/Sub/Up/Average/Paeth) and
/// <see cref="CompressionLevel.Optimal"/> deflate. Supports four pixel
/// formats — 8-bit grayscale, 16-bit grayscale, 8-bit RGBA, 16-bit RGBA —
/// and optionally embeds an ICC profile via an <c>iCCP</c> chunk. No
/// interlacing, no palette, no extra ancillary chunks.
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
    public static byte[] Encode(ReadOnlySpan<byte> rgba, int width, int height) =>
        Encode(rgba, width, height, iccProfile: default);

    /// <summary>
    /// Encode an 8-bit RGBA pixel buffer (row-major, no padding) as a PNG and
    /// optionally embed an ICC profile via an <c>iCCP</c> chunk. Pass an empty
    /// span for <paramref name="iccProfile"/> to omit the chunk (identical
    /// output to the simpler <see cref="Encode(ReadOnlySpan{byte}, int, int)"/>
    /// overload). <see cref="DIR.Lib.Color.IccProfiles.SRgbV4"/> is the
    /// pre-bundled sRGB v4 profile bytes for callers that want colour-managed
    /// output without supplying their own profile.
    /// </summary>
    public static byte[] Encode(ReadOnlySpan<byte> rgba, int width, int height, ReadOnlySpan<byte> iccProfile)
    {
        ValidateSize(rgba.Length, width, height, bytesPerPixel: 4);
        return EncodeCore(rgba, width, height, bitDepth: 8, colorType: 6, bytesPerPixel: 4, iccProfile);
    }

    /// <summary>
    /// Encode an 8-bit grayscale buffer (row-major, one byte per pixel).
    /// Useful for low-bit-depth mask / heat-map output where the alpha channel
    /// of <see cref="Encode(ReadOnlySpan{byte},int,int)"/> would just be a
    /// constant 0xFF.
    /// </summary>
    public static byte[] EncodeGray8(ReadOnlySpan<byte> gray, int width, int height, ReadOnlySpan<byte> iccProfile = default)
    {
        ValidateSize(gray.Length, width, height, bytesPerPixel: 1);
        return EncodeCore(gray, width, height, bitDepth: 8, colorType: 0, bytesPerPixel: 1, iccProfile);
    }

    /// <summary>
    /// Encode a 16-bit grayscale buffer (row-major, one <see cref="ushort"/>
    /// per pixel, system-endian on input). The PNG spec mandates big-endian
    /// sample order on disk, so the bytes are swapped internally before
    /// filtering — callers pass their <c>ushort[]</c> as-is. Used by the
    /// FITS-grayscale preview path so 16-bit stretches don't lose precision
    /// the way an 8-bit downsample would.
    /// </summary>
    public static byte[] EncodeGray16(ReadOnlySpan<ushort> gray, int width, int height, ReadOnlySpan<byte> iccProfile = default)
    {
        if (gray.Length != width * height)
            throw new ArgumentException("gray length must equal width*height");
        var beBytes = ToBigEndianBytes(gray);
        return EncodeCore(beBytes, width, height, bitDepth: 16, colorType: 0, bytesPerPixel: 2, iccProfile);
    }

    /// <summary>
    /// Encode a 16-bit RGBA buffer (row-major, four <see cref="ushort"/>s per
    /// pixel: R, G, B, A; system-endian on input). The PNG spec mandates
    /// big-endian sample order on disk so the bytes are swapped internally
    /// before filtering. Useful when the source is a 16-bit stretched float
    /// channel and an 8-bit quantise would crush gradients.
    /// </summary>
    public static byte[] EncodeRgba16(ReadOnlySpan<ushort> rgba, int width, int height, ReadOnlySpan<byte> iccProfile = default)
    {
        if (rgba.Length != width * height * 4)
            throw new ArgumentException("rgba length must equal width*height*4");
        var beBytes = ToBigEndianBytes(rgba);
        return EncodeCore(beBytes, width, height, bitDepth: 16, colorType: 6, bytesPerPixel: 8, iccProfile);
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

    /// <summary>
    /// Encode <paramref name="rgba"/> with an embedded ICC profile and write
    /// it to <paramref name="path"/>.
    /// </summary>
    public static void Save(string path, ReadOnlySpan<byte> rgba, int width, int height, ReadOnlySpan<byte> iccProfile)
    {
        var png = Encode(rgba, width, height, iccProfile);
        File.WriteAllBytes(path, png);
    }

    private static void ValidateSize(int actualBytes, int width, int height, int bytesPerPixel)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentException("width and height must be positive");
        var expected = width * height * bytesPerPixel;
        if (actualBytes != expected)
            throw new ArgumentException($"pixel buffer length must equal width*height*{bytesPerPixel}");
    }

    /// <summary>
    /// PNG bytes for arbitrary (bitDepth, colorType, bytesPerPixel). The
    /// caller is responsible for arranging <paramref name="samples"/> in
    /// big-endian sample order — for 16-bit formats this means the high byte
    /// of each <c>ushort</c> precedes the low byte (the public 16-bit
    /// entry points do this swap automatically).
    /// </summary>
    private static byte[] EncodeCore(ReadOnlySpan<byte> samples, int width, int height,
        byte bitDepth, byte colorType, int bytesPerPixel, ReadOnlySpan<byte> iccProfile)
    {
        using var ms = new MemoryStream();
        ms.Write(Signature);

        // IHDR: width, height, bit depth, color type, compression (0=deflate),
        // filter (0=adaptive), interlace (0).
        Span<byte> ihdr = stackalloc byte[13];
        WriteBE(ihdr.Slice(0, 4), (uint)width);
        WriteBE(ihdr.Slice(4, 4), (uint)height);
        ihdr[8] = bitDepth;
        ihdr[9] = colorType;
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0;
        WriteChunk(ms, "IHDR"u8, ihdr);

        // iCCP must come after IHDR and before the first IDAT (PNG spec
        // §11.3.3.3). Format: keyword (Latin-1, 1..79 bytes) + NUL +
        // compression method (always 0 = zlib) + zlib-deflated profile.
        // We emit the keyword "ICC profile" — what libpng, Adobe, and every
        // major encoder use; the PNG spec lets it be anything, but matching
        // the wild norm avoids surprises in pedantic readers.
        if (!iccProfile.IsEmpty)
            WriteIccpChunk(ms, "ICC profile"u8, iccProfile);

        WriteIdatChunk(ms, samples, width, height, bytesPerPixel);
        WriteChunk(ms, "IEND"u8, ReadOnlySpan<byte>.Empty);
        return ms.ToArray();
    }

    /// <summary>
    /// Filter every scanline with libpng's "minsum" heuristic and stream the
    /// result through ZLibStream directly into <paramref name="ms"/>. We
    /// back-patch the IDAT length field once we know the IDAT size, and
    /// compute the chunk's CRC over [type + data] straight from the
    /// MemoryStream backing buffer.
    /// </summary>
    private static void WriteIdatChunk(MemoryStream ms, ReadOnlySpan<byte> samples, int width, int height, int bytesPerPixel)
    {
        var stride = width * bytesPerPixel;
        var prevRow = new byte[stride];          // row -1 is all zeros per spec
        var candidateBuf = new byte[5 * stride]; // 5 candidate filters per row
        var sums = new long[5];

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
                var current = samples.Slice(y * stride, stride);

                // Compute all 5 filter candidates into separate slices of
                // candidateBuf, score each, write out the smallest. Keeping
                // 5 buffers (instead of redoing the chosen filter) avoids
                // ~20% extra filtering work per row at the cost of 4×stride
                // bytes of scratch per call, which is negligible.
                FilterRow(current, prevRow, candidateBuf.AsSpan(0 * stride, stride), 0, bytesPerPixel);
                FilterRow(current, prevRow, candidateBuf.AsSpan(1 * stride, stride), 1, bytesPerPixel);
                FilterRow(current, prevRow, candidateBuf.AsSpan(2 * stride, stride), 2, bytesPerPixel);
                FilterRow(current, prevRow, candidateBuf.AsSpan(3 * stride, stride), 3, bytesPerPixel);
                FilterRow(current, prevRow, candidateBuf.AsSpan(4 * stride, stride), 4, bytesPerPixel);
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

        long idatEnd = ms.Position;
        long idatDataLength = idatEnd - typeAndDataStart - 4; // -4 for "IDAT" type
        Span<byte> lenBuf = stackalloc byte[4];
        WriteBE(lenBuf, (uint)idatDataLength);
        ms.Position = lengthFieldPos;
        ms.Write(lenBuf);
        ms.Position = idatEnd;

        var crcSpan = ms.GetBuffer().AsSpan((int)typeAndDataStart, (int)(idatEnd - typeAndDataStart));
        Span<byte> crcBuf = stackalloc byte[4];
        WriteBE(crcBuf, Crc32(crcSpan, ReadOnlySpan<byte>.Empty));
        ms.Write(crcBuf);
    }

    /// <summary>
    /// Emit an iCCP chunk for the given keyword + raw ICC profile bytes. The
    /// profile is zlib-deflated inline (the PNG spec mandates compression
    /// method 0 = zlib) and a single CRC32 is computed over [type + payload]
    /// straight from the MemoryStream backing buffer to avoid an extra copy.
    /// </summary>
    private static void WriteIccpChunk(MemoryStream ms, ReadOnlySpan<byte> keyword, ReadOnlySpan<byte> rawProfile)
    {
        // Reserve length, stream the payload, patch the length once we know it.
        long lengthFieldPos = ms.Position;
        Span<byte> placeholder = stackalloc byte[4];
        ms.Write(placeholder);
        long typeAndDataStart = ms.Position;
        ms.Write("iCCP"u8);

        ms.Write(keyword);
        ms.WriteByte(0); // null separator between keyword and method
        ms.WriteByte(0); // compression method = 0 (zlib/deflate)

        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(rawProfile);

        long end = ms.Position;
        long dataLength = end - typeAndDataStart - 4; // -4 for "iCCP" type bytes
        Span<byte> lenBuf = stackalloc byte[4];
        WriteBE(lenBuf, (uint)dataLength);
        ms.Position = lengthFieldPos;
        ms.Write(lenBuf);
        ms.Position = end;

        // CRC over [type + payload] read directly from the underlying buffer.
        var crcSpan = ms.GetBuffer().AsSpan((int)typeAndDataStart, (int)(end - typeAndDataStart));
        Span<byte> crcBuf = stackalloc byte[4];
        WriteBE(crcBuf, Crc32(crcSpan, ReadOnlySpan<byte>.Empty));
        ms.Write(crcBuf);
    }

    /// <summary>
    /// Reorder <paramref name="samples"/> from system-endian to PNG's required
    /// big-endian byte order, returning a freshly-allocated buffer. On a
    /// little-endian host this is a per-sample byte swap; on a (hypothetical)
    /// big-endian host the bytes are already in network order and we just
    /// reinterpret-cast the ushort span to bytes.
    /// </summary>
    private static byte[] ToBigEndianBytes(ReadOnlySpan<ushort> samples)
    {
        var bytes = new byte[samples.Length * 2];
        if (BitConverter.IsLittleEndian)
        {
            for (var i = 0; i < samples.Length; i++)
                BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(i * 2, 2), samples[i]);
        }
        else
        {
            MemoryMarshal.AsBytes(samples).CopyTo(bytes);
        }
        return bytes;
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
