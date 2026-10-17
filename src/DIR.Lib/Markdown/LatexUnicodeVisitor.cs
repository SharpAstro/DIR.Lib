using System.Collections.Generic;
using System.Text;
using LALR.CC.LexicalGrammar;
using SharpAstro.Fonts;
using static DIR.Lib.Latex;

namespace DIR.Lib.Markdown;

/// <summary>
/// A formula, or a piece of one, as <see cref="LatexUnicodeVisitor"/> renders
/// it: the Unicode <see cref="Text"/>, and the TeX atom class of its first and
/// last atom, which the visitor reads where it juxtaposes two pieces, so that
/// <c>\sin x</c> reads <c>sin x</c> and <c>\sin(x)</c> stays tight.
/// </summary>
public sealed record UnicodeMath(string Text)
{
    internal MathAtomClass First { get; init; }
    internal MathAtomClass Last { get; init; }

    /// <summary>The text without the spaces an operator name put in it, where
    /// it has any; a script whose Unicode form those spaces stop takes this
    /// instead.</summary>
    internal string? Tight { get; init; }

    public override string ToString() => Text;
}

/// <summary>
/// IVisitor&lt;UnicodeMath&gt; for the math-mode LaTeX grammar: walks each AST
/// node to a plain-Unicode string suitable for inline rendering inside a single
/// terminal row. Used by <see cref="MarkdownInlineVisitor"/> for inline
/// math nodes (single-dollar <c>$x^2$</c> / inline <c>\(...\)</c>
/// math); the box-rendered counterpart <see cref="BoxBuildingVisitor"/>
/// handles <c>MathBlock</c> nodes (double-dollar <c>$$...$$</c> / display
/// <c>\[...\]</c> math) where multi-row pixel output is acceptable.
///
/// Strategy: Greek letters render as Greek letters, common digits use
/// Unicode super/subscript codepoints when available, fractions use the
/// fraction slash (U+2044). Anything that doesn't have a Unicode form falls
/// back to caret/underscore notation, so e.g. <c>x^{a+b}</c> reads as
/// <c>x^(a + b)</c> rather than mangling into broken super/subscript runs.
///
/// <para>Each visit returns a <see cref="UnicodeMath"/>, the text with the
/// atom class at each of its ends; <see cref="UnicodeMath.Text"/> is the
/// rendered formula.</para>
///
/// Lifted from the LALR.CC Examples.Latex renderer.
/// </summary>
public sealed class LatexUnicodeVisitor : IVisitor<UnicodeMath>
{
    public UnicodeMath Visit(Add node)      => Joined(node.Arg0, " + ", node.Arg2);
    public UnicodeMath Visit(Subtract node) => Joined(node.Arg0, " − ", node.Arg2);
    public UnicodeMath Visit(Eq node)       => Joined(node.Arg0, " = ", node.Arg2);
    public UnicodeMath Visit(Mul node)      => Joined(node.Arg0, "·", node.Arg2);
    public UnicodeMath Visit(Div node)      => Joined(node.Arg0, "/", node.Arg2);

    public UnicodeMath Visit(Neg node)
    {
        var inner = Of(node.Arg1);
        return new UnicodeMath($"−{inner.Text}") { Last = inner.Last, Tight = inner.Tight is { } t ? $"−{t}" : null };
    }

    /// <summary>
    /// Juxtaposition, with a space where TeX puts its thin space
    /// (<see cref="MarkdownMacros.ThinSpaceBetween"/>): <c>\sin x</c> is
    /// <c>sin x</c>, where it used to run together as <c>sinx</c>, and
    /// <c>\sin(x)</c> stays <c>sin(x)</c>. An empty side (<c>\null</c>) takes
    /// no space and leaves the other side's ends as they are.
    /// </summary>
    public UnicodeMath Visit(Juxt node)
    {
        var left = Of(node.Arg0);
        var right = Of(node.Arg1);
        if (left.Text.Length == 0) return right;
        if (right.Text.Length == 0) return left;
        var space = MarkdownMacros.ThinSpaceBetween(left.Last, right.First) ? " " : "";
        return new UnicodeMath($"{left.Text}{space}{right.Text}")
        {
            First = left.First,
            Last = right.Last,
            Tight = space.Length > 0 || left.Tight is not null || right.Tight is not null
                ? $"{left.Tight ?? left.Text}{right.Tight ?? right.Text}"
                : null,
        };
    }

