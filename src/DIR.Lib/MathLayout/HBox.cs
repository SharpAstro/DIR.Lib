

namespace DIR.Lib.MathLayout;

/// <summary>
/// Horizontal concatenation of boxes on a shared baseline. The HBox's
/// <see cref="Box.Width"/> is the sum of children widths plus inter-child
/// spacing; <see cref="Box.Height"/> is the max ascent across children;
/// <see cref="Box.Depth"/> is the max descent. Children with smaller
/// ascent/descent simply leave space above/below them.
/// </summary>
public sealed class HBox : Box
{
    private readonly Box[] _children;
    private readonly float _spacing;
    private readonly float _width;
    private readonly float _height;
    private readonly float _depth;

    public HBox(params Box[] children) : this(0f, children) { }

    public HBox(float spacing, params Box[] children)
    {
        _children = children;
        _spacing = spacing;

        float w = 0, h = 0, d = 0;
        for (int i = 0; i < children.Length; i++)
        {
            if (i > 0) w += spacing;
            w += children[i].Width;
            if (children[i].Height > h) h = children[i].Height;
            if (children[i].Depth > d) d = children[i].Depth;
        }
        _width = w;
        _height = h;
        _depth = d;
    }

    public override float Width => _width;
    public override float Height => _height;
    public override float Depth => _depth;

    public override void Draw(RgbaImageRenderer renderer, float penX, float baselineY, BoxStyle style)
    {
        float x = penX;
        for (int i = 0; i < _children.Length; i++)
        {
            if (i > 0) x += _spacing;
            _children[i].Draw(renderer, x, baselineY, style);
            x += _children[i].Width;
        }
    }
}

/// <summary>
/// Pure horizontal whitespace — width but no glyph. Used to space binary
/// operators ("a + b" → HBox(a, kern, +, kern, b)) without committing to a
/// rendered space character.
/// </summary>
public sealed class KernBox(float width) : Box
{
    public override float Width => width;
    public override float Height => 0;
    public override float Depth => 0;
    public override void Draw(RgbaImageRenderer renderer, float penX, float baselineY, BoxStyle style) { }
}
