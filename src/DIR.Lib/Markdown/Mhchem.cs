using System.Collections.Frozen;
using System.Text;

namespace DIR.Lib.Markdown;

/// <summary>
/// Renders an mhchem <c>\ce{...}</c> body to single-line Unicode text. Covers
/// the Phase-1 subset of mhchem syntax used by ordinary chemistry markup —
/// element symbols, auto-subscript digits, isotope-prefix scripts, ion
/// charges, reaction arrows, plus-separators, parenthesised state markers.
/// Bonds, labelled arrows, electron arrows, charge stacking, the
/// <c>$..$</c> escape hatch, and full math integration are deferred (see
/// docs/MHCHEM.md for the full limitations list).
///
/// Entry point: <see cref="Render"/>. Wired into the markdown pipeline by
/// <see cref="MarkdownRenderer.ExpandLatexMacros"/>, so both inline
/// (<c>\(\ce{H2O}\)</c>) and block (<c>$$\ce{H2O}$$</c>) math spans expand
/// it before the LaTeX grammar runs.
/// </summary>
public static class Mhchem
{
    /// <summary>
    /// Renders the body of a <c>\ce{...}</c> macro to a Unicode string.
    /// Unknown / unsupported tokens pass through verbatim — the goal is
    /// graceful degradation, not a hard error.
    /// </summary>
    public static string Render(string body)
    {
        if (string.IsNullOrEmpty(body)) return string.Empty;

        var sb = new StringBuilder(body.Length);
        int i = 0;
        // True at the very start, and after any token that begins a new
        // "term": +, whitespace, an arrow, a paren. While true, a digit
        // run is a plain coefficient (3H2 → 3H₂); while false, a digit
        // run is treated as a trailing subscript on whatever just emitted
        // (mostly element symbols, but also closing parens of state
        // markers — handled by clearing the flag on '(' and ')').
        bool atTermStart = true;
        // True immediately after emitting an element symbol. Resets on
        // anything that breaks the symbol→subscript adjacency (whitespace,
        // operators, other symbols). Distinct from atTermStart because a
        // symbol can be mid-term (e.g. the H in "2H2O" after the leading
        // coefficient 2).
        bool justSawSymbol = false;

        while (i < body.Length)
        {
            // Multi-char tokens first — longest match wins.
            if (TryArrow(body, ref i, sb))
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
                // Inside an opening paren a new term begins (a leading
                // digit there should be a coefficient, not a subscript on
                // the paren itself). After a closing paren the
                // parenthesised group acts like a single chemical unit —
                // a following digit run subscripts it ((OH)₂, (NH₄)₂SO₄).
                atTermStart = (ch == '(');
                justSawSymbol = (ch == ')');
                continue;
            }

            if (IsAsciiDigit(ch))
            {
                bool asSubscript = justSawSymbol;
                while (i < body.Length && IsAsciiDigit(body[i]))
                {
                    sb.Append(asSubscript ? Subscripts.ToSubscript(body[i]) : body[i]);
                    i++;
                }
                atTermStart = false;
                justSawSymbol = false;
                continue;
            }

            if (ch == '^')
            {
                i++;
                var content = ReadScriptContent(body, ref i);
                AppendScript(sb, content, super: true);
                atTermStart = false;
                justSawSymbol = false;
                continue;
            }

            if (ch == '_')
            {
                i++;
                var content = ReadScriptContent(body, ref i);
                AppendScript(sb, content, super: false);
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
                if (i + 1 < body.Length && IsAsciiLetterLower(body[i + 1]))
                {
                    var two = body.Substring(i, 2);
                    if (s_elements.Contains(two))
                    {
                        sb.Append(two);
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
    /// Maps each char in <paramref name="content"/> through the appropriate
    /// Subscripts table. If every char maps cleanly, emits the Unicode
    /// run; otherwise falls back to the literal LaTeX form so the original
    /// source survives unmangled.
    /// </summary>
    private static void AppendScript(StringBuilder sb, string content, bool super)
    {
        if (string.IsNullOrEmpty(content)) return;

        // Optimistic single-pass: try to map every char; if anything
        // doesn't map, abandon the buffer and emit verbatim instead.
        var buf = new StringBuilder(content.Length);
        foreach (var ch in content)
        {
            var mapped = super ? Subscripts.ToSuperscript(ch) : Subscripts.ToSubscript(ch);
            if (mapped == ch && !IsScriptPassThrough(ch))
            {
                // Unmappable. Preserve the original macro shape so the
                // caller can still see what the author wrote.
                sb.Append(super ? "^{" : "_{").Append(content).Append('}');
                return;
            }
            buf.Append(mapped);
        }
        sb.Append(buf);
    }

    /// <summary>
    /// Chars that intentionally have no super/sub form but should still be
    /// allowed inside a script run without aborting the Unicode emit —
    /// currently empty (any non-mapped char triggers fallback). Kept as a
    /// helper so the policy is easy to widen later (e.g. allow letters in
    /// charge labels like <c>^{2+ aq}</c>).
    /// </summary>
    private static bool IsScriptPassThrough(char _) => false;

    /// <summary>
    /// Recognises the four reaction-arrow forms (<c>-&gt;</c>, <c>&lt;-</c>,
    /// <c>&lt;=&gt;</c>, <c>&lt;-&gt;</c>) at <paramref name="i"/>. Longest
    /// match wins. On hit, emits the Unicode arrow, advances
    /// <paramref name="i"/> past the match, and returns true.
    /// </summary>
    private static bool TryArrow(string s, ref int i, StringBuilder sb)
    {
        int rem = s.Length - i;
        if (rem >= 3)
        {
            if (s[i] == '<' && s[i + 1] == '=' && s[i + 2] == '>') { sb.Append('⇌'); i += 3; return true; } // ⇌
            if (s[i] == '<' && s[i + 1] == '-' && s[i + 2] == '>') { sb.Append('↔'); i += 3; return true; } // ↔
        }
        if (rem >= 2)
        {
            if (s[i] == '-' && s[i + 1] == '>') { sb.Append('→'); i += 2; return true; } // →
            if (s[i] == '<' && s[i + 1] == '-') { sb.Append('←'); i += 2; return true; } // ←
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
