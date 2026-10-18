using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DIR.Lib.Markdown;

namespace DIR.Lib.Tests;

/// <summary>
/// The markdown parse corpus, read from <c>Fixtures/MarkdownCorpus</c>: three real documents copied
/// from SharpAstro/Console.Lib (its README and CHANGELOG, and mdcat's README) and one document per
/// formula of Console.Lib's <c>MathCorpus</c> (<c>formulas.tsv</c>), each written inline in a sentence
/// and as a <c>$$</c> display block. <see cref="MarkdownCorpusGoldenTests"/> pins the parsed AST of every
/// document, and the <c>MarkdownParse</c> benchmark times the same documents; the benchmark project
/// compiles this file in by link, so the two read one corpus.
/// </summary>
public static class MarkdownCorpus
{
    /// <summary>The real documents, by name and file.</summary>
    public static readonly (string Name, string File)[] RealDocuments =
    [
        ("console-lib-README", "console-lib-README.md"),
        ("console-lib-CHANGELOG", "console-lib-CHANGELOG.md"),
        ("mdcat-README", "mdcat-README.md"),
    ];

    /// <summary>The folder holding the corpus: next to the running assembly when the build copied it
    /// there, otherwise found by walking up to the source tree (a BenchmarkDotNet child process runs
    /// from a generated folder of its own).</summary>
    public static string Directory { get; } = FindDirectory();

    private static string FindDirectory()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "Fixtures", "MarkdownCorpus");
        if (System.IO.Directory.Exists(local)) return local;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "DIR.Lib.Tests", "Fixtures", "MarkdownCorpus");
            if (System.IO.Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("Fixtures/MarkdownCorpus not found from " + AppContext.BaseDirectory);
    }

    /// <summary>The formulas of <c>formulas.tsv</c>, in file order.</summary>
    public static IReadOnlyList<(string Name, string Source)> Formulas() =>
        File.ReadAllLines(Path.Combine(Directory, "formulas.tsv"), Encoding.UTF8)
            .Where(line => line.Length > 0 && line[0] != '#')
            .Select(line => line.Split('\t', 2))
            .Select(parts => (parts[0], parts[1]))
            .ToList();

    /// <summary>A formula written inline, in a sentence.</summary>
    public static string Inline(string source) => $"The formula ${source}$ sits in this sentence.";

    /// <summary>A formula as a display block, the form Console.Lib's <c>MathCorpus.Markdown</c> writes.</summary>
    public static string Display(string source) => $"$$\n{source}\n$$";

    /// <summary>Every single-formula document: <c>formula/NAME/inline</c> and <c>formula/NAME/display</c>.</summary>
    public static IReadOnlyList<(string Name, string Text)> FormulaDocuments() =>
        Formulas()
            .SelectMany(f => new[]
            {
                ($"formula/{f.Name}/inline", Inline(f.Source)),
                ($"formula/{f.Name}/display", Display(f.Source)),
            })
            .ToList();

    /// <summary>The real documents' text, with line endings as committed.</summary>
    public static IReadOnlyList<(string Name, string Text)> RealDocumentTexts() =>
        RealDocuments
            .Select(d => (d.Name, File.ReadAllText(Path.Combine(Directory, d.File), Encoding.UTF8)))
            .ToList();

    /// <summary>Every document of the corpus, the real ones first.</summary>
    public static IReadOnlyList<(string Name, string Text)> AllDocuments() =>
        RealDocumentTexts().Concat(FormulaDocuments()).ToList();
}

/// <summary>
/// A stable text form of a parsed <see cref="MdBlock"/> tree, one node per line, indented by depth.
/// Strings are quoted with control characters and non-ASCII escaped, so the golden is plain ASCII and
/// a difference in a single code point shows in a diff.
/// </summary>
public static class MdAstDump
{
    public static string Dump(IReadOnlyList<MdBlock> blocks)
    {
        var sb = new StringBuilder();
        foreach (var block in blocks) Block(sb, block, 0);
        return sb.ToString();
    }

