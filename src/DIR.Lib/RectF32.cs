using System.Numerics;
using System.Runtime.CompilerServices;

namespace DIR.Lib
{
    /// <summary>
    /// Axis-aligned float rectangle for layout and hit testing, defined by
    /// <see cref="Vector2"/> position (top-left) + size. Used for content areas,
    /// toolbar bounds, and widget layout throughout <see cref="PixelWidgetBase{TSurface}"/>.
    /// <para>
    /// Unlike <see cref="RectInt"/> (which uses exclusive lower-right / inclusive upper-left),
    /// this type uses Position as the inclusive top-left and Size as the extent.
    /// </para>
    /// </summary>
    public readonly record struct RectF32(Vector2 Position, Vector2 Size)
    {
        /// <summary>
        /// Convenience constructor from individual floats. Existing call sites use this.
        /// </summary>
        public RectF32(float x, float y, float width, float height)
            : this(new Vector2(x, y), new Vector2(width, height)) { }

        /// <summary>Top-left X coordinate.</summary>
        public float X
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Position.X;
        }

        /// <summary>Top-left Y coordinate.</summary>
        public float Y
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Position.Y;
        }

        /// <summary>Horizontal extent.</summary>
        public float Width
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Size.X;
        }

        /// <summary>Vertical extent.</summary>
        public float Height
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Size.Y;
        }

        /// <summary>Right edge (X + Width).</summary>
        public float Right => Position.X + Size.X;

        /// <summary>Bottom edge (Y + Height).</summary>
        public float Bottom => Position.Y + Size.Y;

        /// <summary>Center point.</summary>
        public Vector2 Center => Position + Size * 0.5f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(float px, float py) => px >= Position.X && px < Right && py >= Position.Y && py < Bottom;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(Vector2 p) => p.X >= Position.X && p.X < Right && p.Y >= Position.Y && p.Y < Bottom;

        public RectF32 Inset(float padding) => new RectF32(Position + new Vector2(padding), Size - new Vector2(padding * 2));

        /// <summary>
        /// Backward-compatible deconstruction into (x, y, width, height) floats.
        /// </summary>
        public void Deconstruct(out float x, out float y, out float width, out float height)
        {
            x = Position.X;
            y = Position.Y;
            width = Size.X;
            height = Size.Y;
        }
    }
}
