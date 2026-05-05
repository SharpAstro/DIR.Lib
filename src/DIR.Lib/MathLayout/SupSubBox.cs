

namespace DIR.Lib.MathLayout;

/// <summary>
/// Postfix script attached to a base box. Either a superscript (raised) or
/// a subscript (lowered), or both. The script's font is shrunk
/// (<see cref="BoxStyle.Smaller"/>) before its sub-tree is constructed —
/// callers pass already-rasterized smaller boxes via
/// <paramref name="sup"/> / <paramref name="sub"/>.
/// </summary>
public sealed class SupSubBox : Box
{
    private readonly Box _base;
    private readonly Box? _sup;
    private readonly Box? _sub;
    private readonly float _supShift;
    private readonly float _subShift;
    private readonly float _scriptKern;

    public SupSubBox(Box @base, Box? sup, Box? sub, BoxStyle style)
    {
        _base = @base;
        _sup = sup;
        _sub = sub;

        // TeX's superscript-shift-up "sup1" is roughly 0.4·em above the
        // baseline; subscript-shift-down "sub1" is roughly 0.15·em below.
        // Tweak slightly so the script doesn't collide with the base for
        // tall bases (e.g. \sqrt{x}^2 — the radical is the base, the
        // sup pulls up off the radical's top).
        _supShift = MathF.Max(style.FontSize * 0.45f, _base.Height * 0.7f);
        _subShift = MathF.Max(style.FontSize * 0.18f, _base.Depth * 0.6f);
        _scriptKern = style.FontSize * 0.04f;
    }

    public override float Width
    {
        get
        {
            float sw = MathF.Max(_sup?.Width ?? 0, _sub?.Width ?? 0);
            return _base.Width + (sw > 0 ? _scriptKern + sw : 0);
        }
    }

    public override float Height
    {
        get
        {
            float h = _base.Height;
            if (_sup is not null) h = MathF.Max(h, _supShift + _sup.Height);
            return h;
        }
    }

    public override float Depth
    {
        get
        {
            float d = _base.Depth;
            if (_sub is not null) d = MathF.Max(d, _subShift + _sub.Depth);
            return d;
        }
    }

    public override void Draw(RgbaImageRenderer renderer, float penX, float baselineY, BoxStyle style)
    {
        _base.Draw(renderer, penX, baselineY, style);
        float scriptX = penX + _base.Width + _scriptKern;
        if (_sup is not null)
        {
            // Sup baseline sits at (baseline - shift) — sup.Height is its
            // ascent, so the sup glyph occupies [baseline-shift-sup.Height,
            // baseline-shift+sup.Depth].
            _sup.Draw(renderer, scriptX, baselineY - _supShift, style);
        }
        if (_sub is not null)
        {
            _sub.Draw(renderer, scriptX, baselineY + _subShift, style);
        }
    }
}
