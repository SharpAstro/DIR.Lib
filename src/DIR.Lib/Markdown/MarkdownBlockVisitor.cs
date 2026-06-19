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
public sealed class MarkdownBlockVisitor : MarkdownBlock.IVisitor<object>
{
    private readonly MarkdownInlineVisitor _inline = new();

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
            items = result.Content as IReadOnlyList<RawItem> ?? Array.Empty<RawItem>();
        }
        catch (global::LALR.CC.ParseErrorException)
        {
            return Array.Empty<MdBlock>();
        }

        return GroupIntoBlocks(items);
    }

    // ── Items list (epsilon-base + cons; flat list of RawItems) ──────

    public object Visit(MarkdownBlock.ItemsEmpty node) =>
        (IReadOnlyList<RawItem>)Array.Empty<RawItem>();

    public object Visit(MarkdownBlock.ItemsCons node)
    {
        var head = (RawItem)node.Arg0.Content;
        var tail = (IReadOnlyList<RawItem>)node.Arg1.Content;
        var list = new List<RawItem>(tail.Count + 1) { head };
        list.AddRange(tail);
        return (IReadOnlyList<RawItem>)list;
    }

    public object Visit(MarkdownBlock.LineItem node) =>
        new RawItem((string)node.Arg0.Content, IsBlank: false);

    public object Visit(MarkdownBlock.BlankItem node) =>
        new RawItem(string.Empty, IsBlank: true);

    // ── Group items into blocks ──────────────────────────────────────

    /// <summary>Walks the flat item list, accumulating runs of
    /// non-blank lines into block-sized chunks separated by blank
    /// items. Each chunk is classified by <see cref="ClassifyBlock"/>
    /// into the appropriate <see cref="MdBlock"/> subtype.
    /// <para>Fenced code blocks are the exception to the blank-line rule:
    /// once a chunk opens with a ` ``` ` / ` ~~~ ` fence, blank lines are
    /// part of the code body, not block separators, so accumulation
    /// continues (blanks included) until the matching closing fence — per
    /// CommonMark. Without this the fence is shredded at its first blank
    /// line and the orphaned closing fence is mis-lexed as inline code.</para></summary>
    private IReadOnlyList<MdBlock> GroupIntoBlocks(IReadOnlyList<RawItem> items)
    {
        var blocks = new List<MdBlock>();
        var current = new List<string>();
        string? fence = null;   // non-null while inside an open code fence

        void FlushCurrent()
        {
            if (current.Count > 0)
            {
                blocks.Add(ClassifyBlock(current));
                current = new List<string>();
            }
            fence = null;
        }

        foreach (var item in items)
        {
            if (fence is not null)
            {
                // Inside a fence: a blank line is content, keep going until
                // the closing fence. A blank RawItem carries no text, so
                // re-materialise it as an empty body line.
                current.Add(item.IsBlank ? string.Empty : item.Line);
                if (!item.IsBlank && item.Line.TrimStart().StartsWith(fence))
                    FlushCurrent();
                continue;
            }

            if (item.IsBlank) { FlushCurrent(); continue; }

            if (current.Count == 0)
            {
                var opener = FenceOpenRx.Match(item.Line);
                if (opener.Success) fence = opener.Groups[1].Value;
            }
            current.Add(item.Line);
        }
        FlushCurrent();

        return blocks;
    }

    private sealed record RawItem(string Line, bool IsBlank);

    /// <summary>Same grouping logic <see cref="GroupIntoBlocks"/> uses
    /// but applied to a pre-tokenised list of strings (e.g. the
    /// continuation lines inside a list item). Splits at blank lines
    /// and classifies each group via <see cref="ClassifyBlock"/>, with
    /// the same fence-aware exception (blank lines inside a ` ``` ` /
    /// ` ~~~ ` fence stay part of the code body).</summary>
    private IReadOnlyList<MdBlock> ParseLineGroups(IReadOnlyList<string> lines)
    {
        var blocks = new List<MdBlock>();
        var current = new List<string>();
        string? fence = null;
        void Flush()
        {
            if (current.Count > 0) { blocks.Add(ClassifyBlock(current)); current = new List<string>(); }
            fence = null;
        }
        foreach (var line in lines)
        {
            if (fence is not null)
            {
                current.Add(line);
                if (line.TrimStart().StartsWith(fence)) Flush();
                continue;
            }
            if (string.IsNullOrWhiteSpace(line)) { Flush(); continue; }
            if (current.Count == 0)
            {
                var opener = FenceOpenRx.Match(line);
                if (opener.Success) fence = opener.Groups[1].Value;
            }
            current.Add(line);
        }
        Flush();
        return blocks;
    }

    // ── Block classification ──────────────────────────────────────────

    private static readonly Regex HeadingRx = new(@"^(#{1,6})\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex ThematicRx = new(@"^[ \t]*([-*_])(?:[ \t]*\1){2,}[ \t]*$", RegexOptions.Compiled);
    private static readonly Regex FenceOpenRx = new(@"^(```|~~~)\s*([^\s]*)\s*$", RegexOptions.Compiled);
    private static readonly Regex UnorderedItemRx = new(@"^([-*+])\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex OrderedItemRx = new(@"^(\d+)\.\s+(.*)$", RegexOptions.Compiled);

    private MdBlock ClassifyBlock(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return new MdParagraph(Array.Empty<MdInline>());

        var first = lines[0];

        // Heading — single-line construct.
        var headingMatch = HeadingRx.Match(first);
        if (headingMatch.Success && lines.Count == 1)
        {
            var level = headingMatch.Groups[1].Value.Length;
            var text = headingMatch.Groups[2].Value
                .TrimEnd().TrimEnd('#').TrimEnd();
            return new MdHeading(level, _inline.Parse(text));
        }

        // Thematic break — single-line construct.
        if (lines.Count == 1 && ThematicRx.IsMatch(first))
            return new MdThematicBreak();

        // Fenced code — opener fence on first line.
        if (FenceOpenRx.IsMatch(first))
        {
            var openerMatch = FenceOpenRx.Match(first);
            var lang = string.IsNullOrEmpty(openerMatch.Groups[2].Value)
                ? null
                : openerMatch.Groups[2].Value;
            var fence = openerMatch.Groups[1].Value;
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

        // Lists.
        if (UnorderedItemRx.IsMatch(first))
            return BuildList(lines, ordered: false);
        if (OrderedItemRx.IsMatch(first))
            return BuildList(lines, ordered: true);

        // Tables.
        if (lines.Count >= 2 && first.Contains('|') && IsTableSeparator(lines[1]))
            return BuildTable(lines);

        // Default: paragraph. Join lines (newlines become soft breaks
        // via the inline grammar's `soft_break` token).
        var joined = string.Join("\n", lines);
        return new MdParagraph(_inline.Parse(joined));
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

    private string RenderMathBody(string body)
    {
        // Re-use the inline visitor's math sub-parser by wrapping the
        // body in `$..$` and pulling the resulting MdMathInline.
        var spans = _inline.Parse("$" + body + "$");
        var math = spans.OfType<MdMathInline>().FirstOrDefault();
        return math?.Unicode ?? body;
    }

    private MdList BuildList(IReadOnlyList<string> lines, bool ordered)
    {
        var marker = ordered ? OrderedItemRx : UnorderedItemRx;
        var items = new List<MdListItem>();
        int orderedStart = 0;
        int i = 0;

        while (i < lines.Count)
        {
            var line = lines[i];
            // Items at this level start at column 0 (no leading space)
            // and have the appropriate marker. Indented marker lines
            // are sub-list items handled below.
            if (line.Length > 0 && line[0] == ' ') break;

            var m = marker.Match(line);
            if (!m.Success) break;

            if (ordered && items.Count == 0)
                int.TryParse(m.Groups[1].Value, out orderedStart);

            var firstLine = m.Groups[2].Value;
            var indentedContinuation = new List<string>();
            int j = i + 1;
            while (j < lines.Count && lines[j].Length > 0 && lines[j][0] == ' ')
            {
                // Strip up to 2 leading spaces of indentation — CommonMark
                // uses 2-space sub-list indent by convention; deeper indents
                // are passed through as part of the nested content.
                indentedContinuation.Add(lines[j].Length >= 2 ? lines[j].Substring(2) : lines[j].TrimStart());
                j++;
            }

            // Item body: a paragraph for the first line; if indented
            // continuation lines start with a list marker, those become
            // a nested MdList. Otherwise they're additional paragraph
            // lines.
            var body = new List<MdBlock>();
            body.Add(new MdParagraph(_inline.Parse(firstLine)));
            if (indentedContinuation.Count > 0)
            {
                var firstCont = indentedContinuation[0];
                if (UnorderedItemRx.IsMatch(firstCont))
                    body.Add(BuildList(indentedContinuation, ordered: false));
                else if (OrderedItemRx.IsMatch(firstCont))
                    body.Add(BuildList(indentedContinuation, ordered: true));
                else
                {
                    // Generic continuation — could be a math block, code
                    // fence, additional paragraph, etc. Re-group by blank
                    // line and classify each group the same way the
                    // top-level GroupIntoBlocks does.
                    body.AddRange(ParseLineGroups(indentedContinuation));
                }
            }
            items.Add(new MdListItem(body));
            i = j;
        }

        return new MdList(ordered, orderedStart, items);
    }

    private MdTable BuildTable(IReadOnlyList<string> lines)
    {
        var headerCells = SplitTableRow(lines[0]);
        var alignments = ParseTableAlignments(lines[1], headerCells.Count);
        var rows = new List<IReadOnlyList<IReadOnlyList<MdInline>>>();
        for (int i = 2; i < lines.Count; i++)
        {
            var cells = SplitTableRow(lines[i]);
            rows.Add(cells.Select(c => _inline.Parse(c)).ToArray());
        }
        return new MdTable(
            headerCells.Select(c => _inline.Parse(c)).ToArray(),
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
