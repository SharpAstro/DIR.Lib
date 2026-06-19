using System.Collections.Generic;
using DIR.Lib;
using DIR.Lib.MathLayout;

namespace DIR.Lib.Markdown;

/// <summary>
/// AST for the LALR.CC-based markdown renderer. Replaces the Markdig
/// <c>Block</c> and <c>Inline</c> hierarchies that
/// <see cref="MarkdownRenderer"/>'s switch statement currently walks.
/// All types are <c>record</c>s for value-equality semantics — handy
/// for AST diffing in tests, and the cost is negligible for read-mostly
/// tree walks.
/// </summary>
public abstract record MdBlock;

/// <summary>Default block type — prose. <see cref="Content"/> is the
/// inline span list produced by <see cref="MarkdownInlineVisitor"/>
/// from the paragraph's joined-line text.</summary>
public sealed record MdParagraph(IReadOnlyList<MdInline> Content) : MdBlock;

/// <summary>ATX-style heading (<c># H1</c> .. <c>###### H6</c>).
/// <see cref="Level"/> is the number of leading <c>#</c> chars
/// (1–6); <see cref="Content"/> is the inline content of the heading
/// line after the marker.</summary>
public sealed record MdHeading(int Level, IReadOnlyList<MdInline> Content) : MdBlock;

/// <summary>Horizontal rule — <c>---</c> / <c>***</c> / <c>___</c>
/// on its own line. Renders as a full-width separator.</summary>
public sealed record MdThematicBreak : MdBlock;

/// <summary>Fenced code block — content wrapped in
/// ` ``` ` / ` ~~~ ` fences. <see cref="Lang"/> is the optional info
/// string after the opener fence (e.g. <c>"csharp"</c>);
/// <see cref="Lines"/> is the raw body lines with no further inline
/// parsing.</summary>
public sealed record MdCodeFence(string? Lang, IReadOnlyList<string> Lines) : MdBlock;

/// <summary>Block-level math — <c>$$..$$</c> or <c>\[..\]</c> on its
/// own paragraph. <see cref="Source"/> is the raw LaTeX body;
/// <see cref="Unicode"/> is the Unicode rendering through
/// <see cref="LatexUnicodeVisitor"/>; <see cref="Builder"/> is the
/// deferred box-mode rasteriser produced by
/// <see cref="BoxBuildingVisitor"/> (null in Unicode-only mode).</summary>
public sealed record MdMathBlock(string Source, string Unicode, System.Func<BoxStyle, Box>? Builder) : MdBlock;

/// <summary>Ordered or unordered list. <see cref="Ordered"/> is true
/// for <c>1. 2. 3.</c> lists, false for <c>- + *</c> bullets.
/// <see cref="OrderedStart"/> is the first ordered-list number when
/// <see cref="Ordered"/> is true (used for renumber-starts), 0 otherwise.
/// <see cref="Items"/> is the list-item block sequence.</summary>
public sealed record MdList(bool Ordered, int OrderedStart, IReadOnlyList<MdListItem> Items) : MdBlock;

/// <summary>One list item. <see cref="Body"/> is the item's content —
/// usually a single <see cref="MdParagraph"/>, but can include
/// nested blocks (sub-lists, code, etc.) for richly-structured items.</summary>
public sealed record MdListItem(IReadOnlyList<MdBlock> Body);

/// <summary>Pipe-table — header row + alignment row + body rows.
/// <see cref="Headers"/> is the first-row cells; <see cref="Rows"/>
/// is the body row cells (each cell parsed by the inline grammar);
/// <see cref="Alignments"/> is per-column alignment derived from the
/// separator row's <c>:---</c> / <c>:---:</c> / <c>---:</c> markers.</summary>
public sealed record MdTable(
    IReadOnlyList<IReadOnlyList<MdInline>> Headers,
    IReadOnlyList<IReadOnlyList<IReadOnlyList<MdInline>>> Rows,
    IReadOnlyList<MdTableAlignment> Alignments) : MdBlock;

/// <summary>Per-column alignment for <see cref="MdTable"/>.</summary>
public enum MdTableAlignment { Left, Center, Right }

public abstract record MdInline;

/// <summary>Plain text run. <see cref="Text"/> may contain whitespace and
/// punctuation; emphasis / code / link / math markers are stripped by the
/// grammar into their own <see cref="MdInline"/> subtypes before this is
/// emitted.</summary>
public sealed record MdLiteral(string Text) : MdInline;

