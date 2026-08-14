using System.Text;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Coverage-driven font selection. The fixtures are chosen for disjoint cmaps: Merida is
/// chess-only (no Latin at all), DejaVu Sans is broad but has no CJK, NotoColorEmoji has only
/// emoji — so "which font covers this" has an unambiguous right answer for each probe, and
/// U+4E2D is covered by none of them.
/// </summary>
public sealed class FontFallbackResolverTests
{
    private static string Font(string name) => Path.Combine(AppContext.BaseDirectory, "Fonts", name);

    private const string Merida = "Merida.ttf";              // chess pieces only
    private const string DejaVu = "DejaVuSans.ttf";          // broad Latin/Greek/Cyrillic + symbols
    private const string Emoji = "NotoColorEmoji.ttf";       // emoji only

    private static readonly Rune ChessKing = new(0x2654);
    private static readonly Rune Rocket = new(0x1F680);       // emoji font only; DejaVu lacks it
    private static readonly Rune CjkZhong = new(0x4E2D);     // covered by none of the fixtures
    private static readonly Rune LatinA = new('A');

    private static FontFallbackResolver Resolver(string primary, params string[] fallbacks)
        => new(Font(primary), fallbacks.Select(Font));

    [Fact]
    public void TryResolveFont_PrefersPrimaryWhenItCovers()
    {
        var r = Resolver(Merida, DejaVu);
        r.TryResolveFont(ChessKing).ShouldBe(Font(Merida));
    }

    [Fact]
    public void TryResolveFont_FallsBackWhenPrimaryDoesNot()
    {
        var r = Resolver(Merida, DejaVu);
        r.TryResolveFont(LatinA).ShouldBe(Font(DejaVu));
    }

    /// <summary>
    /// The question the resolver could not previously answer. CoverageRuns names the primary for
    /// an uncovered codepoint — right for drawing, since .notdef beats a hole in the line, but it
    /// leaves a caller unable to tell a real match from a fallback-to-primary.
    /// </summary>
    [Fact]
    public void TryResolveFont_NullWhenNothingCovers()
    {
        var r = Resolver(Merida, DejaVu, Emoji);
        r.TryResolveFont(CjkZhong).ShouldBeNull();
        r.CanRender(CjkZhong).ShouldBeFalse();
        r.CanRender("A中").ShouldBeFalse();
        r.CanRender("A♔").ShouldBeTrue();
    }

    /// <summary>Drawing still degrades to the primary rather than dropping the glyph.</summary>
    [Fact]
    public void CoverageRuns_UncoveredCodepoint_StillNamesThePrimary()
    {
        var r = Resolver(Merida, DejaVu);
        var runs = r.CoverageRuns("中");
        runs.ShouldHaveSingleItem().FontPath.ShouldBe(Font(Merida));
    }

    [Fact]
    public void CoverageRuns_SplitsAtFontBoundaries()
    {
        var r = Resolver(DejaVu, Emoji);
        // "hi" + rocket + "!" — the emoji is the only part DejaVu can't draw, and being non-BMP
        // it also proves runs are cut on rune, not UTF-16 code-unit, boundaries.
        var runs = r.CoverageRuns("hi\U0001F680!");

        runs.Count.ShouldBe(3);
        runs[0].ShouldBe(("hi", Font(DejaVu)));
        runs[1].ShouldBe(("\U0001F680", Font(Emoji)));
        runs[2].ShouldBe(("!", Font(DejaVu)));
    }

    /// <summary>
    /// The span overload reports offsets into the source instead of substrings, so a draw loop
    /// allocates nothing. It must agree exactly with the string overload.
    /// </summary>
    [Fact]
    public void CoverageRuns_SpanOverload_MatchesStringOverload()
    {
        var r = Resolver(DejaVu, Emoji);
        const string Text = "hi\U0001F680!";

        var spans = new List<FontFallbackResolver.FontRun>();
        r.CoverageRuns(Text.AsSpan(), spans);
        var strings = r.CoverageRuns(Text);

        spans.Count.ShouldBe(strings.Count);
        for (var i = 0; i < spans.Count; i++)
        {
            Text.Substring(spans[i].Start, spans[i].Length).ShouldBe(strings[i].Text);
            spans[i].FontPath.ShouldBe(strings[i].FontPath);
        }
    }

    /// <summary>The run list is cleared per call, so one buffer can be reused across draws.</summary>
    [Fact]
    public void CoverageRuns_SpanOverload_ClearsTheOutputList()
    {
        var r = Resolver(DejaVu, Emoji);
        var runs = new List<FontFallbackResolver.FontRun> { new(99, 99, "stale") };

        r.CoverageRuns("hi".AsSpan(), runs);

        runs.ShouldHaveSingleItem();
        runs[0].Start.ShouldBe(0);
        runs[0].Length.ShouldBe(2);
    }

    [Fact]
    public void PrimaryCoversAll_TrueForTextThePrimaryCanDraw()
    {
        var r = Resolver(DejaVu, Emoji);
        r.PrimaryCoversAll("hello").ShouldBeTrue();
        r.PrimaryCoversAll("→▴").ShouldBeTrue();          // DejaVu has both
        r.PrimaryCoversAll("hi\U0001F680").ShouldBeFalse();          // the rocket is not DejaVu's
    }