    private static UnicodeMath Of(Item item) => (UnicodeMath)item.Content;

    private static string TextOf(Item item) => Of(item).Text;

    /// <summary>Left, <paramref name="op"/>, right: the ends are the
    /// operands' outer ones.</summary>
    private static UnicodeMath Joined(Item leftItem, string op, Item rightItem)
    {
        var left = Of(leftItem);
        var right = Of(rightItem);
        return new UnicodeMath($"{left.Text}{op}{right.Text}")
        {
            First = left.First,
            Last = right.Last,
            Tight = left.Tight is not null || right.Tight is not null
                ? $"{left.Tight ?? left.Text}{op}{right.Tight ?? right.Text}"
                : null,
        };
    }

    /// <summary>A piece with the same class at both ends.</summary>
    private static UnicodeMath Atom(string text, MathAtomClass atomClass)
        => new(text) { First = atomClass, Last = atomClass };

    // Binary relations (\approx \leq \geq \neq \equiv \ll \gg \in \notin
    // \subset \to \leftarrow \rightarrow \pm \mp). Arg1 is the rel token's
    // raw bytes; RenderCommand looks up the bare glyph and the format
    // string places the outer spaces structurally.
    public UnicodeMath Visit(Rel node)
        => Joined(node.Arg0, $" {RenderCommand((string)node.Arg1.Content)} ", node.Arg2);

    public UnicodeMath Visit(Sup node) => Scripted(node.Arg0, node.Arg2, Superscripts, '^');
    public UnicodeMath Visit(Subscript node) => Scripted(node.Arg0, node.Arg2, Subscripts, '_');

    /// <summary>
    /// A base and its script: the script in its Unicode form where every
    /// character has one, else after <paramref name="mark"/>. An operator
    /// name's space does not stop the Unicode form, so <c>x_{\sin x}</c> is
    /// <c>xₛᵢₙₓ</c>; where the script's letters themselves have none, the
    /// space stays. A script leaves its base's class alone, as in TeX:
    /// <c>\sin^2 x</c> is <c>sin² x</c>.
    /// </summary>
    private static UnicodeMath Scripted(Item baseItem, Item scriptItem, IReadOnlyDictionary<char, char> table, char mark)
    {
        var @base = Of(baseItem);
        var script = Of(scriptItem);
        var suffix = TryUnicodeScript(script.Text, table, out var mapped)
                     || (script.Tight is { } tight && TryUnicodeScript(tight, table, out mapped))
            ? mapped
            : $"{mark}{Wrap(script.Text)}";
        return @base with { Text = $"{@base.Text}{suffix}", Tight = @base.Tight is { } t ? $"{t}{suffix}" : null };
    }

    public UnicodeMath Visit(Number node)   => new((string)node.Arg0.Content);
    public UnicodeMath Visit(Variable node) => new((string)node.Arg0.Content);

    // An operator name is an Op; a spacing macro is space, which takes no
    // further space round it; everything else is an ordinary atom.
    public UnicodeMath Visit(Command node)
    {
        var raw = (string)node.Arg0.Content;
        var name = raw.TrimStart('\\');
        var atomClass = MarkdownMacros.OperatorNames.ContainsKey(name) ? MathAtomClass.Op
            : name is "quad" or "qquad" ? MathAtomClass.Glue
            : MathAtomClass.Ord;
        return Atom(RenderCommand(raw), atomClass);
    }

    // \operatorname{X} is X, spaced as an operator name. The token holds the
    // braced name; any placeholder in it is spliced back after the parse.
    public UnicodeMath Visit(OperatorName node) =>
        Atom(MarkdownMacros.BracedArgument((string)node.Arg0.Content).Trim(), MathAtomClass.Op);

