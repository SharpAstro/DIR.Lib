using System;

namespace DIR.Lib;

/// <summary>
/// The pixel-surface implementation of <see cref="IMeasureContext{T}"/>: text intrinsic size comes from
/// <see cref="Renderer{TSurface}.MeasureText"/>, and design-unit scalars (padding, gaps, fixed/font sizes)
/// scale by <paramref name="dpiScale"/>. So the arranged rects come back in device pixels; the painter must
/// draw text at <c>fontSize * dpiScale</c> to match what was measured (see <see cref="PixelWidgetBase{TSurface}"/>'s
/// layout painter, which does exactly that).
/// </summary>
public sealed class PixelMeasureContext<TSurface>(Renderer<TSurface> renderer, string fontPath, float dpiScale = 1f)
    : IMeasureContext<float>
{
    public Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize)
    {
        if (string.IsNullOrEmpty(fontPath) || text.IsEmpty)
        {
            // No font (e.g. headless) or empty run: width 0, but keep a sensible line height for row sizing.
            return new Size<float>(0f, fontSize * dpiScale);
        }

        var (width, height) = renderer.MeasureText(text, fontPath, fontSize * dpiScale);
        return new Size<float>(width, height);
    }

    public float ToSurface(float designUnits) => designUnits * dpiScale;
}
