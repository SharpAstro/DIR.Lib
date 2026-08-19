using System.Collections.Immutable;
using System.Text;
using DIR.Lib;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// The baking logic, which lives in the library rather than in the tool because it also has to run at
/// RUNTIME -- an app whose theme turns the whole UI one colour needs its normally-full-colour emoji as
/// tintable coverage, and which emoji those are is not known until it draws them.
/// </summary>
public sealed class IconBakerTests
{
    // A face that certainly exists wherever these tests run, and a codepoint it certainly carries.
    // Deliberately NOT an emoji: emoji coverage varies by host, which is the whole reason a caller needs
    // IsEmpty rather than an exception.
    private static string? TextFontPath => FontResolver.ResolveSystemFont() is { Length: > 0 } p ? p : null;

    [Fact]
    public void ABakedGlyphCoversSomethingAndStaysInsideItsSquare()
    {
        var font = TextFontPath;
        Assert.SkipWhen(font is null, "No system font on this host.");

        using var rasterizer = new ManagedFontRasterizer();
        var mask = IconBaker.Bake(rasterizer, font!, new Rune('W'), 20);

        mask.IsEmpty.ShouldBeFalse();
        mask.Size.ShouldBe(20);
        foreach (var run in mask.Runs)
        {
            // Runs are indices into a Size-square grid, and a consumer scales them by size/Size. One that
            // ran past the edge would draw outside the box it was given, over whatever sits beside it.
            (run.X + run.Width).ShouldBeLessThanOrEqualTo(20);
            run.Y.ShouldBeLessThan((byte)20);
            run.Width.ShouldBeGreaterThan((byte)0);
            run.Alpha.ShouldBeGreaterThan((byte)0);
        }
    }

    [Fact]
    public void TheFullyCoveredInteriorIsOpaque()
    {
        // Not cosmetic. A bucket-centre mapping leaves a fully covered pixel at 223 of 255 for four
        // levels, which makes a whole mark read visibly greyer than the text and the drawn marks beside
        // it -- caught by measuring peak brightness of a baked mark against a drawn one, not by eye.
        var font = TextFontPath;
        Assert.SkipWhen(font is null, "No system font on this host.");

        using var rasterizer = new ManagedFontRasterizer();
        var mask = IconBaker.Bake(rasterizer, font!, new Rune('W'), 24);

        mask.Runs.Select(r => r.Alpha).Max().ShouldBe((byte)255);
    }

    [Fact]
    public void RunsOnOneRowNeverOverlap()
    {
        // Overlapping runs double-blend where they cross, so a semi-transparent edge would come out
        // darker than it should exactly along the mark's outline.
        var font = TextFontPath;
        Assert.SkipWhen(font is null, "No system font on this host.");

        using var rasterizer = new ManagedFontRasterizer();
        var mask = IconBaker.Bake(rasterizer, font!, new Rune('M'), 26);

        foreach (var row in mask.Runs.GroupBy(r => r.Y))
        {
            var spans = row.OrderBy(r => r.X).ToArray();
            for (var i = 1; i < spans.Length; i++)
            {
                spans[i].X.ShouldBeGreaterThanOrEqualTo((byte)(spans[i - 1].X + spans[i - 1].Width));
            }
        }
    }

    [Fact]
    public void AnUncoveredCodepointBakesEmptyRatherThanThrowing()
    {
        // An empty result is a normal answer: coverage is a property of the FACE, and a caller with a
        // fallback (a drawn mark, another face) has to be told rather than thrown at. This is also what a
        // build tool turns into a real error, since there a missing glyph means the manifest is wrong.
        var font = TextFontPath;
        Assert.SkipWhen(font is null, "No system font on this host.");

        using var rasterizer = new ManagedFontRasterizer();
        // A Plane-15 private-use codepoint: no ordinary text face carries one.
        var mask = IconBaker.Bake(rasterizer, font!, new Rune(0xF0000), 20);

        mask.IsEmpty.ShouldBeTrue();
        mask.Size.ShouldBe(20);
    }

    [Fact]
    public void BakingIsReproducible()
    {
        // The property that lets a build pipeline VERIFY a generated file rather than trust it: the
        // rasteriser is pure managed, so the same inputs give the same output on any host.
        var font = TextFontPath;
        Assert.SkipWhen(font is null, "No system font on this host.");

        using var a = new ManagedFontRasterizer();
        using var b = new ManagedFontRasterizer();
        IconBaker.Bake(a, font!, new Rune('S'), 20).Runs
            .ShouldBe(IconBaker.Bake(b, font!, new Rune('S'), 20).Runs);
    }

    [Fact]
    public void NearestSizePicksTheClosestBakeRatherThanTheFirst()
    {
        var masks = ImmutableArray.Create(
            new IconBaker.CoverageMask(13, [new IconBaker.CoverageRun(0, 0, 1, 255)]),
            new IconBaker.CoverageMask(20, [new IconBaker.CoverageRun(0, 0, 1, 255)]),
            new IconBaker.CoverageMask(39, [new IconBaker.CoverageRun(0, 0, 1, 255)]));

        // A DPI scale is continuous, so the caller always scales; picking the closest keeps that scale
        // near 1, which is what stops the snapping in DrawCoverageMask having to do real work.
        IconBaker.NearestSize(masks, 19.5f).Size.ShouldBe(20);
        IconBaker.NearestSize(masks, 13f).Size.ShouldBe(13);
        IconBaker.NearestSize(masks, 100f).Size.ShouldBe(39);
    }

    [Fact]
    public void MoreLevelsMeanMoreDistinctCoverageValues()
    {
        var font = TextFontPath;
        Assert.SkipWhen(font is null, "No system font on this host.");

        using var rasterizer = new ManagedFontRasterizer();
        var one = IconBaker.Bake(rasterizer, font!, new Rune('e'), 26, alphaLevels: 1);
        var four = IconBaker.Bake(rasterizer, font!, new Rune('e'), 26, alphaLevels: 4);

        // One level is a hard threshold, which is why it is not the default: it reads chunky against
        // antialiased text beside it.
        one.Runs.Select(r => r.Alpha).Distinct().Count().ShouldBe(1);
        four.Runs.Select(r => r.Alpha).Distinct().Count().ShouldBeGreaterThan(1);
    }
}
