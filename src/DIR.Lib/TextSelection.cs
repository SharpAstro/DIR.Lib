using System.Text;

namespace DIR.Lib;

/// <summary>
/// Drags the mouse over the text a page marked selectable (see <see cref="SelectableTextRegion"/>) and keeps what it covered: the one selection
/// component for a raster host, where <see cref="PixelWidgetBase{TSurface}"/> only RECORDS <see cref="SelectableTextRegion"/>s (the web
/// host overlays real spans, a terminal its own drag-select) and the glyphs are the host's own. A view lays a Fill leaf over its
/// page, calls <see cref="Frame"/> and <see cref="Paint"/> from that leaf, and gives <see cref="HandleInput"/> its pointer events
/// ahead of the router; the copy is the view's own (<see cref="Text"/> to its <c>CopyText</c>), so any host can use
/// the same component beside its own menus.
/// <para>
/// A selection is a range of positions (a region of the frame in paint order, and a character in it), so it follows its text when
/// the page scrolls. A frame whose regions no longer hold the text it was made on (the page changed under it) clears it.
/// </para>
/// </summary>
public sealed class TextSelection<TSurface>(Renderer<TSurface> renderer)
{
    /// <summary>A place in the text: the region (in the frame's paint order) and the character before which it sits.</summary>
    private readonly record struct Spot(int Region, int Char) : IComparable<Spot>
    {
        public int CompareTo(Spot other) => Region != other.Region ? Region.CompareTo(other.Region) : Char.CompareTo(other.Char);
    }

    private readonly List<SelectableTextRegion> _frame = [];
    private RectF32 _viewport;
    private Spot? _anchor;
    private string _anchorText = "";
    private Spot? _caret;
    private string _caretText = "";
    private bool _dragging;

    /// <summary>The pointer's last place over the page, for the "Copied" notice to appear by.</summary>
    public (float X, float Y) Pointer { get; private set; }

    /// <summary>Whether a drag is in progress: the press that started it has not been let go.</summary>
    public bool Dragging => _dragging;

    /// <summary>Whether any text is selected (a press with no drag selects nothing).</summary>
    public bool HasSelection => _anchor is { } a && _caret is { } c && a != c && Range() is not null;

    /// <summary>Lets go of the selection.</summary>
    public void Clear()
    {
        _anchor = _caret = null;
        _dragging = false;
    }

    /// <summary>
    /// Takes the frame being painted: its regions, in paint order, and the rect a scrolled part of the page shows through (empty
    /// when nothing scrolls). Call it from the page's Fill leaf, which is painted after the text and before any menu laid over
    /// it. A frame that no longer holds the selected text drops the selection, unless the same text sits nearby.
    /// </summary>
    public void Frame(ReadOnlySpan<SelectableTextRegion> regions, RectF32 viewport)
    {
        _frame.Clear();
        _frame.AddRange(regions);
        _viewport = viewport;
        if (_anchor is { } a && _caret is { } c)
        {
            var anchor = Relocate(a, _anchorText);
            var caret = Relocate(c, _caretText);
            if (anchor is null || caret is null)
            {
                Clear();
            }
            else
            {
                _anchor = anchor;
                _caret = caret;
            }
        }
    }

    // The spot where the same text now is, searching outward from where it was: a page that gains a line above keeps its selection.
    private Spot? Relocate(Spot spot, string text)
    {
        bool Holds(int i) => i >= 0 && i < _frame.Count && _frame[i].Text == text;
        if (Holds(spot.Region))
        {
            return spot;
        }
        for (var distance = 1; distance < _frame.Count; distance++)
        {
            if (Holds(spot.Region - distance))
            {
                return spot with { Region = spot.Region - distance };
            }
            if (Holds(spot.Region + distance))
            {
                return spot with { Region = spot.Region + distance };
            }
        }
        return null;
    }

