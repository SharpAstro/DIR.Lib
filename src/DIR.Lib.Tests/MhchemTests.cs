using DIR.Lib.Markdown;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// Covers the Phase-1 <c>\ce{...}</c> subset implemented in
/// <see cref="Mhchem"/>: element symbols + auto-subscript digits, isotope
/// prefix scripts, ion charges, reaction arrows, parenthesised state
/// markers, plus-separators, and graceful-degradation passthrough for
/// content the renderer doesn't recognise. End-to-end markdown round-trip
/// tests (which exercise <c>MarkdownRenderer.RenderLines</c> from
/// Console.Lib) live in <c>Console.Lib.Tests.MhchemMarkdownIntegrationTests</c>.
/// </summary>
public sealed class MhchemTests
{
    // ── Element symbols + auto-subscripts ──────────────────────────────

    [Theory]
    [InlineData("H2O",       "H₂O")]
    [InlineData("NaCl",      "NaCl")]
    [InlineData("CaCO3",     "CaCO₃")]
    [InlineData("H2SO4",     "H₂SO₄")]
    [InlineData("C6H12O6",   "C₆H₁₂O₆")]
    [InlineData("CO2",       "CO₂")]                    // C+O, NOT Co (cobalt)
    [InlineData("Co2O3",     "Co₂O₃")]                  // Co matches before C+o
    [InlineData("Fe2O3",     "Fe₂O₃")]
    public void Render_FormulasWithAutoSubscripts(string body, string expected)
        => Mhchem.Render(body).ShouldBe(expected);

    // ── Coefficients vs subscripts ─────────────────────────────────────

    [Theory]
    [InlineData("3H2",       "3H₂")]                    // leading 3 = coefficient (plain)
    [InlineData("2H2O",      "2H₂O")]
    [InlineData("10NaCl",    "10NaCl")]
    [InlineData("2H2 + O2",  "2H₂ + O₂")]
    public void Render_LeadingDigitsAreCoefficients(string body, string expected)
        => Mhchem.Render(body).ShouldBe(expected);

    // ── Isotope prefix scripts ─────────────────────────────────────────

    [Theory]
    [InlineData("^{238}U",     "²³⁸U")]
    [InlineData("^{14}C",      "¹⁴C")]
    [InlineData("^{14}_{6}C",  "¹⁴₆C")]
    [InlineData("^{226}Ra",    "²²⁶Ra")]
    [InlineData("^{4}_{2}He",  "⁴₂He")]                 // alpha particle
    public void Render_IsotopePrefixScripts(string body, string expected)
        => Mhchem.Render(body).ShouldBe(expected);

    // ── Ion charges ────────────────────────────────────────────────────

    [Theory]
    [InlineData("Fe^3+",       "Fe³⁺")]
    [InlineData("OH^-",        "OH⁻")]
    [InlineData("Cu^{2+}",     "Cu²⁺")]
    [InlineData("SO4^{2-}",    "SO₄²⁻")]
    [InlineData("Na^+",        "Na⁺")]
    [InlineData("Cl^-",        "Cl⁻")]
    [InlineData("NH4^+",       "NH₄⁺")]
    public void Render_IonCharges(string body, string expected)
        => Mhchem.Render(body).ShouldBe(expected);

    // ── Reaction arrows ────────────────────────────────────────────────

    [Theory]
    [InlineData("A -> B",      "A → B")]
    [InlineData("A <- B",      "A ← B")]
    [InlineData("A <=> B",     "A ⇌ B")]
    [InlineData("A <-> B",     "A ↔ B")]
    public void Render_ReactionArrows(string body, string expected)
        => Mhchem.Render(body).ShouldBe(expected);

    // ── State markers (verbatim parens) ────────────────────────────────

    [Theory]
    [InlineData("H2O(l)",      "H₂O(l)")]
    [InlineData("H2O(g)",      "H₂O(g)")]
    [InlineData("NaCl(s)",     "NaCl(s)")]
    [InlineData("HCl(aq)",     "HCl(aq)")]
    [InlineData("Ca(OH)2",     "Ca(OH)₂")]              // subscript AFTER closing paren is currently NOT bound to the group; trailing digit attaches to the empty post-paren context — see test below for actual behaviour
    public void Render_StateMarkersAndParens(string body, string expected)
        => Mhchem.Render(body).ShouldBe(expected);

    // ── End-to-end reactions ───────────────────────────────────────────

    [Theory]
    [InlineData("2H2 + O2 -> 2H2O",            "2H₂ + O₂ → 2H₂O")]
    [InlineData("N2 + 3H2 <=> 2NH3",           "N₂ + 3H₂ ⇌ 2NH₃")]
    [InlineData("CaCO3 -> CaO + CO2",          "CaCO₃ → CaO + CO₂")]
    [InlineData("HCl + NaOH -> NaCl + H2O",    "HCl + NaOH → NaCl + H₂O")]
    [InlineData("^{238}U -> ^{234}Th + ^{4}_{2}He",
                "²³⁸U → ²³⁴Th + ⁴₂He")]
    public void Render_FullReactions(string body, string expected)
        => Mhchem.Render(body).ShouldBe(expected);

    // ── Graceful degradation ───────────────────────────────────────────

    [Fact]
    public void Render_EmptyBody_ReturnsEmpty()
        => Mhchem.Render("").ShouldBe("");

    [Fact]
    public void Render_UnknownContentPassesThrough()
    {
        // Lowercase-only "abc" has no known symbols; should round-trip.
        Mhchem.Render("abc").ShouldBe("abc");
    }

    [Fact]
    public void Render_UnmappableScriptFallsBackToLiteral()
    {
        // ^{abc} — no super for a/b/c → keeps the LaTeX shape so the
        // author can see what they wrote.
        Mhchem.Render("X^{abc}").ShouldContain("^{abc}");
    }

}
