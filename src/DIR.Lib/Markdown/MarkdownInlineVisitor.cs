using System;
using System.Collections.Generic;
using System.Text;
using DIR.Lib.MathLayout;
using LALR.CC.LexicalGrammar;

namespace DIR.Lib.Markdown;

/// <summary>
/// Phase-B visitor over the <c>markdown-inline.lalr.yaml</c> grammar.
/// Produces an <see cref="IReadOnlyList{MdInline}"/> by composing per-span
/// builders; for every math form, the rewriter invokes the existing
/// <see cref="Latex"/> parser as a sub-parser on the captured body string
/// and stores the resulting Unicode rendering on <see cref="MdMathInline"/>.
///
/// <para><b>Sub-parser pattern.</b> Symbol-ID spaces stay disjoint because
/// the LaTeX parser is a separate <see cref="LALR.CC.Parser"/> instance;
/// communication is one-way via the visitor's return value stored on
/// <c>Item.Content</c>.</para>
///
/// <para><b>What's covered:</b> plain text, <c>$..$</c>, <c>$$..$$</c>,
/// <c>\(..\)</c>, <c>\[..\]</c>, <c>\boxed{..}</c> (with balanced braces
/// via the recursive <c>BoxedBody</c> rule). Phase B-extension will add
/// inline code, emphasis, links, line breaks, and color inlines.</para>
/// </summary>
public sealed class MarkdownInlineVisitor : MarkdownInline.IVisitor<object>
{
    /// <summary>Parses an inline-only markdown string and returns the
    /// produced span list with transient nodes (MdGroup, MdStarMarker)
    /// resolved. A math delimiter, bracket or backtick run that does not
    /// pair is text (see <see cref="MarkUnpaired"/>). Returns an empty
    /// list on any other parse error so the caller can fall through to a
    /// literal-text render of the source.</summary>
    public IReadOnlyList<MdInline> Parse(string source) =>
        string.IsNullOrEmpty(source) ? Array.Empty<MdInline>() : ParseWithoutPairing(MarkUnpaired(source));

    /// <summary>Test seam: lexes and parses <paramref name="source"/> with
    /// no pairing pass, so a test can reach the grammar's own error path,
    /// which <see cref="Parse"/> is meant never to.</summary>
    internal IReadOnlyList<MdInline> ParseWithoutPairing(string source)
    {
        try
        {
            using var lexer = global::LALR.CC.LexicalGrammar.BytesLexer.FromString(source, s_lexerTable);
            using var tokens = new global::LALR.CC.LexicalGrammar.SyncLATokenIterator(lexer);
            var result = s_parser.ParseInput(tokens, debugger: null);
            if (result.IsError) return Array.Empty<MdInline>();
            var flat = result.Content as IReadOnlyList<MdInline> ?? Array.Empty<MdInline>();
            return Process(flat);
        }
        catch (global::LALR.CC.ParseErrorException)
        {
            return Array.Empty<MdInline>();
        }
    }

    // ── Delimiter pairing (runs before the lexer) ────────────────────
    //
    // The lexer pushes a math state on every `$`, `$$`, `\(`, `\[` and
    // `\boxed{` it meets in root, so one that never closes used to leave
    // it in that state, fail the parse and blank the whole paragraph. A
    // regular lexer cannot see whether a closer is coming, and Pandoc's
    // dollar rule looks one character past the closer, so the pairing is
    // decided here instead. The pass walks the source the way the root
    // lexer would, finds each opener's closer by the same rules its math
    // state uses, and replaces every character of an opener that does
    // not pair with a stand-in the lexer reads as plain text. A bracket
    // with no partner and a backtick run that never closes are handled
    // the same way, since the grammar has no rule for either.
    // LiteralSpan turns the stand-ins back. The grammar is unchanged,
    // so a delimiter that reaches the lexer always pairs.

    /// <summary>Stand-ins for the characters of a delimiter that delimits
    /// nothing: Unicode noncharacters, which are reserved for a program's
    /// internal use and so do not occur in text. They are outside every
    /// special byte the root lexer knows, so they lex as part of a text
    /// run, and outside the dollar states' closers, so a <c>$</c> inside a
    /// <c>\text{..}</c> group in math lexes as body.</summary>
    private const char UnpairedDollar = '\uFDD0';
    private const char UnpairedBackslash = '\uFDD1';
    private const char UnpairedBracket = '\uFDD2';
    private const char UnpairedCloseBracket = '\uFDD3';
    private const char UnpairedBacktick = '\uFDD4';
    private const char UnpairedBang = '\uFDD5';

