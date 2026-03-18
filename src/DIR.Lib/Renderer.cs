namespace DIR.Lib;

public abstract class Renderer<TSurface>(TSurface surface) : IDisposable
{
    public TSurface Surface { get; } = surface;

    public abstract uint Width { get; }
    public abstract uint Height { get; }

    public abstract void Resize(uint width, uint height);

    public abstract void DrawRectangle(in RectInt rect, RGBAColor32 strokeColor, int strokeWidth);
    public abstract void FillRectangle(in RectInt rect, RGBAColor32 fillColor);
    public abstract void FillEllipse(in RectInt rect, RGBAColor32 fillColor);
    public abstract void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize, RGBAColor32 fontColor, in RectInt layout,
        TextAlign horizAlignment = TextAlign.Center, TextAlign vertAlignment = TextAlign.Near);

    /// <summary>
    /// Fills multiple rectangles in a single batched draw call.
    /// Default implementation falls back to individual FillRectangle calls.
    /// </summary>
    public virtual void FillRectangles(ReadOnlySpan<(RectInt Rect, RGBAColor32 Color)> rectangles)
    {
        foreach (var (rect, color) in rectangles)
        {
            FillRectangle(rect, color);
        }
    }

    public abstract void Dispose();
}