    // A region's rect as it shows: a region that straddles the scrolled part's edge is cut to it, one wholly outside or inside is not.
    private RectF32 Shown(int i)
    {
        var r = _frame[i];
        var rect = new RectF32(r.X, r.Y, r.Width, r.Height);
        if (_viewport is not { Width: > 0, Height: > 0 } v)
        {
            return rect;
        }
        var left = MathF.Max(rect.X, v.X);
        var top = MathF.Max(rect.Y, v.Y);
        var right = MathF.Min(rect.X + rect.Width, v.X + v.Width);
        var bottom = MathF.Min(rect.Y + rect.Height, v.Y + v.Height);
        var inside = rect.X >= v.X && rect.Y >= v.Y && rect.X + rect.Width <= v.X + v.Width && rect.Y + rect.Height <= v.Y + v.Height;
        // Wholly outside (the page's own head and foot, which never scroll) or wholly inside shows as it is; only a straddler is cut.
        return inside || right <= left || bottom <= top ? rect : new RectF32(left, top, right - left, bottom - top);
    }

    private float Measure(string text, int length, SelectableTextRegion r) =>
        length <= 0 ? 0f : renderer.MeasureText(text.AsSpan(0, length), r.FontPath, r.FontSize).Width;

    // The x the region's text starts at: its rect's left edge, or where its alignment puts it when the text is narrower.
    private float Origin(SelectableTextRegion r) => r.HorizontalAlign switch
    {
        TextAlign.Center => r.X + MathF.Max(0f, r.Width - Measure(r.Text, r.Text.Length, r)) / 2f,
        TextAlign.Far => r.X + MathF.Max(0f, r.Width - Measure(r.Text, r.Text.Length, r)),
        _ => r.X,
    };

    // The character boundary nearest x, by the widths the renderer draws the text at; never between the halves of a surrogate pair.
    private int CharAt(SelectableTextRegion r, float x)
    {
        var local = x - Origin(r);
        if (local <= 0f)
        {
            return 0;
        }
        var length = r.Text.Length;
        if (local >= Measure(r.Text, length, r))
        {
            return length;
        }
        int lo = 0, hi = length;
        while (hi - lo > 1)
        {
            var mid = (lo + hi) / 2;
            if (Measure(r.Text, mid, r) <= local)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }
        var before = Measure(r.Text, lo, r);
        var after = Measure(r.Text, hi, r);
        var at = local - before < after - local ? lo : hi;
        return at > 0 && at < length && char.IsLowSurrogate(r.Text[at]) ? at + 1 : at;
    }

    // The topmost region whose shown rect holds the point, or -1.
    private int RegionAt(float x, float y)
    {
        for (var i = _frame.Count - 1; i >= 0; i--)
        {
            var rect = Shown(i);
            if (x >= rect.X && x < rect.X + rect.Width && y >= rect.Y && y < rect.Y + rect.Height)
            {
                return i;
            }
        }
        return -1;
    }

    // Where a drag at the point has got to: in a region, its character; between lines or words, the nearest one's nearest end;
    // above or below all the text, the first or last character.
    private Spot? Nearest(float x, float y)
    {
        var best = -1;
        float bestDy = float.MaxValue, bestDx = float.MaxValue, top = float.MaxValue, bottom = float.MinValue;
        int first = -1, last = -1;
        for (var i = 0; i < _frame.Count; i++)
        {
            var rect = Shown(i);
            if (rect.Width <= 0f || rect.Height <= 0f)
            {
                continue;
            }
            first = first < 0 ? i : first;
            last = i;
            top = MathF.Min(top, rect.Y);
            bottom = MathF.Max(bottom, rect.Y + rect.Height);
            var dy = y < rect.Y ? rect.Y - y : y > rect.Y + rect.Height ? y - rect.Y - rect.Height : 0f;
            var dx = x < rect.X ? rect.X - x : x > rect.X + rect.Width ? x - rect.X - rect.Width : 0f;
            // The later region wins a tie: it is the one painted over.
            if (dy < bestDy || (dy == bestDy && dx <= bestDx))
            {
                (best, bestDy, bestDx) = (i, dy, dx);
            }
        }
        if (best < 0)
        {
            return null;
        }
        if (y < top)
        {
            return new Spot(first, 0);
        }
        if (y > bottom)
        {
            return new Spot(last, _frame[last].Text.Length);
        }
        return new Spot(best, CharAt(_frame[best], x));
    }

