using System.Collections.Generic;

namespace DIR.Lib
{
    /// <summary>
    /// Dock direction for <see cref="PixelLayout"/>.
    /// </summary>
    public enum PixelDockStyle { Top, Bottom, Left, Right }

    /// <summary>
    /// Dock-based layout engine. Consumes strips from the edges of a root rectangle.
    /// Renderer-agnostic — works with any <see cref="DIR.Lib.Renderer{TSurface}"/>.
    /// </summary>
    public sealed class PixelLayout
    {
        private RectF32 _remaining;
        private readonly List<(PixelDockStyle Style, float Size)> _docks = [];

        public PixelLayout(RectF32 root)
        {
            _remaining = root;
        }

        /// <summary>
        /// Allocates a strip of the given <paramref name="size"/> from the specified edge
        /// and returns its rectangle. The remaining space shrinks accordingly.
        /// </summary>
        public RectF32 Dock(PixelDockStyle style, float size)
        {
            _docks.Add((style, size));

            RectF32 result;
            switch (style)
            {
                case PixelDockStyle.Top:
                    result = new RectF32(_remaining.X, _remaining.Y, _remaining.Width, size);
                    _remaining = new RectF32(_remaining.X, _remaining.Y + size, _remaining.Width, _remaining.Height - size);
                    break;
                case PixelDockStyle.Bottom:
                    result = new RectF32(_remaining.X, _remaining.Bottom - size, _remaining.Width, size);
                    _remaining = new RectF32(_remaining.X, _remaining.Y, _remaining.Width, _remaining.Height - size);
                    break;
                case PixelDockStyle.Left:
                    result = new RectF32(_remaining.X, _remaining.Y, size, _remaining.Height);
                    _remaining = new RectF32(_remaining.X + size, _remaining.Y, _remaining.Width - size, _remaining.Height);
                    break;
                case PixelDockStyle.Right:
                    result = new RectF32(_remaining.Right - size, _remaining.Y, size, _remaining.Height);
                    _remaining = new RectF32(_remaining.X, _remaining.Y, _remaining.Width - size, _remaining.Height);
                    break;
                default:
                    result = _remaining;
                    break;
            }

            return result;
        }

        /// <summary>
        /// Returns the remaining rectangle after all docks have been applied.
        /// </summary>
        public RectF32 Fill() => _remaining;

        /// <summary>
        /// Replays the recorded dock sequence against a new root rectangle.
        /// </summary>
        public void Recompute(RectF32 newRoot)
        {
            _remaining = newRoot;
            var count = _docks.Count;
            for (var i = 0; i < count; i++)
            {
                var (style, size) = _docks[i];
                switch (style)
                {
                    case PixelDockStyle.Top:
                        _remaining = new RectF32(_remaining.X, _remaining.Y + size, _remaining.Width, _remaining.Height - size);
                        break;
                    case PixelDockStyle.Bottom:
                        _remaining = new RectF32(_remaining.X, _remaining.Y, _remaining.Width, _remaining.Height - size);
                        break;
                    case PixelDockStyle.Left:
                        _remaining = new RectF32(_remaining.X + size, _remaining.Y, _remaining.Width - size, _remaining.Height);
                        break;
                    case PixelDockStyle.Right:
                        _remaining = new RectF32(_remaining.X, _remaining.Y, _remaining.Width - size, _remaining.Height);
                        break;
                }
            }
        }
    }
}
