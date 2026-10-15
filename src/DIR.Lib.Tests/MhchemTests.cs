using DIR.Lib.Markdown;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// Two layers of coverage for the Phase-2 <c>\ce{...}</c> rewrite:
///
/// <para><b>ToLatex_*</b> tests assert the exact LaTeX math source emitted
/// by <see cref="Mhchem.ToLatex"/> — element-symbol wrapping, auto-subscript
/// brace form, postfix <c>\plus</c> / <c>\minus</c> sign rewrites for ion
/// charges, prefix-isotope Unicode pre-bake, and trailing-space-padded
/// arrow commands. These pin the transform contract.</para>
///
/// <para><b>Roundtrip_*</b> tests cover the user-visible behaviour: chem
/// body → <c>\ce{...}</c> → <see cref="MarkdownMacros.RenderMathUnicode"/>
/// → final Unicode. They reuse the Phase-1 Unicode-string expectations so
/// that "<c>\ce{H2O}</c> still renders as H₂O" is a regression check
/// independent of how the intermediate LaTeX is shaped. End-to-end markdown
/// integration (the surrounding inline / display math span machinery) lives
/// in <c>Console.Lib.Tests.MhchemMarkdownIntegrationTests</c>.</para>
/// </summary>
public sealed class MhchemTests
{
    // ╔══════════════════════════════════════════════════════════════╗
    // ║ ToLatex — unit tests for the chem → LaTeX-source transform.  ║
    // ╚══════════════════════════════════════════════════════════════╝

    // ── Element symbols + auto-subscripts ──────────────────────────────

    // Every symbol is its own \mathrm{...}: upright in box mode, and one
    // grammar atom, so a trailing script binds the whole symbol.
    [Theory]
    [InlineData("H2O",       @"\mathrm{H}_{2}\mathrm{O}")]
    [InlineData("NaCl",      @"\mathrm{Na}\mathrm{Cl}")]
    [InlineData("CaCO3",     @"\mathrm{Ca}\mathrm{C}\mathrm{O}_{3}")]
    [InlineData("H2SO4",     @"\mathrm{H}_{2}\mathrm{S}\mathrm{O}_{4}")]
    [InlineData("C6H12O6",   @"\mathrm{C}_{6}\mathrm{H}_{12}\mathrm{O}_{6}")]
    [InlineData("CO2",       @"\mathrm{C}\mathrm{O}_{2}")]                 // C+O, NOT Co (cobalt)
    [InlineData("Co2O3",     @"\mathrm{Co}_{2}\mathrm{O}_{3}")]            // Co matches before C+o
    [InlineData("Fe2O3",     @"\mathrm{Fe}_{2}\mathrm{O}_{3}")]
    public void ToLatex_FormulasWithAutoSubscripts(string body, string expected)
        => Mhchem.ToLatex(body).ShouldBe(expected);

    // ── Coefficients vs subscripts ─────────────────────────────────────

    [Theory]
    [InlineData("3H2",       @"3\mathrm{H}_{2}")]                          // leading 3 = coefficient (bare)
    [InlineData("2H2O",      @"2\mathrm{H}_{2}\mathrm{O}")]
    [InlineData("10NaCl",    @"10\mathrm{Na}\mathrm{Cl}")]
    [InlineData("2H2 + O2",  @"2\mathrm{H}_{2} + \mathrm{O}_{2}")]
    public void ToLatex_LeadingDigitsAreCoefficients(string body, string expected)
        => Mhchem.ToLatex(body).ShouldBe(expected);

    // ── Isotope prefix scripts (\null zero-width base) ────────────────
    //
    // Prefix scripts emit a leading \null so the script attaches to a
    // zero-width atom — the math grammar's P → P op A needs a left
    // operand. The atomic-number `_{M}` after a prefix `^{N}` is parsed
    // as postfix (atTermStart resets after the first script emits), so
    // the chain is `\null^{N}_{M}` which the visitor's sub+sup merge
    // collapses into a single stacked SupSubBox.

