using System.Collections.Generic;
using System.Numerics;

namespace DIR.Lib;

/// <summary>
/// Dock direction for <see cref="DockLayout{T}"/>.
/// </summary>
public enum DockStyle { Top, Bottom, Left, Right }

/// <summary>
/// Generic axis-aligned rectangle with numeric coordinates.
/// </summary>
public readonly record struct Rect<T>(T X, T Y, T Width, T Height) where T : INumber<T>
{
    public T Right => X + Width;
    public T Bottom => Y + Height;
    public bool Contains(T px, T py) => px >= X && px < Right && py >= Y && py < Bottom;
    public Rect<T> Inset(T padding) => new(X + padding, Y + padding, Width - padding - padding, Height - padding - padding);
}

/// <summary>
/// Dock-based layout engine using generic math. Consumes strips from the edges
/// of a root rectangle. Works with any numeric coordinate type (int, float, etc.).
/// </summary>
public class DockLayout<T> where T : INumber<T>
{
    private Rect<T> _remaining;
    private readonly List<(DockStyle Style, T Size)> _docks = [];

    public DockLayout(Rect<T> root)
    {
        _remaining = root;
    }

    /// <summary>
    /// Allocates a strip of the given <paramref name="size"/> from the specified edge
    /// and returns its rectangle. The remaining space shrinks accordingly.
    /// </summary>
    public Rect<T> Dock(DockStyle style, T size)
    {
        _docks.Add((style, size));

        Rect<T> result;
        switch (style)
        {
            case DockStyle.Top:
                result = new Rect<T>(_remaining.X, _remaining.Y, _remaining.Width, size);
                _remaining = new Rect<T>(_remaining.X, _remaining.Y + size, _remaining.Width, _remaining.Height - size);
                break;
            case DockStyle.Bottom:
                result = new Rect<T>(_remaining.X, _remaining.Bottom - size, _remaining.Width, size);
                _remaining = new Rect<T>(_remaining.X, _remaining.Y, _remaining.Width, _remaining.Height - size);
                break;
            case DockStyle.Left:
                result = new Rect<T>(_remaining.X, _remaining.Y, size, _remaining.Height);
                _remaining = new Rect<T>(_remaining.X + size, _remaining.Y, _remaining.Width - size, _remaining.Height);
                break;
            case DockStyle.Right:
                result = new Rect<T>(_remaining.Right - size, _remaining.Y, size, _remaining.Height);
                _remaining = new Rect<T>(_remaining.X, _remaining.Y, _remaining.Width - size, _remaining.Height);
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
    public Rect<T> Fill() => _remaining;

    /// <summary>
    /// Replays the recorded dock sequence against a new root rectangle.
    /// </summary>
    public void Recompute(Rect<T> newRoot)
    {
        _remaining = newRoot;
        var count = _docks.Count;
        for (var i = 0; i < count; i++)
        {
            var (style, size) = _docks[i];
            switch (style)
            {
                case DockStyle.Top:
                    _remaining = new Rect<T>(_remaining.X, _remaining.Y + size, _remaining.Width, _remaining.Height - size);
                    break;
                case DockStyle.Bottom:
                    _remaining = new Rect<T>(_remaining.X, _remaining.Y, _remaining.Width, _remaining.Height - size);
                    break;
                case DockStyle.Left:
                    _remaining = new Rect<T>(_remaining.X + size, _remaining.Y, _remaining.Width - size, _remaining.Height);
                    break;
                case DockStyle.Right:
                    _remaining = new Rect<T>(_remaining.X, _remaining.Y, _remaining.Width - size, _remaining.Height);
                    break;
            }
        }
    }
}
