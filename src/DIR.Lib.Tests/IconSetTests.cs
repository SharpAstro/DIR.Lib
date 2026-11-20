using System;
using System.IO;
using System.Linq;
using DIR.Lib;
using SharpAstro.Png;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// The SVG-backed <see cref="Layout.IconKind"/> members, drawn through <see cref="RgbaImageRenderer"/>: each loads
/// its embedded outline cleanly, paints coverage in exactly the ink given, the four severity marks differ in
/// shape, and every kind has one cell character. The baseline sheet (<c>Baselines/iconset_sheet.png</c>) shows
/// all twelve at 12, 18 and 32 px on dark and light; regenerate it with <c>DIR_LIB_UPDATE_BASELINES=1</c> and
/// a filter on this class, and look at it.
/// </summary>
public class IconSetTests
{
    private static readonly Layout.IconKind[] SvgKinds =
    [
        Layout.IconKind.Info, Layout.IconKind.Warning, Layout.IconKind.Error, Layout.IconKind.Success,
        Layout.IconKind.Close, Layout.IconKind.Pin, Layout.IconKind.PinFilled, Layout.IconKind.OpenInPage,
        Layout.IconKind.CompareSideBySide, Layout.IconKind.CompareOverlay, Layout.IconKind.CompareSlider,
        Layout.IconKind.CompareChanges,
    ];

    private static readonly bool UpdateBaselines = Environment.GetEnvironmentVariable("DIR_LIB_UPDATE_BASELINES") == "1";
    private static readonly string BaselineDir = Path.Combine(AppContext.BaseDirectory, "Baselines");
    private static readonly string SourceBaselineDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Baselines");