    /// <summary>
    /// The ASCII shortcut must be gated on the primary actually covering ASCII. Merida has no
    /// Latin at all, so "A" needs the fallback — a primary-lacks-ASCII case that a blind
    /// "all-ASCII ⇒ primary" rule would silently render as .notdef.
    /// </summary>
    [Fact]
    public void AsciiShortcut_IsCheckedAgainstThePrimary()
    {
        var r = Resolver(Merida, DejaVu);

        r.PrimaryCoversAll("A").ShouldBeFalse();
        r.CoverageRuns("A").ShouldHaveSingleItem().FontPath.ShouldBe(Font(DejaVu));
    }

    [Fact]
    public void PrimaryCoversAll_TrueWhenThereAreNoFallbacks()
    {
        // Nothing to fall back to, so no split is possible and the answer is trivially yes.
        var r = new FontFallbackResolver(Font(Merida), []);
        r.HasFallbacks.ShouldBeFalse();
        r.PrimaryCoversAll("A").ShouldBeTrue();
    }

    /// <summary>
    /// Missing files are dropped at construction, so a role declared for a font the machine
    /// doesn't have reports null rather than a path that can't be loaded.
    /// </summary>
    [Fact]
    public void FromRoles_DropsFacesThatArentInstalled()
    {
        var r = FontFallbackResolver.FromRoles(Font(DejaVu),
            symbolFontPath: Font("does-not-exist.ttf"),
            emojiFontPath: Font(Emoji));

        r.PrimaryFontPath.ShouldBe(Font(DejaVu));
        r.SymbolFontPath.ShouldBeNull();
        r.EmojiFontPath.ShouldBe(Font(Emoji));
    }

    /// <summary>
    /// Role order decides which face wins when several cover a codepoint — the symbol face must
    /// beat the script faces, or a caret gets drawn out of a multi-megabyte CJK font.
    /// </summary>
    [Fact]
    public void FromRoles_SymbolFaceOutranksLaterScriptFaces()
    {
        // Both DejaVu and Merida cover the chess king; whichever is declared first should win.
        var symbolFirst = FontFallbackResolver.FromRoles(Font(Emoji),
            symbolFontPath: Font(Merida), scriptFontPaths: [Font(DejaVu)]);
        symbolFirst.TryResolveFont(ChessKing).ShouldBe(Font(Merida));

        var scriptOnly = FontFallbackResolver.FromRoles(Font(Emoji), scriptFontPaths: [Font(DejaVu)]);
        scriptOnly.TryResolveFont(ChessKing).ShouldBe(Font(DejaVu));
    }

    [Fact]
    public void FromRoles_PrimaryStillWinsOverEveryRole()
    {
        var r = FontFallbackResolver.FromRoles(Font(DejaVu), emojiFontPath: Font(Emoji));
        // DejaVu has its own (monochrome) chess glyphs; the primary is consulted first.
        r.TryResolveFont(ChessKing).ShouldBe(Font(DejaVu));
        r.TryResolveFont(Rocket).ShouldBe(Font(Emoji));
    }

    // ---- The emoji role, and the context that derives from it -------------------------------------

    /// <summary>
    /// The emoji face is reachable BY COVERAGE, which is the reason a separate emoji path is redundant
    /// once a chain exists: an emoji codepoint resolves to the emoji face exactly as any other script
    /// resolves to its own.
    /// </summary>
    [Fact]
    public void FromRoles_ResolvesAnEmojiCodepointToTheEmojiFace()
    {
        var r = FontFallbackResolver.FromRoles(Font(DejaVu), emojiFontPath: Font(Emoji));

        r.TryResolveFont(Rocket).ShouldBe(Font(Emoji));
        r.EmojiFontPath.ShouldBe(Font(Emoji));
    }

    /// <summary>
    /// A context carrying a chain needs no emoji path of its own -- it reads the chain's role. Storing it
    /// twice is the same fact in two places, and the copy that drifts is the one nothing draws from.
    /// </summary>
    [Fact]
    public void MeasureContext_TakesTheEmojiFaceFromTheChainWhenNoneIsStated()
    {
        using var renderer = new RgbaImageRenderer(16, 16);
        var ctx = new PixelMeasureContext<RgbaImage>(renderer, Font(DejaVu))
        {
            Fallback = FontFallbackResolver.FromRoles(Font(DejaVu), emojiFontPath: Font(Emoji)),
        };

        ctx.EmojiFontPath.ShouldBe(Font(Emoji));
    }

    /// <summary>An explicitly stated face wins: a caller naming one specifically is obeyed.</summary>
    [Fact]
    public void MeasureContext_PrefersAnExplicitEmojiFaceOverTheChainsRole()
    {
        using var renderer = new RgbaImageRenderer(16, 16);
        var ctx = new PixelMeasureContext<RgbaImage>(renderer, Font(DejaVu))
        {
            Fallback = FontFallbackResolver.FromRoles(Font(DejaVu), emojiFontPath: Font(Emoji)),
            EmojiFontPath = Font(Merida),
        };

        ctx.EmojiFontPath.ShouldBe(Font(Merida));
    }

    /// <summary>And with no chain at all it is simply what was stated, so the pre-chain consumers work.</summary>
    [Fact]
    public void MeasureContext_KeepsAnExplicitEmojiFaceWithNoChain()
    {
        using var renderer = new RgbaImageRenderer(16, 16);
        var ctx = new PixelMeasureContext<RgbaImage>(renderer, Font(DejaVu)) { EmojiFontPath = Font(Emoji) };

        ctx.EmojiFontPath.ShouldBe(Font(Emoji));
        ctx.Fallback.ShouldBeNull();
    }
}
