using System;
using System.Collections.Generic;
using System.Globalization;

namespace DIR.Lib;

/// <summary>The width of a run of text in the layout's own units; the only question a wrap asks of a font.</summary>
/// <remarks>
/// A delegate rather than a renderer so the wrap is a pure function of text, width and this. The paint hands it
/// the measure the single-line field uses (the fallback chain's <c>Measure</c>, or <c>MeasureText</c> on the
/// primary), and a test hands it a fixed advance per character.
/// </remarks>
public delegate float TextMeasure(ReadOnlySpan<char> text);

/// <summary>
/// One row of a wrapped text: the characters <c>Text[Start..End)</c>. A hard line ends at its <c>'\n'</c>
/// (<c>End</c> is the break's own index, which the next line starts one past) or at the end of the text; a soft
/// line ends where the width ran out, and the next starts exactly at its <c>End</c>.
/// </summary>
public readonly record struct VisualLine(int Start, int End, bool IsSoftWrapped);

/// <summary>
/// The visual-line geometry of a multi-line text: <see cref="TextEditorState"/>'s text wrapped to a width.
/// Pure -- text, width, line height and a <see cref="TextMeasure"/>, no surface -- so the wrap, the vertical
/// motions and the hit test are testable with a stub measure and no font.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where a line breaks.</b> At a <c>'\n'</c> always; otherwise when the next character would push the line
/// past the width, in this order of preference: after the last run of spaces (a word boundary), after the last
/// separator <c>/ \ . - _ :</c> (so a long path or URL breaks at a segment, not mid-name), and only for a run
/// with neither, at any grapheme boundary. A word that fits a line of its own is moved whole to the next line
/// rather than broken. A cluster is never split.
/// </para>
/// <para>
/// <b>Trailing spaces hang.</b> They stay on the line they follow and do not count against the width, so
/// typing a space at the edge of the box does not wrap by itself, and a line never begins with the spaces that
/// separated it from the one before.
/// </para>
/// <para>
/// <b>A caret at a soft break belongs to the next line.</b> The index where one line ends and the next starts
/// is one position and is drawn at the start of the lower line; the last caret position of a soft line is the
/// boundary before its end (<see cref="LastCaretIndex"/>). At a hard break the caret before the <c>'\n'</c> is
/// the end of its own line.
/// </para>
/// <para>
/// A <see cref="Width"/> that is not positive or not finite turns soft wrapping off: lines break at
/// <c>'\n'</c> only.
/// </para>
/// </remarks>
public sealed class TextEditorLayout
{
    private static readonly char[] Separators = ['/', '\\', '.', '-', '_', ':'];

    private readonly TextMeasure _measure;
    private readonly List<VisualLine> _lines = [];
    private readonly List<int> _bounds = [0];

    /// <summary>Wraps <paramref name="text"/> to <paramref name="width"/>, one <paramref name="lineHeight"/> per line.</summary>
    public TextEditorLayout(string text, float width, TextMeasure measure, float lineHeight)
    {
        Text = text ?? "";
        Width = width;
        LineHeight = lineHeight;
        _measure = measure ?? throw new ArgumentNullException(nameof(measure));
        Build();
    }

    /// <summary>The text this layout is of.</summary>
    public string Text { get; }

    /// <summary>The width lines wrap to, in the units of the measure.</summary>
    public float Width { get; }

    /// <summary>The height of one visual line.</summary>
    public float LineHeight { get; }

    /// <summary>The visual lines in order; there is always at least one, an empty text being one empty line.</summary>
    public IReadOnlyList<VisualLine> Lines => _lines;

    /// <summary>The number of visual lines.</summary>
    public int LineCount => _lines.Count;

    /// <summary>The height of every line together.</summary>
    public float ContentHeight => _lines.Count * LineHeight;

    /// <summary>The same wrap, measure and line height over <paramref name="text"/>.</summary>
    public TextEditorLayout WithText(string text) => new(text, Width, _measure, LineHeight);

    // ---- Geometry --------------------------------------------------------------------------------

    /// <summary>The visual line the caret at <paramref name="index"/> is drawn on (a soft break's index is the next line's).</summary>
    public int LineOf(int index)
    {
        index = Math.Clamp(index, 0, Text.Length);
        var lo = 0;
        var hi = _lines.Count - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (_lines[mid].Start <= index) lo = mid; else hi = mid - 1;
        }