    // \textrm{X} is X, blanks and all.
    public UnicodeMath Visit(TextRm node) => new(MarkdownMacros.BracedArgument((string)node.Arg0.Content));

    // \mathbb{X}: X's Latin letters and digits double-struck, ℝ for R, from
    // wherever Unicode put each (Letterlike Symbols for ℂ ℍ ℕ ℙ ℚ ℝ ℤ). The
    // letters of a \name in X (a placeholder still to be spliced back) stay.
    public UnicodeMath Visit(MathBb node)
    {
        var text = TextOf(node.Arg1);
        var sb = new StringBuilder(text.Length * 2);
        var inName = false;
        foreach (var c in text)
        {
            inName = c == '\\' || (inName && char.IsAsciiLetter(c));
            if (!inName && char.IsAsciiLetterOrDigit(c) && MathAlphanumerics.MapCodepoint(c, MathStyle.DoubleStruck) is { } mapped)
                sb.Append(char.ConvertFromUtf32((int)mapped));
            else
                sb.Append(c);
        }
        return new UnicodeMath(sb.ToString());
    }

    public UnicodeMath Visit(Paren node) => Delimited("(", node.Arg1, ")");

    // { E } is E, and to its neighbours one ordinary atom, as in TeX and on
    // the box path, whatever E's own ends are: {\sin} x is sinx, and
    // \sin{(x)} is sin (x).
    public UnicodeMath Visit(Group node)
    {
        var inner = Of(node.Arg1);
        return new UnicodeMath(inner.Text) { Tight = inner.Tight };
    }

    // \{E\}, a set, keeps its braces. MarkdownMacros.RenderMathUnicode lexes
    // a balanced pair, so it gets here; an unbalanced one ("\{x") does not
    // parse, and is rendered again with each brace a placeholder atom.
    public UnicodeMath Visit(Brace node) => Delimited("{", node.Arg1, "}");

    // (), [] or \{\} as written, an escaped brace without its backslash.
    public UnicodeMath Visit(EmptyPair node) => new UnicodeMath(
        ((string)node.Arg0.Content).TrimStart('\\') + ((string)node.Arg1.Content).TrimStart('\\'))
    {
        First = MathAtomClass.Open,
        Last = MathAtomClass.Close,
    };

    // \mathrm{X} is X: a terminal row has no italic to switch off, so the
    // argument renders exactly as it would bare.
    public UnicodeMath Visit(MathRm node) => Of(node.Arg1);

    // [E], [E) and (E]: Arg0 and Arg2 are the delimiters as written.
    public UnicodeMath Visit(Bracket node)
        => Delimited((string)node.Arg0.Content, node.Arg1, (string)node.Arg2.Content);

    private static UnicodeMath Delimited(string open, Item inner, string close)
        => new($"{open}{TextOf(inner)}{close}") { First = MathAtomClass.Open, Last = MathAtomClass.Close };

    // `,` as written. Before the grammar lexed it, a comma reached the parser
    // as a placeholder atom and came back verbatim, so this keeps every
    // formula with a comma rendering exactly as it did.
    public UnicodeMath Visit(Punct node) => Atom((string)node.Arg0.Content, MathAtomClass.Punct);

    public UnicodeMath Visit(Sqrt node) => new($"√{Wrap(TextOf(node.Arg1))}");

    /// <summary>
    /// <c>\sqrt[n]{x}</c>. Unicode has dedicated cube and fourth roots
    /// (∛ ∜); any other index that maps to superscripts is written before
    /// the radical (ⁿ√y, ¹⁰√x). An index with no superscript form keeps the
    /// bracketed LaTeX shape after the radical, √[a + b]x, which is how such
    /// a root read before the grammar knew the index.
    /// </summary>
    public UnicodeMath Visit(NthRoot node)
    {
        var index = TextOf(node.Arg2);
        var radicand = Wrap(TextOf(node.Arg4));
        return new UnicodeMath(index switch
        {
            "3" => $"∛{radicand}",
            "4" => $"∜{radicand}",
            _ when TryUnicodeScript(index, Superscripts, out var sup) => $"{sup}√{radicand}",
            _ => $"√[{index}]{radicand}",
        });
    }

