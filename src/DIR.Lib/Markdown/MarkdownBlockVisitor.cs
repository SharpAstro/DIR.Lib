using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DIR.Lib.MathLayout;
using LALR.CC.LexicalGrammar;

namespace DIR.Lib.Markdown;

/// <summary>
/// Phase-C visitor over the <c>markdown-block.lalr.yaml</c> grammar.
/// The grammar tokenises the input into a flat sequence of items where
/// each item is either a content line or a blank separator. The
/// visitor's <see cref="Parse"/> then groups consecutive content lines
/// into blocks and classifies each group's first line via
/// <see cref="ClassifyBlock"/> to produce the right <see cref="MdBlock"/>
/// subtype. Paragraph-style blocks have their joined-line text run
/// through <see cref="MarkdownInlineVisitor"/> for inline parsing.
///
/// <para>This grammar-as-tokeniser, visitor-as-classifier split sidesteps
/// the LR(1) reduce-reduce conflict that pure-grammar block parsing
/// produces — the parser can't tell "continue the current block's
/// Lines" from "start the next block's Lines" without a lexer-level
/// separator-vs-newline distinction, and adding that distinction means
/// either a stateful lexer (push on first \n, pop on second) or a
/// custom merge that LALR.CC doesn't expose.</para>
///
/// <para>List and table detection is also visitor-level prefix
/// inspection rather than dedicated grammar productions. Keeping the
/// LR layer at the line level and lifting structural detection into
/// C# is the same trade-off the inline grammar makes for emphasis
/// (delimiter-stack post-pass) and bracket disambiguation.</para>
/// </summary>
public sealed partial class MarkdownBlockVisitor : MarkdownBlock.IVisitor<object>
{
    private readonly MarkdownInlineVisitor _inline = new();
    private readonly Func<string, IReadOnlyList<MdInline>> _parseInline;

    public MarkdownBlockVisitor() => _parseInline = _inline.Parse;

    /// <summary>Test seam: <paramref name="parseInline"/> stands in for the
    /// inline parser, so a test can make classification throw and reach
    /// <see cref="ClassifyBlockSafe"/>'s fallback on purpose.</summary>
    internal MarkdownBlockVisitor(Func<string, IReadOnlyList<MdInline>> parseInline) =>
        _parseInline = parseInline;

    /// <summary>Parses a full markdown document and returns the list
    /// of blocks. Returns an empty list on parse error.</summary>
    public IReadOnlyList<MdBlock> Parse(string source)
    {
        if (string.IsNullOrEmpty(source)) return Array.Empty<MdBlock>();
        // Markdig's parsers tolerate input that doesn't end in a newline;
        // the LR(1) grammar expects every line to end with `newline`. Add
        // a trailing newline if the caller didn't.
        if (source[source.Length - 1] != '\n') source += '\n';

        IReadOnlyList<RawItem> items;
        try
        {
            using var lexer = global::LALR.CC.LexicalGrammar.BytesLexer.FromString(source, s_lexerTable);
            using var tokens = new global::LALR.CC.LexicalGrammar.SyncLATokenIterator(lexer);
            var result = s_parser.ParseInput(tokens, debugger: null);
            if (result.IsError) return Array.Empty<MdBlock>();
            items = result.Content is ItemsBuilder built ? built.ToList() : Array.Empty<RawItem>();
        }
        catch (global::LALR.CC.ParseErrorException)
        {
            return Array.Empty<MdBlock>();
        }

        return GroupIntoBlocks(items);
    }

    // ── Items list (epsilon-base + cons; flat list of RawItems) ──────

    public object Visit(MarkdownBlock.ItemsEmpty node) => new ItemsBuilder();

    public object Visit(MarkdownBlock.ItemsCons node) =>
        ((ItemsBuilder)node.Arg1.Content).Prepend((RawItem)node.Arg0.Content);

