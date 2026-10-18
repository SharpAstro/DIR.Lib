using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DIR.Lib.MathLayout;
using LALR.CC.LexicalGrammar;

namespace DIR.Lib.Markdown;

/// <summary>
/// Parser-side facade for the markdown / LaTeX math pipeline: macro
/// expansion (<c>\text{}</c>, <c>\boxed{}</c>, <c>\ce{}</c>,
/// <c>\begin{}/\end{}</c> environments), backslash-escape resolution,
/// the math grammar's lexer-safety placeholder pass, and the entry point
/// <see cref="RenderMathUnicode"/> that turns a math-source string into
/// inline-Unicode output. Used by the visitors in this namespace and by
/// <c>Console.Lib.MarkdownRenderer</c> via <c>InternalsVisibleTo</c>.
/// </summary>
public static class MarkdownMacros
{
    /// <summary>
    /// Cached math parser + lexer table. The LALR.CC source generator
    /// pre-bakes the parse table and lexer transitions at build time, so
    /// constructing these is just struct/array initialization — but we
    /// still hold the result so each math node doesn't pay the cost.
    /// </summary>
    private static readonly LatexUnicodeVisitor MathVisitor = new();

    private static readonly LALR.CC.Parser MathParser = Latex.BuildParser(MathVisitor);

    public static readonly System.Collections.Generic.Dictionary<string, LexRule[]> MathLexerTable = Latex.BuildLexer();

    /// <summary>
    /// Lazily resolved math-rendering font. <see cref="ResolveMathFont"/>
    /// picks the first existing candidate from a STIX-Math-preferred list;
    /// null means no usable font is installed (in which case pixel-rendered
    /// math falls back to the Unicode path).
    /// </summary>
    private static string? s_mathFontPath;

    private static bool s_mathFontResolved;

    /// <summary>
    /// Parse + visit a LaTeX math source string through <see cref="LatexUnicodeVisitor"/>.
    /// Returns the rendered Unicode string, or the literal input wrapped in
    /// fallback markers on parse error — so a single mangled formula doesn't
    /// take down the surrounding markdown render.
    ///
    /// Before parsing, the source is run through <see cref="ExpandLatexMacros"/>
    /// which extracts <c>\text{...}</c> and <c>\boxed{...}</c> bodies. The math
    /// grammar treats them as opaque <c>\name</c> commands which would otherwise
    /// surface as literal "\text" / "\boxed" in the output and lose any internal
    /// whitespace (since the grammar tokenises letters as math-italic variables
    /// and discards whitespace).
    /// </summary>
    public static string RenderMathUnicode(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return string.Empty;

        // A set's escaped braces are grammar atoms too, lexed first so the
        // visitor sees the pair: an operator name before one takes no space,
        // as before a paren. An unbalanced one ("\{x") does not parse, and
        // the formula is rendered again with each brace a placeholder atom,
        // which is how every escaped brace rendered before.
        if (TryRenderMathUnicode(source, lexBrackets: true, lexBraces: true, out var rendered))
            return rendered;
        if (HasEscapedBrace(source) && TryRenderMathUnicode(source, lexBrackets: true, lexBraces: false, out rendered))
            return rendered;

        // Square brackets are grammar atoms, so an unbalanced one ("[0, 1[",
        // or "\left[ x \right." once the aliases are normalised) no longer
        // parses. Such a formula gets the treatment every bracket had before
        // the grammar lexed them: a placeholder atom, spliced back verbatim.
        // A formula that fails both ways falls back to its own text, which is
        // the same string either way.
        if (source.AsSpan().IndexOfAny('[', ']') < 0)
            return rendered;
        TryRenderMathUnicode(source, lexBrackets: false, lexBraces: false, out rendered);
        return rendered;
    }

    /// <summary>True when <paramref name="source"/> spells a brace as a glyph:
    /// <c>\{</c>, <c>\}</c>, <c>\lbrace</c> or <c>\rbrace</c>.</summary>
    private static bool HasEscapedBrace(string source)
        => source.Contains(@"\{", StringComparison.Ordinal) || source.Contains(@"\}", StringComparison.Ordinal)
           || ContainsMacro(source, "lbrace") || ContainsMacro(source, "rbrace");