    [Theory]
    [InlineData("^{238}U",     @"\null^{238}\mathrm{U}")]
    [InlineData("^{14}C",      @"\null^{14}\mathrm{C}")]
    [InlineData("^{14}_{6}C",  @"\null^{14}_{6}\mathrm{C}")]
    [InlineData("^{226}Ra",    @"\null^{226}\mathrm{Ra}")]
    [InlineData("^{4}_{2}He",  @"\null^{4}_{2}\mathrm{He}")]        // alpha particle
    public void ToLatex_IsotopePrefixScripts(string body, string expected)
        => Mhchem.ToLatex(body).ShouldBe(expected);

    // ── Ion charges (postfix LaTeX scripts with \plus / \minus) ────────

    [Theory]
    [InlineData("Fe^3+",       @"\mathrm{Fe}^{3\plus}")]
    [InlineData("OH^-",        @"\mathrm{O}\mathrm{H}^{\minus}")]
    [InlineData("Cu^{2+}",     @"\mathrm{Cu}^{2\plus}")]
    [InlineData("SO4^{2-}",    @"\mathrm{S}\mathrm{O}_{4}^{2\minus}")]
    [InlineData("Na^+",        @"\mathrm{Na}^{\plus}")]
    [InlineData("Cl^-",        @"\mathrm{Cl}^{\minus}")]
    [InlineData("NH4^+",       @"\mathrm{N}\mathrm{H}_{4}^{\plus}")]
    [InlineData("e^-",         @"\mathrm{e}^{\minus}")]                    // the electron: a lowercase run
    [InlineData("M^{n+}",      @"\mathrm{M}^{\mathrm{n}\plus}")]          // a letter in a script is upright too
    public void ToLatex_IonCharges(string body, string expected)
        => Mhchem.ToLatex(body).ShouldBe(expected);

    // ── Reaction arrows ────────────────────────────────────────────────

    [Theory]
    [InlineData("A -> B",      @"\mathrm{A} \to  \mathrm{B}")]               // space, \to, trailing space + source space = double
    [InlineData("A <- B",      @"\mathrm{A} \leftarrow  \mathrm{B}")]
    [InlineData("A <=> B",     @"\mathrm{A} \rightleftharpoons  \mathrm{B}")]
    [InlineData("A <-> B",     @"\mathrm{A} \leftrightarrow  \mathrm{B}")]
    public void ToLatex_ReactionArrows(string body, string expected)
        => Mhchem.ToLatex(body).ShouldBe(expected);

    // ── State markers (verbatim parens, upright contents) ──────────────

    [Theory]
    [InlineData("H2O(l)",      @"\mathrm{H}_{2}\mathrm{O}(\mathrm{l})")]
    [InlineData("H2O(g)",      @"\mathrm{H}_{2}\mathrm{O}(\mathrm{g})")]
    [InlineData("NaCl(s)",     @"\mathrm{Na}\mathrm{Cl}(\mathrm{s})")]
    [InlineData("HCl(aq)",     @"\mathrm{H}\mathrm{Cl}(\mathrm{aq})")]
    [InlineData("Ca(OH)2",     @"\mathrm{Ca}(\mathrm{O}\mathrm{H})_{2}")]  // trailing digit subscripts the paren expression via P → P _ A
    public void ToLatex_StateMarkersAndParens(string body, string expected)
        => Mhchem.ToLatex(body).ShouldBe(expected);

    // ── End-to-end reactions ───────────────────────────────────────────

    [Theory]
    [InlineData("2H2 + O2 -> 2H2O",
                @"2\mathrm{H}_{2} + \mathrm{O}_{2} \to  2\mathrm{H}_{2}\mathrm{O}")]
    [InlineData("N2 + 3H2 <=> 2NH3",
                @"\mathrm{N}_{2} + 3\mathrm{H}_{2} \rightleftharpoons  2\mathrm{N}\mathrm{H}_{3}")]
    [InlineData("CaCO3 -> CaO + CO2",
                @"\mathrm{Ca}\mathrm{C}\mathrm{O}_{3} \to  \mathrm{Ca}\mathrm{O} + \mathrm{C}\mathrm{O}_{2}")]
    [InlineData("HCl + NaOH -> NaCl + H2O",
                @"\mathrm{H}\mathrm{Cl} + \mathrm{Na}\mathrm{O}\mathrm{H} \to  \mathrm{Na}\mathrm{Cl} + \mathrm{H}_{2}\mathrm{O}")]
    [InlineData("^{238}U -> ^{234}Th + ^{4}_{2}He",
                @"\null^{238}\mathrm{U} \to  \null^{234}\mathrm{Th} + \null^{4}_{2}\mathrm{He}")]
    public void ToLatex_FullReactions(string body, string expected)
        => Mhchem.ToLatex(body).ShouldBe(expected);