    /// <summary>Returns <paramref name="s"/> with every math opener that
    /// does not pair, every bracket with no partner and every backtick run
    /// that never closes replaced by stand-ins, or <paramref name="s"/>
    /// itself when everything pairs.
    /// <para>A <c>$</c> follows Pandoc's <c>tex_math_dollars</c>: it opens
    /// only with a non-space to its right, and its closer is the next
    /// unescaped <c>$</c>, which needs a non-space to its left and no digit
    /// to its right. <c>$$</c> closes at the next unescaped <c>$$</c>. In
    /// either, <c>\text</c> and the balanced brace group after it are one
    /// piece of the body, so a <c>$</c> inside the braces closes nothing.
    /// <c>\(</c>, <c>\[</c> and <c>\boxed{</c> close where their lexer
    /// state pops.</para>
    /// <para>A link, image or colour label is parsed on its own: its
    /// <c>[</c> opens before any math in it, so an opener inside the label
    /// pairs only inside it. The label is found first (<see cref="LabelEnd"/>),
    /// the way a code span is. A math span that opens before the <c>[</c>
    /// runs to its own closer and takes the bracket with it; the bracket
    /// or link tail it leaves with no <c>[</c> is then text.</para>
    /// <para>This deliberately differs from Pandoc, whose bracket scan
    /// (<c>inBalancedBrackets</c>, Readers/Markdown.hs:217-243 in
    /// jgm/pandoc) skips a math span whole, so Pandoc renders
    /// <c>See [the $5 plan](https://x) or pay 5$ later.</c> as
    /// <c>See [the </c> + math <c>5 plan](https://x) or pay 5</c> +
    /// <c> later.</c>. Here a link label bounds the math inside it, and
    /// math that opens before the <c>[</c> still takes the bracket.</para>
    /// <para>A <c>[</c> or <c>]</c> with no partner, a backtick run with no
    /// closing run and a link or colour tail that never closes are literal,
    /// as in CommonMark and Pandoc. So is the <c>!</c> of an image opener
    /// that no <c>](url)</c> closes; its <c>[</c> is then a plain bracket.</para>
    /// <para>One departure from Pandoc: a closer inside a code span that
    /// opens after the opener does not count, so the <c>$</c> in
    /// <c>$5 to run `echo $PATH`</c> stays with the code span (code spans
    /// bind tighter, as in CommonMark).</para></summary>
    private static string MarkUnpaired(string s)
    {
        char[]? marked = null;
        List<int>? textDollars = null;
        void Mark(int at, char standIn) => (marked ??= s.ToCharArray())[at] = standIn;

        // Walks s[from..bound) the way the root lexer would. The bound is
        // the end of the text, or the `]` of the label being walked, which
        // no math span opened inside the label may cross.
        void Walk(int from, int bound)
        {
            // Each `[` here that opens no label (a plain bracket, or one
            // with no partner), innermost last; a `]` closes the last one.
            List<(int At, bool Image)>? open = null;
            int i = from;
            while (i < bound)
            {
                switch (s[i])
                {
                    case '$' when At(s, i + 1) == '$':
                    {
                        int end = DisplayDollarEnd(s, i, bound, textDollars ??= new());
                        if (end > 0) { MarkTextDollars(); i = end; break; }
                        Mark(i, UnpairedDollar);
                        Mark(i + 1, UnpairedDollar);
                        i += 2;
                        break;
                    }
                    case '$':
                    {
                        int end = InlineDollarEnd(s, i, bound, textDollars ??= new());
                        if (end > 0) { MarkTextDollars(); i = end; break; }
                        Mark(i, UnpairedDollar);
                        i++;
                        break;
                    }
                    case '\\' when s.AsSpan(i).StartsWith(@"\boxed{"):
                    {
                        int end = BoxedEnd(s, i + 7, bound);
                        if (end > 0) { i = end; break; }
                        Mark(i, UnpairedBackslash);   // `boxed{` after it is plain text
                        i++;
                        break;
                    }
                    case '\\' when At(s, i + 1) is '(' or '[':
                    {
                        char opener = s[i + 1];
                        int end = LatexEnd(s, i + 2, opener == '(' ? ')' : ']', bound);
                        if (end > 0) { i = end; break; }
                        Mark(i, UnpairedBackslash);
                        if (opener == '[') Mark(i + 1, UnpairedBracket);   // a bare `[` would open a bracket span
                        i += 2;
                        break;
                    }
                    case '\\' when i + 1 >= s.Length:
                        // A backslash at the very end matches no root lexer
                        // rule (an escape and a hard break both need the
                        // character after it), so it is text.
                        Mark(i, UnpairedBackslash);
                        i++;
                        break;
                    case '\\':
                        // Any other `\X` is one token (escape, command or hard
                        // break), so X opens nothing: `\$` is a literal dollar.
                        if (IsLoneCarriageReturn(s, i + 1)) Mark(i + 1, '\n');
                        i += 2;
                        break;
                    case '\r' when IsLoneCarriageReturn(s, i):
                        // A carriage return with no line feed matches no root
                        // lexer rule. It is a line ending, as in CommonMark,
                        // so the lexer gets one.
                        Mark(i, '\n');
                        i++;
                        break;
                    case '`':
                    {
                        // A code span is opaque. A fence that never closes
                        // is text, and so is the rest of its run.
                        int end = CodeSpanEnd(s, i);
                        if (end > 0) { i = end; break; }
                        int fence = FenceLength(s, i);
                        for (int k = i; k < i + fence; k++) Mark(k, UnpairedBacktick);
                        i += fence;
                        break;
                    }
                    case '[':
                    case '!' when At(s, i + 1) == '[':
                    {
                        bool image = s[i] == '!';
                        int label = i + (image ? 2 : 1);
                        int close = LabelEnd(s, label);
                        char tail = close > 0 && close < bound ? At(s, close + 1) : '\0';
                        if (image && tail == '{')
                        {
                            // No image takes a colour: this is a `!` and
                            // then a colour label.
                            Mark(i, UnpairedBang);
                            i++;
                            break;
                        }
                        int tailEnd = tail is '(' or '{' ? TailEnd(s, close) : -1;
                        if (tailEnd > 0)
                        {
                            Walk(label, close);
                            i = tailEnd;
                        }
                        else
                        {
                            // Not a label, so it bounds nothing; a math
                            // span may still close past its `]`.
                            (open ??= new()).Add((label - 1, image));
                            i = label;
                        }
                        break;
                    }
                    case ']':
                    {
                        bool hasTail = At(s, i + 1) is '(' or '{';
                        int tailEnd = hasTail ? TailEnd(s, i) : -1;
                        if (open is not { Count: > 0 } || (hasTail && tailEnd < 0))
                        {
                            // No `[` for this bracket (or a math span took
                            // it), or a tail that never closes: text, and so
                            // is what follows it.
                            Mark(i, UnpairedCloseBracket);
                            i++;
                            break;
                        }
                        var (at, image) = open[^1];
                        open.RemoveAt(open.Count - 1);
                        // An image needs `](url)`. Closed any other way, its
                        // `!` is text and its `[` a plain bracket, which
                        // pairs with this `]` just the same.
                        if (image && !(hasTail && s[i + 1] == '(')) Mark(at - 1, UnpairedBang);
                        i = hasTail ? tailEnd : i + 1;
                        break;
                    }
                    default:
                        i++;
                        break;
                }
            }

            // A `[` that nothing closed is text. No pairing above depended
            // on it, since nothing ever paired with it.
            if (open is not null)
                foreach (var (at, _) in open) Mark(at, UnpairedBracket);
        }

        void MarkTextDollars()
        {
            foreach (int at in textDollars!) Mark(at, UnpairedDollar);
        }

        Walk(0, s.Length);
        return marked is null ? s : new string(marked);
    }