    private sealed class IconWidget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public void Render(Layout.Node root, RectF32 bounds)
        {
            BeginFrame();
            RenderLayout(root, bounds, fontPath: string.Empty, scale: DesignScale.One);
        }
    }

    private static void Fill(RgbaImage image, RGBAColor32 bg)
    {
        var p = image.Pixels;
        for (var i = 0; i < p.Length; i += 4)
        {
            p[i] = bg.Red; p[i + 1] = bg.Green; p[i + 2] = bg.Blue; p[i + 3] = 255;
        }
    }

    private static void Paint(IconWidget widget, Layout.IconKind kind, int x, int y, int size, RGBAColor32 ink)
        => widget.Render(Layout.Builder.Icon(kind, size, ink), new RectF32(x, y, size, size));

    [Fact]
    public void EveryNewKindLoadsItsEmbeddedSvgWithNoIssues()
    {
        foreach (var kind in SvgKinds)
        {
            IconSet.IsVectorBacked(kind).ShouldBeTrue(kind.ToString());
            var icon = IconSet.Get(kind).ShouldNotBeNull(kind.ToString());
            icon.Issues.ShouldBeEmpty(kind.ToString());
            icon.Width.ShouldBe(16f);
            icon.Height.ShouldBe(16f);
            IconSet.Get(kind).ShouldBeSameAs(icon);
        }

        IconSet.Get(Layout.IconKind.Grid).ShouldBeNull();
    }

    [Theory]
    [InlineData(12)]
    [InlineData(18)]
    [InlineData(32)]
    public void EveryNewKindPaintsCoverageInExactlyTheGivenInk(int size)
    {
        var red = new RGBAColor32(0xff, 0x00, 0x00, 0xff);
        foreach (var kind in SvgKinds)
        {
            var renderer = new RgbaImageRenderer(40, 40);
            Fill(renderer.Surface, new RGBAColor32(0, 0, 0, 255));
            Paint(new IconWidget(renderer), kind, 4, 4, size, red);

            var p = renderer.Surface.Pixels;
            var inked = 0;
            for (var i = 0; i < p.Length; i += 4)
            {
                p[i + 1].ShouldBe((byte)0, kind.ToString());
                p[i + 2].ShouldBe((byte)0, kind.ToString());
                if (p[i] > 0) inked++;
            }

            inked.ShouldBeGreaterThan(size, $"{kind} at {size}px");
        }
    }

    [Fact]
    public void TheFourSeverityIconsDifferPairwiseInCoverage()
    {
        var severity = new[] { Layout.IconKind.Info, Layout.IconKind.Warning, Layout.IconKind.Error, Layout.IconKind.Success };
        var coverage = severity.Select(k =>
        {
            var renderer = new RgbaImageRenderer(32, 32);
            Paint(new IconWidget(renderer), k, 0, 0, 32, new RGBAColor32(255, 255, 255, 255));
            var p = renderer.Surface.Pixels;
            return Enumerable.Range(0, 32 * 32).Select(i => p[i * 4 + 3] > 0).ToArray();
        }).ToArray();

        for (var a = 0; a < coverage.Length; a++)
        {
            for (var b = a + 1; b < coverage.Length; b++)
            {
                var differing = coverage[a].Zip(coverage[b]).Count(t => t.First != t.Second);
                differing.ShouldBeGreaterThan(20, $"{severity[a]} vs {severity[b]}");
            }
        }
    }

    [Fact]
    public void EveryIconKindHasAOneCharacterCellGlyph()
    {
        foreach (var kind in Enum.GetValues<Layout.IconKind>())
        {
            var glyph = IconGlyphs.For(kind);
            char.IsControl(glyph).ShouldBeFalse(kind.ToString());
            char.IsSurrogate(glyph).ShouldBeFalse(kind.ToString());
            (glyph == '?' && kind != Layout.IconKind.Search).ShouldBeFalse($"{kind} fell through to the default");
        }

        SvgKinds.Select(IconGlyphs.For).Distinct().Count().ShouldBeGreaterThan(8);
    }

    [Fact]
    public void BaselineSheetOfAllTwelveAt12_18And32OnDarkAndLight()
    {
        const int cell = 44;
        int[] sizes = [12, 18, 32];
        var width = SvgKinds.Length * cell;
        var height = sizes.Length * cell * 2;
        var renderer = new RgbaImageRenderer((uint)width, (uint)height);
        var widget = new IconWidget(renderer);

        var themes = new[]
        {
            (Bg: new RGBAColor32(0x1e, 0x1e, 0x22, 255), Ink: new RGBAColor32(0xe6, 0xe8, 0xec, 255)),
            (Bg: new RGBAColor32(0xf4, 0xf4, 0xf6, 255), Ink: new RGBAColor32(0x22, 0x24, 0x2a, 255)),
        };

        var band = sizes.Length * cell;
        for (var t = 0; t < themes.Length; t++)
        {
            for (var y = t * band; y < (t + 1) * band; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var i = (y * width + x) * 4;
                    var p = renderer.Surface.Pixels;
                    p[i] = themes[t].Bg.Red; p[i + 1] = themes[t].Bg.Green; p[i + 2] = themes[t].Bg.Blue; p[i + 3] = 255;
                }
            }

            for (var r = 0; r < sizes.Length; r++)
            {
                for (var c = 0; c < SvgKinds.Length; c++)
                {
                    var s = sizes[r];
                    Paint(widget, SvgKinds[c], c * cell + (cell - s) / 2, t * band + r * cell + (cell - s) / 2, s, themes[t].Ink);
                }
            }
        }

        CompareBaseline(renderer.Surface, "iconset_sheet.png");
    }

    private static void CompareBaseline(RgbaImage img, string name)
    {
        var baselinePath = Path.Combine(BaselineDir, name);
        if (UpdateBaselines)
        {
            Directory.CreateDirectory(SourceBaselineDir);
            Directory.CreateDirectory(BaselineDir);
            var png = PngWriter.Encode(img.Pixels, img.Width, img.Height);
            File.WriteAllBytes(Path.Combine(SourceBaselineDir, name), png);
            File.WriteAllBytes(baselinePath, png);
            return;
        }

        File.Exists(baselinePath).ShouldBeTrue($"Baseline '{name}' is missing; generate it with DIR_LIB_UPDATE_BASELINES=1.");
        var baseline = PngReader.Decode(File.ReadAllBytes(baselinePath));
        baseline.Width.ShouldBe(img.Width);
        baseline.Height.ShouldBe(img.Height);

        var maxDiff = 0;
        var diffCount = 0;
        for (var i = 0; i < baseline.Pixels.Length; i++)
        {
            var diff = Math.Abs(img.Pixels[i] - baseline.Pixels[i]);
            if (diff > 0) diffCount++;
            if (diff > maxDiff) maxDiff = diff;
        }

        if (maxDiff > 2)
        {
            var actualPath = Path.ChangeExtension(baselinePath, ".actual.png");
            PngWriter.Save(actualPath, img.Pixels, img.Width, img.Height);
            Assert.Fail($"Baseline mismatch for '{name}': {diffCount} pixels differ, max diff={maxDiff}. Actual saved to '{actualPath}'.");
        }
    }
}
