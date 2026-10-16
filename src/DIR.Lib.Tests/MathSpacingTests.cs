using System.Reflection;
using DIR.Lib.Markdown;
using DIR.Lib.MathLayout;
using LALR.CC.LexicalGrammar;
using SharpAstro.Png;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Math spacing as TeX sets it, and the commands sets and functions need.
/// An operator name (<c>\sin</c>, <c>\log</c>, <c>\operatorname{tr}</c>) is an
/// Op atom: a thin space (3 mu, a sixth of an em) goes between it and a
/// following Ord or Op, and between an Ord and a following Op, but none
/// between it and an opening bracket, and none at either end of a formula.
/// Juxtaposition itself adds nothing, so a bracket's two inner gaps match.
/// <c>\mid</c> is a relation, <c>\mathbb</c> an alphabet, <c>\textrm</c>
/// upright text.
///
/// <para>The box path is the whole of it up to the parse:
/// <see cref="MarkdownMacros.TryPrepareMathBoxSource"/>, then the grammar's
/// lexer and a parser bound to a <see cref="BoxBuildingVisitor"/>. Gaps are
/// measured between BOX edges, never ink, so a glyph's own side bearing (the
/// font's, which TeX keeps too) cannot pass for spacing or hide it: each
/// glyph and bracket is swapped for a probe in its parent's slot, as
/// <see cref="RadicalJoinTests"/> swaps its radical, and the probe records the
/// pen its parent draws it at.</para>
/// </summary>
public sealed class MathSpacingTests(ITestOutputHelper output)
{
    private const float FontSize = 32f;

    /// <summary>TeX's thin space, 3 mu: a sixth of an em.</summary>
    private const float Thin = FontSize / 6f;

    private const float HalfPixel = 0.5f;

    private static readonly RGBAColor32 Ink = new(0, 0, 0, 255);

    private static readonly BoxStyle Stix = StyleFor("STIX2Math");