    // ── LaTeX commands in the body ─────────────────────────────────────
    //
    // A command goes through as written, with the brace groups straight
    // after its name. Its letters are not chem letters: wrapping them in
    // \mathrm would break the command, and before this \Gamma was cut at
    // the element Ga.

    [Theory]
    [InlineData(@"\alpha",            @"\alpha")]
    [InlineData(@"\Gamma",            @"\Gamma")]
    [InlineData(@"\gamma + e^-",      @"\gamma + \mathrm{e}^{\minus}")]
    [InlineData(@"H2O(\text{aq})",    @"\mathrm{H}_{2}\mathrm{O}(\text{aq})")]
    [InlineData(@"OH^{\bullet}",      @"\mathrm{O}\mathrm{H}^{\bullet}")]
    public void ToLatex_CommandsPassThrough(string body, string expected)
        => Mhchem.ToLatex(body).ShouldBe(expected);

    // ── Graceful degradation ───────────────────────────────────────────

    [Fact]
    public void ToLatex_EmptyBody_ReturnsEmpty()
        => Mhchem.ToLatex("").ShouldBe("");

    [Fact]
    public void ToLatex_UnknownContentIsUprightText()
    {
        // Lowercase-only "abc" has no known symbols: one upright run.
        Mhchem.ToLatex("abc").ShouldBe(@"\mathrm{abc}");
    }

    [Fact]
    public void ToLatex_UnmappableScriptKeepsLatexForm()
    {
        // ^{abc} in postfix position emits as LaTeX `^{...}` with its
        // letters upright; they aren't signs, so no \plus / \minus, and the
        // script body parses through the math grammar as one \mathrm atom.
        Mhchem.ToLatex("X^{abc}").ShouldBe(@"\mathrm{X}^{\mathrm{abc}}");
    }

    [Fact]
    public void ToLatex_PrefixScriptWithUnmappableContent()
    {
        // ^{abc} at term start: still emits \null^{...} LaTeX. The math
        // grammar parses it, the Unicode visitor's TryUnicodeScript fails
        // on "abc" and falls back to caret notation, so the chem author
        // sees their source preserved rather than silently mangled.
        Mhchem.ToLatex("^{abc}Y").ShouldBe(@"\null^{\mathrm{abc}}\mathrm{Y}");
    }

    // ╔══════════════════════════════════════════════════════════════╗
    // ║ Roundtrip — chem → \ce{X} → RenderMathUnicode → Unicode.     ║
    // ║ Reuses Phase-1 Unicode expectations as the regression spec   ║
    // ║ for the user-visible behaviour of the new LaTeX pipeline.    ║
    // ╚══════════════════════════════════════════════════════════════╝

    [Theory]
    [InlineData("H2O",       "H₂O")]
    [InlineData("NaCl",      "NaCl")]
    [InlineData("CaCO3",     "CaCO₃")]
    [InlineData("H2SO4",     "H₂SO₄")]
    [InlineData("C6H12O6",   "C₆H₁₂O₆")]
    [InlineData("CO2",       "CO₂")]
    [InlineData("Co2O3",     "Co₂O₃")]
    [InlineData("Fe2O3",     "Fe₂O₃")]
    public void Roundtrip_FormulasWithAutoSubscripts(string body, string expected)
        => MarkdownMacros.RenderMathUnicode(@"\ce{" + body + "}").ShouldBe(expected);