    public UnicodeMath Visit(Frac node) => new($"{Wrap(TextOf(node.Arg1))}⁄{Wrap(TextOf(node.Arg2))}");

    private static string Wrap(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (s.Length == 1) return s;
        foreach (var c in s)
        {
            if (c == ' ' || c == '+' || c == '−' || c == '=' || c == '/' || c == '·' || c == '⁄')
                return $"({s})";
        }
        return s;
    }

    private static bool TryUnicodeScript(string s, IReadOnlyDictionary<char, char> table, out string? mapped)
    {
        if (string.IsNullOrEmpty(s) || s.Length > 4)
        {
            mapped = null;
            return false;
        }
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (!table.TryGetValue(c, out var sc))
            {
                mapped = null;
                return false;
            }
            sb.Append(sc);
        }
        mapped = sb.ToString();
        return true;
    }

    private static string RenderCommand(string raw)
    {
        if (raw.Length < 2 || raw[0] != '\\') return raw;
        var name = raw.Substring(1);
        return MarkdownMacros.OperatorNames.TryGetValue(name, out var text) ? text
            : Commands.TryGetValue(name, out var glyph) ? glyph : raw;
    }

    private static readonly Dictionary<string, string> Commands = new(System.StringComparer.Ordinal)
    {
        // Lowercase Greek
        ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ", ["delta"] = "δ",
        ["epsilon"] = "ε", ["zeta"] = "ζ", ["eta"] = "η", ["theta"] = "θ",
        ["iota"] = "ι", ["kappa"] = "κ", ["lambda"] = "λ", ["mu"] = "μ",
        ["nu"] = "ν", ["xi"] = "ξ", ["pi"] = "π", ["rho"] = "ρ",
        ["sigma"] = "σ", ["tau"] = "τ", ["upsilon"] = "υ", ["phi"] = "φ",
        ["chi"] = "χ", ["psi"] = "ψ", ["omega"] = "ω",
        // Uppercase Greek (ones that aren't Latin lookalikes)
        ["Gamma"] = "Γ", ["Delta"] = "Δ", ["Theta"] = "Θ", ["Lambda"] = "Λ",
        ["Xi"] = "Ξ", ["Pi"] = "Π", ["Sigma"] = "Σ", ["Phi"] = "Φ",
        ["Psi"] = "Ψ", ["Omega"] = "Ω",
        // Function names (\sin, \log, \lim, ...) are in
        // MarkdownMacros.OperatorNames, which both visitors share.
        // Big operators
        ["sum"] = "∑", ["prod"] = "∏", ["int"] = "∫", ["oint"] = "∮",
        ["bigcup"] = "⋃", ["bigcap"] = "⋂",
        // Standalone-symbol constants (no surrounding-space concern — these
        // read as nouns or unary modifiers, never as binary operators).
        ["infty"] = "∞", ["partial"] = "∂", ["nabla"] = "∇",
        ["cup"] = "∪", ["cap"] = "∩",
        // Binary relations / arrows / set operators. With LALR.CC 3.1+ these
        // tokenise as `rel` and Visit(Rel) places the outer spaces
        // structurally — so the dict here returns the bare glyph. (Pre-3.1
        // shipped these as " X " strings to work around the cmd-juxt path;
        // that hack is gone.) Visit(Command) still falls through to this
        // dict when a rel-categorised glyph appears in a non-binary position
        // (rare: e.g. `{\approx}` standalone), where bare glyph is correct.
        ["pm"] = "±", ["mp"] = "∓",
        ["to"] = "→", ["leftarrow"] = "←", ["rightarrow"] = "→",
        // Chemistry arrows. \rightleftharpoons (⇌) is the canonical
        // equilibrium symbol; \leftrightarrow (↔) covers both resonance
        // notation in chemistry and bidirectional implication elsewhere.
        ["rightleftharpoons"] = "⇌", ["leftrightarrow"] = "↔",
        ["leq"] = "≤", ["geq"] = "≥", ["neq"] = "≠",
        ["approx"] = "≈", ["equiv"] = "≡",
        ["in"] = "∈", ["notin"] = "∉", ["subset"] = "⊂",
        // Set-builder's bar: U+2223 DIVIDES, the relation, not a plain |.
        ["mid"] = "∣",
        // Arithmetic operators that the model often emits as commands inside
        // math (\cdot and \times are also lexer-aliased to '*' but the model
        // may end up here too).
        ["div"] = "÷", ["cdot"] = "·",
        // Sign atoms used by mhchem-emitted LaTeX. The math grammar treats
        // bare + / - as binary operators, so they can't appear standalone
        // inside a script group (e.g. `Cl^{-}` would fail to parse the `-`).
        // Mhchem.ToLatex rewrites the signs in superscript/subscript content
        // to \plus / \minus commands; these entries provide the rendered
        // glyphs. ASCII + and HYPHEN-MINUS so the Unicode Superscripts table
        // lookup that maps `+` → ⁺ / `-` → ⁻ continues to work.
        ["plus"] = "+", ["minus"] = "-",
        // Zero-width "null" atom — the chem-prefix counterpart on the
        // Unicode path. \ce{^{238}U} expands to `\null^{238}U` so the
        // sup attaches to a zero-width base in the math grammar; the
        // visitor's Sup rule emits `{base}{sup}` which is `` + `²³⁸` +
        // (juxt) `U` = `²³⁸U` — the same single-row layout Phase-1
        // produced.
        ["null"] = "",
        // Spacing macros — render as plain spaces so juxtaposed atoms don't
        // run together when the model used them as visual separators.
        ["quad"] = "  ", ["qquad"] = "    ",
        // Ellipsis variants — the catch-all \dots plus the orientation-
        // specific forms.
        ["dots"] = "…", ["ldots"] = "…", ["cdots"] = "⋯",
        ["vdots"] = "⋮", ["ddots"] = "⋱",
        // Much-less-than / much-greater-than. Tokenise as `rel` in
        // LALR.CC 3.1+, so this dict returns the bare glyph and the
        // surrounding spaces come from Visit(Rel)'s format string.
        ["ll"] = "≪", ["gg"] = "≫",
    };

    private static readonly Dictionary<char, char> Superscripts = new()
    {
        ['0'] = '⁰', ['1'] = '¹', ['2'] = '²', ['3'] = '³',
        ['4'] = '⁴', ['5'] = '⁵', ['6'] = '⁶', ['7'] = '⁷',
        ['8'] = '⁸', ['9'] = '⁹',
        ['+'] = '⁺', ['-'] = '⁻', ['='] = '⁼', ['('] = '⁽', [')'] = '⁾',
        ['n'] = 'ⁿ', ['i'] = 'ⁱ',
    };

    private static readonly Dictionary<char, char> Subscripts = new()
    {
        ['0'] = '₀', ['1'] = '₁', ['2'] = '₂', ['3'] = '₃',
        ['4'] = '₄', ['5'] = '₅', ['6'] = '₆', ['7'] = '₇',
        ['8'] = '₈', ['9'] = '₉',
        ['+'] = '₊', ['-'] = '₋', ['='] = '₌', ['('] = '₍', [')'] = '₎',
        // Lowercase letters with Unicode subscript forms (partial coverage).
        ['a'] = 'ₐ', ['e'] = 'ₑ', ['h'] = 'ₕ', ['i'] = 'ᵢ',
        ['j'] = 'ⱼ', ['k'] = 'ₖ', ['l'] = 'ₗ', ['m'] = 'ₘ',
        ['n'] = 'ₙ', ['o'] = 'ₒ', ['p'] = 'ₚ', ['r'] = 'ᵣ',
        ['s'] = 'ₛ', ['t'] = 'ₜ', ['u'] = 'ᵤ', ['v'] = 'ᵥ', ['x'] = 'ₓ',
    };
}