    private static void Line(StringBuilder sb, int depth, string text) =>
        sb.Append(' ', depth * 2).Append(text).Append('\n');

    private static void Block(StringBuilder sb, MdBlock block, int depth)
    {
        switch (block)
        {
            case MdParagraph p:
                Line(sb, depth, "Paragraph");
                Inlines(sb, p.Content, depth + 1);
                break;
            case MdHeading h:
                Line(sb, depth, $"Heading {h.Level}");
                Inlines(sb, h.Content, depth + 1);
                break;
            case MdThematicBreak:
                Line(sb, depth, "ThematicBreak");
                break;
            case MdCodeFence f:
                Line(sb, depth, $"CodeFence lang={Quote(f.Lang)} lines={f.Lines.Count}");
                foreach (var l in f.Lines) Line(sb, depth + 1, Quote(l));
                break;
            case MdMathBlock m:
                Line(sb, depth, $"MathBlock source={Quote(m.Source)} unicode={Quote(m.Unicode)} builder={m.Builder is not null}");
                break;
            case MdList list:
                Line(sb, depth, $"List ordered={list.Ordered} start={list.OrderedStart} items={list.Items.Count}");
                foreach (var item in list.Items)
                {
                    Line(sb, depth + 1, "Item");
                    foreach (var b in item.Body) Block(sb, b, depth + 2);
                }
                break;
            case MdTable t:
                Line(sb, depth, $"Table columns={t.Headers.Count} rows={t.Rows.Count} align={string.Join(",", t.Alignments)}");
                Line(sb, depth + 1, "Header");
                foreach (var cell in t.Headers) { Line(sb, depth + 2, "Cell"); Inlines(sb, cell, depth + 3); }
                foreach (var row in t.Rows)
                {
                    Line(sb, depth + 1, "Row");
                    foreach (var cell in row) { Line(sb, depth + 2, "Cell"); Inlines(sb, cell, depth + 3); }
                }
                break;
            default:
                Line(sb, depth, "UNKNOWN " + block.GetType().Name);
                break;
        }
    }

    private static void Inlines(StringBuilder sb, IReadOnlyList<MdInline> inlines, int depth)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case MdLiteral l:
                    Line(sb, depth, "Literal " + Quote(l.Text));
                    break;
                case MdMathInline m:
                    Line(sb, depth, $"Math display={m.IsDisplay} source={Quote(m.Source)} unicode={Quote(m.Unicode)} builder={m.Builder is not null}");
                    break;
                case MdCodeInline c:
                    Line(sb, depth, "Code " + Quote(c.Content));
                    break;
                case MdEmphasis e:
                    Line(sb, depth, $"Emphasis {e.Level}");
                    Inlines(sb, e.Content, depth + 1);
                    break;
                case MdLink link:
                    Line(sb, depth, "Link url=" + Quote(link.Url));
                    Inlines(sb, link.Text, depth + 1);
                    break;
                case MdImage img:
                    Line(sb, depth, "Image url=" + Quote(img.Url));
                    Inlines(sb, img.Alt, depth + 1);
                    break;
                case MdColor col:
                    Line(sb, depth, "Color " + Quote(col.Color));
                    Inlines(sb, col.Text, depth + 1);
                    break;
                case MdLineBreak br:
                    Line(sb, depth, br.Hard ? "LineBreak hard" : "LineBreak soft");
                    break;
                default:
                    // Transient nodes (MdGroup, MdStarMarker) are internal and must not survive a parse.
                    Line(sb, depth, "UNKNOWN " + inline.GetType().Name);
                    break;
            }
        }
    }

    private static string Quote(string? s)
    {
        if (s is null) return "null";
        var sb = new StringBuilder(s.Length + 2).Append('"');
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 0x20 || ch > 0x7E) sb.Append("\\u").Append(((int)ch).ToString("X4"));
                    else sb.Append(ch);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}