    /// <summary>Index of the <c>]</c> that closes the bracket whose label
    /// starts at <paramref name="from"/>, or -1 when none does. Brackets
    /// nest. A code span, a <c>\X</c> pair, and a link's URL or a colour's
    /// value hide the brackets they hold, as they do from the lexer; a
    /// <c>]</c> whose tail never closes is text, as <see cref="MarkUnpaired"/>
    /// makes it. Math does not: the label is found before the math in it
    /// is paired, so a <c>$</c> in the label cannot carry its bracket away.</summary>
    private static int LabelEnd(string s, int from)
    {
        int depth = 0;
        for (int k = from; k < s.Length; k++)
        {
            switch (s[k])
            {
                case '\\':
                    k++;
                    break;
                case '`':
                {
                    int end = CodeSpanEnd(s, k);
                    k = (end > 0 ? end : k + FenceLength(s, k)) - 1;
                    break;
                }
                case '[':
                    depth++;
                    break;
                case ']':
                {
                    int tailEnd = At(s, k + 1) is '(' or '{' ? TailEnd(s, k) : 0;
                    if (tailEnd < 0) break;   // text
                    if (depth-- == 0) return k;
                    if (tailEnd > 0) k = tailEnd - 1;
                    break;
                }
            }
        }
        return -1;
    }

    /// <summary>End (exclusive) of the link or colour tail whose <c>]</c>
    /// is at <paramref name="close"/>, or -1 when it never closes:
    /// url_body and color_body run to the next <c>)</c> or <c>}</c>.</summary>
    private static int TailEnd(string s, int close)
    {
        int end = s.IndexOf(s[close + 1] == '(' ? ')' : '}', close + 2);
        return end < 0 ? -1 : end + 1;
    }

    /// <summary>End (exclusive) of the <c>$..$</c> span opening at
    /// <paramref name="i"/> and closing before <paramref name="bound"/>, or
    /// -1. The closer is the next <c>$</c> that is not escaped, the same
    /// one math_dollar's body stops at: that body takes <c>\</c> and the
    /// character after it as one piece. An escaped space before the closer
    /// is such a piece, not a bare space. A <c>\text{..}</c> group is one
    /// piece too (<see cref="TextGroupEnd"/>); the <c>$</c> inside it go
    /// into <paramref name="textDollars"/>.</summary>
    private static int InlineDollarEnd(string s, int i, int bound, List<int> textDollars)
    {
        textDollars.Clear();
        if (i + 1 >= bound || char.IsWhiteSpace(s[i + 1])) return -1;
        bool bareSpaceBefore = false;
        for (int k = i + 1; k < bound; k++)
        {
            switch (s[k])
            {
                case '$':
                    if (bareSpaceBefore || char.IsAsciiDigit(At(s, k + 1))) return -1;
                    return CodeSpanCrosses(s, i + 1, k) ? -1 : k + 1;
                case '\\':
                    if (k + 1 >= bound) return -1;
                    int text = TextGroupEnd(s, k, bound, textDollars);
                    k = text > 0 ? text - 1 : k + 1;
                    bareSpaceBefore = false;
                    break;
                default:
                    bareSpaceBefore = char.IsWhiteSpace(s[k]);
                    break;
            }
        }
        return -1;
    }

