using System.Collections.Frozen;
using System.Text;

namespace DIR.Lib.Markdown;

/// <summary>
/// Translates an mhchem <c>\ce{...}</c> body to LaTeX math-mode source so it
/// flows through the same parser + visitor pipeline as ordinary math. That
/// gives `\ce{H2O}` proper sub-baseline subscripts under
/// <see cref="BoxBuildingVisitor"/> (Sixel / sextant / half-block rasters),
/// while the Unicode path (<see cref="LatexUnicodeVisitor"/>) still produces
/// the familiar `H₂O / Fe³⁺ / →` single-row output via the math grammar's
/// script-to-Unicode mapping.
///
/// <para>Replaces the Phase-1 state machine that pre-baked the body to
/// Unicode and stuffed it into a placeholder atom. All chem syntax now
/// emits LaTeX math source so the entire body flows through the grammar +
/// visitor pipeline and earns box layout under
/// <see cref="BoxBuildingVisitor"/>. Prefix isotope scripts use the
/// <c>\null</c> zero-width atom trick (<c>^{238}U</c> →
/// <c>\null^{238}U</c>) to satisfy the grammar's left-operand requirement
/// on <c>^</c>/<c>_</c>; the visitor's sub+sup merge collapses combined
/// scripts (<c>\null^{14}_{6}C</c>) into a single stacked
/// <see cref="DIR.Lib.MathLayout.SupSubBox"/> so they share a baseline pair
/// like a proper chemistry isotope.</para>
///
/// <para>Limitations carried over:
/// <list type="bullet">
///   <item>Element symbols render math-italic in box mode (the grammar maps
///   single letters to <c>id</c>; there's no <c>\mathrm</c> support in the
///   visitor yet). Box-mode chem looks "math-style" — readable but not
///   strictly chemistry-convention upright.</item>
/// </list></para>
///
/// <para>Wired into the markdown pipeline by
/// <see cref="MarkdownMacros.ExpandLatexMacros"/> and by Console.Lib's
/// <c>TryRenderMathBox</c>; both inline (<c>\(\ce{H2O}\)</c>) and block
/// (<c>$$\ce{H2O}$$</c>) math spans pick up the expansion.</para>
/// </summary>
public static class Mhchem
{
    /// <summary>
    /// Translates a <c>\ce{...}</c> body to LaTeX math source. Unknown /
    /// unsupported tokens fall through as plain text — the goal is graceful
    /// degradation, not a hard error.
    /// </summary>
    public static string ToLatex(string body)
    {
        if (string.IsNullOrEmpty(body)) return string.Empty;

        var sb = new StringBuilder(body.Length * 2);
        int i = 0;
        // True at the very start, and after any token that begins a new
        // "term": +, whitespace, an arrow, a paren. While true, a digit run
        // is a plain coefficient (3H2 → 3H_{2}); while false, a digit run
        // is a trailing subscript on whatever just emitted (element symbols,
        // or the closing paren of a state marker). Also gates `^`/`_`
        // emission between prefix-script (Unicode best-effort, since the
        // grammar can't do prefix scripts) and postfix-script (LaTeX, gets
        // proper box layout downstream).
        bool atTermStart = true;
        // True immediately after emitting an element symbol or closing
        // paren. Resets on whitespace, operators, opening paren, etc.
        // Distinct from atTermStart because a symbol can be mid-term (the
        // `H` in `2H2O` after the leading coefficient `2`).
        bool justSawSymbol = false;

        while (i < body.Length)
        {
            // Multi-char arrows first — longest match wins. `<=>` and `<->`
            // are 3-char; `->` and `<-` are 2-char.
            if (TryArrowLatex(body, ref i, sb))
            {
                atTermStart = true;
                justSawSymbol = false;
                continue;
            }

            char ch = body[i];

            if (ch == ' ' || ch == '\t')
            {
                sb.Append(' ');
                i++;
                atTermStart = true;
                justSawSymbol = false;
                continue;
            }

            if (ch == '+')
            {
                // Plus separator between reactants / products. The math
                // grammar's `E -> E + T` rule handles the surrounding
                // relation kerning for free.
                sb.Append('+');
                i++;
                atTermStart = true;
                justSawSymbol = false;
                continue;
            }

            if (ch == '(' || ch == ')')
            {
                sb.Append(ch);
                i++;
                // Inside an opening paren a new term begins (a leading digit
                // there is a coefficient, not a subscript on the paren).
                // After a closing paren the parenthesised group acts like a
                // single chemical unit — a following digit run subscripts it
                // ((OH)_{2}, (NH_{4})_{2}SO_{4}).
                atTermStart = (ch == '(');
                justSawSymbol = (ch == ')');
                continue;
            }

            if (IsAsciiDigit(ch))
            {
                int start = i;
                while (i < body.Length && IsAsciiDigit(body[i])) i++;
                var digits = body.AsSpan(start, i - start);
                if (justSawSymbol)
                    sb.Append("_{").Append(digits).Append('}');
                else
                    sb.Append(digits);
                atTermStart = false;
                justSawSymbol = false;
                continue;
            }

            if (ch == '^')
            {
                i++;
                var content = ReadScriptContent(body, ref i);
                if (atTermStart)
                {
                    // Isotope-prefix superscript. The math grammar requires
                    // a left operand for `^` (P → P op A), so prepend the
                    // \null zero-width atom — Commands map renders it as
                    // the empty string, GlyphBox("") has Width=Height=0,
                    // and the visitor's sub+sup merge collapses any
                    // following `_{M}` postfix into the same SupSubBox so
                    // ^{14}_{6}C lands as stacked scripts to the LEFT of
                    // the element symbol (chemistry convention).
                    sb.Append(@"\null");
                }
                AppendLatexScript(sb, content, super: true);
                atTermStart = false;
                justSawSymbol = false;
                continue;
            }

            if (ch == '_')
            {
                i++;
                var content = ReadScriptContent(body, ref i);
                // Same \null trick for a prefix subscript on its own. In
                // practice prefix _{M} alone is rare (isotopes pair it
                // with a leading ^{N}, which sets atTermStart=false), but
                // emitting \null_{M} keeps the source grammar-valid.
                if (atTermStart) sb.Append(@"\null");
                AppendLatexScript(sb, content, super: false);
                atTermStart = false;
                justSawSymbol = false;
                continue;
            }

            if (IsAsciiLetterUpper(ch))
            {
                // Greedy 2-letter symbol if the second char is lowercase
                // AND the pair is a known element; otherwise fall back to
                // the 1-letter form (still gated on element-set membership
                // so stray uppercase letters in arbitrary prose don't get
                // mistakenly tagged).
                //
                // 2-letter elements wrap in `{...}` so a trailing script
                // attaches to the whole symbol via the grammar's group
                // rule (A → '{' E '}'), not just to the second letter via
                // its P → P op A reduction. `Fe_{2}` should subscript Fe,
                // not just `e`; `{Fe}_{2}` parses that way.
                if (i + 1 < body.Length && IsAsciiLetterLower(body[i + 1]))
                {
                    var two = body.Substring(i, 2);
                    if (s_elements.Contains(two))
                    {
                        sb.Append('{').Append(two).Append('}');
                        i += 2;
                        atTermStart = false;
                        justSawSymbol = true;
                        continue;
                    }
                }
                var one = ch.ToString();
                if (s_elements.Contains(one))
                {
                    sb.Append(ch);
                    i++;
                    atTermStart = false;
                    justSawSymbol = true;
                    continue;
                }
                // Unknown uppercase — passthrough, no symbol state.
                sb.Append(ch);
                i++;
                atTermStart = false;
                justSawSymbol = false;
                continue;
            }

            // Anything else: passthrough one char (lowercase letters not
            // adjacent to an uppercase, punctuation other than the cases
            // above, etc.).
            sb.Append(ch);
            i++;
            atTermStart = false;
            justSawSymbol = false;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Reads the body of a script (after <c>^</c> or <c>_</c>). If the next
    /// char is <c>{</c>, returns the brace-balanced content. Otherwise
    /// returns a "bare" run: optional digit prefix followed by an optional
    /// trailing <c>+</c> or <c>-</c> — covers <c>^3</c>, <c>^-</c>,
    /// <c>^3+</c>, <c>_2</c>. Falls back to a single char when neither
    /// pattern matches, so the caller doesn't have to worry about
    /// zero-width script content.
    /// </summary>
    private static string ReadScriptContent(string s, ref int i)
    {
        if (i >= s.Length) return string.Empty;
        if (s[i] == '{')
        {
            int start = i + 1;
            int depth = 1;
            int j = i + 1;
            while (j < s.Length && depth > 0)
            {
                if (s[j] == '{') depth++;
                else if (s[j] == '}') depth--;
                if (depth > 0) j++;
            }
            var content = s.Substring(start, j - start);
            i = j < s.Length ? j + 1 : j;
            return content;
        }

        int k = i;
        while (k < s.Length && IsAsciiDigit(s[k])) k++;
        if (k < s.Length && (s[k] == '+' || s[k] == '-')) k++;
        if (k == i)
        {
            // No digits, no sign — accept one char so e.g. ^x doesn't break.
            var single = s[i].ToString();
            i++;
            return single;
        }
        var bare = s.Substring(i, k - i);
        i = k;
        return bare;
    }

    /// <summary>
    /// Emits a postfix LaTeX script (<c>^{...}</c> or <c>_{...}</c>) with
    /// bare <c>+</c> / <c>-</c> rewritten to <c>\plus</c> / <c>\minus</c>
    /// commands. The math grammar treats <c>+</c> / <c>-</c> as binary
    /// operators, so a script body like <c>{3+}</c> won't parse (the trailing
    /// <c>+</c> has no right operand). Rewriting to <c>\plus</c> /
    /// <c>\minus</c> turns them into cmd atoms; the visitors translate the
    /// commands to the appropriate glyph (and the Unicode Superscripts table
    /// still maps the rendered glyph to U+207A / U+207B downstream).
    /// </summary>
    private static void AppendLatexScript(StringBuilder sb, string content, bool super)
    {
        if (string.IsNullOrEmpty(content)) return;
        sb.Append(super ? "^{" : "_{");
        foreach (var c in content)
        {
            if (c == '+') sb.Append(@"\plus");
            else if (c == '-') sb.Append(@"\minus");
            else sb.Append(c);
        }
        sb.Append('}');
    }

    /// <summary>
    /// Recognises the four reaction-arrow forms (<c>-&gt;</c>, <c>&lt;-</c>,
    /// <c>&lt;=&gt;</c>, <c>&lt;-&gt;</c>) at <paramref name="i"/>. Longest
    /// match wins. Emits the LaTeX command name with a trailing space so
    /// the lexer can break it cleanly from a following identifier
    /// (<c>\to B</c> tokenises as rel + id; <c>\toB</c> would tokenise as
    /// the cmd <c>\toB</c> via the <c>\\[a-zA-Z]+</c> longest-match rule).
    /// No leading space — the previous emit's lexer-skipped whitespace
    /// (source space, or the natural break between <c>\</c> and a letter)
    /// suffices.
    /// </summary>
    private static bool TryArrowLatex(string s, ref int i, StringBuilder sb)
    {
        int rem = s.Length - i;
        if (rem >= 3)
        {
            if (s[i] == '<' && s[i + 1] == '=' && s[i + 2] == '>') { sb.Append(@"\rightleftharpoons "); i += 3; return true; } // ⇌
            if (s[i] == '<' && s[i + 1] == '-' && s[i + 2] == '>') { sb.Append(@"\leftrightarrow "); i += 3; return true; } // ↔
        }
        if (rem >= 2)
        {
            if (s[i] == '-' && s[i + 1] == '>') { sb.Append(@"\to "); i += 2; return true; } // →
            if (s[i] == '<' && s[i + 1] == '-') { sb.Append(@"\leftarrow "); i += 2; return true; } // ←
        }
        return false;
    }

    private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';
    private static bool IsAsciiLetterUpper(char c) => c >= 'A' && c <= 'Z';
    private static bool IsAsciiLetterLower(char c) => c >= 'a' && c <= 'z';

    /// <summary>
    /// All 118 IUPAC element symbols. FrozenSet for AOT-friendly fast lookup;
    /// the symbol parser checks 2-char then 1-char membership so CO parses as
    /// C + O (carbon, then oxygen — "carbon monoxide"), not as Co (cobalt).
    /// </summary>
    private static readonly FrozenSet<string> s_elements = new[]
    {
        "H", "He",
        "Li", "Be", "B", "C", "N", "O", "F", "Ne",
        "Na", "Mg", "Al", "Si", "P", "S", "Cl", "Ar",
        "K", "Ca", "Sc", "Ti", "V", "Cr", "Mn", "Fe", "Co", "Ni", "Cu", "Zn",
        "Ga", "Ge", "As", "Se", "Br", "Kr",
        "Rb", "Sr", "Y", "Zr", "Nb", "Mo", "Tc", "Ru", "Rh", "Pd", "Ag", "Cd",
        "In", "Sn", "Sb", "Te", "I", "Xe",
        "Cs", "Ba",
        "La", "Ce", "Pr", "Nd", "Pm", "Sm", "Eu", "Gd", "Tb", "Dy", "Ho", "Er", "Tm", "Yb", "Lu",
        "Hf", "Ta", "W", "Re", "Os", "Ir", "Pt", "Au", "Hg",
        "Tl", "Pb", "Bi", "Po", "At", "Rn",
        "Fr", "Ra",
        "Ac", "Th", "Pa", "U", "Np", "Pu", "Am", "Cm", "Bk", "Cf", "Es", "Fm", "Md", "No", "Lr",
        "Rf", "Db", "Sg", "Bh", "Hs", "Mt", "Ds", "Rg", "Cn",
        "Nh", "Fl", "Mc", "Lv", "Ts", "Og",
    }.ToFrozenSet(StringComparer.Ordinal);
}
