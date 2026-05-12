using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace DIR.Lib.Tiff;

/// <summary>
/// Pure-managed TIFF reader — the dual of <see cref="TiffWriter"/>. Reads
/// every IFD in chain order and decodes its strips into a single contiguous
/// byte buffer per page. <see cref="TiffPage.Pixels"/> is normalised to the
/// host's byte order: file-byte-order is detected from the "II" / "MM"
/// header and a final per-sample swap runs when (file order != host order)
/// so callers can re-interpret the bytes as <c>ushort</c> / <c>float</c>
/// with <c>MemoryMarshal.Cast</c> without further endian gymnastics.
///
/// Scope (v1):
/// <list type="bullet">
/// <item>Both byte orders: "II" (little-endian) and "MM" (big-endian). Most
///       astronomy TIFFs are II; some scanner / Photoshop output is MM.</item>
/// <item>Strip layout (no tile decoding yet — tiled TIFFs throw).</item>
/// <item>Bit depths 8, 16, 32 — uniform across all samples (per TIFF norm).</item>
/// <item>Compression: <see cref="TiffCompression.Uncompressed"/>,
///       <see cref="TiffCompression.Deflate"/>, <see cref="TiffCompression.ZlibPkzip"/>.</item>
/// <item>Sample formats: <see cref="TiffSampleFormat.Uint"/>, <see cref="TiffSampleFormat.IeeeFloat"/>.</item>
/// <item>Contiguous planar config only (one sample per pixel position; chunky).</item>
/// </list>
/// LZW / JPEG / Tile / BigTIFF / planar-separate are out of scope for v1 — when
/// TianWen needs them, add them here (the existing code only loops over strips
/// and dispatches on compression).
/// </summary>
public static class TiffReader
{
    /// <summary>Decode every page from a TIFF in-memory.</summary>
    public static TiffDocument Read(ReadOnlySpan<byte> tiff)
    {
        if (tiff.Length < 8) throw new InvalidDataException("TIFF too small for header");

        // Detect byte order from header bytes 0-1. "II" = little-endian,
        // "MM" = big-endian (TIFF 6.0 §2). All multi-byte values in the file
        // — including the magic, the IFD offsets, every IFD entry, every
        // pixel sample of width > 8 bits — follow this same order.
        bool fileIsLE;
        if (tiff[0] == (byte)'I' && tiff[1] == (byte)'I') fileIsLE = true;
        else if (tiff[0] == (byte)'M' && tiff[1] == (byte)'M') fileIsLE = false;
        else throw new InvalidDataException($"Unknown TIFF byte order tag {tiff[0]:X2}{tiff[1]:X2}");

        if (ReadUInt16(tiff.Slice(2, 2), fileIsLE) != 42)
            throw new InvalidDataException("TIFF magic mismatch");

        var pages = new List<TiffPage>();
        var ifdOffset = (int)ReadUInt32(tiff.Slice(4, 4), fileIsLE);
        while (ifdOffset != 0)
        {
            var (page, nextOffset) = ReadPage(tiff, ifdOffset, fileIsLE);
            pages.Add(page);
            ifdOffset = nextOffset;
        }
        return new TiffDocument(pages);
    }

    /// <summary>Decode every page from a TIFF stream (slurped to a byte array).</summary>
    public static TiffDocument Read(Stream stream)
    {
        // TIFF readers need random access via the IFD chain — slurp the stream
        // and operate on the in-memory span. For very large TIFFs the caller
        // should map the file (MemoryMappedFile) and pass that span instead.
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Read(ms.GetBuffer().AsSpan(0, (int)ms.Length));
    }