/// <summary>An inline math span — anything between <c>$..$</c>,
/// <c>\(..\)</c>, or the bare <c>\boxed{..}</c> form once Phase B adds
/// those productions. <see cref="Source"/> is the raw LaTeX body (no
/// delimiters). <see cref="Unicode"/> is the rendering through
/// <see cref="LatexUnicodeVisitor"/>; <see cref="Builder"/> is the
/// deferred box-mode rasteriser produced by <see cref="BoxBuildingVisitor"/>
/// (null when the renderer is in Unicode-only mode).</summary>
public sealed record MdMathInline(
    string Source,
    string Unicode,
    System.Func<BoxStyle, Box>? Builder
) : MdInline;

/// <summary>Inline code span — text wrapped in single backticks. Emitted
/// for `` `code` `` patterns; the renderer paints <see cref="Content"/>
/// in the Code theme colour. Phase B handles single-backtick fences only;
/// CommonMark's multi-backtick fence (which lets single backticks appear
/// inside the body) lands in Phase B-extension.</summary>
public sealed record MdCodeInline(string Content) : MdInline;

/// <summary>Emphasis (italic / bold / bold-italic). <see cref="Level"/>
/// is 1 for <c>*italic*</c>, 2 for <c>**bold**</c>, 3 for
/// <c>***bold-italic***</c> (the pairing pass collapses adjacent
/// markers). The renderer maps level to VT bold + italic attributes.</summary>
public sealed record MdEmphasis(int Level, System.Collections.Generic.IReadOnlyList<MdInline> Content) : MdInline;

/// <summary>Transient emphasis-delimiter marker. Emitted by the grammar
/// for each `*` or `**` token; replaced by <see cref="MdEmphasis"/>
/// nodes during <see cref="MarkdownInlineVisitor.Parse"/>'s post-pass
/// pairing step. Any markers that survive the pairing (unmatched
/// delimiters like the `*` in <c>2 * 3 = 6</c>) are rewritten back to
/// <see cref="MdLiteral"/> so the rendered output shows the original
/// text rather than a stray placeholder. Should not normally appear
/// in the final span list returned to consumers.</summary>
internal sealed record MdStarMarker(int Level) : MdInline;

/// <summary>Transient container produced by the plain-bracket
/// production (<c>[text]</c> with no link/color tail). The
/// <see cref="MarkdownInlineVisitor.Parse"/> post-pass flattens
/// <see cref="MdGroup"/> nodes into their parent span list so the
/// final result is a flat sequence with no MdGroup wrappers. Useful
/// when a grammar production needs to emit multiple inlines but the
/// visitor surface returns a single MdInline.</summary>
internal sealed record MdGroup(System.Collections.Generic.IReadOnlyList<MdInline> Children) : MdInline;

/// <summary>Link inline — <c>[text](url)</c>. <see cref="Text"/> is the
/// link's display content (parsed as inline spans by the same grammar,
/// so the brackets can wrap bold/italic/math, etc.); <see cref="Url"/>
/// is the raw URL string from the parens body.</summary>
public sealed record MdLink(System.Collections.Generic.IReadOnlyList<MdInline> Text, string Url) : MdInline;

/// <summary>Image inline — <c>![alt](url)</c>. <see cref="Alt"/> is the
/// alt-text content (parsed as inline spans by the same grammar, so it
/// can carry emphasis etc.); <see cref="Url"/> is the raw source string
/// from the parens body (a file path or URL — resolution is the
/// renderer's job). Mirrors <see cref="MdLink"/> but signals an image
/// rather than a hyperlink.</summary>
public sealed record MdImage(System.Collections.Generic.IReadOnlyList<MdInline> Alt, string Url) : MdInline;

/// <summary>Color inline — Console.Lib extension syntax
/// <c>[text]{color}</c>. <see cref="Color"/> is the literal colour
/// string from the brace body (validated by the renderer against
/// <c>MarkdownTheme.TryParseColor</c>); invalid colours render as
/// plain text with the brackets and braces stripped. <see cref="Text"/>
/// is the bracketed inline content.</summary>
public sealed record MdColor(System.Collections.Generic.IReadOnlyList<MdInline> Text, string Color) : MdInline;

/// <summary>Line break — soft or hard per CommonMark. A soft break is
/// a lone newline inside a paragraph and renders as a single space; a
/// hard break is two-plus trailing spaces + newline (or trailing
/// <c>\\</c> + newline) and renders as an actual line terminator in
/// the output.</summary>
public sealed record MdLineBreak(bool Hard) : MdInline;