    // The range in order, or null when a spot no longer names a character of the frame.
    private (Spot From, Spot To)? Range()
    {
        if (_anchor is not { } a || _caret is not { } c)
        {
            return null;
        }
        var (from, to) = a.CompareTo(c) <= 0 ? (a, c) : (c, a);
        return from.Region >= 0 && to.Region < _frame.Count && from.Char <= _frame[from.Region].Text.Length && to.Char <= _frame[to.Region].Text.Length
            ? (from, to)
            : null;
    }

    // The characters of region i the selection covers.
    private (int From, int To) Covered(int i, (Spot From, Spot To) range) =>
        (i == range.From.Region ? range.From.Char : 0, i == range.To.Region ? range.To.Char : _frame[i].Text.Length);

    /// <summary>
    /// The selected text, or null with none: the regions in paint order, a line break where the next region starts a new row, a
    /// space where a visible gap lies between two on one row that neither ends or begins in one.
    /// </summary>
    public string? Text
    {
        get
        {
            if (!HasSelection || Range() is not { } range)
            {
                return null;
            }
            var text = new StringBuilder();
            SelectableTextRegion? previous = null;
            for (var i = range.From.Region; i <= range.To.Region; i++)
            {
                var (from, to) = Covered(i, range);
                if (to <= from)
                {
                    continue;
                }
                var r = _frame[i];
                var piece = r.Text[from..to];
                if (previous is { } p)
                {
                    var sameRow = MathF.Abs(r.Y + r.Height / 2f - (p.Y + p.Height / 2f)) <= MathF.Min(p.Height, r.Height) / 2f;
                    if (!sameRow)
                    {
                        while (text.Length > 0 && text[^1] == ' ')
                        {
                            text.Length--;
                        }
                        text.Append('\n');
                    }
                    // The gap is measured from where the previous text ends, not from its rect (a list's marker has the column's).
                    else if (Origin(r) - (Origin(p) + Measure(p.Text, p.Text.Length, p)) > r.FontSize * 0.2f && text.Length > 0 && !char.IsWhiteSpace(text[^1]) && !char.IsWhiteSpace(piece[0]))
                    {
                        text.Append(' ');
                    }
                }
                text.Append(piece);
                previous = r;
            }
            var said = text.ToString().TrimEnd();
            return said.Length == 0 ? null : said;
        }
    }

    // The rect the characters from..to of region i are highlighted over: as wide as they measure, as tall as a line of them.
    private RectF32 Bar(int i, int from, int to)
    {
        var r = _frame[i];
        var origin = Origin(r);
        var left = origin + Measure(r.Text, from, r);
        var right = origin + Measure(r.Text, to, r);
        var line = renderer.MeasureText(r.Text.AsSpan(from, to - from), r.FontPath, r.FontSize).Height;
        line = MathF.Min(line, r.Height);
        var top = r.VerticalAlign switch
        {
            TextAlign.Near => r.Y,
            TextAlign.Far => r.Y + r.Height - line,
            _ => r.Y + (r.Height - line) / 2f,
        };
        return new RectF32(left, top, right - left, line);
    }

