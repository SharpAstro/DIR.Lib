using System.Buffers.Binary;

namespace DIR.Lib.Tests;

/// <summary>
/// Minimal BMP writer for test output inspection. No external dependencies.
/// </summary>
internal static class BmpWriter
{
    public static void Save(string path, byte[] rgba, int width, int height)
    {
        // BMP stores rows bottom-to-top, BGR(A) format
        var rowSize = width * 4;
        var pixelDataSize = rowSize * height;
        var headerSize = 14 + 108; // BITMAPFILEHEADER + BITMAPV4HEADER
        var fileSize = headerSize + pixelDataSize;

        using var fs = File.Create(path);
        Span<byte> header = stackalloc byte[headerSize];
        header.Clear();

        // BITMAPFILEHEADER (14 bytes)
        header[0] = (byte)'B';
        header[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(header[2..], fileSize);
        BinaryPrimitives.WriteInt32LittleEndian(header[10..], headerSize);

        // BITMAPV4HEADER (108 bytes) — supports alpha channel
        BinaryPrimitives.WriteInt32LittleEndian(header[14..], 108);          // biSize
        BinaryPrimitives.WriteInt32LittleEndian(header[18..], width);        // biWidth
        BinaryPrimitives.WriteInt32LittleEndian(header[22..], -height);      // biHeight (negative = top-down)
        BinaryPrimitives.WriteInt16LittleEndian(header[26..], 1);            // biPlanes
        BinaryPrimitives.WriteInt16LittleEndian(header[28..], 32);           // biBitCount
        BinaryPrimitives.WriteInt32LittleEndian(header[30..], 3);            // biCompression = BI_BITFIELDS
        BinaryPrimitives.WriteInt32LittleEndian(header[34..], pixelDataSize);
        // Color masks: R, G, B, A
        BinaryPrimitives.WriteUInt32LittleEndian(header[54..], 0x00FF0000);  // red mask
        BinaryPrimitives.WriteUInt32LittleEndian(header[58..], 0x0000FF00);  // green mask
        BinaryPrimitives.WriteUInt32LittleEndian(header[62..], 0x000000FF);  // blue mask
        BinaryPrimitives.WriteUInt32LittleEndian(header[66..], 0xFF000000);  // alpha mask

        fs.Write(header);

        // Write pixels — convert RGBA to BGRA
        var row = new byte[rowSize];
        for (var y = 0; y < height; y++)
        {
            var srcOffset = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var si = srcOffset + x * 4;
                var di = x * 4;
                row[di] = rgba[si + 2];     // B
                row[di + 1] = rgba[si + 1]; // G
                row[di + 2] = rgba[si];     // R
                row[di + 3] = rgba[si + 3]; // A
            }
            fs.Write(row);
        }
    }
}
