using System.Collections;
using System.Reflection;
using DIR.Lib.Markdown;
using DIR.Lib.MathLayout;
using LALR.CC.LexicalGrammar;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>Runs alone: its test empties and then counts SqrtBox's static scratch pool, which every other
/// test drawing a radical also uses.</summary>
[CollectionDefinition(nameof(RadicalScratchPoolTests), DisableParallelization = true)]
public sealed class RadicalScratchPoolCollection;

/// <summary>
/// SqrtBox measures a radicand's ink by drawing it on a pooled scratch renderer, and the pool outlives every
/// render, so the glyphs those renderers cache must not grow with every new size a radical is drawn at.
/// </summary>
[Collection(nameof(RadicalScratchPoolTests))]
public sealed class RadicalScratchPoolTests
{
    private static readonly string Stix = Path.Combine(AppContext.BaseDirectory, "Fonts", "STIX2Math.otf");

    /// <summary>
    /// Review round 2: <c>\sqrt{abcdefghijklm}</c> drawn at 1000 distinct STIX Two Math sizes (8 to 107.9 px,
    /// 100 rounded sizes a glyph cache keys) left a pooled scratch renderer holding 13 glyphs at every one of
    /// them. Now no pooled renderer has measured at more than <see cref="SqrtBox.InkScratchSizesKept"/> sizes,
    /// so the pool keeps at most 13 glyphs at each of that many sizes per renderer.
    /// </summary>
    [Fact]
    public void DrawingRadicalsAtManySizesKeepsTheGlyphsOfAFewSizes()
    {
        const int distinctRunes = 13;
        var pool = (IEnumerable)typeof(SqrtBox).GetField("InkScratch", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        pool.GetType().GetMethod("Clear")!.Invoke(pool, null);

        for (var i = 0; i < 1000; i++)
        {
            var style = new BoxStyle(Stix, 8f + i * 0.1f, new RGBAColor32(0, 0, 0, 255));
            BoxRasterizer.RenderToRgba(Parse(@"\sqrt{abcdefghijklm}", style), style);
        }

        var entries = pool.Cast<object>().ToList();
        entries.ShouldNotBeEmpty("the pool keeps a scratch renderer for the next radical");
        foreach (var entry in entries)
        {
            var sizes = ((IEnumerable)entry.GetType().GetField("Sizes")!.GetValue(entry)!).Cast<object>().ToList();
            var renderer = (RgbaImageRenderer)entry.GetType().GetField("Renderer")!.GetValue(entry)!;
            var glyphs = (ICollection)typeof(RgbaImageRenderer).GetField("_glyphCache", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(renderer)!;
            sizes.Count.ShouldBeLessThanOrEqualTo(SqrtBox.InkScratchSizesKept, "sizes a pooled scratch renderer has measured at");
            glyphs.Count.ShouldBeLessThanOrEqualTo(SqrtBox.InkScratchSizesKept * distinctRunes, "glyphs a pooled scratch renderer keeps");
        }
    }

    private static Box Parse(string latex, BoxStyle style)
    {
        var src = MarkdownMacros.ResolveBackslashEscapes(MarkdownMacros.NormalizeLatexAliases(latex));
        var parser = Latex.BuildParser(new BoxBuildingVisitor(style));
        using var lexer = BytesLexer.FromString(src, MarkdownMacros.MathLexerTable);
        using var tokens = new SyncLATokenIterator(lexer);
        var item = parser.ParseInput(tokens, debugger: null);
        item.IsError.ShouldBeFalse($"'{latex}' did not parse");
        return item.Content.ShouldBeOfType<Func<BoxStyle, Box>>()(style);
    }
}