    /// <summary>End (exclusive) of the <c>$$..$$</c> span opening at
    /// <paramref name="i"/> and closing before <paramref name="bound"/>, or
    /// -1. The closer is the next unescaped <c>$$</c>: math_dollar2 lexes a
    /// body of one or more pieces (a non-dollar, or <c>\</c> and the
    /// character after it), each of which may carry one <c>$</c> in front,
    /// and then <c>$$</c>. A <c>\text{..}</c> group is one piece, as in
    /// <see cref="InlineDollarEnd"/>.</summary>
    private static int DisplayDollarEnd(string s, int i, int bound, List<int> textDollars)
    {
        textDollars.Clear();
        for (int k = i + 2; k < bound; k++)
        {
            switch (s[k])
            {
                case '$':
                    if (At(s, k + 1) != '$') break;   // a lone `$` is body
                    return k == i + 2 || CodeSpanCrosses(s, i + 2, k) ? -1 : k + 2;
                case '\\':
                    if (k + 1 >= bound) return -1;
                    int text = TextGroupEnd(s, k, bound, textDollars);
                    k = text > 0 ? text - 1 : k + 1;
                    break;
            }
        }
        return -1;
    }

    /// <summary>End (exclusive) of the <c>\text{..}</c> group at
    /// <paramref name="k"/>, or -1 when there is none or its braces do not
    /// balance before <paramref name="bound"/>. Pandoc's math scanner reads
    /// <c>\text</c> and the brace group right after it as one piece of the
    /// body (a <c>\</c> inside takes the next character with it), so a
    /// <c>$</c> in the braces closes nothing. Each such <c>$</c> is added
    /// to <paramref name="dollars"/>, for the caller to hide from the lexer
    /// once the span pairs.</summary>
    private static int TextGroupEnd(string s, int k, int bound, List<int> dollars)
    {
        const string Text = @"\text{";
        if (!s.AsSpan(k, bound - k).StartsWith(Text)) return -1;
        int found = dollars.Count;
        int depth = 1;
        for (int j = k + Text.Length; j < bound; j++)
        {
            switch (s[j])
            {
                case '\\':
                    j++;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    if (--depth == 0) return j + 1;
                    break;
                case '$':
                    dollars.Add(j);
                    break;
            }
        }
        dollars.RemoveRange(found, dollars.Count - found);
        return -1;
    }

    /// <summary>End (exclusive) of a <c>\(..\)</c> or <c>\[..\]</c> body
    /// starting at <paramref name="from"/> and closing before
    /// <paramref name="bound"/>, or -1. math_paren and math_bracket lex
    /// every <c>\X</c> as one fragment and pop on <c>\)</c> / <c>\]</c>; a
    /// <c>\</c> at the very end matches nothing.</summary>
    private static int LatexEnd(string s, int from, char close, int bound)
    {
        for (int k = from; k < bound; k++)
        {
            if (s[k] != '\\') continue;
            if (k + 1 >= bound) return -1;
            if (s[k + 1] == close) return CodeSpanCrosses(s, from, k) ? -1 : k + 2;
            k++;
        }
        return -1;
    }

    /// <summary>End (exclusive) of a <c>\boxed{..}</c> body starting at
    /// <paramref name="from"/> and closing before <paramref name="bound"/>,
    /// or -1. math_boxed nests on <c>{</c> and <c>}</c>, lexes <c>\X</c> as
    /// one fragment, and has no rule for <c>\{</c> or <c>\}</c>.</summary>
    private static int BoxedEnd(string s, int from, int bound)
    {
        int depth = 1;
        for (int k = from; k < bound; k++)
        {
            switch (s[k])
            {
                case '{':
                    depth++;
                    break;
                case '}':
                    if (--depth == 0) return CodeSpanCrosses(s, from, k) ? -1 : k + 1;
                    break;
                case '\\':
                    if (k + 1 >= bound || s[k + 1] is '{' or '}') return -1;
                    k++;
                    break;
            }
        }
        return -1;
    }

    /// <summary>End (exclusive) of the code span whose fence starts at
    /// <paramref name="i"/>, or -1 when it never closes. A <c>``</c>
    /// fence closes at the next <c>``</c> (a single backtick inside lexes
    /// with the character after it); a <c>`</c> fence at the next
    /// <c>`</c>.</summary>
    private static int CodeSpanEnd(string s, int i)
    {
        if (FenceLength(s, i) == 1)
        {
            int close = s.IndexOf('`', i + 1);
            return close < 0 ? -1 : close + 1;
        }
        for (int k = i + 2; k < s.Length; k++)
        {
            if (s[k] != '`') continue;
            if (At(s, k + 1) == '`') return k + 2;
            k++;
        }
        return -1;
    }

