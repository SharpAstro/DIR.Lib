using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using LALR.CC.LexicalGrammar;
using SharpAstro.Fonts;
using static DIR.Lib.Latex;

namespace DIR.Lib.Markdown;

/// <summary>
/// IVisitor&lt;string&gt; for the math-mode LaTeX grammar: walks each AST node
/// to a plain-Unicode string suitable for inline rendering inside a single
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
/// Lifted from the LALR.CC Examples.Latex renderer.
/// </summary>
public sealed class LatexUnicodeVisitor : IVisitor<string>
{
    public string Visit(Add node)      => $"{node.Arg0.Content} + {node.Arg2.Content}";
    public string Visit(Subtract node) => $"{node.Arg0.Content} − {node.Arg2.Content}";
    public string Visit(Eq node)       => $"{node.Arg0.Content} = {node.Arg2.Content}";

    public string Visit(Mul node) => Binary((string)node.Arg0.Content, "·", (string)node.Arg2.Content);
    public string Visit(Div node) => Binary((string)node.Arg0.Content, "/", (string)node.Arg2.Content);

    public string Visit(Neg node)
    {
        var inner = (string)node.Arg1.Content;
        return WithOpEnds($"−{inner}", left: false, right: EndsWithOp(inner));
    }

    /// <summary>
    /// Juxtaposition, with a space where TeX puts its thin space round an
    /// operator name (<see cref="SpaceBetween"/>): <c>\sin x</c> is
    /// <c>sin x</c>, where it used to run together as <c>sinx</c>, and
    /// <c>\sin(x)</c> stays <c>sin(x)</c>.
    /// </summary>
    public string Visit(Juxt node)
    {
        var left = (string)node.Arg0.Content;
        var right = (string)node.Arg1.Content;
        return WithOpEnds(SpaceBetween(left, right) ? $"{left} {right}" : $"{left}{right}",
            left: StartsWithOp(left), right: EndsWithOp(right));
    }

    private static string Binary(string left, string op, string right)
        => WithOpEnds($"{left}{op}{right}", left: StartsWithOp(left), right: EndsWithOp(right));

