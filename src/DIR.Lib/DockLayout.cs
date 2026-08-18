using System.Collections.Generic;
using System.Numerics;

namespace DIR.Lib;

/// <summary>
/// Dock direction for <see cref="DockLayout{T}"/>.
/// </summary>
public enum DockStyle { Top, Bottom, Left, Right, Fill }

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
    /// <remarks>
    /// <paramref name="size"/> is <b>clamped to what is left</b>, so a strip can consume the remainder but
    /// never more. Requested extents are consumer-owned and routinely exceed the container once a window
    /// gets small -- the same reason the layout engine clamps a split's first extent. Unclamped, an
    /// over-large strip did two invisible things at once: it placed itself OUTSIDE the container (a Right
    /// strip resolves its x as <c>Right - size</c>, which walks left past <c>X</c>) and it left the fill
    /// rect with a NEGATIVE width. Neither reads as "does not fit" at the call site -- the strip paints
    /// over its own siblings, and the widget under it becomes unclickable, which is how it presented: a
    /// window narrow enough that an info panel overhung a split divider made that divider impossible to
    /// grab, while everything still looked drawn.
    /// </remarks>
    public Rect<T> Dock(DockStyle style, T size)
    {
        // Never negative, never more than remains on the axis this strip consumes.
        var axisAvail = style is DockStyle.Top or DockStyle.Bottom ? _remaining.Height : _remaining.Width;
        if (size < T.Zero)
        {
            size = T.Zero;
        }
        else if (style is not DockStyle.Fill && size > axisAvail)
        {
            size = axisAvail > T.Zero ? axisAvail : T.Zero;
        }

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
    /// <remarks>Clamps exactly as <see cref="Dock"/> does. The recorded sizes fitted the ORIGINAL root, so
    /// against a smaller one they can overrun; without the same clamp a replay would produce negative
    /// remainders that the original arrangement never had.</remarks>
    public void Recompute(Rect<T> newRoot)
    {
        _remaining = newRoot;
        var count = _docks.Count;
        for (var i = 0; i < count; i++)
        {
            var (style, recorded) = _docks[i];
            var axisAvail = style is DockStyle.Top or DockStyle.Bottom ? _remaining.Height : _remaining.Width;
            var size = recorded < T.Zero ? T.Zero
                : style is not DockStyle.Fill && recorded > axisAvail ? (axisAvail > T.Zero ? axisAvail : T.Zero)
                : recorded;
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