    private static int FenceLength(string s, int i) => At(s, i + 1) == '`' ? 2 : 1;

    /// <summary>Whether a code span opening in
    /// [<paramref name="from"/>, <paramref name="closer"/>) is still open
    /// at <paramref name="closer"/>, which makes that closer code rather
    /// than math.</summary>
    private static bool CodeSpanCrosses(string s, int from, int closer)
    {
        for (int k = from; k < closer; k++)
        {
            if (s[k] == '\\') { k++; continue; }
            if (s[k] != '`') continue;
            int end = CodeSpanEnd(s, k);
            if (end > closer) return true;
            k = end > 0 ? end - 1 : k + FenceLength(s, k) - 1;
        }
        return false;
    }

    private static char At(string s, int i) => i < s.Length ? s[i] : '\0';

    private static bool IsLoneCarriageReturn(string s, int i) => At(s, i) == '\r' && At(s, i + 1) != '\n';

    /// <summary>Turns <see cref="MarkUnpaired"/>'s stand-ins back into
    /// the delimiter characters they replaced.</summary>
    private static string RestoreUnpaired(string text) =>
        text.AsSpan().IndexOfAnyInRange(UnpairedDollar, UnpairedBang) < 0
            ? text
            : text.Replace(UnpairedDollar, '$').Replace(UnpairedBackslash, '\\')
                .Replace(UnpairedBracket, '[').Replace(UnpairedCloseBracket, ']')
                .Replace(UnpairedBacktick, '`').Replace(UnpairedBang, '!');

    /// <summary>Post-pass over the raw span list: flatten <see cref="MdGroup"/>
    /// wrappers (transient containers produced by plain-bracket spans)
    /// into the parent sequence, then pair <see cref="MdStarMarker"/>
    /// tokens into <see cref="MdEmphasis"/>. Run on every inline-content
    /// region — top-level spans plus the inner content of link / color
    /// containers. <see cref="MdEmphasis"/> nodes built inside this pass
    /// already have processed content, so they don't get re-walked.</summary>
    private static IReadOnlyList<MdInline> Process(IReadOnlyList<MdInline> spans) =>
        MergeAdjacentLiterals(PairEmphasis(Flatten(spans)));

    /// <summary>Coalesce adjacent <see cref="MdLiteral"/> spans into one,
    /// recursively inside containers. The lexer emits separate text
    /// tokens for non-space runs vs space runs (so the hard-break
    /// rule's leading `  +` can win on longest match), which leaves
    /// consumers with fragmented literals like [text("Hello"),
    /// text(" "), text("world")] for plain prose. This pass stitches
    /// them back together so downstream consumers see the natural
    /// single literal. Recurses into <see cref="MdEmphasis.Content"/>
    /// because nested emphasis is the most common place adjacent
    /// literals stay split.</summary>
    private static IReadOnlyList<MdInline> MergeAdjacentLiterals(IReadOnlyList<MdInline> spans)
    {
        // First pass: recurse into containers. Always do this because
        // even when the top-level has no adjacent literals, inner
        // contents might.
        var recursed = new List<MdInline>(spans.Count);
        bool changed = false;
        foreach (var s in spans)
        {
            if (s is MdEmphasis em)
            {
                var inner = MergeAdjacentLiterals(em.Content);
                if (!ReferenceEquals(inner, em.Content)) { changed = true; recursed.Add(new MdEmphasis(em.Level, inner)); }
                else recursed.Add(em);
            }
            else
            {
                recursed.Add(s);
            }
        }
        var src = changed ? (IReadOnlyList<MdInline>)recursed : spans;

        bool hasAdjacent = false;
        for (int i = 0; i + 1 < src.Count && !hasAdjacent; i++)
            hasAdjacent = src[i] is MdLiteral && src[i + 1] is MdLiteral;
        if (!hasAdjacent) return src;

        var result = new List<MdInline>(src.Count);
        foreach (var s in src)
        {
            if (s is MdLiteral lit
                && result.Count > 0
                && result[result.Count - 1] is MdLiteral prev)
            {
                result[result.Count - 1] = new MdLiteral(prev.Text + lit.Text);
            }
            else
            {
                result.Add(s);
            }
        }
        return result;
    }

    private static IReadOnlyList<MdInline> Flatten(IReadOnlyList<MdInline> spans)
    {
        bool anyGroup = false;
        for (int i = 0; i < spans.Count && !anyGroup; i++)
            anyGroup = spans[i] is MdGroup;
        if (!anyGroup) return spans;

        var result = new List<MdInline>(spans.Count);
        foreach (var s in spans)
        {
            if (s is MdGroup g) result.AddRange(Flatten(g.Children));
            else result.Add(s);
        }
        return result;
    }

