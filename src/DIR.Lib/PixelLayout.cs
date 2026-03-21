namespace DIR.Lib;

/// <summary>
/// Dock direction for <see cref="PixelLayout"/>. Alias for <see cref="DockStyle"/>.
/// </summary>
public enum PixelDockStyle { Top = DockStyle.Top, Bottom = DockStyle.Bottom, Left = DockStyle.Left, Right = DockStyle.Right }

/// <summary>
/// Float-coordinate dock layout. Convenience wrapper around <see cref="DockLayout{T}"/>
/// using <see cref="RectF32"/> for pixel-based UI.
/// </summary>
public sealed class PixelLayout(RectF32 root)
{
    private readonly DockLayout<float> _inner = new(new Rect<float>(root.X, root.Y, root.Width, root.Height));

    /// <summary>
    /// Allocates a strip from the specified edge.
    /// </summary>
    public RectF32 Dock(PixelDockStyle style, float size)
    {
        var r = _inner.Dock((DockStyle)style, size);
        return new RectF32(r.X, r.Y, r.Width, r.Height);
    }

    /// <summary>
    /// Returns the remaining rectangle.
    /// </summary>
    public RectF32 Fill()
    {
        var r = _inner.Fill();
        return new RectF32(r.X, r.Y, r.Width, r.Height);
    }

    /// <summary>
    /// Replays docks against a new root.
    /// </summary>
    public void Recompute(RectF32 newRoot)
    {
        _inner.Recompute(new Rect<float>(newRoot.X, newRoot.Y, newRoot.Width, newRoot.Height));
    }
}
