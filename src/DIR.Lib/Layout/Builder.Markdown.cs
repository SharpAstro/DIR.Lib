using System;
using System.Collections.Generic;
using System.Text;
using DIR.Lib.Markdown;

namespace DIR.Lib.Layout;

/// <summary>
/// How <see cref="Builder.Markdown(string, MarkdownStyle)"/> draws a document: the body's size and colour, and the
/// colours that set its parts apart. A surface-neutral palette, as everything in a tree is: the TUI maps each colour to
/// its nearest SGR.
/// </summary>
public sealed record MarkdownStyle
{
    /// <summary>The body's font size, in design units; a heading is larger by its level.</summary>
    public float FontSize { get; init; } = 13f;

    public RGBAColor32 Text { get; init; } = new(0xc4, 0xc6, 0xd0, 0xff);

    /// <summary>Headings and <c>**strong**</c> text.</summary>
    public RGBAColor32 Strong { get; init; } = new(0xe6, 0xe7, 0xee, 0xff);

    /// <summary><c>*Emphasis*</c>: a surface with one face has no italic, so it is a colour of its own.</summary>
    public RGBAColor32 Emphasis { get; init; } = new(0xd8, 0xc8, 0xa8, 0xff);

    /// <summary>Inline code and fenced code blocks.</summary>
    public RGBAColor32 Code { get; init; } = new(0x9f, 0xc8, 0x8a, 0xff);

    /// <summary>The ground under a fenced code block.</summary>
    public RGBAColor32 CodeBackground { get; init; } = new(0x1c, 0x1e, 0x26, 0xff);

    public RGBAColor32 Link { get; init; } = new(0x6f, 0x9b, 0xd8, 0xff);

    /// <summary>List bullets, table rules and a thematic break.</summary>
    public RGBAColor32 Muted { get; init; } = new(0x86, 0x89, 0x98, 0xff);

    /// <summary>Opens a link's target; null leaves links as coloured text with nothing to click.</summary>
    public Action<string>? OnLink { get; init; }
}

public static partial class Builder
{
    private static readonly MarkdownBlockVisitor s_markdown = new();
    private static readonly object s_markdownGate = new();

    /// <summary>
    /// A Markdown document as a tree: headings, paragraphs that wrap at the width they are given, lists, fenced code,
    /// tables and thematic breaks, with <c>**strong**</c>, <c>*emphasis*</c>, <c>`code`</c> and links set apart by
    /// colour. Parsed by <see cref="MarkdownBlockVisitor"/>, the grammar Console.Lib's terminal renderer reads too.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A paragraph is its words</b>, each a text leaf ending in its space, in a <see cref="WrapH"/>: a text leaf is one
    /// line, and wrapping the words is what lets a paragraph follow its width on every surface, cells included. A word
    /// whose parts are styled differently (<c>**this**,</c>) is one unit, its parts side by side with no gap, so the
    /// comma never wraps away from its word.
    /// </para>
    /// <para>
    /// Code is not re-wrapped: a fenced block's line is a line, cut where it does not fit, as a terminal would show it.
    /// Math is its Unicode rendering, inline and as a block; images are their alt text.
    /// </para>
    /// </remarks>
    public static Node Markdown(string source, MarkdownStyle? style = null)
    {
        IReadOnlyList<MdBlock> blocks;
        lock (s_markdownGate)
        {
            blocks = s_markdown.Parse(source);
        }
        return Markdown(blocks, style ?? new MarkdownStyle());
    }

    /// <summary>Already-parsed blocks as a tree, as <see cref="Markdown(string, MarkdownStyle)"/> draws them.</summary>
    public static Node Markdown(IReadOnlyList<MdBlock> blocks, MarkdownStyle style)
    {
        var nodes = new List<Node>(blocks.Count);
        foreach (var block in blocks)
        {
            nodes.Add(MarkdownBlock(block, style));
        }
        return VStack([.. nodes]).WithGap(style.FontSize * 0.5f).WStar();
    }

    // Every block takes its container's width: a paragraph wraps at the width it is given, and one left at its
    // intrinsic width (a wrap's is nothing) would put a word on each line.
    private static Node MarkdownBlock(MdBlock block, MarkdownStyle style) => MarkdownBlockAtItsWidth(block, style).WStar();

    private static Node MarkdownBlockAtItsWidth(MdBlock block, MarkdownStyle style) => block switch
    {
        MdHeading h => Paragraph(h.Content, style, style.FontSize * (h.Level switch { 1 => 1.45f, 2 => 1.25f, 3 => 1.1f, _ => 1f }), style.Strong),
        MdParagraph p => Paragraph(p.Content, style, style.FontSize, style.Text),
        MdCodeFence code => VStack([.. MarkdownCodeLines(code.Lines, style)]).Pad(style.FontSize * 0.6f, style.FontSize * 0.35f).Bg(style.CodeBackground),
        MdMathBlock math => Text(math.Unicode, style.FontSize, style.Code),
        MdList list => MarkdownList(list, style),
        MdTable table => MarkdownTable(table, style),
        MdThematicBreak => Box(1f, 1f, style.Muted).WStar(),
        _ => Text(block.ToString() ?? "", style.FontSize, style.Text),
    };

    private static IEnumerable<Node> MarkdownCodeLines(IReadOnlyList<string> lines, MarkdownStyle style)
    {
        foreach (var line in lines)
        {
            yield return Text(line.Length == 0 ? " " : line, style.FontSize, style.Code);
        }
    }