    /// <summary>An <c>Items</c> being reduced. The rule is right-recursive, so
    /// the lines arrive last first, and copying the rest into a new list for
    /// each one made a document's assembly quadratic in its lines (about 4 MB
    /// for Console.Lib's CHANGELOG). They are gathered back to front instead and
    /// put in order once, as <see cref="MarkdownInlineVisitor"/> does for spans.
    /// One builder per parse, made by that parse's own reductions, so the
    /// shared static parser holds no state between calls.</summary>
    private sealed class ItemsBuilder
    {
        private readonly List<RawItem> _backToFront = new();

        public ItemsBuilder Prepend(RawItem item)
        {
            _backToFront.Add(item);
            return this;
        }

        public IReadOnlyList<RawItem> ToList()
        {
            var items = new List<RawItem>(_backToFront);
            items.Reverse();
            return items;
        }
    }

    public object Visit(MarkdownBlock.LineItem node) =>
        new RawItem((string)node.Arg0.Content, IsBlank: false);

    public object Visit(MarkdownBlock.BlankItem node) =>
        new RawItem(string.Empty, IsBlank: true);

    // ── Group lines into blocks ──────────────────────────────────────

    private IReadOnlyList<MdBlock> GroupIntoBlocks(IReadOnlyList<RawItem> items) =>
        ParseLines(items.Select(item => item.IsBlank ? string.Empty : item.Line).ToList());

    private sealed record RawItem(string Line, bool IsBlank);

    /// <summary>What a run of lines is building, which decides what may interrupt it.</summary>
    private enum Run { None, Paragraph, Table }

    /// <summary>Splits lines into blocks and classifies each. A blank line
    /// ends a block, and so does a line that starts a block CommonMark lets
    /// interrupt it (<see cref="Interrupts"/>); a heading or a thematic break is
    /// one line, so the line after it always starts afresh. This used to split
    /// at blank lines only, which folded a heading, a list or a fence written
    /// on the line after text into that text's paragraph: GitHub's renderer
    /// and VS Code's preview split every one of them.
    /// <para>A fenced code block runs to its closing fence, blank lines
    /// included, per CommonMark; without that a fence is shredded at its first
    /// blank line and its orphaned closer is lexed as inline code. A
    /// display-math block opened by a lone <c>$$</c> or <c>\[</c> runs to its
    /// closer. A list is built where it starts, by <see cref="BuildList"/> on
    /// the lines from there on, which knows where it ends (a loose list stays
    /// one list), and the loop goes on after the lines it took. Lists used to
    /// be gathered first by a cruder blank-line rule, and what
    /// <see cref="BuildList"/> did not take was parsed again by recursion: a
    /// fence too little indented for its item was cut at its first blank line,
    /// and a document of lists that kept breaking recursed once per break and
    /// overflowed the stack (PR #129 round 1). Used for the document and,
    /// through <see cref="BuildList"/>, for each list item's lines.</para></summary>
    private IReadOnlyList<MdBlock> ParseLines(IReadOnlyList<string> lines, int depth = 0)
    {
        var blocks = new List<MdBlock>();
        var current = new List<string>();
        var run = Run.None;
        string? fence = null;       // non-null while inside an open code fence
        string? mathCloser = null;  // non-null while inside a display-math block

        void Flush()
        {
            if (current.Count > 0)
            {
                blocks.Add(ClassifyBlockSafe(current));
                current = new List<string>();
            }
            run = Run.None;
            fence = null;
            mathCloser = null;
        }

        for (int n = 0; n < lines.Count; n++)
        {
            var line = lines[n];
            if (fence is not null)
            {
                current.Add(line);
                if (line.TrimStart().StartsWith(fence, StringComparison.Ordinal)) Flush();
                continue;
            }
            if (IsBlankLine(line))
            {
                Flush();   // a blank line also ends a display-math block left open
                continue;
            }
            if (mathCloser is not null)
            {
                current.Add(line);
                if (line.Trim() == mathCloser) Flush();
                continue;
            }
            if (current.Count > 0)
            {
                if (run == Run.Paragraph && IsTableStart(current, line))
                    run = Run.Table;
                else if (run == Run.Paragraph && SetextRx.Match(line) is { Success: true } underline)
                {
                    blocks.Add(SetextHeadingSafe(current, underline.Groups[1].Value[0] == '=' ? 1 : 2));
                    current = new List<string>();
                    run = Run.None;
                    continue;
                }
                else if (Interrupts(run, lines, n))
                    Flush();
            }
            if (current.Count == 0)
            {
                if (HeadingRx.IsMatch(line) || ThematicRx.IsMatch(line))
                {
                    current.Add(line);
                    Flush();
                    continue;
                }
                if (ListItemRx.IsMatch(line))
                {
                    n += BuildListSafe(lines, n, blocks, depth) - 1;
                    continue;
                }
                var opener = FenceOpenRx.Match(line);
                if (opener.Success) fence = opener.Groups["fence"].Value;
                else if (IsMathOpener(line)) mathCloser = line.Trim() == "$$" ? "$$" : @"\]";
                else run = Run.Paragraph;
            }
            current.Add(line);
        }
        Flush();
        return blocks;
    }

