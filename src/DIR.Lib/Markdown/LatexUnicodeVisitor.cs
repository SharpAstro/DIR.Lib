using System.Collections.Generic;
using System.Text;
using LALR.CC.LexicalGrammar;
using static DIR.Lib.Latex;

namespace DIR.Lib.Markdown;

/// <summary>
/// IVisitor&lt;string&gt; for the math-mode LaTeX grammar: walks each AST node
/// to a plain-Unicode string suitable for inline rendering inside a single
/// terminal row. Used by <see cref="MarkdownRenderer"/> for Markdig's
/// <c>MathInline</c> nodes (single-dollar <c>$x^2$</c> / inline <c>\(...\)</c>
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
    public string Visit(Mul node)      => $"{node.Arg0.Content}·{node.Arg2.Content}";
    public string Visit(Div node)      => $"{node.Arg0.Content}/{node.Arg2.Content}";
    public string Visit(Juxt node)     => $"{node.Arg0.Content}{node.Arg1.Content}";
    public string Visit(Neg node)      => $"−{node.Arg1.Content}";

    // Binary relations (\approx \leq \geq \neq \equiv \ll \gg \in \notin
    // \subset \to \leftarrow \rightarrow \pm \mp). Arg1 is the rel token's
    // raw bytes; RenderCommand looks up the bare glyph and the format
    // string places the outer spaces structurally.
    public string Visit(Rel node) =>
        $"{node.Arg0.Content} {RenderCommand((string)node.Arg1.Content)} {node.Arg2.Content}";

    public string Visit(Sup node) =>
        TryUnicodeScript((string)node.Arg2.Content, Superscripts, out var sup)
            ? $"{node.Arg0.Content}{sup}"
            : $"{node.Arg0.Content}^{Wrap((string)node.Arg2.Content)}";

    public string Visit(Subscript node) =>
        TryUnicodeScript((string)node.Arg2.Content, Subscripts, out var sub)
            ? $"{node.Arg0.Content}{sub}"
            : $"{node.Arg0.Content}_{Wrap((string)node.Arg2.Content)}";

    public string Visit(Number node)   => (string)node.Arg0.Content;
    public string Visit(Variable node) => (string)node.Arg0.Content;
    public string Visit(Command node)  => RenderCommand((string)node.Arg0.Content);

    public string Visit(Paren node) => $"({node.Arg1.Content})";
    public string Visit(Group node) => (string)node.Arg1.Content;

    public string Visit(Sqrt node) => $"√{Wrap((string)node.Arg1.Content)}";
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
        ["sin"] = "sin", ["cos"] = "cos", ["tan"] = "tan",
        ["sec"] = "sec", ["csc"] = "csc", ["cot"] = "cot",
        ["arcsin"] = "arcsin", ["arccos"] = "arccos", ["arctan"] = "arctan",
        ["sinh"] = "sinh", ["cosh"] = "cosh", ["tanh"] = "tanh",
        ["log"] = "log", ["ln"] = "ln", ["exp"] = "exp",
        ["lim"] = "lim", ["max"] = "max", ["min"] = "min",
        ["det"] = "det", ["dim"] = "dim", ["gcd"] = "gcd",
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