    /// <summary>
    /// Whether TeX's thin space between two juxtaposed atoms shows here, as
    /// a plain space: after an operator name, before anything but an opening
    /// bracket or punctuation, and before an operator name, after anything
    /// but an opening bracket or punctuation. A string's class at an end it
    /// does not mark as an operator name is read off its character there.
    /// The big operators (∑ ∫) are left as they were: a glyph does not run
    /// into a letter the way a name does.
    /// </summary>
    private static bool SpaceBetween(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0) return false;
        if (EndsWithOp(left)) return StartsWithOp(right) || !IsOpenOrPunct(right[0]);
        return StartsWithOp(right) && !IsOpenOrPunct(left[^1]);
    }

    private static bool IsOpenOrPunct(char c) => c is '(' or '[' or '{' or ',' || char.IsWhiteSpace(c);

    /// <summary>
    /// Which ends of a rendered string are an operator name, for the strings
    /// that have one. A string carries no atom class of its own, so this
    /// table keeps it beside the very instance a visit returned, which is
    /// what the parser hands the next visit; it holds them weakly, and it is
    /// safe on the shared visitor across threads. Only an operator name's
    /// glyph and the composites built round one are entered.
    /// </summary>
    private static readonly ConditionalWeakTable<string, OpEnds> s_opEnds = new();

    private sealed record OpEnds(bool Left, bool Right);

    private static bool StartsWithOp(string s) => s_opEnds.TryGetValue(s, out var ends) && ends.Left;
    private static bool EndsWithOp(string s) => s_opEnds.TryGetValue(s, out var ends) && ends.Right;

    /// <summary>
    /// Enters <paramref name="s"/>'s ends. A string already entered keeps its
    /// own: concatenating an empty string returns the other operand itself,
    /// whose ends are its own and must not be overwritten.
    /// </summary>
    private static string WithOpEnds(string s, bool left, bool right)
    {
        if ((left || right) && s.Length > 0) s_opEnds.TryAdd(s, new OpEnds(left, right));
        return s;
    }

    // Binary relations (\approx \leq \geq \neq \equiv \ll \gg \in \notin
    // \subset \to \leftarrow \rightarrow \pm \mp). Arg1 is the rel token's
    // raw bytes; RenderCommand looks up the bare glyph and the format
    // string places the outer spaces structurally.
    public string Visit(Rel node) =>
        $"{node.Arg0.Content} {RenderCommand((string)node.Arg1.Content)} {node.Arg2.Content}";

    // A script leaves its base's class alone, as in TeX: \sin^2 x is sin² x.
    public string Visit(Sup node) => Scripted((string)node.Arg0.Content,
        TryUnicodeScript((string)node.Arg2.Content, Superscripts, out var sup)
            ? $"{node.Arg0.Content}{sup}"
            : $"{node.Arg0.Content}^{Wrap((string)node.Arg2.Content)}");

    public string Visit(Subscript node) => Scripted((string)node.Arg0.Content,
        TryUnicodeScript((string)node.Arg2.Content, Subscripts, out var sub)
            ? $"{node.Arg0.Content}{sub}"
            : $"{node.Arg0.Content}_{Wrap((string)node.Arg2.Content)}");

    private static string Scripted(string @base, string scripted)
        => WithOpEnds(scripted, left: StartsWithOp(@base), right: EndsWithOp(@base));

    public string Visit(Number node)   => (string)node.Arg0.Content;
    public string Visit(Variable node) => (string)node.Arg0.Content;

    public string Visit(Command node)
    {
        var raw = (string)node.Arg0.Content;
        var rendered = RenderCommand(raw);
        return OperatorNames.Contains(raw.TrimStart('\\')) ? WithOpEnds(rendered, left: true, right: true) : rendered;
    }

    // \operatorname{X} is X, spaced as an operator name. The token holds the
    // braced name; any placeholder in it is spliced back after the parse.
    public string Visit(OperatorName node) =>
        WithOpEnds(MarkdownMacros.BracedArgument((string)node.Arg0.Content).Trim(), left: true, right: true);

    // \textrm{X} is X, blanks and all.
    public string Visit(TextRm node) => MarkdownMacros.BracedArgument((string)node.Arg0.Content);

    // \mathbb{X}: X's Latin letters and digits double-struck, ℝ for R, from
    // wherever Unicode put each (Letterlike Symbols for ℂ ℍ ℕ ℙ ℚ ℝ ℤ). The
    // letters of a \name in X (a placeholder still to be spliced back) stay.
    public string Visit(MathBb node)
    {
        var text = (string)node.Arg1.Content;
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
        return sb.ToString();
    }

    public string Visit(Paren node) => $"({node.Arg1.Content})";
    public string Visit(Group node) => (string)node.Arg1.Content;

    // \{E\}, a set, keeps its braces. MarkdownMacros.RenderMathUnicode never
    // gets here: it turns each \{ and \} into a placeholder before the lexer
    // sees it, which is what keeps an unbalanced one ("\{x") rendering.
    public string Visit(Brace node) => $"{{{node.Arg1.Content}}}";

    // (), [] or \{\} as written, an escaped brace without its backslash.
    public string Visit(EmptyPair node) =>
        ((string)node.Arg0.Content).TrimStart('\\') + ((string)node.Arg1.Content).TrimStart('\\');

    // \mathrm{X} is X: a terminal row has no italic to switch off, so the
    // argument renders exactly as it would bare.
    public string Visit(MathRm node) => (string)node.Arg1.Content;

    // [E], [E) and (E]: Arg0 and Arg2 are the delimiters as written.
    public string Visit(Bracket node) => $"{node.Arg0.Content}{node.Arg1.Content}{node.Arg2.Content}";

    // `,` as written. Before the grammar lexed it, a comma reached the parser
    // as a placeholder atom and came back verbatim, so this keeps every
    // formula with a comma rendering exactly as it did.
    public string Visit(Punct node) => (string)node.Arg0.Content;

    public string Visit(Sqrt node) => $"√{Wrap((string)node.Arg1.Content)}";

    /// <summary>
    /// <c>\sqrt[n]{x}</c>. Unicode has dedicated cube and fourth roots
    /// (∛ ∜); any other index that maps to superscripts is written before
    /// the radical (ⁿ√y, ¹⁰√x). An index with no superscript form keeps the
    /// bracketed LaTeX shape after the radical, √[a + b]x, which is how such
    /// a root read before the grammar knew the index.
    /// </summary>
    public string Visit(NthRoot node)
    {
        var index = (string)node.Arg2.Content;
        var radicand = Wrap((string)node.Arg4.Content);
        return index switch
        {
            "3" => $"∛{radicand}",
            "4" => $"∜{radicand}",
            _ when TryUnicodeScript(index, Superscripts, out var sup) => $"{sup}√{radicand}",
            _ => $"√[{index}]{radicand}",
        };
    }

    public string Visit(Frac node) => $"{Wrap((string)node.Arg1.Content)}⁄{Wrap((string)node.Arg2.Content)}";

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
        return Commands.TryGetValue(name, out var glyph) ? glyph : raw;
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
        // Function names — kept as letters, render upright in fixed-width fonts.
        // LaTeX's whole predefined set, plus argmax and argmin.
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

    /// <summary>The commands in <see cref="Commands"/> that are operator
    /// names, spaced by <see cref="SpaceBetween"/>.</summary>
    private static readonly HashSet<string> OperatorNames = new(System.StringComparer.Ordinal)
    {
        "sin", "cos", "tan", "sec", "csc", "cot",
        "arcsin", "arccos", "arctan", "sinh", "cosh", "tanh", "coth",
        "log", "ln", "lg", "exp", "lim", "max", "min",
        "sup", "inf", "limsup", "liminf", "argmax", "argmin",
        "det", "dim", "gcd", "arg", "deg", "hom", "ker", "Pr",
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
