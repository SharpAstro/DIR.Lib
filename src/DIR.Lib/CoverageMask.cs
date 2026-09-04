namespace DIR.Lib;

/// <summary>
/// Per-pixel coverage for one paint operation, composited onto an <see cref="RgbaImage"/> when the
/// operation is complete.
///
/// <para><b>Why an operation does not composite as it draws.</b> A stroked polyline is many segments
/// that overlap at every join, and a source-over blend applied per segment darkens those overlaps:
/// a right angle comes out visibly heavier than the two lines meeting at it. Gathering the whole
/// operation's coverage first, by <see cref="MaxRow"/> rather than by addition, makes an overlap
/// idempotent, which is what one stroke actually means.</para>
///
/// <para>Only the rows an operation touched are cleared between operations, so a drawing made of
/// thousands of small marks does not pay a full-surface clear per mark.</para>
/// </summary>
public sealed class CoverageMask
{
    private readonly float[] _coverage;
    private int _dirtyTop = int.MaxValue;
    private int _dirtyBottom = int.MinValue;

    public CoverageMask(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        _coverage = new float[checked(width * height)];
    }

    public int Width { get; }
    public int Height { get; }

    public bool IsEmpty => _dirtyTop > _dirtyBottom;

    public ReadOnlySpan<float> Row(int y) => _coverage.AsSpan(y * Width, Width);

    /// <summary>
    /// Unions <paramref name="values"/> into row <paramref name="y"/>, keeping the larger per pixel.
    /// Union rather than sum: see the type's remarks.
    /// </summary>
    public void MaxRow(int y, ReadOnlySpan<float> values)
    {
        if ((uint)y >= (uint)Height) return;

        var row = _coverage.AsSpan(y * Width, Width);
        var to = Math.Min(values.Length, Width);
        var touched = false;

        for (var x = 0; x < to; x++)
        {
            var v = values[x];
            if (v <= 0f) continue;
            if (v > row[x]) row[x] = v;
            touched = true;
        }

        if (!touched) return;
        if (y < _dirtyTop) _dirtyTop = y;
        if (y > _dirtyBottom) _dirtyBottom = y;
    }

    /// <summary>Unions a single pixel, for marks too small to have a span.</summary>
    public void MaxPixel(int x, int y, float value)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height || value <= 0f) return;

        var i = y * Width + x;
        if (value > _coverage[i]) _coverage[i] = value;

        if (y < _dirtyTop) _dirtyTop = y;
        if (y > _dirtyBottom) _dirtyBottom = y;
    }

    /// <summary>
    /// Composites what has been gathered onto <paramref name="image"/> in <paramref name="color"/>,
    /// and clears it ready for the next operation.
    /// </summary>
    public void FlushTo(RgbaImage image, RGBAColor32 color)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (IsEmpty || color.Alpha == 0) return;

        var top = Math.Max(0, _dirtyTop);
        var bottom = Math.Min(image.Height - 1, _dirtyBottom);

        for (var y = top; y <= bottom; y++)
        {
            var row = Row(y);
            var to = Math.Min(row.Length, image.Width);

            for (var x = 0; x < to; x++)
            {
                var c = row[x];
                if (c <= 0f) continue;
                if (c > 1f) c = 1f;

                // Coverage scales the source alpha, which is what makes a half-covered pixel a half
                // strength mark rather than a differently coloured one.
                var a = (byte)Math.Clamp(MathF.Round(color.Alpha * c), 0, 255);
                if (a == 0) continue;

                image.BlendPixelAt(x, y, new RGBAColor32(color.Red, color.Green, color.Blue, a));
            }
        }

        for (var y = Math.Max(0, _dirtyTop); y <= _dirtyBottom && y < Height; y++)
        {
            _coverage.AsSpan(y * Width, Width).Clear();
        }

        _dirtyTop = int.MaxValue;
        _dirtyBottom = int.MinValue;
    }
}