    [Theory]
    [InlineData("3H2",       "3H₂")]
    [InlineData("2H2O",      "2H₂O")]
    [InlineData("10NaCl",    "10NaCl")]
    [InlineData("2H2 + O2",  "2H₂ + O₂")]
    public void Roundtrip_LeadingDigitsAreCoefficients(string body, string expected)
        => MarkdownMacros.RenderMathUnicode(@"\ce{" + body + "}").ShouldBe(expected);

    [Theory]
    [InlineData("^{238}U",     "²³⁸U")]
    [InlineData("^{14}C",      "¹⁴C")]
    [InlineData("^{14}_{6}C",  "¹⁴₆C")]
    [InlineData("^{226}Ra",    "²²⁶Ra")]
    [InlineData("^{4}_{2}He",  "⁴₂He")]
    public void Roundtrip_IsotopePrefixScripts(string body, string expected)
        => MarkdownMacros.RenderMathUnicode(@"\ce{" + body + "}").ShouldBe(expected);

    [Theory]
    [InlineData("Fe^3+",       "Fe³⁺")]
    [InlineData("OH^-",        "OH⁻")]
    [InlineData("Cu^{2+}",     "Cu²⁺")]
    [InlineData("SO4^{2-}",    "SO₄²⁻")]
    [InlineData("Na^+",        "Na⁺")]
    [InlineData("Cl^-",        "Cl⁻")]
    [InlineData("NH4^+",       "NH₄⁺")]
    [InlineData("e^-",         "e⁻")]
    [InlineData("M^{n+}",      "Mⁿ⁺")]
    public void Roundtrip_IonCharges(string body, string expected)
        => MarkdownMacros.RenderMathUnicode(@"\ce{" + body + "}").ShouldBe(expected);

    [Theory]
    [InlineData("A -> B",      "A → B")]
    [InlineData("A <- B",      "A ← B")]
    [InlineData("A <=> B",     "A ⇌ B")]
    [InlineData("A <-> B",     "A ↔ B")]
    public void Roundtrip_ReactionArrows(string body, string expected)
        => MarkdownMacros.RenderMathUnicode(@"\ce{" + body + "}").ShouldBe(expected);

    [Theory]
    [InlineData("H2O(l)",      "H₂O(l)")]
    [InlineData("H2O(g)",      "H₂O(g)")]
    [InlineData("NaCl(s)",     "NaCl(s)")]
    [InlineData("HCl(aq)",     "HCl(aq)")]
    [InlineData("Ca(OH)2",     "Ca(OH)₂")]
    public void Roundtrip_StateMarkersAndParens(string body, string expected)
        => MarkdownMacros.RenderMathUnicode(@"\ce{" + body + "}").ShouldBe(expected);

    [Theory]
    [InlineData("2H2 + O2 -> 2H2O",            "2H₂ + O₂ → 2H₂O")]
    [InlineData("N2 + 3H2 <=> 2NH3",           "N₂ + 3H₂ ⇌ 2NH₃")]
    [InlineData("CaCO3 -> CaO + CO2",          "CaCO₃ → CaO + CO₂")]
    [InlineData("HCl + NaOH -> NaCl + H2O",    "HCl + NaOH → NaCl + H₂O")]
    [InlineData("^{238}U -> ^{234}Th + ^{4}_{2}He",
                "²³⁸U → ²³⁴Th + ⁴₂He")]
    public void Roundtrip_FullReactions(string body, string expected)
        => MarkdownMacros.RenderMathUnicode(@"\ce{" + body + "}").ShouldBe(expected);

    // The \mathrm wrapping is invisible on the Unicode path: these render
    // as they did when the letters went out bare. \Gamma is the exception,
    // and a fix: it used to be cut at the element Ga.
    [Theory]
    [InlineData("X^{abc}",           "X^abc")]
    [InlineData("^{abc}Y",           "^abcY")]
    [InlineData("abc",               "abc")]
    [InlineData(@"\gamma + e^-",     "γ + e⁻")]
    [InlineData(@"H2O(\text{aq})",   "H₂O(aq)")]
    [InlineData(@"\Gamma",           "Γ")]
    public void Roundtrip_UprightTextAndCommands(string body, string expected)
        => MarkdownMacros.RenderMathUnicode(@"\ce{" + body + "}").ShouldBe(expected);
}