    private static Node MarkdownList(MdList list, MarkdownStyle style)
    {
        var items = new List<Node>(list.Items.Count);
        for (var i = 0; i < list.Items.Count; i++)
        {
            var marker = list.Ordered ? $"{list.OrderedStart + i}." : "•";
            var body = new List<Node>();
            foreach (var block in list.Items[i].Body)
            {
                body.Add(MarkdownBlock(block, style));
            }
            items.Add(HStack(
                    Text(marker, style.FontSize, style.Muted, vAlign: TextAlign.Near).WFixed(style.FontSize * (list.Ordered ? 2f : 1.2f)),
                    VStack([.. body]).WithGap(style.FontSize * 0.3f).WStar())
                .HAuto().WStar());
        }
        return VStack([.. items]).WithGap(style.FontSize * 0.25f);
    }

    private static Node MarkdownTable(MdTable table, MarkdownStyle style)
    {
        var rows = new List<Node> { MarkdownTableRow(table.Headers, table.Alignments, style, style.Strong), Box(1f, 1f, style.Muted).WStar() };
        foreach (var row in table.Rows)
        {
            rows.Add(MarkdownTableRow(row, table.Alignments, style, style.Text));
        }
        return VStack([.. rows]).WithGap(style.FontSize * 0.25f);
    }

    private static Node MarkdownTableRow(IReadOnlyList<IReadOnlyList<MdInline>> cells, IReadOnlyList<MdTableAlignment> alignments, MarkdownStyle style, RGBAColor32 color)
    {
        var nodes = new List<Node>(cells.Count);
        foreach (var cell in cells)
        {
            nodes.Add(Paragraph(cell, style, style.FontSize, color).WStar());
        }
        return HStack([.. nodes]).WithGap(style.FontSize).HAuto().WStar();
    }

    // A run of inlines as words that wrap; a hard break starts a new line of them.
    private static Node Paragraph(IReadOnlyList<MdInline> content, MarkdownStyle style, float size, RGBAColor32 color)
    {
        var runs = new List<MarkdownRun>();
        FlattenInlines(content, color, null, style, runs);
        var lines = new List<Node>();
        var words = new List<Node>();
        var parts = new List<MarkdownRun>();
        var text = new StringBuilder();

        void EndPart(MarkdownRun run)
        {
            if (text.Length > 0)
            {
                parts.Add(run with { Text = text.ToString() });
                text.Clear();
            }
        }
        void EndWord()
        {
            if (parts.Count > 0)
            {
                // The space after a word is its own, measured in its font, rather than the wrap's gap: a gap is in
                // design units, and on a cell surface a fraction of a cell rounds to none, running the words together.
                parts[^1] = parts[^1] with { Text = parts[^1].Text + " " };
                words.Add(MarkdownWord(parts, size, style));
                parts.Clear();
            }
        }
        void EndLine()
        {
            EndWord();
            if (words.Count > 0)
            {
                lines.Add(WrapH([.. words]).WithLineGap(size * 0.2f).WStar());
                words.Clear();
            }
        }

        foreach (var run in runs)
        {
            if (run.HardBreak)
            {
                EndLine();
                continue;
            }
            foreach (var c in run.Text)
            {
                if (char.IsWhiteSpace(c))
                {
                    EndPart(run);
                    EndWord();
                }
                else
                {
                    text.Append(c);
                }
            }
            EndPart(run);
        }
        EndLine();
        return lines.Count == 1 ? lines[0] : VStack([.. lines]).WithGap(size * 0.2f);
    }

    private static Node MarkdownWord(List<MarkdownRun> parts, float size, MarkdownStyle style)
    {
        if (parts.Count == 1)
        {
            return MarkdownPart(parts[0], size, style);
        }
        var nodes = new Node[parts.Count];
        for (var i = 0; i < parts.Count; i++)
        {
            nodes[i] = MarkdownPart(parts[i], size, style);
        }
        return HStack(nodes).WAuto();
    }

    private static Node MarkdownPart(MarkdownRun run, float size, MarkdownStyle style)
    {
        var node = Text(run.Text, size, run.Color).WAuto();
        return run.Url is { } url && style.OnLink is { } open
            ? node.Clickable(new HitResult.LinkHit(url), _ => open(url))
            : node;
    }

    // The inlines as runs of one colour (and link) each, emphasis and links nested as the grammar gives them.
    private static void FlattenInlines(IReadOnlyList<MdInline> inlines, RGBAColor32 color, string? url, MarkdownStyle style, List<MarkdownRun> runs)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case MdLiteral literal:
                    runs.Add(new MarkdownRun(literal.Text, color, url));
                    break;
                case MdCodeInline code:
                    runs.Add(new MarkdownRun(code.Content, style.Code, url));
                    break;
                case MdMathInline math:
                    runs.Add(new MarkdownRun(math.Unicode, style.Code, url));
                    break;
                case MdEmphasis emphasis:
                    FlattenInlines(emphasis.Content, emphasis.Level >= 2 ? style.Strong : style.Emphasis, url, style, runs);
                    break;
                case MdLink link:
                    FlattenInlines(link.Text, style.Link, link.Url, style, runs);
                    break;
                case MdImage image:
                    FlattenInlines(image.Alt, style.Muted, url, style, runs);
                    break;
                case MdColor colored:
                    FlattenInlines(colored.Text, color, url, style, runs);
                    break;
                case MdLineBreak { Hard: true }:
                    runs.Add(new MarkdownRun("", color, url) { HardBreak = true });
                    break;
                case MdLineBreak:
                    runs.Add(new MarkdownRun(" ", color, url));
                    break;
                default:
                    runs.Add(new MarkdownRun(inline.ToString() ?? "", color, url));
                    break;
            }
        }
    }

    private sealed record MarkdownRun(string Text, RGBAColor32 Color, string? Url)
    {
        public bool HardBreak { get; init; }
    }
}