    /// <summary>Whether <paramref name="lines"/>[<paramref name="n"/>] ends the
    /// run being built and starts a block of its own. A paragraph gives way to a
    /// heading, a fence, a thematic break, a display-math opener with its closer
    /// ahead, a bullet item and an ordered item numbered 1, the last two only
    /// with text after the marker (CommonMark 5.2: "14.  The number of doors"
    /// on the line after prose stays prose), and to a table's header row, as in
    /// GFM, inside a list item as at the top (a table under an item's first
    /// line was its prose). A table gives way to any block start or item.</summary>
    private static bool Interrupts(Run run, IReadOnlyList<string> lines, int n)
    {
        var line = lines[n];
        return run switch
        {
            Run.Paragraph => StartsBlock(lines, n) || ItemInterruptsParagraph(line) || IsTableHeader(lines, n),
            Run.Table => StartsBlock(lines, n) || ListItemRx.IsMatch(line),
            _ => false,
        };
    }

    /// <summary>Whether <paramref name="lines"/>[<paramref name="n"/>] is a table's
    /// header row: it holds a <c>|</c> and the line after it is a delimiter row.</summary>
    private static bool IsTableHeader(IReadOnlyList<string> lines, int n) =>
        lines[n].Contains('|') && n + 1 < lines.Count && IsDelimiterRowFor(lines[n], lines[n + 1]);

    private static bool StartsBlock(IReadOnlyList<string> lines, int n)
    {
        var line = lines[n];
        if (StartsSingleLineOrFence(line)) return true;
        if (!IsMathOpener(line)) return false;
        // A lone `$$` interrupts only with its closer ahead in the same run of
        // lines; otherwise it may be closing a `$$` opened inside the text.
        var closer = line.Trim() == "$$" ? "$$" : @"\]";
        for (int k = n + 1; k < lines.Count && !IsBlankLine(lines[k]); k++)
            if (lines[k].Trim() == closer) return true;
        return false;
    }

    private static bool StartsSingleLineOrFence(string line) =>
        HeadingRx.IsMatch(line) || FenceOpenRx.IsMatch(line) || ThematicRx.IsMatch(line);

    private static bool IsMathOpener(string line) => line.Trim() is "$$" or @"\[";

    private static bool ItemInterruptsParagraph(string line)
    {
        var m = ListItemRx.Match(line);
        return m.Success
            && !string.IsNullOrWhiteSpace(m.Groups[5].Value)
            && (!m.Groups[3].Success || m.Groups[3].Value.TrimStart('0') == "1");
    }

    private static bool IsTableStart(IReadOnlyList<string> current, string line) =>
        current.Count == 1 && current[0].Contains('|') && IsDelimiterRowFor(current[0], line);

    /// <summary>A paragraph underlined by <c>===</c> or <c>---</c>: a heading
    /// of its lines, trimmed, with their soft breaks.</summary>
    private MdBlock SetextHeadingSafe(IReadOnlyList<string> lines, int level)
    {
        var text = string.Join("\n", lines.Select(l => l.Trim()));
        try { return new MdHeading(level, ParseInline(text)); }
        catch { return new MdHeading(level, LiteralLines(text)); }
    }

