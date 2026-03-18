using System.Buffers.Binary;

namespace DIR.Lib.Tests;

/// <summary>
/// Minimal BMP reader for baseline comparison. Reads 32-bit BGRA BMPs back to RGBA byte[].
/// </summary>
internal static class BmpReader
{
    public static (byte[] Rgba, int Width, int Height) Load(string path)
    {
        var data = File.ReadAllBytes(path);

        // BITMAPFILEHEADER
        var pixelOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(10));

        // DIB header
        var width = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(18));
        var height = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(22));
        var topDown = height < 0;
        if (topDown) height = -height;

        var rgba = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            var srcY = topDown ? y : height - 1 - y;
            var srcRow = pixelOffset + srcY * width * 4;
            var dstRow = y * width * 4;

            for (var x = 0; x < width; x++)
            {
                var si = srcRow + x * 4;
                var di = dstRow + x * 4;
                rgba[di] = data[si + 2];     // R (from B position in BGRA)
                rgba[di + 1] = data[si + 1]; // G
                rgba[di + 2] = data[si];     // B (from R position in BGRA)
                rgba[di + 3] = data[si + 3]; // A
            }
        }

        return (rgba, width, height);
    }
}