    private static (TiffPage Page, int NextIfdOffset) ReadPage(ReadOnlySpan<byte> tiff, int ifdOffset, bool fileIsLE)
    {
        // ---- Parse the IFD into a tag dictionary ---------------------------
        if (ifdOffset + 2 > tiff.Length) throw new InvalidDataException("IFD offset out of bounds");
        var entryCount = ReadUInt16(tiff.Slice(ifdOffset, 2), fileIsLE);
        const int entrySize = 12;

        var directoryEnd = ifdOffset + 2 + entryCount * entrySize;
        if (directoryEnd + 4 > tiff.Length) throw new InvalidDataException("IFD truncated");

        // ---- Required tags ------------------------------------------------
        var width = 0;
        var height = 0;
        var samplesPerPixel = 1;
        var bitsPerSample = 1;
        var compression = TiffCompression.Uncompressed;
        var photometric = TiffPhotometric.MinIsBlack;
        var sampleFormat = TiffSampleFormat.Uint;
        var planarConfig = TiffPlanarConfig.Contig;
        var rowsPerStrip = 0;
        uint[]? stripOffsets = null;
        uint[]? stripByteCounts = null;
        float? sMin = null;
        float? sMax = null;
        byte[]? icc = null;

        for (var i = 0; i < entryCount; i++)
        {
            var entryStart = ifdOffset + 2 + i * entrySize;
            var tag = ReadUInt16(tiff.Slice(entryStart, 2), fileIsLE);
            var type = (TiffFieldType)ReadUInt16(tiff.Slice(entryStart + 2, 2), fileIsLE);
            var count = (int)ReadUInt32(tiff.Slice(entryStart + 4, 4), fileIsLE);
            var valueSpan = tiff.Slice(entryStart + 8, 4);

            switch (tag)
            {
                case TiffTag.ImageWidth:
                    width = (int)ReadScalar(type, valueSpan, fileIsLE);
                    break;
                case TiffTag.ImageLength:
                    height = (int)ReadScalar(type, valueSpan, fileIsLE);
                    break;
                case TiffTag.SamplesPerPixel:
                    samplesPerPixel = (int)ReadScalar(type, valueSpan, fileIsLE);
                    break;
                case TiffTag.BitsPerSample:
                    bitsPerSample = (int)ReadShortArray(tiff, type, count, valueSpan, fileIsLE)[0];
                    break;
                case TiffTag.Compression:
                    compression = (TiffCompression)ReadScalar(type, valueSpan, fileIsLE);
                    break;
                case TiffTag.PhotometricInterp:
                    photometric = (TiffPhotometric)ReadScalar(type, valueSpan, fileIsLE);
                    break;
                case TiffTag.PlanarConfig:
                    planarConfig = (TiffPlanarConfig)ReadScalar(type, valueSpan, fileIsLE);
                    break;
                case TiffTag.RowsPerStrip:
                    rowsPerStrip = (int)ReadScalar(type, valueSpan, fileIsLE);
                    break;
                case TiffTag.StripOffsets:
                    stripOffsets = ReadLongOrShortArray(tiff, type, count, valueSpan, fileIsLE);
                    break;
                case TiffTag.StripByteCounts:
                    stripByteCounts = ReadLongOrShortArray(tiff, type, count, valueSpan, fileIsLE);
                    break;
                case TiffTag.SampleFormat:
                    sampleFormat = (TiffSampleFormat)ReadShortArray(tiff, type, count, valueSpan, fileIsLE)[0];
                    break;
                case TiffTag.SMinSampleValue:
                    sMin = ReadFloatArray(tiff, type, count, valueSpan, fileIsLE)[0];
                    break;
                case TiffTag.SMaxSampleValue:
                    sMax = ReadFloatArray(tiff, type, count, valueSpan, fileIsLE)[0];
                    break;
                case TiffTag.IccProfile:
                    icc = ReadByteArray(tiff, type, count, valueSpan, fileIsLE);
                    break;
                default:
                    // Unrecognised tag — TIFF spec says skip unknown tags.
                    break;
            }
        }

        var nextIfdOffset = (int)ReadUInt32(tiff.Slice(directoryEnd, 4), fileIsLE);

        // ---- Validate the layout we're willing to handle ------------------
        if (width <= 0 || height <= 0)
            throw new InvalidDataException("ImageWidth/ImageLength missing or invalid");
        if (planarConfig != TiffPlanarConfig.Contig)
            throw new NotSupportedException("Only PlanarConfig=Contig is supported");
        if (stripOffsets is null || stripByteCounts is null)
            throw new NotSupportedException("Tiled or stripless TIFFs are not supported in this reader");
        if (stripOffsets.Length != stripByteCounts.Length)
            throw new InvalidDataException("StripOffsets/StripByteCounts length mismatch");
        if (bitsPerSample != 8 && bitsPerSample != 16 && bitsPerSample != 32)
            throw new NotSupportedException($"BitsPerSample={bitsPerSample} not supported (expected 8/16/32)");
        if (sampleFormat != TiffSampleFormat.Uint && sampleFormat != TiffSampleFormat.IeeeFloat)
            throw new NotSupportedException($"SampleFormat={sampleFormat} not supported");

        // ---- Decode every strip into one contiguous byte buffer -----------
        var bytesPerPixel = samplesPerPixel * (bitsPerSample / 8);
        var expectedBytes = width * height * bytesPerPixel;
        var pixels = new byte[expectedBytes];
        var pixelPos = 0;
        for (var i = 0; i < stripOffsets.Length; i++)
        {
            var stripStart = (int)stripOffsets[i];
            var stripLen = (int)stripByteCounts[i];
            if (stripStart < 0 || stripLen < 0 || stripStart + stripLen > tiff.Length)
                throw new InvalidDataException($"Strip {i} extents out of bounds");
            var stripSpan = tiff.Slice(stripStart, stripLen);

            switch (compression)
            {
                case TiffCompression.Uncompressed:
                    var copy = Math.Min(stripSpan.Length, pixels.Length - pixelPos);
                    stripSpan[..copy].CopyTo(pixels.AsSpan(pixelPos, copy));
                    pixelPos += copy;
                    break;
                case TiffCompression.Deflate:
                case TiffCompression.ZlibPkzip:
                    pixelPos += InflateInto(stripSpan, pixels.AsSpan(pixelPos));
                    break;
                default:
                    throw new NotSupportedException($"Compression {compression} not supported in this reader");
            }
        }

        // ---- Endian-normalise pixels to host order ------------------------
        // After strip decode, pixels are still in *file* byte order (raw on-disk
        // samples). If host and file disagree, swap so MemoryMarshal.Cast gives
        // a meaningful ushort/float view. 8-bit samples need no swap.
        if (fileIsLE != BitConverter.IsLittleEndian)
            SwapPixelsToHostOrder(pixels, bitsPerSample);

        var page = new TiffPage(
            Width: width,
            Height: height,
            SamplesPerPixel: samplesPerPixel,
            BitsPerSample: bitsPerSample,
            Photometric: photometric,
            SampleFormat: sampleFormat,
            Compression: compression,
            RowsPerStrip: rowsPerStrip,
            Pixels: pixels,
            SMinSampleValue: sMin,
            SMaxSampleValue: sMax,
            IccProfile: icc);
        return (page, nextIfdOffset);
    }