    /// <summary>The column a line's text starts at, a tab advancing to the next multiple of four.</summary>
    private static int Indentation(string line)
    {
        int col = 0;
        foreach (var c in line)
        {
            if (c == ' ') col++;
            else if (c == '\t') col += 4 - col % 4;
            else break;
        }
        return col;
    }

    /// <summary><paramref name="line"/> without its first <paramref name="columns"/>
    /// columns of indentation; a tab that straddles the cut leaves its remainder as spaces.</summary>
    private static string Unindent(string line, int columns)
    {
        int col = 0, i = 0;
        while (i < line.Length && col < columns && (line[i] == ' ' || line[i] == '\t'))
        {
            var next = line[i] == '\t' ? col + 4 - col % 4 : col + 1;
            if (next > columns) return new string(' ', next - columns) + line.Substring(i + 1);
            col = next;
            i++;
        }
        return line.Substring(i);
    }

    // ── Block classification ──────────────────────────────────────────

    [GeneratedRegex(@"^ {0,3}(#{1,6})(?:[ \t]+(.*?))?[ \t]*$")]
    private static partial Regex HeadingRx { get; }
    [GeneratedRegex(@"(?:^|[ \t]+)#+$")]
    private static partial Regex HeadingCloseRx { get; }
    [GeneratedRegex(@"^ {0,3}(=+|-+)[ \t]*$")]
    private static partial Regex SetextRx { get; }
    [GeneratedRegex(@"^ {0,3}([-*_])(?:[ \t]*\1){2,}[ \t]*$")]
    private static partial Regex ThematicRx { get; }
    /// <summary>A fence's opening line: the fence and its language. A backtick
    /// fence's info string holds no backtick; a tilde fence's may (CommonMark
    /// example 146), which this used to refuse.</summary>
    [GeneratedRegex(
        @"^ {0,3}(?:(?<fence>`{3,})[ \t]*(?<lang>[^\s`]*)[^`]*|(?<fence>~{3,})[ \t]*(?<lang>\S*).*)$")]
    private static partial Regex FenceOpenRx { get; }

    /// <summary>A list item's first line: indent, marker (a bullet, or a number
    /// with its <c>.</c> or <c>)</c>), the number alone, the gap, then the text.</summary>
    [GeneratedRegex(@"^( {0,3})([-*+]|(\d{1,9})[.)])(?:([ \t]+)(.*))?$")]
    private static partial Regex ListItemRx { get; }

    /// <summary>Classifies a block, degrading to a raw-text paragraph on any
    /// parser/lexer error instead of throwing. Blank lines are the grouping
    /// sync points (Vim-style resync), so one malformed block can't abort the
    /// whole document — the next block is classified from a clean state. This
    /// matches the renderer's best-effort contract: show the source rather
    /// than crash. A genuine classification regression still surfaces in tests
    /// (the AST won't match the expected node type). The raw text is built by
    /// <see cref="LiteralLines"/>, as the inline fallback is, so it reflows.</summary>
    private MdBlock ClassifyBlockSafe(IReadOnlyList<string> lines)
    {
        try { return ClassifyBlock(lines); }
        catch
        {
            return new MdParagraph(LiteralLines(string.Join("\n", lines)));
        }
    }

    /// <summary>The inline content of a paragraph, heading, list item or
    /// table cell. When the inline parse fails, the text stays literal
    /// (<see cref="LiteralLines"/>) rather than leaving the block empty. No
    /// ordinary input is known to fail it any more (an unclosed link URL,
    /// the old example, is text since the stray-delimiter rules); the
    /// fallback stays for whatever still does, and tests reach it through
    /// the parser seam. The inline parser itself keeps returning an empty
    /// list on failure; only a block knows its text has to show.</summary>
    private IReadOnlyList<MdInline> ParseInline(string text)
    {
        var spans = _parseInline(text);
        return spans.Count == 0 && !string.IsNullOrWhiteSpace(text)
            ? LiteralLines(text)
            : spans;
    }

