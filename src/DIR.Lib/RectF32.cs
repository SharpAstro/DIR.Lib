namespace DIR.Lib
{
    /// <summary>
    /// Axis-aligned rectangle in pixel coordinates, used for layout and hit testing.
    /// Renderer-agnostic — works with any <see cref="DIR.Lib.Renderer{TSurface}"/>.
    /// </summary>
    public readonly record struct RectF32(float X, float Y, float Width, float Height)
    {
        public float Right => X + Width;
        public float Bottom => Y + Height;
        public bool Contains(float px, float py) => px >= X && px < Right && py >= Y && py < Bottom;
        public RectF32 Inset(float padding) => new RectF32(X + padding, Y + padding, Width - padding * 2, Height - padding * 2);
    }
}