    /// <summary>
    /// In-place per-sample byte-reverse. Float32 is treated as a 32-bit blob
    /// because reversing the 4 raw bytes of an IEEE-754 number gives back the
    /// other-endian IEEE-754 representation of the same value.
    /// </summary>
    private static void SwapPixelsToHostOrder(Span<byte> pixels, int bitsPerSample)
    {
        switch (bitsPerSample)
        {
            case 16:
                var asU16 = MemoryMarshal.Cast<byte, ushort>(pixels);
                for (var i = 0; i < asU16.Length; i++)
                    asU16[i] = BinaryPrimitives.ReverseEndianness(asU16[i]);
                break;
            case 32:
                var asU32 = MemoryMarshal.Cast<byte, uint>(pixels);
                for (var i = 0; i < asU32.Length; i++)
                    asU32[i] = BinaryPrimitives.ReverseEndianness(asU32[i]);
                break;
            // 8-bit: byte order is irrelevant.
        }
    }

    /// <summary>
    /// Inflate the zlib-wrapped strip into the destination span, returning the
    /// number of bytes written. ZLibStream is forgiving of trailing zero
    /// padding the writer may have left after the deflate trailer (none for
    /// our writer, but some encoders pad strips up to the row boundary).
    /// </summary>
    private static int InflateInto(ReadOnlySpan<byte> strip, Span<byte> dst)
    {
        // ZLibStream needs a MemoryStream → minor allocation per strip; in
        // practice strips are large enough that this is irrelevant.
        var srcArray = strip.ToArray();
        using var src = new MemoryStream(srcArray);
        using var z = new ZLibStream(src, CompressionMode.Decompress);
        var written = 0;
        while (written < dst.Length)
        {
            var n = z.Read(dst.Slice(written));
            if (n == 0) break;
            written += n;
        }
        return written;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, bool fileIsLE) => fileIsLE
        ? BinaryPrimitives.ReadUInt16LittleEndian(bytes)
        : BinaryPrimitives.ReadUInt16BigEndian(bytes);

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, bool fileIsLE) => fileIsLE
        ? BinaryPrimitives.ReadUInt32LittleEndian(bytes)
        : BinaryPrimitives.ReadUInt32BigEndian(bytes);

    private static float ReadSingle(ReadOnlySpan<byte> bytes, bool fileIsLE) => fileIsLE
        ? BinaryPrimitives.ReadSingleLittleEndian(bytes)
        : BinaryPrimitives.ReadSingleBigEndian(bytes);

    private static uint ReadScalar(TiffFieldType type, ReadOnlySpan<byte> valueOrOffset, bool fileIsLE) => type switch
    {
        TiffFieldType.Byte  => valueOrOffset[0],
        TiffFieldType.Short => ReadUInt16(valueOrOffset.Slice(0, 2), fileIsLE),
        TiffFieldType.Long  => ReadUInt32(valueOrOffset.Slice(0, 4), fileIsLE),
        _ => throw new NotSupportedException($"Scalar tag has unexpected type {type}"),
    };

    private static ushort[] ReadShortArray(ReadOnlySpan<byte> tiff, TiffFieldType type, int count, ReadOnlySpan<byte> valueSpan, bool fileIsLE)
    {
        if (type != TiffFieldType.Short)
            throw new NotSupportedException($"Expected SHORT, got {type}");
        var totalBytes = count * 2;
        var data = totalBytes <= 4
            ? valueSpan.Slice(0, totalBytes)
            : tiff.Slice((int)ReadUInt32(valueSpan, fileIsLE), totalBytes);
        var result = new ushort[count];
        for (var i = 0; i < count; i++)
            result[i] = ReadUInt16(data.Slice(i * 2, 2), fileIsLE);
        return result;
    }

    private static uint[] ReadLongOrShortArray(ReadOnlySpan<byte> tiff, TiffFieldType type, int count, ReadOnlySpan<byte> valueSpan, bool fileIsLE)
    {
        var elemSize = type switch
        {
            TiffFieldType.Short => 2,
            TiffFieldType.Long  => 4,
            _ => throw new NotSupportedException($"Expected SHORT/LONG, got {type}"),
        };
        var totalBytes = count * elemSize;
        var data = totalBytes <= 4
            ? valueSpan.Slice(0, totalBytes)
            : tiff.Slice((int)ReadUInt32(valueSpan, fileIsLE), totalBytes);
        var result = new uint[count];
        for (var i = 0; i < count; i++)
            result[i] = elemSize == 2
                ? ReadUInt16(data.Slice(i * 2, 2), fileIsLE)
                : ReadUInt32(data.Slice(i * 4, 4), fileIsLE);
        return result;
    }

    private static float[] ReadFloatArray(ReadOnlySpan<byte> tiff, TiffFieldType type, int count, ReadOnlySpan<byte> valueSpan, bool fileIsLE)
    {
        if (type != TiffFieldType.Float)
            throw new NotSupportedException($"Expected FLOAT, got {type}");
        var totalBytes = count * 4;
        var data = totalBytes <= 4
            ? valueSpan.Slice(0, totalBytes)
            : tiff.Slice((int)ReadUInt32(valueSpan, fileIsLE), totalBytes);
        var result = new float[count];
        for (var i = 0; i < count; i++)
            result[i] = ReadSingle(data.Slice(i * 4, 4), fileIsLE);
        return result;
    }

    private static byte[] ReadByteArray(ReadOnlySpan<byte> tiff, TiffFieldType type, int count, ReadOnlySpan<byte> valueSpan, bool fileIsLE)
    {
        if (type != TiffFieldType.Undefined && type != TiffFieldType.Byte)
            throw new NotSupportedException($"Expected UNDEFINED/BYTE, got {type}");
        var data = count <= 4
            ? valueSpan.Slice(0, count)
            : tiff.Slice((int)ReadUInt32(valueSpan, fileIsLE), count);
        return data.ToArray();
    }
}

/// <summary>
/// Top-level result of a TIFF decode: every IFD in chain order, each as a
/// <see cref="TiffPage"/>. For single-page TIFFs (typical) this list has
/// length 1.
/// </summary>
public sealed record TiffDocument(IReadOnlyList<TiffPage> Pages);

/// <summary>
/// One decoded IFD plus its strip-concatenated pixel buffer.
/// <see cref="Pixels"/> is laid out in row-major contiguous order with each
/// sample in the *host's* byte order — the reader already swapped any
/// file-byte-order mismatch (e.g. MM file on an LE host) — so callers on
/// x64/arm64 can reinterpret-cast it as <c>ushort[]</c> / <c>float[]</c>
/// with <c>System.Runtime.InteropServices.MemoryMarshal.Cast</c> for
/// zero-copy access.
/// </summary>
public sealed record TiffPage(
    int Width,
    int Height,
    int SamplesPerPixel,
    int BitsPerSample,
    TiffPhotometric Photometric,
    TiffSampleFormat SampleFormat,
    TiffCompression Compression,
    int RowsPerStrip,
    byte[] Pixels,
    float? SMinSampleValue,
    float? SMaxSampleValue,
    byte[]? IccProfile);