    /// <summary>
    /// Post-pass over the flat span list: pairs <see cref="MdStarMarker"/>
    /// tokens into <see cref="MdEmphasis"/> nodes via a delimiter stack
    /// (the same approach Markdig uses). Unmatched markers are rewritten
    /// back to literal text so the rendered output shows the original
    /// `*` or `**` — matching CommonMark's behaviour for unbalanced
    /// delimiters (a stray `*` in <c>2 * 3 = 6</c> stays literal).
    ///
    /// <para><b>Pairing rule:</b> scan left to right. On each marker,
    /// search backwards in the open-marker stack for a same-level match.
    /// If found, collapse the range between them into an
    /// <see cref="MdEmphasis"/>; otherwise push the marker as an
    /// open candidate. CommonMark's flanking rules (which `*` left/right
    /// flanks based on surrounding whitespace + punctuation) are not yet
    /// applied — for typical LLM output the simpler "match nearest
    /// same-level opener" is close enough.</para>
    /// </summary>
    private static IReadOnlyList<MdInline> PairEmphasis(IReadOnlyList<MdInline> flat)
    {
        // Fast path: nothing to pair.
        var anyMarker = false;
        for (int i = 0; i < flat.Count && !anyMarker; i++)
            anyMarker = flat[i] is MdStarMarker;
        if (!anyMarker) return flat;

        // Output buffer + stack of (index-in-output, level) for open markers.
        var output = new List<MdInline>(flat.Count);
        var openStack = new List<(int Index, int Level)>();

        foreach (var span in flat)
        {
            if (span is not MdStarMarker marker)
            {
                output.Add(span);
                continue;
            }

            // Look for matching opener: same level, nearest first.
            int matchIdx = -1;
            for (int j = openStack.Count - 1; j >= 0; j--)
            {
                if (openStack[j].Level == marker.Level) { matchIdx = j; break; }
            }

            if (matchIdx < 0)
            {
                // No matching opener — record as potential opener.
                openStack.Add((output.Count, marker.Level));
                output.Add(marker);
                continue;
            }

            // Collapse: drop any intervening unpaired openers (they didn't
            // find a match, so they revert to literal in the rewrite step
            // below) and wrap the content between opener and this marker
            // in an MdEmphasis.
            int openerOutputIdx = openStack[matchIdx].Index;
            openStack.RemoveRange(matchIdx, openStack.Count - matchIdx);
            var content = new List<MdInline>(output.Count - openerOutputIdx - 1);
            for (int k = openerOutputIdx + 1; k < output.Count; k++)
                content.Add(output[k]);
            output.RemoveRange(openerOutputIdx, output.Count - openerOutputIdx);
            output.Add(new MdEmphasis(marker.Level, content));
        }

        // Rewrite any markers that survived (unpaired delimiters) back
        // to literal text so the renderer shows the original characters.
        for (int i = 0; i < output.Count; i++)
        {
            if (output[i] is MdStarMarker m)
                output[i] = new MdLiteral(m.Level switch { 3 => "***", 2 => "**", _ => "*" });
        }

        return output;
    }

    // ── Span-list assembly (epsilon-base + cons recursion) ────────────

    public object Visit(MarkdownInline.SpansEmpty node) =>
        (IReadOnlyList<MdInline>)Array.Empty<MdInline>();

    public object Visit(MarkdownInline.SpansCons node)
    {
        var head = (MdInline)node.Arg0.Content;
        var tail = (IReadOnlyList<MdInline>)node.Arg1.Content;
        var list = new List<MdInline>(tail.Count + 1) { head };
        list.AddRange(tail);
        return (IReadOnlyList<MdInline>)list;
    }

    // ── Plain text ────────────────────────────────────────────────────

    public object Visit(MarkdownInline.LiteralSpan node)
    {
        var raw = (string)node.Arg0.Content;
        // `\name` LaTeX commands in prose surface to the inline grammar
        // as `text` tokens via the `\\[a-zA-Z]+` rule. The Markdig path
        // ran SubstituteLooseLatex to convert known commands to Unicode
        // (\div → ÷, \alpha → α, etc.) before parsing. Replicate that
        // here at the literal-emission site so the LALR path produces
        // the same output for prose-embedded LaTeX commands.
        if (raw.Length >= 2 && raw[0] == '\\' && IsAsciiLetter(raw[1]))
        {
            var name = raw.Substring(1);
            if (s_looseLatexCommands.TryGetValue(name, out var glyph))
                return new MdLiteral(glyph);
        }
        // Text runs and dollar bodies are the only tokens a stand-in can land in.
        return new MdLiteral(RestoreUnpaired(raw));
    }

    private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