    private static bool TryRenderMathUnicode(string source, bool lexBrackets, bool lexBraces, out string rendered)
    {
        var (expanded, replacements) = ExpandLatexMacros(source, lexBrackets, lexBraces);

        bool parsed;
        try
        {
            using var lexer = BytesLexer.FromString(expanded, MathLexerTable);
            using var tokens = new SyncLATokenIterator(lexer);
            var item = MathParser.ParseInput(tokens, debugger: null);
            parsed = !item.IsError;
            rendered = item.IsError
                ? expanded
                : (item.Content as string ?? expanded);
        }
        catch
        {
            parsed = false;
            rendered = expanded;
        }

        // A formula that did not parse falls back to its source text, where
        // \mathrm{X} must still read as X: Mhchem wraps every chem letter in
        // one, so \ce{H+} (a bare charge, no right operand) printed \mathrm{H}+.
        if (!parsed) rendered = UnwrapMathrm(rendered);

        // Substitute placeholders back. Multiple passes because a replacement
        // for an outer macro can itself contain a placeholder for an inner one
        // (e.g. \boxed{\text{X}} — the boxed-replacement is the rendered X
        // which still references the text placeholder).
        var prev = string.Empty;
        while (prev != rendered && replacements.Count > 0)
        {
            prev = rendered;
            foreach (var kv in replacements)
                rendered = rendered.Replace(kv.Key, kv.Value);
        }
        return parsed;
    }

    /// <summary>Replaces every <c>\mathrm{X}</c> in <paramref name="text"/>
    /// with X, a nested one included.</summary>
    private static string UnwrapMathrm(string text) => ExpandBalancedMacro(text, "mathrm", UnwrapMathrm);

    /// <summary>A <c>\text{X}</c> body as it renders: X with any
    /// <c>\mathrm{...}</c> unwrapped and its backslash-non-letter macros
    /// resolved. The math pass and an environment body's pass both render
    /// <c>\text</c> through this, so the two cannot disagree.</summary>
    private static string RenderTextBody(string inner) => ResolveBackslashEscapes(UnwrapMathrm(inner));

    /// <summary>
    /// Prepares raw math source for the box (raster) path: the result is what
    /// <see cref="Latex"/>'s parser, bound to a <see cref="BoxBuildingVisitor"/>,
    /// should read. False when the box path cannot build the formula, which
    /// the caller then renders with <see cref="RenderMathUnicode"/>: a
    /// <c>\text{...}</c> or a <c>\begin{...}...\end{...}</c> environment, for
    /// which there is no box yet.
    ///
    /// <para>The steps, in order: turn away <c>\text</c> and environments;
    /// <see cref="NormalizeLatexAliases"/>; <c>\boxed{X}</c> to X (there is no
    /// boxed frame); <c>\ce{X}</c> to <see cref="Mhchem.ToLatex"/> of X; turn
    /// away again, since either expansion can bring one in; then
    /// <see cref="ResolveBackslashEscapes(string)"/>, except that <c>\{</c> and
    /// <c>\}</c> stay as written. The grammar lexes those as a set's braces,
    /// which the visitor draws as a curly <see cref="BracketBox"/>; resolved to
    /// a bare <c>{</c> they would open an invisible group. So
    /// <c>\{1, 2, 3\}</c>, <c>\left\{ … \right\}</c> and
    /// <c>\lbrace … \rbrace</c> all keep their braces. An unbalanced one does
    /// not parse, and the caller falls back to the Unicode path as for any
    /// formula the grammar rejects. The math spaces <c>\,</c> <c>\:</c>
    /// <c>\;</c> <c>\!</c> stay as written too, for the grammar to lex as
    /// space; resolved to a blank, they would be dropped.</para>
    /// </summary>
    public static bool TryPrepareMathBoxSource(string source, [NotNullWhen(true)] out string? prepared)
    {
        prepared = null;
        if (!BoxPathCanBuild(source)) return false;
        source = NormalizeLatexAliases(source);
        source = ExpandBalancedMacro(source, "boxed", inner => inner);
        source = ExpandBalancedMacro(source, "ce", inner => Mhchem.ToLatex(inner));
        if (!BoxPathCanBuild(source)) return false;
        prepared = ResolveBackslashEscapes(source, keepLexed: true);
        return true;
    }

