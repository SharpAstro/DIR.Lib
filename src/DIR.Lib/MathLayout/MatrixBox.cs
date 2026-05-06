

namespace DIR.Lib.MathLayout;

/// <summary>
/// 2-D grid of cells, with per-column alignment and per-row baseline.
/// Surrounded by scalable brackets via <see cref="BracketBox"/> when the
/// caller wants <c>\pmatrix</c> / <c>\bmatrix</c> / <c>\Bmatrix</c>; bare
/// <c>\matrix</c> is just a MatrixBox without an outer BracketBox wrapper.
///
/// Per-column width is the max child width in that column; per-row height
/// is the max child Height (ascent), depth the max Depth. Inter-cell
/// spacing scales with the font size — 0.5em horizontal, 0.3em vertical.
/// The whole matrix's vertical centre lines up with the math axis (centre
/// of the row strip), matching TeX behaviour for inline <c>\pmatrix</c>.
/// </summary>
public sealed class MatrixBox : Box
{
    private readonly Box[,] _cells;
    private readonly float[] _colWidths;
    private readonly float[] _rowHeights;
    private readonly float[] _rowDepths;
    private readonly float _hSpacing;
    private readonly float _vSpacing;
    private readonly float _width;
    private readonly float _height;
    private readonly float _depth;

    public MatrixBox(Box[,] cells, BoxStyle style)
    {
        _cells = cells;
        _hSpacing = style.FontSize * 0.6f;
        _vSpacing = style.FontSize * 0.3f;

        int rows = cells.GetLength(0);
        int cols = cells.GetLength(1);
        _colWidths = new float[cols];
        _rowHeights = new float[rows];
        _rowDepths = new float[rows];

        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            var box = cells[r, c];
            if (box.Width    > _colWidths[c]) _colWidths[c] = box.Width;
            if (box.Height   > _rowHeights[r]) _rowHeights[r] = box.Height;
            if (box.Depth    > _rowDepths[r]) _rowDepths[r] = box.Depth;
        }

        _width = 0;
        for (int c = 0; c < cols; c++)
        {
            if (c > 0) _width += _hSpacing;
            _width += _colWidths[c];
        }

        // Total vertical extent — used to centre on the math axis.
        float totalH = 0;
        for (int r = 0; r < rows; r++)
        {
            totalH += _rowHeights[r] + _rowDepths[r];
            if (r < rows - 1) totalH += _vSpacing;
        }
        // Centre the matrix's vertical midpoint on the MATH AXIS (above the
        // line baseline by AxisHeight), not on the line baseline. Matches
        // MathJax/TeX behaviour for inline \pmatrix: a matrix sits beside
        // an '=' or '+' with its centre at the same level as the operator
        // glyph centres, so a 2-row matrix straddles the math axis. With
        // the previous baseline-centred placement, the whole matrix sat
        // visibly low — its midpoint fell on the baseline while operator
        // glyphs sat AxisHeight (~0.25 em) above it.
        var axis = style.AxisHeight;
        _height = totalH / 2f + axis;
        _depth  = totalH - _height;
    }

    public override float Width => _width;
    public override float Height => _height;
    public override float Depth => _depth;

    public override void Draw(RgbaImageRenderer renderer, float penX, float baselineY, BoxStyle style)
    {
        int rows = _cells.GetLength(0);
        int cols = _cells.GetLength(1);

        // Walk down rows, painting each row at its own baseline.
        float y = baselineY - _height; // top edge
        for (int r = 0; r < rows; r++)
        {
            float rowBaseline = y + _rowHeights[r];
            float x = penX;
            for (int c = 0; c < cols; c++)
            {
                if (c > 0) x += _hSpacing;
                // Centre-align cells within their column.
                float cellX = x + (_colWidths[c] - _cells[r, c].Width) / 2f;
                _cells[r, c].Draw(renderer, cellX, rowBaseline, style);
                x += _colWidths[c];
            }
            y += _rowHeights[r] + _rowDepths[r];
            if (r < rows - 1) y += _vSpacing;
        }
    }
}