    /// <summary>Mirror of <c>MarkdownRenderer.LooseLatexCommands</c> for
    /// prose-embedded LaTeX commands (commands the model emits without
    /// math-delimiter wrappers, like <c>v \ll c</c> or <c>131 \div 2</c>).
    /// The Markdig path applies these via a regex pre-pass; the LALR
    /// path applies them per-literal at visit time.</summary>
    private static readonly Dictionary<string, string> s_looseLatexCommands = new(StringComparer.Ordinal)
    {
        ["div"] = "÷",
        ["times"] = "×",
        ["cdot"] = "·",
        ["approx"] = "≈",
        ["equiv"] = "≡",
        ["to"] = "→",
        ["rightarrow"] = "→",
        ["leftarrow"] = "←",
        ["infty"] = "∞",
        ["partial"] = "∂",
        ["nabla"] = "∇",
        ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ", ["delta"] = "δ",
        ["epsilon"] = "ε", ["theta"] = "θ", ["lambda"] = "λ", ["mu"] = "μ",
        ["pi"] = "π", ["sigma"] = "σ", ["phi"] = "φ", ["omega"] = "ω",
        ["Gamma"] = "Γ", ["Delta"] = "Δ", ["Sigma"] = "Σ", ["Omega"] = "Ω",
        ["quad"] = "  ", ["qquad"] = "    ",
        ["dots"] = "…", ["ldots"] = "…", ["cdots"] = "⋯", ["vdots"] = "⋮", ["ddots"] = "⋱",
        ["ll"] = "≪", ["gg"] = "≫",
    };

    // ── Inline code (single-backtick fences) ─────────────────────────

    public object Visit(MarkdownInline.CodeSpan node) =>
        new MdCodeInline((string)node.Arg1.Content);

    public object Visit(MarkdownInline.CodeSpan2 node) =>
        new MdCodeInline((string)node.Arg1.Content);

    public object Visit(MarkdownInline.CodeBody2Empty node) => string.Empty;

    public object Visit(MarkdownInline.CodeBody2Cons node)
    {
        var head = (string)node.Arg0.Content;
        var tail = (string)node.Arg1.Content;
        return head + tail;
    }

    // ── Backslash escape (\* → *, \_ → _, \\ → \, …) ─────────────────

    public object Visit(MarkdownInline.EscapeSpan node)
    {
        // Lexer matched `\X` as the two-char escape token where X is a
        // non-letter. Most of these are CommonMark escapes — `\X` → X
        // literal — but four LaTeX thin-space macros (`\,` `\;` `\:`
        // `\!`) need special handling: the math-benchmark LaTeX renders
        // them as visible (or invisible) horizontal space, so render
        // them as whitespace rather than literal punctuation.
        var raw = (string)node.Arg0.Content;
        if (raw.Length >= 2)
        {
            switch (raw[1])
            {
                case ',':
                case ';':
                case ':':
                    return new MdLiteral(" ");
                case '!':
                    return new MdLiteral(string.Empty);
            }
            return new MdLiteral(raw.Substring(1));
        }
        return new MdLiteral(raw);
    }

    // ── Emphasis markers (paired by the post-pass) ───────────────────

    public object Visit(MarkdownInline.StarMarkerSpan node) => new MdStarMarker(Level: 1);
    public object Visit(MarkdownInline.Star2MarkerSpan node) => new MdStarMarker(Level: 2);
    public object Visit(MarkdownInline.Star3MarkerSpan node) => new MdStarMarker(Level: 3);

    // ── Line breaks (soft / hard per CommonMark) ─────────────────────

    public object Visit(MarkdownInline.SoftBreakSpan node) => new MdLineBreak(Hard: false);
    public object Visit(MarkdownInline.HardBreakSpan node) => new MdLineBreak(Hard: true);

    // ── Bracket constructs: link, color, plain ───────────────────────
    //
    // The grammar disambiguates via the `](` / `]{` compound tokens —
    // the lexer emits link_tail_open or color_tail_open only when the
    // matching follower is present, so a plain `[text]` (with no tail)
    // tokenises as lbracket + Spans + rbracket and reduces here
    // unchanged. Link and color bodies are captured by dedicated
    // lexer states (url_body / color_body) that pop on the close
    // delimiter; the visitor just receives the raw body string.

    public object Visit(MarkdownInline.PlainBracketSpan node)
    {
        // `[text]` with no link/color tail — surface as literal text
        // including the brackets. Inline content inside the brackets
        // is preserved so e.g. `[**bold**]` still emphasises.
        var inner = (IReadOnlyList<MdInline>)node.Arg1.Content;
        var list = new List<MdInline>(inner.Count + 2)
        {
            new MdLiteral("["),
        };
        list.AddRange(inner);
        list.Add(new MdLiteral("]"));
        // Returning a list here would conflict with Span's expected
        // single-inline shape — wrap in a synthetic single MdLink-like
        // container? No: just return the inline list via a marker that
        // gets flattened in SpansCons. Simpler: emit as a literal that
        // re-stringifies the inner. For nested emphasis to survive,
        // emit a Group container instead.
        return new MdGroup(list);
    }

    public object Visit(MarkdownInline.LinkSpan node)
    {
        var text = Process((IReadOnlyList<MdInline>)node.Arg1.Content);
        var url = (string)node.Arg3.Content;
        return new MdLink(text, url);
    }

    // Same arg shape as LinkSpan (Arg0=image_open, Arg1=Spans alt-text,
    // Arg2=link_tail_open `](`, Arg3=urlbody, Arg4=rparen) — the image
    // production reuses the link tail tokens, differing only in the opener.
    public object Visit(MarkdownInline.ImageSpan node)
    {
        var alt = Process((IReadOnlyList<MdInline>)node.Arg1.Content);
        var url = (string)node.Arg3.Content;
        return new MdImage(alt, url);
    }