    /// <summary>Whether the point is over selected text: where a right-click offers Copy.</summary>
    public bool Contains(float x, float y)
    {
        if (!HasSelection || Range() is not { } range)
        {
            return false;
        }
        for (var i = range.From.Region; i <= range.To.Region; i++)
        {
            var (from, to) = Covered(i, range);
            if (to > from && Bar(i, from, to) is var bar && x >= bar.X && x < bar.X + bar.Width && y >= bar.Y && y < bar.Y + bar.Height
                && RegionAt(x, y) == i)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The text of one region drawn at a place: the view's own text drawing, which knows the fallback fonts the page was painted
    /// with. Left-aligned, <paramref name="region"/>'s font, size and vertical alignment.
    /// </summary>
    public delegate void DrawGlyphs(ReadOnlySpan<char> text, SelectableTextRegion region, float x, float y, float width, float height, RGBAColor32 color);

    /// <summary>
    /// Paints the selection over the page: a bar in <paramref name="background"/> under each covered stretch, and its glyphs
    /// drawn again on it in <paramref name="foreground"/>, so the text sits ON the highlight and is read against it (a
    /// translucent wash would be read against the panel and the glyph's own colour, which the palette does not promise).
    /// </summary>
    public void Paint(DrawGlyphs draw, RGBAColor32 background, RGBAColor32 foreground)
    {
        if (!HasSelection || Range() is not { } range)
        {
            return;
        }
        for (var i = range.From.Region; i <= range.To.Region; i++)
        {
            var (from, to) = Covered(i, range);
            if (to <= from)
            {
                continue;
            }
            var r = _frame[i];
            var bar = Bar(i, from, to);
            var shown = Shown(i);
            // Only a region cut by the scrolled part's edge is clipped; the rest is painted whole.
            var cut = shown.X != r.X || shown.Y != r.Y || shown.Width != r.Width || shown.Height != r.Height;
            if (cut)
            {
                renderer.PushClip(Rect(shown));
            }
            renderer.FillRectangle(Rect(bar), background);
            draw(r.Text.AsSpan(from, to - from), r, bar.X, r.Y, bar.Width + 2f, r.Height, foreground);
            if (cut)
            {
                renderer.PopClip();
            }
        }
    }

    private static RectInt Rect(RectF32 r) =>
        new(new PointInt((int)MathF.Ceiling(r.X + r.Width), (int)MathF.Ceiling(r.Y + r.Height)), new PointInt((int)MathF.Floor(r.X), (int)MathF.Floor(r.Y)));

    /// <summary>
    /// A left press over selectable text starts a selection (two presses select the word), a move with it held extends it, its
    /// release ends the drag; a left press anywhere else lets the selection go. True for what it took. <paramref name="inert"/>
    /// says whether the point is over nothing that acts on a press (a button, a link, a field, a scrollbar): a press on one of those
    /// is the router's, and starts no selection.
    /// </summary>
    public bool HandleInput(InputEvent evt, Func<float, float, bool> inert)
    {
        switch (evt)
        {
            case InputEvent.MouseMove { X: var mx, Y: var my }:
                Pointer = (mx, my);
                if (!_dragging)
                {
                    return false;
                }
                if (Nearest(mx, my) is { } caret)
                {
                    SetCaret(caret);
                }
                return true;
            case InputEvent.MouseDown { Button: MouseButton.Left, X: var x, Y: var y, ClickCount: var clicks }:
                Pointer = (x, y);
                Clear();
                if (RegionAt(x, y) is var at and >= 0 && inert(x, y))
                {
                    var spot = new Spot(at, CharAt(_frame[at], x));
                    if (clicks >= 2)
                    {
                        (_anchor, _caret) = Word(spot);
                        _anchorText = _caretText = _frame[at].Text;
                    }
                    else
                    {
                        _anchor = _caret = spot;
                        _anchorText = _caretText = _frame[at].Text;
                        _dragging = true;
                    }
                    return true;
                }
                return false;
            case InputEvent.MouseUp { Button: MouseButton.Left } when _dragging:
                _dragging = false;
                if (_anchor == _caret)
                {
                    // A press that went nowhere selects nothing.
                    Clear();
                }
                return true;
            default:
                return false;
        }
    }

    private void SetCaret(Spot caret)
    {
        _caret = caret;
        _caretText = _frame[caret.Region].Text;
    }

    // The word the spot is in: the run of characters round it that are not white space.
    private (Spot, Spot) Word(Spot spot)
    {
        var text = _frame[spot.Region].Text;
        int from = Math.Min(spot.Char, text.Length), to = from;
        while (from > 0 && !char.IsWhiteSpace(text[from - 1]))
        {
            from--;
        }
        while (to < text.Length && !char.IsWhiteSpace(text[to]))
        {
            to++;
        }
        return (spot with { Char = from }, spot with { Char = to });
    }
}