    /// <summary>Where the renders land for a look by eye; never compared.</summary>
    private static string OutputDir =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "obj", "math-spacing-output");

    // ── Operator names: TeX's Op spacing ───────────────────────────────

    /// <summary>
    /// An operator name and the atom after it are a thin space apart: Op then
    /// Ord in <c>\sin x</c>, Op then Op in <c>\sin \cos x</c>. Before, every
    /// one of these read as one word (<c>sinx</c>): juxtaposition put 0.05 em
    /// there whatever the atoms were.
    /// </summary>
    [Theory]
    [InlineData("STIX2Math", @"\sin x", "sin")]
    [InlineData("STIX2Math", @"\log n", "log")]
    [InlineData("STIX2Math", @"\sin \cos x", "sin")]
    [InlineData("STIX2Math", @"\sin \cos x", "cos")]
    [InlineData("STIX2Math", @"\operatorname{tr} A", "tr")]
    [InlineData("DejaVuSans", @"\sin x", "sin")]
    [InlineData("DejaVuSans", @"\log n", "log")]
    [InlineData("DejaVuSans", @"\sin \cos x", "sin")]
    [InlineData("DejaVuSans", @"\sin \cos x", "cos")]
    [InlineData("DejaVuSans", @"\operatorname{tr} A", "tr")]
    public void OperatorName_ThinSpaceBeforeTheNextAtom(string font, string latex, string name)
    {
        var atoms = AtomsOf(latex, StyleFor(font));
        var op = atoms.FindIndex(a => a.Text == name);
        op.ShouldBeGreaterThanOrEqualTo(0, $"'{latex}' has no glyph box '{name}'; it drew {Describe(atoms)}");
        op.ShouldBeLessThan(atoms.Count - 1, $"nothing follows '{name}' in '{latex}'");

        var gap = atoms[op + 1].Left - atoms[op].Right;
        output.WriteLine($"{font} '{latex}': {name} to {atoms[op + 1].Describe()} = {gap:F2} px (thin = {Thin:F2})");
        gap.ShouldBe(Thin, HalfPixel, $"'{latex}' in {font}: the gap after '{name}'");
    }

    /// <summary>
    /// Ord then Op is a thin space too, so <c>x \sin y</c> has one on each
    /// side of the name.
    /// </summary>
    [Theory]
    [InlineData("STIX2Math")]
    [InlineData("DejaVuSans")]
    public void OperatorName_AfterAnOrd_ThinSpaceOnBothSides(string font)
    {
        var atoms = AtomsOf(@"x \sin y", StyleFor(font));
        var op = atoms.FindIndex(a => a.Text == "sin");
        op.ShouldBe(1, $"'x \\sin y' drew {Describe(atoms)}");

        var before = atoms[op].Left - atoms[op - 1].Right;
        var after = atoms[op + 1].Left - atoms[op].Right;
        output.WriteLine($"{font} 'x \\sin y': before = {before:F2} px, after = {after:F2} px");
        before.ShouldBe(Thin, HalfPixel, "Ord then Op");
        after.ShouldBe(Thin, HalfPixel, "Op then Ord");
    }

    /// <summary>
    /// Op then Open takes no space: <c>\sin(x)</c> stays tight, its paren
    /// against the name, as TeX sets it.
    /// </summary>
    [Theory]
    [InlineData("STIX2Math")]
    [InlineData("DejaVuSans")]
    public void OperatorName_BeforeAnOpeningBracket_NoSpace(string font)
    {
        var atoms = AtomsOf(@"\sin(x)", StyleFor(font));
        var op = atoms.FindIndex(a => a.Text == "sin");
        op.ShouldBe(0, $"'\\sin(x)' drew {Describe(atoms)}");
        atoms[op + 1].Box.ShouldBeOfType<BracketBox>();

        var gap = atoms[op + 1].Left - atoms[op].Right;
        output.WriteLine($"{font} '\\sin(x)': sin to ( = {gap:F2} px");
        gap.ShouldBe(0f, HalfPixel);
    }

    /// <summary>
    /// No space at either end of a formula: one that ends in an operator name
    /// ends at the name's box, and a lone name is exactly its glyph box.
    /// </summary>
    [Theory]
    [InlineData("STIX2Math", @"x \sin")]
    [InlineData("STIX2Math", @"\sin")]
    [InlineData("STIX2Math", @"\sin x")]
    [InlineData("DejaVuSans", @"x \sin")]
    [InlineData("DejaVuSans", @"\sin")]
    [InlineData("DejaVuSans", @"\sin x")]
    public void OperatorName_NoSpaceAtTheEndsOfAFormula(string font, string latex)
    {
        var style = StyleFor(font);
        var box = BuildBox(latex, style).ShouldNotBeNull($"'{latex}' should build");
        var atoms = AtomsOf(latex, style);
        atoms[0].Left.ShouldBe(0f, HalfPixel, $"'{latex}' starts at its first atom");
        atoms[^1].Right.ShouldBe(box.Width, HalfPixel, $"'{latex}' ends at its last atom");
    }

    /// <summary>
    /// A script does not change an Op atom's class, so <c>\sin^2 x</c> still
    /// has its thin space before the x, and <c>\log_2 n</c> before the n.
    /// Measured as widths: the whole is its two parts and a thin space.
    /// </summary>
    [Theory]
    [InlineData("STIX2Math", @"\sin^2 x", @"\sin^2", "x")]
    [InlineData("STIX2Math", @"\log_2 n", @"\log_2", "n")]
    [InlineData("DejaVuSans", @"\sin^2 x", @"\sin^2", "x")]
    [InlineData("DejaVuSans", @"\log_2 n", @"\log_2", "n")]
    public void ScriptedOperatorName_KeepsItsThinSpace(string font, string latex, string op, string next)
    {
        var style = StyleFor(font);
        var gap = Width(latex, style) - Width(op, style) - Width(next, style);
        output.WriteLine($"{font} '{latex}': gap = {gap:F2} px");
        gap.ShouldBe(Thin, HalfPixel);
    }

    /// <summary>
    /// <c>\lim</c> keeps its limits layout (the script under the name, not to
    /// its right) and, being an Op too, its thin space before what follows.
    /// </summary>
    [Theory]
    [InlineData("STIX2Math")]
    [InlineData("DejaVuSans")]
    public void Lim_KeepsItsLimits_AndItsThinSpace(string font)
    {
        var style = StyleFor(font);
        var atoms = AtomsOf(@"\lim_{n}", style);
        var lim = atoms.Single(a => a.Text == "lim");
        var n = atoms.Single(a => a.Text != "lim");
        n.BaselineY.ShouldBeGreaterThan(lim.BaselineY, "the limit sits below the name");
        n.Left.ShouldBeLessThan(lim.Right, "the limit sits under the name, not after it");

        var gap = Width(@"\lim_{n} a", style) - Width(@"\lim_{n}", style) - Width("a", style);
        output.WriteLine($"{font} '\\lim_{{n}} a': gap = {gap:F2} px");
        gap.ShouldBe(Thin, HalfPixel);
    }

    // ── \operatorname and \textrm ──────────────────────────────────────

    /// <summary>
    /// <c>\operatorname{X}</c> and <c>\textrm{X}</c> set X in upright letters.
    /// Before, the grammar took each for an unknown command and printed its
    /// name, then the argument in math italic.
    /// </summary>
    [Theory]
    [InlineData("STIX2Math", @"\operatorname{argmax}", "argmax")]
    [InlineData("STIX2Math", @"\textrm{if}", "if")]
    [InlineData("STIX2Math", @"\textrm{a b}", "a b")]
    [InlineData("STIX2Math", @"\operatorname*{arg\,max}", "arg max")]
    [InlineData("DejaVuSans", @"\operatorname{argmax}", "argmax")]
    [InlineData("DejaVuSans", @"\textrm{if}", "if")]
    [InlineData("DejaVuSans", @"\textrm{a b}", "a b")]
    public void UprightCommand_PaintsItsArgumentUpright(string font, string latex, string expected)
    {
        var painted = Painted(latex, StyleFor(font));
        painted.ShouldNotContain(t => t.Contains('\\'), $"'{latex}' painted a command name: {string.Join(" | ", painted)}");
        painted.ShouldBe([expected]);
    }

    /// <summary>
    /// <c>\operatorname{X}</c> is an Op like <c>\sin</c>, so a subscript goes
    /// to its right (it is not a limits operator unless starred) and a thin
    /// space follows it.
    /// </summary>
    [Theory]
    [InlineData("STIX2Math")]
    [InlineData("DejaVuSans")]
    public void OperatorName_WithScript_IsAnOp(string font)
    {
        var style = StyleFor(font);
        var atoms = AtomsOf(@"\operatorname{argmax}_x f", style);
        var name = atoms.Single(a => a.Text == "argmax");
        var x = atoms[atoms.IndexOf(name) + 1];
        x.Left.ShouldBeGreaterThanOrEqualTo(name.Right - HalfPixel, "the script sits to the name's right");

        var gap = Width(@"\operatorname{argmax}_x f", style) - Width(@"\operatorname{argmax}_x", style) - Width("f", style);
        gap.ShouldBe(Thin, HalfPixel);
    }

    // ── Juxtaposition ──────────────────────────────────────────────────

    /// <summary>
    /// Juxtaposition adds no kern a single atom does not have: Ord next to
    /// Ord or Open is no space in TeX. The whole is exactly as wide as its
    /// atoms built one by one (a comma's thin space is the comma's own).
    /// </summary>
    [Theory]
    [InlineData("STIX2Math", "xy", new[] { "x", "y" })]
    [InlineData("STIX2Math", "2x", new[] { "2", "x" })]
    [InlineData("STIX2Math", "n(n+1)", new[] { "n", "(n+1)" })]
    [InlineData("STIX2Math", "1, 2, 3", new[] { "1", ",", "2", ",", "3" })]
    [InlineData("STIX2Math", @"\mathrm{d}x", new[] { @"\mathrm{d}", "x" })]
    [InlineData("DejaVuSans", "xy", new[] { "x", "y" })]
    [InlineData("DejaVuSans", "n(n+1)", new[] { "n", "(n+1)" })]
    [InlineData("DejaVuSans", "1, 2, 3", new[] { "1", ",", "2", ",", "3" })]
    public void Juxtaposition_AddsNoKern(string font, string latex, string[] parts)
    {
        var style = StyleFor(font);
        var extra = Width(latex, style) - parts.Sum(p => Width(p, style));
        output.WriteLine($"{font} '{latex}': {extra:F2} px beyond its atoms");
        extra.ShouldBe(0f, HalfPixel);
    }

    /// <summary>
    /// The gap between a bracket's box and the first atom's box equals the gap
    /// between the last atom's box and the closing bracket's box. Before, it
    /// did not: each juxtaposition put 0.05 em at its LEFT, and juxtapositions
    /// nest leftwards, so <c>1, 2, 3</c> (four of them) carried 0.2 em, 6.4 px
    /// at 32 px, in front of the 1 and nothing after the 3. That is the
    /// visitor's kern, not the digit's side bearing: these are box edges.
    /// </summary>
    [Theory]
    [InlineData("STIX2Math", @"\{1, 2, 3\}")]
    [InlineData("STIX2Math", "[1, 2, 3]")]
    [InlineData("STIX2Math", "(a + b)")]
    [InlineData("STIX2Math", @"\{x, y\}")]
    [InlineData("DejaVuSans", @"\{1, 2, 3\}")]
    [InlineData("DejaVuSans", "[1, 2, 3]")]
    [InlineData("DejaVuSans", "(a + b)")]
    [InlineData("DejaVuSans", @"\{x, y\}")]
    public void Bracket_InnerGapsMatch(string font, string latex)
    {
        var atoms = AtomsOf(latex, StyleFor(font));
        var bracket = atoms[0];
        var (openRight, closeLeft) = DelimiterEdges(bracket);
        var inside = atoms.Skip(1).ToList();
        inside.ShouldNotBeEmpty();

        var open = inside[0].Left - openRight;
        var close = closeLeft - inside[^1].Right;
        output.WriteLine($"{font} '{latex}': after the opening bracket {open:F2} px, before the closing {close:F2} px");
        open.ShouldBe(close, HalfPixel, $"'{latex}' in {font}");
    }

    // ── \mid and \mathbb ───────────────────────────────────────────────

    /// <summary>
    /// <c>\mid</c> is the relation ∣ (U+2223), spaced like <c>=</c>. Before,
    /// it printed as its own name, and the <c>&gt;</c> after it did not lex on
    /// the box path at all, so the set-builder never built.
    /// </summary>
    [Theory]
    [InlineData("STIX2Math")]
    [InlineData("DejaVuSans")]
    public void Mid_IsARelation(string font)
    {
        var style = StyleFor(font);
        const string latex = @"\{ x \mid x > 0 \}";
        var painted = Painted(latex, style);
        painted.ShouldContain("∣");
        painted.ShouldNotContain(t => t.Contains("mid"), $"'{latex}' painted {string.Join(" | ", painted)}");

        var atoms = AtomsOf(latex, style);
        var mid = atoms.FindIndex(a => a.Text == "∣");
        var eqAtoms = AtomsOf("x = y", style);
        var eq = eqAtoms.FindIndex(a => a.Text == "=");
        var eqBefore = eqAtoms[eq].Left - eqAtoms[eq - 1].Right;
        var eqAfter = eqAtoms[eq + 1].Left - eqAtoms[eq].Right;

        var before = atoms[mid].Left - atoms[mid - 1].Right;
        var after = atoms[mid + 1].Left - atoms[mid].Right;
        output.WriteLine($"{font}: around ∣ {before:F2} / {after:F2} px, around = {eqBefore:F2} / {eqAfter:F2} px");
        before.ShouldBe(eqBefore, HalfPixel);
        after.ShouldBe(eqAfter, HalfPixel);
    }

    /// <summary>
    /// <c>\mathbb{X}</c> paints the double-struck letter. Three of the ones
    /// sets use live in Letterlike Symbols (ℕ U+2115, ℝ U+211D, ℤ U+2124), not
    /// in the U+1D538 run, which has holes where they would be.
    /// </summary>
    [Theory]
    [InlineData(@"\mathbb{R}", "ℝ")]
    [InlineData(@"\mathbb{N}", "ℕ")]
    [InlineData(@"\mathbb{Z}", "ℤ")]
    [InlineData(@"\mathbb{C}", "ℂ")]
    [InlineData(@"\mathbb{A}", "𝔸")]
    public void Mathbb_PaintsTheDoubleStruckLetter(string latex, string expected)
        => Painted(latex, Stix).ShouldBe([expected]);

    /// <summary>
    /// DejaVu Sans paints whatever its cmap has: the double-struck letter
    /// where it has one, else the plain letter, the way a variable falls back
    /// from math italic, and never the command's name. Its cmap turns out to
    /// hold every one of these (the oracle reads it), so here it paints them
    /// all double-struck; the fallback is <see cref="MathGlyphBox"/>'s, per
    /// rune, which <see cref="MathItalicTests"/> drives with this font.
    /// </summary>
    [Theory]
    [InlineData(@"\mathbb{R}", 'R', 0x211D)]
    [InlineData(@"\mathbb{N}", 'N', 0x2115)]
    [InlineData(@"\mathbb{Z}", 'Z', 0x2124)]
    [InlineData(@"\mathbb{A}", 'A', 0x1D538)]
    [InlineData(@"\mathbb{B}", 'B', 0x1D539)]
    public void Mathbb_DejaVuSans_PaintsWhatItsCmapHas(string latex, char letter, int doubleStruck)
    {
        var dejaVu = StyleFor("DejaVuSans");
        BoxStyle.SharedRasterizer.TryGetOpenTypeFont(dejaVu.FontPath, out var font).ShouldBeTrue();
        var expected = font.GetGlyphId((uint)doubleStruck) != 0 ? char.ConvertFromUtf32(doubleStruck) : letter.ToString();
        output.WriteLine($"DejaVu Sans has U+{doubleStruck:X4}: {expected != letter.ToString()}");

        Painted(latex, dejaVu).ShouldBe([expected]);
    }

    // ── Unicode path ───────────────────────────────────────────────────

    /// <summary>
    /// The Unicode path shares the parse, and spaces an operator name the
    /// same way, with a plain space for the thin one: before, <c>\sin x</c>
    /// printed <c>sinx</c>.
    /// </summary>
    [Theory]
    [InlineData(@"\sin x", "sin x")]
    [InlineData(@"\sin(x)", "sin(x)")]
    [InlineData(@"x \sin y", "x sin y")]
    [InlineData(@"\sin \cos x", "sin cos x")]
    [InlineData(@"\sin^2 x", "sin² x")]
    [InlineData(@"\log_2 n", "log₂ n")]
    [InlineData(@"\operatorname{tr} A", "tr A")]
    [InlineData(@"\textrm{if}", "if")]
    [InlineData(@"\textrm{a b}", "a b")]
    [InlineData(@"x \mid y", "x ∣ y")]
    [InlineData(@"\mathbb{R}", "ℝ")]
    [InlineData(@"\mathbb{N}", "ℕ")]
    [InlineData(@"\sin[\alpha + \beta]", "sin[α + β]")]
    public void UnicodePath(string latex, string expected)
        => MarkdownMacros.RenderMathUnicode(latex).ShouldBe(expected);

    // ── Renders for the eye ────────────────────────────────────────────

    /// <summary>
    /// Renders written to <c>obj/</c> for a look by eye. Asserts only that
    /// they have ink; the PNGs are not baselines.
    /// </summary>
    [Theory]
    [InlineData(@"\sin x + \cos(y)", "sin-x-plus-cos-y")]
    [InlineData(@"\operatorname{argmax}_x f(x)", "operatorname-argmax")]
    [InlineData(@"\{1, 2, 3\}", "set-1-2-3")]
    [InlineData(@"\{ x \in \mathbb{R} \mid x > 0 \}", "set-builder-reals")]
    public void WritesPngForInspection(string latex, string name)
    {
        var box = BuildBox(latex, Stix).ShouldNotBeNull($"'{latex}' should build");
        var image = BoxRasterizer.RenderToRgba(box, Stix);
        image.Pixels.Where((_, i) => i % 4 == 3).Count(a => a > 0).ShouldBeGreaterThan(0);

        Directory.CreateDirectory(OutputDir);
        var path = Path.GetFullPath(Path.Combine(OutputDir, name + ".png"));
        File.WriteAllBytes(path, PngWriter.Encode(image.Pixels, image.Width, image.Height));
        output.WriteLine(path);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static BoxStyle StyleFor(string font) => new(font switch
    {
        "STIX2Math" => Path.Combine(AppContext.BaseDirectory, "Fonts", "STIX2Math.otf"),
        "DejaVuSans" => Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf"),
        _ => throw new ArgumentException($"unknown font '{font}'"),
    }, FontSize, Ink);

    /// <summary>
    /// Prepares <paramref name="latex"/> with
    /// <see cref="MarkdownMacros.TryPrepareMathBoxSource"/>, then parses and
    /// builds it at <paramref name="style"/>. Null when the helper turns it
    /// away or it does not lex or parse (LALR.CC throws an
    /// <see cref="InvalidOperationException"/> for both).
    /// </summary>
    private static Box? BuildBox(string latex, BoxStyle style)
    {
        if (!MarkdownMacros.TryPrepareMathBoxSource(latex, out var source)) return null;
        var parser = Latex.BuildParser(new BoxBuildingVisitor(style));
        try
        {
            using var lexer = BytesLexer.FromString(source, MarkdownMacros.MathLexerTable);
            using var tokens = new SyncLATokenIterator(lexer);
            var item = parser.ParseInput(tokens, debugger: null);
            return item.IsError || item.Content is not Func<BoxStyle, Box> builder ? null : builder(style);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static float Width(string latex, BoxStyle style)
        => BuildBox(latex, style).ShouldNotBeNull($"'{latex}' should build").Width;

    /// <summary>An atom as its parent drew it: the box, and its box edges and
    /// baseline relative to the formula's own pen.</summary>
    private sealed record Placed(Box Box, float Left, float Right, float BaselineY)
    {
        public string? Text => Box switch
        {
            GlyphBox g => g.Text,
            MathGlyphBox m => m.Text,
            _ => null,
        };

        public string Describe() => Text is { } t ? $"'{t}'" : Box.GetType().Name;
    }

    private static string Describe(List<Placed> atoms)
        => string.Join(" ", atoms.Select(a => $"{a.Describe()}[{a.Left:F1}..{a.Right:F1}]"));

    /// <summary>
    /// The atoms <paramref name="latex"/>'s box tree draws, in paint order:
    /// every glyph (a <see cref="GlyphBox"/> or <see cref="MathGlyphBox"/>) and
    /// every <see cref="BracketBox"/>, whose own atoms follow it. Kerns are
    /// space, not atoms, so they are left out. An empty glyph (<c>\null</c>) is
    /// left out too.
    /// </summary>
    private static List<Placed> AtomsOf(string latex, BoxStyle style)
    {
        var box = BuildBox(latex, style).ShouldNotBeNull($"'{latex}' should build");
        var log = new List<Placed>();
        var root = Probe(box, log);
        var margin = (int)MathF.Ceiling(style.FontSize);
        using var renderer = new RgbaImageRenderer(
            (uint)((int)MathF.Ceiling(box.Width) + margin * 2),
            (uint)((int)MathF.Ceiling(box.TotalHeight) + margin * 2));
        root.Draw(renderer, margin, margin + box.Height, style);
        return log
            .Where(a => a.Text is null or { Length: > 0 })
            .Select(a => a with { Left = a.Left - margin, Right = a.Right - margin })
            .ToList();
    }

    private static bool IsAtom(object node) => node is GlyphBox or MathGlyphBox or BracketBox;

    /// <summary>
    /// Stands in for an atom in its parent's slot and records the pen the
    /// parent draws it at. It forwards the atom's metrics and its draw
    /// unchanged, so the formula lays out and paints exactly as it does
    /// without it.
    /// </summary>
    private sealed class PenProbe(Box atom, List<Placed> log) : Box
    {
        public override float Width => atom.Width;
        public override float Height => atom.Height;
        public override float Depth => atom.Depth;

        public override void Draw(RgbaImageRenderer renderer, float penX, float baselineY, BoxStyle style)
        {
            log.Add(new Placed(atom, penX, penX + atom.Width, baselineY));
            atom.Draw(renderer, penX, baselineY, style);
        }
    }

    /// <summary>
    /// <paramref name="root"/> with a <see cref="PenProbe"/> in every slot that
    /// holds an atom. Boxes keep their children in private fields, so this
    /// walks those, as <see cref="RadicalJoinTests"/>' probe does; a bracket is
    /// walked into as well as probed, so the atoms inside it report too.
    /// </summary>
    private static Box Probe(Box root, List<Placed> log)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<object>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            if (!seen.Add(node)) continue;
            for (var type = node.GetType(); type is not null && type != typeof(object); type = type.BaseType)
            {
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    switch (field.GetValue(node))
                    {
                        case Box child when IsAtom(child) && field.FieldType.IsAssignableFrom(typeof(PenProbe)):
                            field.SetValue(node, new PenProbe(child, log));
                            if (child is BracketBox) pending.Push(child);
                            break;
                        case MathGlyphBox or GlyphBox:
                            break;
                        case Box child:
                            pending.Push(child);
                            break;
                        case Box[] items when items.GetType().GetElementType()!.IsAssignableFrom(typeof(PenProbe)):
                            for (var i = 0; i < items.Length; i++)
                            {
                                if (IsAtom(items[i]))
                                {
                                    if (items[i] is BracketBox) pending.Push(items[i]);
                                    items[i] = new PenProbe(items[i], log);
                                }
                                else
                                {
                                    pending.Push(items[i]);
                                }
                            }
                            break;
                    }
                }
            }
        }
        return IsAtom(root) ? new PenProbe(root, log) : root;
    }

    /// <summary>
    /// The right edge of a bracket's opening delimiter box and the left edge of
    /// its closing one. <see cref="BracketBox"/> keeps its delimiters private,
    /// so they are read by name; a font without MATH coverage for them draws
    /// parametric strokes <c>bracketWidth</c> wide instead.
    /// </summary>
    private static (float OpenRight, float CloseLeft) DelimiterEdges(Placed bracket)
    {
        var box = bracket.Box.ShouldBeOfType<BracketBox>();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var fields = typeof(BracketBox).GetFields(flags);
        var left = fields.Single(f => f.FieldType == typeof(StretchyVerticalBox) && f.Name.Contains("left", StringComparison.OrdinalIgnoreCase)).GetValue(box) as Box;
        var right = fields.Single(f => f.FieldType == typeof(StretchyVerticalBox) && f.Name.Contains("right", StringComparison.OrdinalIgnoreCase)).GetValue(box) as Box;
        float leftWidth, rightWidth;
        if (left is not null && right is not null)
        {
            leftWidth = left.Width;
            rightWidth = right.Width;
        }
        else
        {
            leftWidth = rightWidth = (float)fields.Single(f => f.Name.Contains("bracketWidth", StringComparison.OrdinalIgnoreCase)).GetValue(box)!;
        }
        return (bracket.Left + leftWidth, bracket.Right - rightWidth);
    }

    /// <summary>
    /// The strings <paramref name="latex"/>'s box tree hands the renderer, in
    /// paint order. An empty string (<c>\null</c>'s glyph) is left out.
    /// </summary>
    private static List<string> Painted(string latex, BoxStyle style)
    {
        var box = BuildBox(latex, style).ShouldNotBeNull($"'{latex}' should build");
        var margin = (int)MathF.Ceiling(style.FontSize * 0.15f);
        using var recorder = new PaintRecorder(
            (uint)((int)MathF.Ceiling(box.Width) + margin * 2),
            (uint)((int)MathF.Ceiling(box.TotalHeight) + margin * 2));
        box.Draw(recorder, margin, margin + box.Height, style);
        return recorder.Texts;
    }

    private sealed class PaintRecorder(uint width, uint height) : RgbaImageRenderer(width, height)
    {
        public List<string> Texts { get; } = [];

        public override void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize,
            RGBAColor32 fontColor, in RectInt layout,
            TextAlign horizAlignment = TextAlign.Center, TextAlign vertAlignment = TextAlign.Near)
        {
            if (!text.IsEmpty) Texts.Add(text.ToString());
            base.DrawText(text, fontFamily, fontSize, fontColor, layout, horizAlignment, vertAlignment);
        }
    }
}
