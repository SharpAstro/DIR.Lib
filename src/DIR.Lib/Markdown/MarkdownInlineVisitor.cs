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
    /// resolved. Returns an empty list on parse error so the caller
    /// can fall through to a literal-text render of the source.</summary>
    public IReadOnlyList<MdInline> Parse(string source)
    {
        if (string.IsNullOrEmpty(source)) return Array.Empty<MdInline>();

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
        return new MdLiteral(raw);
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

    public object Visit(MarkdownInline.ColorSpan node)
    {
        var text = Process((IReadOnlyList<MdInline>)node.Arg1.Content);
        var color = (string)node.Arg3.Content;
        return new MdColor(text, color);
    }

    // ── Math: dollar forms ($..$, $$..$$) ─────────────────────────────

    public object Visit(MarkdownInline.MathSpan node) =>
        BuildMath((string)node.Arg1.Content);

    public object Visit(MarkdownInline.MathDisplaySpan node) =>
        BuildMath((string)node.Arg1.Content);

    // ── Math: LaTeX-backslash forms (\(..\), \[..\]) ──────────────────

    public object Visit(MarkdownInline.MathParenSpan node) =>
        BuildMath((string)node.Arg1.Content);

    public object Visit(MarkdownInline.MathBracketSpan node) =>
        BuildMath((string)node.Arg1.Content);

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

    private static MdMathInline BuildMath(string body) =>
        new(Source: body,
            Unicode: ParseMathUnicode(body),
            Builder: null);

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