        return lo;
    }

    /// <summary>The top of <paramref name="line"/>, in content coordinates (before any scroll).</summary>
    public float YOf(int line) => Math.Clamp(line, 0, _lines.Count - 1) * LineHeight;

    /// <summary>How far right of its line's start the caret at <paramref name="index"/> is drawn.</summary>
    public float XOf(int index)
    {
        index = Math.Clamp(index, 0, Text.Length);
        var line = _lines[LineOf(index)];
        return _measure(Text.AsSpan(line.Start, index - line.Start));
    }

    /// <summary>
    /// The last index the caret can have on <paramref name="line"/> and still be drawn there: the line's end for
    /// a hard line, the grapheme boundary before it for a soft one (whose end is the next line's start).
    /// </summary>
    public int LastCaretIndex(int line)
    {
        var l = _lines[Math.Clamp(line, 0, _lines.Count - 1)];
        return l.IsSoftWrapped ? Math.Max(l.Start, PreviousBoundary(l.End)) : l.End;
    }

    /// <summary>Home on a visual line: the start of the line the caret at <paramref name="index"/> is on.</summary>
    public int Home(int index) => _lines[LineOf(index)].Start;

    /// <summary>End on a visual line: the last caret position of the line the caret at <paramref name="index"/> is on.</summary>
    public int End(int index) => LastCaretIndex(LineOf(index));

    /// <summary>
    /// The caret index on <paramref name="line"/> nearest <paramref name="x"/> (a clamped line, an x before the
    /// start or past the end landing at the line's first or last caret position).
    /// </summary>
    public int IndexAtX(int line, float x)
    {
        line = Math.Clamp(line, 0, _lines.Count - 1);
        var l = _lines[line];
        var last = LastCaretIndex(line);
        if (x <= 0f || last <= l.Start)
        {
            return l.Start;
        }

        // Prefix widths are monotonic in the boundary, which is what lets this search them; the nearer of the two
        // boundaries around x wins, so the right half of a character means "after it".
        var lo = BoundaryAtOrAfter(l.Start);
        var hi = BoundaryAtOrAfter(last);
        float WidthTo(int b) => _measure(Text.AsSpan(l.Start, _bounds[b] - l.Start));

        var first = lo;
        var limit = hi;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (WidthTo(mid) <= x) lo = mid; else hi = mid - 1;
        }

        var pick = lo;
        if (lo < limit && WidthTo(lo + 1) - x < x - WidthTo(lo))
        {
            pick = lo + 1;
        }

        return _bounds[Math.Max(first, pick)];
    }

    /// <summary>
    /// The caret index under a point, in content coordinates (the box's top-left, scroll already added): a
    /// point above the first line is on it, a point right of a line's last character is at its end, and one
    /// below the last line is at the text's end.
    /// </summary>
    public int CaretIndexAt(float x, float y)
    {
        if (y < 0f)
        {
            return IndexAtX(0, x);
        }

        var line = (int)(y / LineHeight);
        return line >= _lines.Count ? Text.Length : IndexAtX(line, x);
    }

    /// <summary>
    /// Where the caret at <paramref name="index"/> lands after <paramref name="lines"/> visual lines down (up
    /// when negative), at <paramref name="preferredX"/>. Past the first line it is the text's start, past the
    /// last its end, as the logical-line motion of <see cref="TextEditorState"/> is.
    /// </summary>
    public int MoveVertically(int index, int lines, float preferredX)
    {
        var target = LineOf(index) + lines;
        if (target < 0)
        {
            return 0;
        }

        return target >= _lines.Count ? Text.Length : IndexAtX(target, preferredX);
    }

    // ---- Growth and scroll -----------------------------------------------------------------------

    /// <summary>
    /// The lines the box shows: as many as there are, but at least <paramref name="minLines"/> and at most
    /// <paramref name="maxLines"/> (a max below the min is the min).
    /// </summary>
    public int VisibleLines(int minLines, int maxLines)
    {
        minLines = Math.Max(1, minLines);
        return Math.Max(minLines, Math.Min(_lines.Count, Math.Max(1, maxLines)));
    }

    /// <summary>The height of the box: <see cref="VisibleLines"/> lines, so it grows with the text up to the max and then stops.</summary>
    public float ViewportHeight(int minLines, int maxLines) => VisibleLines(minLines, maxLines) * LineHeight;

    /// <summary>The largest scroll offset: how far the content overruns a viewport of <paramref name="viewportHeight"/>.</summary>
    public float MaxScroll(float viewportHeight) => Math.Max(0f, ContentHeight - viewportHeight);

    /// <summary>
    /// <paramref name="offset"/> moved the least that shows the whole of <paramref name="line"/> in a viewport
    /// of <paramref name="viewportHeight"/>, then held inside <c>[0, MaxScroll]</c> so a shrinking text drags the
    /// window back with it.
    /// </summary>
    public float RevealLine(int line, float offset, float viewportHeight)
    {
        var top = YOf(line);
        var bottom = top + LineHeight;
        if (top < offset)
        {
            offset = top;
        }
        else if (bottom > offset + viewportHeight)
        {
            offset = bottom - viewportHeight;
        }

        return Math.Clamp(offset, 0f, MaxScroll(viewportHeight));
    }

    // ---- Wrapping --------------------------------------------------------------------------------

    private void Build()
    {
        var wrap = Width > 0f && float.IsFinite(Width);
        var lineStart = 0;
        while (true)
        {
            var nl = Text.IndexOf('\n', lineStart);
            var lineEnd = nl < 0 ? Text.Length : nl;

            var firstBound = _bounds.Count - 1;
            for (var pos = lineStart; pos < lineEnd;)
            {
                pos += Math.Max(1, StringInfo.GetNextTextElementLength(Text.AsSpan(pos, lineEnd - pos)));
                _bounds.Add(pos);
            }

            WrapSegment(firstBound, _bounds.Count - 1, wrap);
            if (nl < 0)
            {
                break;
            }

            _bounds.Add(nl + 1);
            lineStart = nl + 1;
        }
    }

    /// <summary>Wraps the logical line between bound indexes <paramref name="first"/> and <paramref name="last"/>.</summary>
    private void WrapSegment(int first, int last, bool wrap)
    {
        var end = _bounds[last];
        var p = first;
        while (true)
        {
            var start = _bounds[p];
            if (!wrap || Fits(start, end))
            {
                _lines.Add(new VisualLine(start, end, false));
                return;
            }

            if (p + 1 >= last)
            {
                // A single cluster wider than the box: nothing to break.
                _lines.Add(new VisualLine(start, end, false));
                return;
            }

            // The furthest boundary that still fits, and always at least one cluster so the wrap advances.
            var lo = p + 1;
            var hi = last - 1;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) / 2;
                if (Fits(start, _bounds[mid])) lo = mid; else hi = mid - 1;
            }

            var next = ChooseBreak(start, _bounds[lo], end);
            _lines.Add(new VisualLine(start, next, true));
            p = BoundaryAtOrAfter(next);
        }
    }

    /// <summary>Where the line starting at <paramref name="start"/> ends, given that <c>Text[start..fit)</c> is the most that fits.</summary>
    private int ChooseBreak(int start, int fit, int lineEnd)
    {
        // The last word boundary, so the word that did not fit moves down whole. <fit> already runs over the
        // spaces after a word (they do not count against the width), so a word ending exactly at the edge
        // breaks here too. Spaces that only indent do not count: breaking after them would leave a line of nothing.
        var firstInk = start;
        while (firstInk < fit && IsBlank(Text[firstInk]))
        {
            firstInk++;
        }

        for (var j = fit; j > firstInk; j--)
        {
            if (IsBlank(Text[j - 1]) && !IsBlank(Text[j]) && IsBoundary(j))
            {
                return j;
            }
        }

        // One long word: after its last separator that fits. A break that leaves only separators on the row
        // ("/" of "/abcdefghij") would waste the row, so the leading run of separators is not a break point.
        var firstWord = start;
        while (firstWord < fit && Array.IndexOf(Separators, Text[firstWord]) >= 0)
        {
            firstWord++;
        }

        for (var j = fit; j > firstWord; j--)
        {
            if (Array.IndexOf(Separators, Text[j - 1]) >= 0 && IsBoundary(j))
            {
                return j;
            }
        }

        return fit;
    }

    /// <summary>Whether <c>Text[from..to)</c> fits the width, not counting the spaces it ends in.</summary>
    private bool Fits(int from, int to)
    {
        while (to > from && IsBlank(Text[to - 1]))
        {
            to--;
        }

        return _measure(Text.AsSpan(from, to - from)) <= Width;
    }

    private static bool IsBlank(char c) => c is ' ' or '\t';

    private int BoundaryAtOrAfter(int index)
    {
        var i = _bounds.BinarySearch(index);
        return i >= 0 ? i : ~i;
    }

    private bool IsBoundary(int index) => _bounds.BinarySearch(index) >= 0;

    private int PreviousBoundary(int index)
    {
        var i = BoundaryAtOrAfter(index);
        return _bounds[Math.Max(0, i - 1)];
    }
}