    /// <summary>
    /// The space TeX puts between two adjacent atoms of the given classes,
    /// where it is a thin space (3 mu, a sixth of an em) in every style: an
    /// Op and a following Ord or Op, and an Ord or Close and a following Op.
    /// So <c>\sin x</c>, <c>\sin \cos x</c> and <c>x \sin y</c> are spaced,
    /// while Op then Open is not, so <c>\sin(x)</c> stays tight. Both math
    /// visitors ask it where they juxtapose two pieces, and only there, so
    /// nothing is added at the ends of a formula.
    /// </summary>
    internal static bool ThinSpaceBetween(MathAtomClass left, MathAtomClass right) =>
        right == MathAtomClass.Op
            ? left is MathAtomClass.Ord or MathAtomClass.Op or MathAtomClass.Close
            : left == MathAtomClass.Op && right == MathAtomClass.Ord;

    /// <summary>
    /// The operator names both math visitors know, each with the text it
    /// prints: LaTeX's whole predefined set, plus <c>\argmax</c> and
    /// <c>\argmin</c>. Each is an Op atom, set upright.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> OperatorNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sin"] = "sin", ["cos"] = "cos", ["tan"] = "tan",
            ["sec"] = "sec", ["csc"] = "csc", ["cot"] = "cot",
            ["arcsin"] = "arcsin", ["arccos"] = "arccos", ["arctan"] = "arctan",
            ["sinh"] = "sinh", ["cosh"] = "cosh", ["tanh"] = "tanh", ["coth"] = "coth",
            ["log"] = "log", ["ln"] = "ln", ["lg"] = "lg", ["exp"] = "exp",
            ["lim"] = "lim", ["max"] = "max", ["min"] = "min",
            ["sup"] = "sup", ["inf"] = "inf", ["limsup"] = "lim sup", ["liminf"] = "lim inf",
            ["argmax"] = "argmax", ["argmin"] = "argmin",
            ["det"] = "det", ["dim"] = "dim", ["gcd"] = "gcd",
            ["arg"] = "arg", ["deg"] = "deg", ["hom"] = "hom", ["ker"] = "ker", ["Pr"] = "Pr",
        };

    /// <summary>
    /// The <see cref="OperatorNames"/> that take their scripts as limits,
    /// below and above, in display style (the others take them to the
    /// right): LaTeX's set, which includes <c>\Pr</c>, plus <c>\argmax</c>
    /// and <c>\argmin</c>. The box path draws them at the body size, as TeX
    /// does; only the symbol big operators grow.
    /// </summary>
    internal static readonly IReadOnlySet<string> LimitOperatorNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "lim", "limsup", "liminf", "max", "min", "sup", "inf",
        "argmax", "argmin", "det", "gcd", "Pr",
    };

    /// <summary>
    /// The text of the braced argument of a token that carries one,
    /// <c>\textrm{X}</c> as the grammar lexes it: X as it reads, blanks
    /// included. A group's braces inside X print as
    /// nothing, as in TeX, and an escaped brace as the brace. A math space
    /// (<c>\, \: \;</c>, and <c>\!</c>) is a blank (nothing), as the Unicode
    /// path renders it: the box path's preparation keeps these for the
    /// grammar to lex, so they reach the token as written, and were painted
    /// with their backslash. Any other escape pair stays as written (a
    /// placeholder still to be spliced back).
    /// </summary>
    internal static string BracedArgument(string token)
    {
        var open = token.IndexOf('{');
        var body = token.AsSpan(open + 1, token.Length - open - 2);
        var sb = new StringBuilder(body.Length);
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] == '\\' && i + 1 < body.Length && body[i + 1] is ',' or ':' or ';' or '!')
            {
                sb.Append(RenderBackslashEscape(body[++i]));
            }
            else if (body[i] == '\\' && i + 1 < body.Length)
            {
                if (body[i + 1] is not ('{' or '}')) sb.Append('\\');
                sb.Append(body[++i]);
            }
            else if (body[i] is not ('{' or '}'))
            {
                sb.Append(body[i]);
            }
        }
        return sb.ToString();
    }

    /// <summary>False when <paramref name="source"/> holds what only the
    /// Unicode path can draw: <c>\text</c> or an environment.</summary>
    private static bool BoxPathCanBuild(string source)
        => !ContainsMacro(source, "text") && !source.Contains(@"\begin", StringComparison.Ordinal);

    /// <summary>
    /// Expands the LaTeX macros that the math-only grammar can't represent.
    /// Each match is replaced with a synthetic <c>\PHxxx</c> command (which the
    /// lexer accepts as a generic <c>cmd</c> atom and the Unicode visitor emits
    /// verbatim, so we can splice the expansion back in after rendering).
    ///
    /// Macros handled:
    /// <list type="bullet">
    ///   <item><c>\text{X}</c> — X is captured verbatim, preserving whitespace.</item>
    ///   <item><c>\boxed{X}</c> — X is recursively math-rendered, then wrapped in
    ///         square brackets as a Unicode-friendly stand-in for the LaTeX
    ///         <c>\boxed</c> frame.</item>
    /// </list>
    ///
    /// <para>With <paramref name="lexBraces"/>, <c>\{</c> and <c>\}</c> are left
    /// as written for the grammar to lex as a set's braces; without it, each is
    /// a placeholder like the other backslash escapes. With
    /// <paramref name="lexBrackets"/> unset, so is each square bracket.</para>
    /// </summary>
    private static (string expanded, List<KeyValuePair<string, string>> replacements) ExpandLatexMacros(
        string source, bool lexBrackets, bool lexBraces)
    {
        var replacements = new List<KeyValuePair<string, string>>();
        var counter = 0;

        string NewPlaceholder()
        {
            // Lexable as `\\[a-zA-Z]+` so it tokenises as a single `cmd` atom.
            // Base-26 letters give us plenty of room without colliding with any
            // real LaTeX command name.
            var n = counter++;
            var sb = new StringBuilder("\\PH");
            do
            {
                sb.Append((char)('a' + (n % 26)));
                n /= 26;
            } while (n > 0);
            return sb.ToString();
        }

        // Common LaTeX aliases the grammar doesn't recognise but reduce to a
        // known form. Done up-front so the substituted text flows through the
        // rest of the expansion as if the model had written it canonically.
        //   \dfrac / \tfrac   — display-style / text-style fractions, alias
        //                       for \frac (the grammar's cmdfrac rule).
        //   \left[ … \right]  — auto-sizing delimiters; in plain text they're
        //                       indistinguishable from the bare delimiter.
        //   \bigl / \bigr …   — sizing hints. Same treatment — strip the size
        //                       prefix and keep the delimiter as a plain char.
        source = NormalizeLatexAliases(source);

        // \begin{NAME}[args]...\end{NAME} environments (array, matrix, align,
        // pmatrix, tabular, …) are out-of-scope for the math grammar — feeding
        // the body to the parser would discard the row/column whitespace and
        // collapse it onto one line. Instead, replace the whole environment
        // with a single placeholder whose replacement is a plain-text render:
        //   \\           → newline (row break)
        //   &            → "  "  (column separator, two-space gutter)
        //   \text{X}     → X as the \text pass below renders it
        //   other macros → ResolveBackslashEscapes
        // The body never reaches the lexer, so spaces and newlines survive.
        source = ExpandBalancedEnvironment(source, body =>
        {
            var key = NewPlaceholder();
            replacements.Add(new KeyValuePair<string, string>(key, RenderEnvironmentBody(body)));
            return key;
        });

        // \ce{X} → inline LaTeX math source via Mhchem.ToLatex. Phase-2
        // routes chem through the same parser + visitor pipeline as ordinary
        // math, so display-mode chem picks up real sub/super box layout
        // under BoxBuildingVisitor (Sixel / sextant / half-block rasters)
        // and inline chem still reaches the same Unicode glyphs via the
        // grammar's script-to-Unicode mapping in LatexUnicodeVisitor.
        //
        // Runs BEFORE \text{} so any \text{...} the chem emitter chooses to
        // produce in the future (e.g. for upright state markers) gets caught
        // by the \text pass that follows.
        source = ExpandBalancedMacro(source, "ce", inner => Mhchem.ToLatex(inner));

        // \text{X} → placeholder; replacement is X with backslash-non-letter
        // macros already resolved (so e.g. "Yes,\ 131" renders as "Yes, 131"
        // rather than carrying the LaTeX explicit-space "\ " through to the
        // final output). The math grammar's whitespace-skip can't touch the
        // captured-text region because it never sees those bytes — it only
        // sees the opaque placeholder. Any \mathrm{X} in the body becomes X:
        // the \ce pass above runs first, so the body can hold the wrappers
        // Mhchem puts on chem letters (\text{\ce{H2O}}, or the `aq` of
        // \ce{H2O(\text {aq})}, which Mhchem reads past the blank).
        source = ExpandBalancedMacro(source, "text", inner =>
        {
            var key = NewPlaceholder();
            replacements.Add(new KeyValuePair<string, string>(key, RenderTextBody(inner)));
            return key;
        });

        // \boxed{X} → placeholder; replacement is "[X-rendered]" where X is
        // recursively run through RenderMathUnicode (so a nested \frac, \text,
        // etc. inside the box body still renders correctly).
        source = ExpandBalancedMacro(source, "boxed", inner =>
        {
            var key = NewPlaceholder();
            replacements.Add(new KeyValuePair<string, string>(key, "[" + RenderMathUnicode(inner) + "]"));
            return key;
        });

        // Outer-source pass over LaTeX backslash-non-letter macros (\, \; \: \!
        // \\ \{ \} \_ \$ \% \# \&). Each becomes a placeholder mapping to its
        // rendered equivalent so the lexer doesn't choke on the orphan `\` +
        // non-letter pair (the cmd rule is `\\[a-zA-Z]+`). A set's braces stay
        // as written when the grammar is to lex them.
        var spaceSb = new StringBuilder(source.Length);
        int p = 0;
        while (p < source.Length)
        {
            if (source[p] == '\\' && p + 1 < source.Length && !IsAsciiLetter(source[p + 1]))
            {
                if (lexBraces && source[p + 1] is '{' or '}')
                {
                    spaceSb.Append(source, p, 2);
                    p += 2;
                    continue;
                }
                var key = NewPlaceholder();
                replacements.Add(new KeyValuePair<string, string>(key, RenderBackslashEscape(source[p + 1])));
                spaceSb.Append(key);
                p += 2;
            }
            else
            {
                spaceSb.Append(source[p]);
                p++;
            }
        }
        source = spaceSb.ToString();

        // Any char the latex.lalr.yaml lexer has no rule for (Unicode operators
        // like ÷ ≈ ≤ ≥ × −, punctuation like ; ! |, …): wrap each in a
        // placeholder so the lexer keeps going. Without this, a single stray
        // U+2248 in "\sqrt{131} ≈ 11.45" kills the lex pass and \sqrt never
        // gets to render. The placeholder lexes as a cmd atom; the visitor
        // emits it verbatim; we splice the original char back in afterwards.
        var sb = new StringBuilder(source.Length);
        foreach (var ch in source)
        {
            if (IsLexerSafe(ch) && (lexBrackets || ch is not ('[' or ']')))
            {
                sb.Append(ch);
            }
            else
            {
                var key = NewPlaceholder();
                replacements.Add(new KeyValuePair<string, string>(key, ch.ToString()));
                sb.Append(key);
            }
        }
        source = sb.ToString();

        return (source, replacements);
    }

    /// <summary>
    /// True iff the latex.lalr.yaml lexer has a tokenisation rule that matches
    /// a single occurrence of <paramref name="c"/>. ASCII letters, digits, dot
    /// (number fraction), whitespace, the seven operator chars + - * / = ^ _,
    /// brackets ( ) [ ] { }, the comma, and backslash (command lead-in);
    /// anything else aborts the lex pass and so needs a placeholder substitution.
    /// </summary>
    private static bool IsLexerSafe(char c)
    {
        if (c == ' ' || c == '\t' || c == '\r' || c == '\n') return true;
        if (c >= '0' && c <= '9') return true;
        if (c == '.') return true;
        if (IsAsciiLetter(c)) return true;
        return c is '+' or '-' or '*' or '/' or '=' or '^' or '_'
                or '(' or ')' or '[' or ']' or '{' or '}' or ',' or '\\';
    }

    /// <summary>
    /// Walks <paramref name="source"/> looking for <c>\<paramref name="commandName"/>{…}</c>
    /// with balanced braces, replacing each occurrence with the result of
    /// <paramref name="onMatch"/> applied to the inner body. Skips matches
    /// where the command name has trailing letters (so <c>\textit</c> doesn't
    /// match a <c>\text</c> rule) or where the trailing brace can't be located.
    /// </summary>
    public static string ExpandBalancedMacro(string source, string commandName, Func<string, string> onMatch)
    {
        var sb = new StringBuilder(source.Length);
        var marker = "\\" + commandName;
        int i = 0;
        while (i < source.Length)
        {
            if (i + marker.Length <= source.Length
                && string.CompareOrdinal(source, i, marker, 0, marker.Length) == 0
                && (i + marker.Length == source.Length
                    || !IsAsciiLetter(source[i + marker.Length])))
            {
                int afterCmd = i + marker.Length;
                int j = afterCmd;
                while (j < source.Length && (source[j] == ' ' || source[j] == '\t' || source[j] == '\r' || source[j] == '\n'))
                    j++;
                if (j < source.Length && source[j] == '{')
                {
                    int braceEnd = FindMatchingBrace(source, j);
                    if (braceEnd > j)
                    {
                        var inner = source.Substring(j + 1, braceEnd - j - 1);
                        sb.Append(onMatch(inner));
                        i = braceEnd + 1;
                        continue;
                    }
                }
            }
            sb.Append(source[i]);
            i++;
        }
        return sb.ToString();
    }


    private static bool IsAsciiLetter(char c) =>
        (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

    /// <summary>
    /// Pre-substitutes LaTeX aliases / size hints that the math grammar doesn't
    /// natively understand, into forms it does. Conservative whitelist — only
    /// substitutions where the rendered text is identical to the canonical form.
    /// </summary>
    public static string NormalizeLatexAliases(string source)
    {
        // \lbrace and \rbrace are the braces as glyphs, the same as \{ and \}.
        // They are command names, so each aliases only where its name ends
        // (\lbracex is another command). They go first, so \left\lbrace
        // becomes \left\{ and then \{ like any other.
        source = ReplaceMacro(source, "lbrace", @"\{");
        source = ReplaceMacro(source, "rbrace", @"\}");

        // LaTeX lets whitespace separate \left and \right from their
        // delimiter (\left ( x \right )). Close that gap before each delimiter
        // the table below handles on that side, so the table matches the pair
        // spaced or not.
        source = SpacedSizedDelimiterRegex.Replace(source, m => m.Value.TrimEnd());

        // Order matters: longer prefixes first so e.g. \Biggl isn't eaten by
        // an earlier \bigl pass (they don't share a 5-char prefix today but
        // keeping the ordering explicit for future additions).
        foreach (var (from, to) in LatexAliases)
            source = source.Replace(from, to);
        return source;
    }

    /// <summary>
    /// Replaces each <c>\<paramref name="commandName"/></c> in
    /// <paramref name="source"/> with <paramref name="replacement"/>, where the
    /// name is whole (not followed by another ASCII letter, as
    /// <see cref="ContainsMacro"/> reads it) and its backslash is not the
    /// second half of a <c>\\</c> line break.
    /// </summary>
    private static string ReplaceMacro(string source, string commandName, string replacement)
    {
        var marker = "\\" + commandName;
        if (source.IndexOf(marker, StringComparison.Ordinal) < 0) return source;

        var sb = new StringBuilder(source.Length);
        var i = 0;
        while (i < source.Length)
        {
            if (source[i] == '\\' && i + 1 < source.Length && source[i + 1] == '\\')
            {
                sb.Append(source, i, 2);
                i += 2;
                continue;
            }
            if (string.CompareOrdinal(source, i, marker, 0, marker.Length) == 0
                && (i + marker.Length == source.Length || !IsAsciiLetter(source[i + marker.Length])))
            {
                sb.Append(replacement);
                i += marker.Length;
                continue;
            }
            sb.Append(source[i]);
            i++;
        }
        return sb.ToString();
    }


    /// <summary><c>\left</c> and the whitespace after it when a delimiter
    /// <see cref="LatexAliases"/> pairs with <c>\left</c> follows (<c>[</c>
    /// <c>(</c> <c>\{</c> <c>|</c> <c>.</c>), and the same for <c>\right</c>
    /// (<c>]</c> <c>)</c> <c>\}</c> <c>|</c> <c>.</c>).</summary>
    private static readonly Regex SpacedSizedDelimiterRegex = new(
        @"\\left\s+(?=[\[(|.]|\\\{)|\\right\s+(?=[\])|.]|\\\})",
        RegexOptions.Compiled);

    private static readonly (string From, string To)[] LatexAliases =
    [
        // Fraction-style aliases.
        (@"\dfrac",   @"\frac"),
        (@"\tfrac",   @"\frac"),
        // Auto-sizing delimiter pairs collapse to the bare delimiter. A
        // brace's bare delimiter is the escaped one: a bare { opens a group,
        // and the braces would vanish.
        (@"\left[",   "["),
        (@"\right]",  "]"),
        (@"\left(",   "("),
        (@"\right)",  ")"),
        (@"\left\{",  @"\{"),
        (@"\right\}", @"\}"),
        (@"\left|",   "|"),
        (@"\right|",  "|"),
        (@"\left.",   ""),   // null delimiter
        (@"\right.",  ""),
        // Manual size prefixes — strip, keep the delimiter that follows.
        (@"\biggl",   ""),
        (@"\biggr",   ""),
        (@"\Biggl",   ""),
        (@"\Biggr",   ""),
        (@"\bigl",    ""),
        (@"\bigr",    ""),
        (@"\Bigl",    ""),
        (@"\Bigr",    ""),
    ];

    /// <summary>
    /// Renders a single LaTeX backslash-non-letter macro to its plain-Unicode
    /// equivalent. <c>\,</c> <c>\;</c> <c>\:</c> and <c>\ </c> (explicit space)
    /// render as a regular space; <c>\!</c> (negative thin space) renders as
    /// empty; <c>\\</c> is a line break in math mode (rendered as a space here
    /// for inline contexts); the typesetter-escapes <c>\&amp;</c> <c>\$</c>
    /// <c>\#</c> <c>\%</c> <c>\_</c> <c>\{</c> <c>\}</c> render as the bare
    /// character. Unknown <c>\?</c> falls through as the literal two-char
    /// sequence so it's visible in output for debugging.
    /// </summary>
    private static string RenderBackslashEscape(char next) => next switch
    {
        ',' or ';' or ':' or ' ' => " ",
        '!'                       => string.Empty,
        '\\'                      => " ",
        '&' or '$' or '#' or '%' or '_' or '{' or '}' => next.ToString(),
        _                         => "\\" + next,
    };

    /// <summary>
    /// Scans a captured <c>\text{...}</c> body and resolves any
    /// backslash-non-letter macros to their Unicode equivalent in-place.
    /// Differs from the outer-source pass in <see cref="ExpandLatexMacros"/>:
    /// no placeholder substitution is needed because the body never reaches
    /// the lexer — it's stashed in the replacement map and spliced back in
    /// after the rest of the formula has rendered.
    /// </summary>
    public static string ResolveBackslashEscapes(string body) => ResolveBackslashEscapes(body, keepLexed: false);

    /// <summary><see cref="ResolveBackslashEscapes(string)"/>, leaving the
    /// escapes the box path's grammar lexes as written when
    /// <paramref name="keepLexed"/> is set: <c>\{</c> and <c>\}</c>, a set's
    /// braces, and the math spaces <c>\,</c> <c>\:</c> <c>\;</c> <c>\!</c>. A
    /// <c>\\</c> is consumed whole, so in <c>\\{</c> the brace still opens a
    /// group.</summary>
    private static string ResolveBackslashEscapes(string body, bool keepLexed)
    {
        if (body.IndexOf('\\') < 0) return body;
        var sb = new StringBuilder(body.Length);
        int i = 0;
        while (i < body.Length)
        {
            if (body[i] == '\\' && i + 1 < body.Length && !IsAsciiLetter(body[i + 1]))
            {
                if (keepLexed && body[i + 1] is '{' or '}' or ',' or ':' or ';' or '!')
                    sb.Append(body, i, 2);
                else
                    sb.Append(RenderBackslashEscape(body[i + 1]));
                i += 2;
            }
            else
            {
                sb.Append(body[i]);
                i++;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Walks <paramref name="source"/> looking for <c>\begin{NAME}[args]...\end{NAME}</c>
    /// environments (matched names, balanced braces ignored — non-greedy body),
    /// replacing each with <paramref name="onMatch"/> applied to the body. The
    /// outer regex eats any number of positional argument groups (the <c>{ll}</c>
    /// column spec in <c>\begin{array}{ll}</c>, the optional <c>[t]</c>, etc.).
    /// </summary>
    private static string ExpandBalancedEnvironment(string source, Func<string, string> onMatch)
    {
        if (source.IndexOf(@"\begin{", StringComparison.Ordinal) < 0) return source;
        return EnvironmentRegex.Replace(source, m => onMatch(m.Groups[2].Value));
    }


    private static readonly Regex EnvironmentRegex = new(
        @"\\begin\{([a-zA-Z*]+)\}(?:\s*\{[^{}]*\}|\s*\[[^\]]*\])*([\s\S]*?)\\end\{\1\}",
        RegexOptions.Compiled);

    /// <summary>
    /// Plain-text rendering of an environment body: row break (<c>\\</c>) to
    /// newline, column separator (<c>&amp;</c>) to a two-space gutter, any
    /// <c>\text{X}</c> bodies to X exactly as the math pass renders them
    /// (<see cref="RenderTextBody"/>), then a final pass over remaining
    /// backslash-non-letter macros. Each row is then trimmed of trailing
    /// whitespace and the whole block trimmed at the edges so the
    /// surrounding <c>\boxed{}</c> sees clean content.
    /// </summary>
    internal static string RenderEnvironmentBody(string body)
    {
        body = body.Replace(@"\\", "\n");
        body = ExpandBalancedMacro(body, "text", RenderTextBody);
        body = body.Replace("&", "  ");
        body = ResolveBackslashEscapes(body);
        var rows = body.Split('\n');
        for (int r = 0; r < rows.Length; r++) rows[r] = rows[r].TrimEnd();
        return string.Join("\n", rows).Trim('\n', ' ', '\t');
    }

    /// <summary>
    /// Returns true if <paramref name="source"/> contains a <c>\<paramref name="commandName"/></c>
    /// token, i.e. the literal name preceded by a backslash and not followed by
    /// another ASCII letter (so <c>\text</c> matches but <c>\textbf</c> doesn't).
    /// </summary>
    public static bool ContainsMacro(string source, string commandName)
    {
        var marker = "\\" + commandName;
        int idx = 0;
        while ((idx = source.IndexOf(marker, idx, StringComparison.Ordinal)) >= 0)
        {
            int after = idx + marker.Length;
            if (after == source.Length || !IsAsciiLetter(source[after]))
                return true;
            idx = after;
        }
        return false;
    }


    /// <summary>
    /// The index of the <c>}</c> that closes the <c>{</c> at
    /// <paramref name="openPos"/>, or -1. An escaped brace (<c>\{</c>,
    /// <c>\}</c>) is a glyph, not a group brace, so it is skipped; so is
    /// whatever follows any other backslash, which keeps <c>\\</c> one unit and
    /// lets the brace after it close.
    /// </summary>
    private static int FindMatchingBrace(string s, int openPos)
    {
        int depth = 1;
        for (int i = openPos + 1; i < s.Length; i++)
        {
            if (s[i] == '\\') i++;
            else if (s[i] == '{') depth++;
            else if (s[i] == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Pick a font with good math/Greek coverage. STIX Two Math is the
    /// gold-standard for math typography (full math glyphs + Greek,
    /// OpenType MATH tables); Cambria, Consolas, DejaVu Sans Mono, and the
    /// platform's resolved system monospace are fallbacks when nothing
    /// better is installed. Returns null if even the platform fallback
    /// fails — pixel math then falls through to the Unicode renderer.
    /// Result is cached per process; the lookup runs once on first math
    /// block.
    /// </summary>
    public static string? ResolveMathFont()
    {
        if (s_mathFontResolved) return s_mathFontPath;

        string[] candidates;
        if (OperatingSystem.IsWindows())
            candidates =
            [
                @"C:\Windows\Fonts\STIXTwoMath-Regular.otf",
                @"C:\Windows\Fonts\cambria.ttc",
                @"C:\Windows\Fonts\consola.ttf",
                @"C:\Windows\Fonts\cour.ttf",
            ];
        else if (OperatingSystem.IsMacOS())
            candidates =
            [
                "/Library/Fonts/STIXTwoMath-Regular.otf",
                "/System/Library/Fonts/Menlo.ttc",
                "/System/Library/Fonts/Monaco.dfont",
            ];
        else
            candidates =
            [
                "/usr/share/fonts/opentype/stix/STIXTwoMath-Regular.otf",
                "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf",
                "/usr/share/fonts/TTF/DejaVuSansMono.ttf",
            ];

        foreach (var p in candidates)
        {
            if (File.Exists(p)) { s_mathFontPath = p; s_mathFontResolved = true; return p; }
        }

        try { s_mathFontPath = FontResolver.ResolveSystemFont(); }
        catch { s_mathFontPath = null; }
        s_mathFontResolved = true;
        return s_mathFontPath;
    }
}

/// <summary>
/// TeX's atom classes, as many of them as the space between two juxtaposed
/// pieces of a formula depends on (<see cref="MarkdownMacros.ThinSpaceBetween"/>).
/// Ord is every atom the math visitors do not single out: a letter, a digit, a
/// fraction, a radical, a braced group. Glue is not an atom but space, which
/// takes no further space round it.
/// </summary>
internal enum MathAtomClass { Ord, Op, Open, Close, Punct, Glue }