    /// <summary>The fallback literal, split at each newline into a soft
    /// break as the inline grammar's <c>soft_break</c> would. A newline
    /// left inside one literal would not reflow, and a consumer would draw
    /// it as a hard break.</summary>
    private static IReadOnlyList<MdInline> LiteralLines(string text)
    {
        if (text.IndexOf('\n') < 0) return new MdInline[] { new MdLiteral(text) };
        var lines = text.Split('\n');
        var spans = new List<MdInline>(2 * lines.Length - 1);
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) spans.Add(new MdLineBreak(Hard: false));
            if (lines[i].Length > 0) spans.Add(new MdLiteral(lines[i]));
        }
        return spans;
    }

    private MdBlock ClassifyBlock(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return new MdParagraph(Array.Empty<MdInline>());

        var first = lines[0];

        // Heading, a single-line construct. A closing run of `#` goes only
        // after a space, so `# C#` keeps its `#`.
        var headingMatch = HeadingRx.Match(first);
        if (headingMatch.Success && lines.Count == 1)
        {
            var level = headingMatch.Groups[1].Value.Length;
            var text = HeadingCloseRx.Replace(headingMatch.Groups[2].Value, string.Empty);
            return new MdHeading(level, ParseInline(text));
        }

        // Thematic break — single-line construct.
        if (lines.Count == 1 && ThematicRx.IsMatch(first))
            return new MdThematicBreak();

        // Fenced code — opener fence on first line.
        if (FenceOpenRx.IsMatch(first))
        {
            var openerMatch = FenceOpenRx.Match(first);
            var lang = string.IsNullOrEmpty(openerMatch.Groups["lang"].Value)
                ? null
                : openerMatch.Groups["lang"].Value;
            var fence = openerMatch.Groups["fence"].Value;
            var bodyLines = new List<string>(lines.Count - 1);
            for (int i = 1; i < lines.Count; i++)
            {
                if (lines[i].TrimStart().StartsWith(fence)) break;
                bodyLines.Add(lines[i]);
            }
            return new MdCodeFence(lang, bodyLines);
        }

        // Display math: $$..$$ or \[..\] on its own paragraph.
        if (TryClassifyMathBlock(lines, out var mathBlock))
            return mathBlock!;

        // Lists. ParseLines builds a list where it starts and never hands one
        // here; should one come, what the list does not take stays as text
        // rather than being dropped.
        if (ListItemRx.IsMatch(first))
        {
            var (list, taken) = BuildList(lines, 0);
            return taken >= lines.Count ? list : new MdParagraph(LiteralLines(string.Join("\n", lines)));
        }

        // Tables.
        if (lines.Count >= 2 && first.Contains('|') && IsDelimiterRowFor(first, lines[1]))
            return BuildTable(lines);

        // Default: paragraph. Join lines (newlines become soft breaks
        // via the inline grammar's `soft_break` token), each without the
        // spaces and tabs it opens with, as CommonMark strips a paragraph line's.
        var joined = string.Join("\n", lines.Select(line => line.TrimStart(' ', '\t')));
        return new MdParagraph(ParseInline(joined));
    }

    private bool TryClassifyMathBlock(IReadOnlyList<string> lines, out MdBlock? result)
    {
        result = null;
        if (lines.Count < 2) return false;

        var first = lines[0].TrimEnd();
        var last = lines[lines.Count - 1].TrimEnd();
        string body;
        if (first == "$$" && last == "$$")
        {
            body = string.Join("\n", lines.Skip(1).Take(lines.Count - 2));
        }
        else if (first == "\\[" && last == "\\]")
        {
            body = string.Join("\n", lines.Skip(1).Take(lines.Count - 2));
        }
        else
        {
            return false;
        }

        result = new MdMathBlock(
            Source: body,
            Unicode: RenderMathBody(body),
            Builder: null);
        return true;
    }

    // The same renderer the inline visitor gives a math span's body. This
    // used to wrap the body in `$..$` and reparse it inline, which the
    // dollar rule would reject for a body with a space at either end.
    private static string RenderMathBody(string body) =>
        MarkdownMacros.RenderMathUnicode(body);

    /// <summary>Adds to <paramref name="blocks"/> the list that starts at
    /// <paramref name="lines"/>[<paramref name="start"/>] and returns how many
    /// lines it took, at least one; the caller goes on after them, so an item
    /// with another bullet or delimiter (CommonMark 5.3) or a block start that
    /// is not indented into an item is parsed by the caller's own loop. On an
    /// error the first line stays as text.</summary>
    private int BuildListSafe(IReadOnlyList<string> lines, int start, List<MdBlock> blocks, int depth)
    {
        try
        {
            var (list, taken) = BuildList(lines, start, depth);
            blocks.Add(list);
            return Math.Max(1, taken);
        }
        catch
        {
            blocks.Add(new MdParagraph(LiteralLines(lines[start])));
            return 1;
        }
    }

    /// <summary>The list that starts at <paramref name="lines"/>[<paramref name="start"/>],
    /// and how many lines it took. An item's text starts at its content column, the column after
    /// the marker and the spaces that follow it (one when there are five or
    /// more, or none); a line indented that far belongs to the item, with that
    /// indentation removed, and a less indented line that starts no block and
    /// no item is a lazy continuation of its paragraph. The item's lines are
    /// then parsed like a document, so a nested list, a fence or display math
    /// inside an item is found the way it is at the top. This used to strip a
    /// fixed two columns, drop a lazy line, and make an item's second line a
    /// paragraph of its own.</summary>
    private (MdList List, int Taken) BuildList(IReadOnlyList<string> lines, int start, int depth = 0)
    {
        var head = ListItemRx.Match(lines[start]);
        var ordered = head.Groups[3].Success;
        var kind = ordered ? head.Groups[2].Value[^1] : head.Groups[2].Value[0];
        int orderedStart = 0;
        if (ordered) int.TryParse(head.Groups[3].Value, out orderedStart);

        var items = new List<MdListItem>();
        int i = start;
        while (i < lines.Count)
        {
            var m = ListItemRx.Match(lines[i]);
            if (!m.Success || m.Groups[3].Success != ordered) break;
            if ((ordered ? m.Groups[2].Value[^1] : m.Groups[2].Value[0]) != kind) break;

            // The gap after the marker, in columns (the indent is spaces only).
            int markerEnd = m.Groups[1].Length + m.Groups[2].Length, gap = 0;
            foreach (var c in m.Groups[4].Value) gap += c == '\t' ? 4 - (markerEnd + gap) % 4 : 1;
            var text = m.Groups[5].Value;
            var wide = gap > 4 || string.IsNullOrWhiteSpace(text);
            var content = markerEnd + (wide ? 1 : gap);
            var body = new List<string> { gap > 4 ? new string(' ', gap - 1) + text : text };

            int j = i + 1;
            var afterBlank = false;
            while (j < lines.Count)
            {
                var line = lines[j];
                // An item can begin with at most one blank line (CommonMark 5.2):
                // an empty marker followed by a blank line is an empty item.
                if (IsBlankLine(line) && j == i + 1 && string.IsNullOrWhiteSpace(text))
                {
                    // Blank lines between it and the next item still belong to the list.
                    var next = j;
                    while (next < lines.Count && IsBlankLine(lines[next])) next++;
                    if (next < lines.Count && ListItemRx.IsMatch(lines[next])) j = next;
                    break;
                }
                if (IsBlankLine(line)) { body.Add(string.Empty); afterBlank = true; j++; continue; }
                if (Indentation(line) >= content) { body.Add(Unindent(line, content)); afterBlank = false; j++; continue; }
                if (afterBlank || ListItemRx.IsMatch(line) || StartsSingleLineOrFence(line) || IsMathOpener(line)) break;
                body.Add(line.TrimStart(' ', '\t'));   // a lazy continuation of the item's paragraph
                j++;
            }

            // Past MaxListDepth an item's lines are its text: each level is a
            // parse, and a line of nested markers ("- - - ... x") recursed once
            // per marker until the stack ran out, which no catch survives.
            items.Add(new MdListItem(depth >= MaxListDepth
                ? new MdBlock[] { new MdParagraph(ParseInline(string.Join("\n", body))) }
                : ParseLines(body, depth + 1)));
            i = j;
        }

        return (new MdList(ordered, orderedStart, items), i - start);
    }

    private MdTable BuildTable(IReadOnlyList<string> lines)
    {
        var headerCells = SplitTableRow(lines[0]);
        var alignments = ParseTableAlignments(lines[1], headerCells.Count);
        var rows = new List<IReadOnlyList<IReadOnlyList<MdInline>>>();
        for (int i = 2; i < lines.Count; i++)
        {
            var cells = SplitTableRow(lines[i]);
            rows.Add(cells.Select(ParseInline).ToArray());
        }
        return new MdTable(
            headerCells.Select(ParseInline).ToArray(),
            rows,
            alignments);
    }

    private static IReadOnlyList<string> SplitTableRow(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith("|")) trimmed = trimmed.Substring(1);
        if (trimmed.EndsWith("|")) trimmed = trimmed.Substring(0, trimmed.Length - 1);
        return trimmed.Split('|').Select(c => c.Trim()).ToArray();
    }

    /// <summary>How deep lists nest before an item's lines are taken as its text.</summary>
    private const int MaxListDepth = 32;

    /// <summary>A blank line, as CommonMark counts one: spaces and tabs only, so a
    /// line holding a full-width or no-break space is text.</summary>
    private static bool IsBlankLine(string line)
    {
        foreach (var c in line)
            if (c is not (' ' or '\t')) return false;
        return true;
    }

    /// <summary>Whether <paramref name="delimiter"/> is a GFM delimiter row for
    /// <paramref name="header"/>: as many cells, each <c>:?-+:?</c>. A dash line
    /// under prose that holds a pipe is otherwise a setext underline or a
    /// thematic break, as on GitHub, not a header-only table.</summary>
    private static bool IsDelimiterRowFor(string header, string delimiter)
    {
        if (!IsTableSeparator(delimiter)) return false;
        var cells = SplitTableRow(delimiter);
        return cells.Count == SplitTableRow(header).Count && cells.All(cell => DelimiterCellRx.IsMatch(cell));
    }

    [GeneratedRegex(@"^:?-+:?$")]
    private static partial Regex DelimiterCellRx { get; }

    private static bool IsTableSeparator(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0) return false;
        foreach (var c in trimmed)
            if (c != '-' && c != ':' && c != '|' && c != ' ' && c != '\t') return false;
        return trimmed.Contains('-');
    }

    private static IReadOnlyList<MdTableAlignment> ParseTableAlignments(string separatorRow, int columnCount)
    {
        var result = new MdTableAlignment[columnCount];
        var cells = SplitTableRow(separatorRow);
        for (int i = 0; i < columnCount; i++)
        {
            if (i >= cells.Count) { result[i] = MdTableAlignment.Left; continue; }
            var cell = cells[i].Trim();
            var left = cell.StartsWith(":");
            var right = cell.EndsWith(":");
            result[i] = (left, right) switch
            {
                (true, true) => MdTableAlignment.Center,
                (true, false) => MdTableAlignment.Left,
                (false, true) => MdTableAlignment.Right,
                _ => MdTableAlignment.Left,
            };
        }
        return result;
    }

    // ── Parser construction ───────────────────────────────────────────

    private static readonly global::LALR.CC.Parser s_parser =
        MarkdownBlock.BuildParser(new MarkdownBlockVisitor());

    private static readonly Dictionary<string, LexRule[]> s_lexerTable =
        MarkdownBlock.BuildLexer();
}
