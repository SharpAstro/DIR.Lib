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

    [Theory]
    [InlineData("H2O",       "H_{2}O")]
    [InlineData("NaCl",      "{Na}{Cl}")]                  // 2-letter elements wrap so a trailing script binds the whole symbol
    [InlineData("CaCO3",     "{Ca}CO_{3}")]
    [InlineData("H2SO4",     "H_{2}SO_{4}")]
    [InlineData("C6H12O6",   "C_{6}H_{12}O_{6}")]
    [InlineData("CO2",       "CO_{2}")]                    // C+O, NOT Co (cobalt) — 1-letter elements emit bare
    [InlineData("Co2O3",     "{Co}_{2}O_{3}")]             // Co matches before C+o → 2-letter wrap
    [InlineData("Fe2O3",     "{Fe}_{2}O_{3}")]
    public void ToLatex_FormulasWithAutoSubscripts(string body, string expected)
        => Mhchem.ToLatex(body).ShouldBe(expected);

    // ── Coefficients vs subscripts ─────────────────────────────────────

    [Theory]
    [InlineData("3H2",       "3H_{2}")]                    // leading 3 = coefficient (bare)
    [InlineData("2H2O",      "2H_{2}O")]
    [InlineData("10NaCl",    "10{Na}{Cl}")]
    [InlineData("2H2 + O2",  "2H_{2} + O_{2}")]
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
    [InlineData("^{238}U",     @"\null^{238}U")]
    [InlineData("^{14}C",      @"\null^{14}C")]
    [InlineData("^{14}_{6}C",  @"\null^{14}_{6}C")]
    [InlineData("^{226}Ra",    @"\null^{226}{Ra}")]
    [InlineData("^{4}_{2}He",  @"\null^{4}_{2}{He}")]        // alpha particle
    public void ToLatex_IsotopePrefixScripts(string body, string expected)
        => Mhchem.ToLatex(body).ShouldBe(expected);

    // ── Ion charges (postfix LaTeX scripts with \plus / \minus) ────────

    [Theory]
    [InlineData("Fe^3+",       @"{Fe}^{3\plus}")]
    [InlineData("OH^-",        @"OH^{\minus}")]
    [InlineData("Cu^{2+}",     @"{Cu}^{2\plus}")]
    [InlineData("SO4^{2-}",    @"SO_{4}^{2\minus}")]
    [InlineData("Na^+",        @"{Na}^{\plus}")]
    [InlineData("Cl^-",        @"{Cl}^{\minus}")]
    [InlineData("NH4^+",       @"NH_{4}^{\plus}")]
    public void ToLatex_IonCharges(string body, string expected)
        => Mhchem.ToLatex(body).ShouldBe(expected);

    // ── Reaction arrows ────────────────────────────────────────────────

    [Theory]
    [InlineData("A -> B",      @"A \to  B")]               // space, \to, trailing space + source space = double
    [InlineData("A <- B",      @"A \leftarrow  B")]
    [InlineData("A <=> B",     @"A \rightleftharpoons  B")]
    [InlineData("A <-> B",     @"A \leftrightarrow  B")]
    public void ToLatex_ReactionArrows(string body, string expected)
        => Mhchem.ToLatex(body).ShouldBe(expected);

    // ── State markers (verbatim parens) ────────────────────────────────

    [Theory]
    [InlineData("H2O(l)",      "H_{2}O(l)")]
    [InlineData("H2O(g)",      "H_{2}O(g)")]
    [InlineData("NaCl(s)",     "{Na}{Cl}(s)")]
    [InlineData("HCl(aq)",     "H{Cl}(aq)")]
    [InlineData("Ca(OH)2",     "{Ca}(OH)_{2}")]            // trailing digit subscripts the paren expression via P → P _ A
    public void ToLatex_StateMarkersAndParens(string body, string expected)
        => Mhchem.ToLatex(body).ShouldBe(expected);

    // ── End-to-end reactions ───────────────────────────────────────────

    [Theory]
    [InlineData("2H2 + O2 -> 2H2O",            @"2H_{2} + O_{2} \to  2H_{2}O")]
    [InlineData("N2 + 3H2 <=> 2NH3",           @"N_{2} + 3H_{2} \rightleftharpoons  2NH_{3}")]
    [InlineData("CaCO3 -> CaO + CO2",          @"{Ca}CO_{3} \to  {Ca}O + CO_{2}")]
    [InlineData("HCl + NaOH -> NaCl + H2O",    @"H{Cl} + {Na}OH \to  {Na}{Cl} + H_{2}O")]
    [InlineData("^{238}U -> ^{234}Th + ^{4}_{2}He",
                @"\null^{238}U \to  \null^{234}{Th} + \null^{4}_{2}{He}")]
    public void ToLatex_FullReactions(string body, string expected)
        => Mhchem.ToLatex(body).ShouldBe(expected);

    // ── Graceful degradation ───────────────────────────────────────────

    [Fact]
    public void ToLatex_EmptyBody_ReturnsEmpty()
        => Mhchem.ToLatex("").ShouldBe("");

    [Fact]
    public void ToLatex_UnknownContentPassesThrough()
    {
        // Lowercase-only "abc" has no known symbols; should round-trip.
        Mhchem.ToLatex("abc").ShouldBe("abc");
    }

    [Fact]
    public void ToLatex_UnmappableScriptKeepsLatexForm()
    {
        // ^{abc} in postfix position emits as LaTeX `^{abc}` — letters aren't
        // signs, so EscapeSigns is a no-op and the script body parses fine
        // through the math grammar as a group of juxtaposed ids.
        Mhchem.ToLatex("X^{abc}").ShouldBe("X^{abc}");
    }

    [Fact]
    public void ToLatex_PrefixScriptWithUnmappableContent()
    {
        // ^{abc} at term start: still emits \null^{abc} LaTeX. The math
        // grammar parses it (group of juxtaposed ids inside the script),
        // the Unicode visitor's TryUnicodeScript fails on "abc" and
        // falls back to caret notation — chem author sees their source
        // preserved rather than silently mangled.
        Mhchem.ToLatex("^{abc}Y").ShouldBe(@"\null^{abc}Y");
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
}