    public object Visit(MarkdownInline.ColorSpan node)
    {
        var text = Process((IReadOnlyList<MdInline>)node.Arg1.Content);
        var color = (string)node.Arg3.Content;
        return new MdColor(text, color);
    }

    // ── Math: dollar forms ($..$, $$..$$) ─────────────────────────────
    //
    // A dollar body can hold stand-ins: the `$` inside a `\text{..}`
    // group, which MarkUnpaired hides from the closer.

    public object Visit(MarkdownInline.MathSpan node) =>
        BuildMath(RestoreUnpaired((string)node.Arg1.Content));

    public object Visit(MarkdownInline.MathDisplaySpan node) =>
        BuildMath(RestoreUnpaired((string)node.Arg1.Content), isDisplay: true);

    // ── Math: LaTeX-backslash forms (\(..\), \[..\]) ──────────────────

    public object Visit(MarkdownInline.MathParenSpan node) =>
        BuildMath((string)node.Arg1.Content);

    public object Visit(MarkdownInline.MathBracketSpan node) =>
        BuildMath((string)node.Arg1.Content, isDisplay: true);

    // ── Math: \boxed{..} with balanced braces ─────────────────────────
    //
    // Body is captured as a balanced-brace structure (BoxedBody). The
    // sub-parser invocation wraps the body in `\boxed{X}` again so the
    // existing math pipeline's \boxed handler (frame in Unicode mode,
    // strip in box mode) sees the macro it expects.

    public object Visit(MarkdownInline.MathBoxedSpan node)
    {
        // Source = the body alone (consistent with the other math forms,
        // where the wrapper delimiters are not part of `Source`). The
        // math grammar's lexer has no `\boxed` rule, so feeding the
        // wrapped `\boxed{X}` directly would tokenise as cmd(\boxed) +
        // group({X}), parse as juxtaposition, and render `\boxedX` with
        // both halves visible. Instead we recursively parse the body
        // alone and wrap the result in brackets — same convention the
        // Markdig path's ExpandLatexMacros uses for `\boxed{}`.
        var body = (string)node.Arg1.Content;
        return new MdMathInline(
            Source: body,
            Unicode: "[" + ParseMathUnicode(body) + "]",
            Builder: null);
    }

    // ── LatexBody assembly: concat frags ──────────────────────────────

    public object Visit(MarkdownInline.LatexBodyEmpty node) => string.Empty;

    public object Visit(MarkdownInline.LatexBodyCons node)
    {
        var head = (string)node.Arg0.Content;
        var tail = (string)node.Arg1.Content;
        return head + tail;
    }

    // ── BoxedBody assembly: items can be frags or { nested } groups ──

    public object Visit(MarkdownInline.BoxedBodyEmpty node) => string.Empty;

    public object Visit(MarkdownInline.BoxedBodyCons node)
    {
        var head = (string)node.Arg0.Content;
        var tail = (string)node.Arg1.Content;
        return head + tail;
    }

    public object Visit(MarkdownInline.BoxedItemFrag node) =>
        (string)node.Arg0.Content;

    public object Visit(MarkdownInline.BoxedItemGroup node)
    {
        // Preserve the literal `{ inner }` so the LaTeX sub-parser sees
        // it as the grouping construct it is.
        var inner = (string)node.Arg1.Content;
        return "{" + inner + "}";
    }

    // ── LaTeX sub-parser invocation ───────────────────────────────────

    private static MdMathInline BuildMath(string body, bool isDisplay = false) =>
        new(Source: body,
            Unicode: ParseMathUnicode(body),
            Builder: null)
        { IsDisplay = isDisplay };

    private static readonly LatexUnicodeVisitor s_unicodeVisitor = new();
    private static readonly global::LALR.CC.Parser s_unicodeParser = Latex.BuildParser(s_unicodeVisitor);
    private static readonly Dictionary<string, LexRule[]> s_mathLexerTable = Latex.BuildLexer();

    private static string ParseMathUnicode(string source)
    {
        // Delegate to MarkdownMacros.RenderMathUnicode — it does the full
        // ExpandLatexMacros pre-pass (`\text{}`, `\boxed{}`, `\ce{}`,
        // `\begin{...}\end{...}`, backslash-escape resolution, unsafe-byte
        // placeholder mapping) plus the LaTeX grammar parse + Unicode
        // visitor. Duplicating that here would mean re-implementing the
        // macro-expansion logic in the inline visitor; delegating keeps a
        // single source of truth.
        return MarkdownMacros.RenderMathUnicode(source);
    }

    // ── Parser construction ───────────────────────────────────────────

    private static readonly global::LALR.CC.Parser s_parser =
        MarkdownInline.BuildParser(new MarkdownInlineVisitor());

    private static readonly Dictionary<string, LexRule[]> s_lexerTable =
        MarkdownInline.BuildLexer();
}
