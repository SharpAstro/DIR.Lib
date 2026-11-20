using System.Collections.Immutable;
using System.Diagnostics;
using DIR.Lib;
using SharpAstro.Svg;
using SharpAstro.VectorGraphics;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// SVG in DIR.Lib: a drawing baked to the same coverage runs a glyph is, and held ready so that nothing is read or
/// rasterised while drawing.
/// </summary>
public sealed class VectorIconTests
{
    private static string Svg(string body, string box = "0 0 24 24") =>
        $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{box}\">{body}</svg>";

    /// <summary>The mask as rows of alpha, <c>size</c> x <c>size</c>.</summary>
    private static byte[,] Paint(IconBaker.CoverageMask mask)
    {
        var grid = new byte[mask.Size, mask.Size];
        foreach (IconBaker.CoverageRun run in mask.Runs)
        {
            for (int x = run.X; x < run.X + run.Width; x++) grid[run.Y, x] = run.Alpha;
        }
        return grid;
    }

    [Fact]
    public void ADrawingBakesToTheSameRunsAGlyphDoes_OneARowWhereTheRowIsSolid()
    {
        // The middle half of the box, on pixel edges at 24 px.
        IconBaker.CoverageMask mask = IconBaker.Bake(SvgReader.Parse(Svg("<rect x=\"6\" y=\"6\" width=\"12\" height=\"12\"/>")).Scene, 24);
        mask.Size.ShouldBe(24);
        mask.Runs.Length.ShouldBe(12);
        mask.Runs.ShouldAllBe(run => run.X == 6 && run.Width == 12 && run.Alpha == 255);
        mask.Runs.Select(run => (int)run.Y).ShouldBe(Enumerable.Range(6, 12));
    }

    [Fact]
    public void TheDrawingsOwnBoxIsKept_SoASetOfIconsLinesUp()
    {
        // The same small square in the top-left of a 24-unit box: it stays in the top-left, where a glyph's ink would
        // have been centred.
        IconBaker.CoverageMask mask = IconBaker.Bake(SvgReader.Parse(Svg("<rect x=\"2\" y=\"2\" width=\"4\" height=\"4\"/>")).Scene, 48);
        byte[,] grid = Paint(mask);
        grid[4, 4].ShouldBe((byte)255);
        grid[11, 11].ShouldBe((byte)255);
        grid[12, 12].ShouldBe((byte)0);
        grid[3, 3].ShouldBe((byte)0);
    }

    [Fact]
    public void ADrawingThatIsNotSquare_IsFittedInsideTheSquareAndCentred()
    {
        // 40 x 20, filled: at 20 px it is 20 x 10, with 5 empty rows above and below.
        IconBaker.CoverageMask mask = IconBaker.Bake(SvgReader.Parse(Svg("<rect width=\"40\" height=\"20\"/>", "0 0 40 20")).Scene, 20);
        mask.Runs.Length.ShouldBe(10);
        mask.Runs.ShouldAllBe(run => run.X == 0 && run.Width == 20 && run.Y >= 5 && run.Y < 15);
    }

    [Fact]
    public void ColourIsIgnored_OnlyCoverageIsBaked_AndEdgesAreBucketed()
    {
        VectorScene scene = SvgReader.Parse(Svg(
            "<rect width=\"12\" height=\"24\" fill=\"#ff0000\"/><rect x=\"12\" width=\"12\" height=\"24\" fill=\"#0000ff\" fill-opacity=\"0.5\"/>" +
            "<circle cx=\"12\" cy=\"12\" r=\"0\"/>")).Scene;
        byte[,] grid = Paint(IconBaker.Bake(scene, 24));
        grid[10, 3].ShouldBe((byte)255);
        // Half coverage lands in the third of four buckets: 128 * 4 / 256 + 1 = 3, drawn at 3/4.
        grid[10, 20].ShouldBe((byte)191);

        // One level is a hard threshold: anything covered at all is fully on.
        Paint(IconBaker.Bake(scene, 24, alphaLevels: 1))[10, 20].ShouldBe((byte)255);
    }

    [Fact]
    public void AStrokedOutlineIcon_BakesToItsStroke()
    {
        // A ring: ink on the circle, none in the middle or the corners.
        VectorScene scene = SvgReader.Parse(Svg("<circle cx=\"12\" cy=\"12\" r=\"8\" fill=\"none\" stroke=\"#000\" stroke-width=\"2\"/>")).Scene;
        byte[,] grid = Paint(IconBaker.Bake(scene, 48));
        grid[24, 8].ShouldBe((byte)255);     // on the ring, left
        grid[8, 24].ShouldBe((byte)255);     // on the ring, top
        grid[24, 24].ShouldBe((byte)0);      // the middle
        grid[2, 2].ShouldBe((byte)0);
    }

    [Fact]
    public void SeveralSizesAreBakedSeparately_AndNothingDrawnBakesEmpty()
    {
        VectorScene scene = SvgReader.Parse(Svg("<rect x=\"6\" y=\"6\" width=\"12\" height=\"12\"/>")).Scene;
        ImmutableArray<IconBaker.CoverageMask> masks = IconBaker.Bake(scene, [13, 16, 20, 26]);
        masks.Select(mask => mask.Size).ShouldBe([13, 16, 20, 26]);
        masks.ShouldAllBe(mask => !mask.IsEmpty);
        IconBaker.NearestSize(masks, 17.2f).Size.ShouldBe(16);

        IconBaker.Bake(SvgReader.Parse(Svg("")).Scene, 16).IsEmpty.ShouldBeTrue();
        IconBaker.Bake(new VectorScene(0, 0, []), 16).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void ASizeARunCannotHold_IsRefused()
    {
        VectorScene scene = SvgReader.Parse(Svg("<rect width=\"24\" height=\"24\"/>")).Scene;
        IconBaker.Bake(scene, IconBaker.MaxSceneSize).Runs.Length.ShouldBe(255);
        Should.Throw<ArgumentOutOfRangeException>(() => IconBaker.Bake(scene, 256));
        Should.Throw<ArgumentOutOfRangeException>(() => IconBaker.Bake(scene, 0));
        Should.Throw<ArgumentOutOfRangeException>(() => IconBaker.Bake(scene, 16, alphaLevels: 9));
        Should.Throw<ArgumentNullException>(() => IconBaker.Bake((VectorScene)null!, 16));
    }

    // ---- VectorIcon: read once, rasterise once a size ---------------------------------------------------------------

    private const string Clock =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\">" +
        "<path d=\"M12 2a10 10 0 1 0 10 10A10 10 0 0 0 12 2z\"/><path d=\"M12 6v6l4 2\"/></svg>";

    [Fact]
    public void AnIconIsReadOnce_AndEachSizeIsRasterisedOnce()
    {
        VectorIcon icon = VectorIcon.FromSvg(Clock);
        (icon.Width, icon.Height).ShouldBe((24f, 24f));
        icon.Issues.ShouldBeEmpty();

        IconBaker.CoverageMask first = icon.Mask(20);
        first.IsEmpty.ShouldBeFalse();
        // The same runs object, not an equal one: nothing was rasterised the second time.
        icon.Mask(20).Runs.ShouldBe(first.Runs);
        ImmutableArray<IconBaker.CoverageRun> again = icon.Mask(20).Runs;
        System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsArray(again)
            .ShouldBeSameAs(System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsArray(first.Runs));
        // Another size, and other levels, are their own bakes.
        icon.Mask(40).Size.ShouldBe(40);
        icon.Mask(20, alphaLevels: 1).Runs.ShouldAllBe(run => run.Alpha == 255);

        RgbaImage image = icon.Image(48, 48);
        (image.Width, image.Height).ShouldBe((48, 48));
        icon.Image(48, 48).ShouldBeSameAs(image);
    }

    [Fact]
    public void DrawingFromAnIcon_DoesNotReadOrRasteriseAgain()
    {
        VectorIcon icon = VectorIcon.FromSvg(Clock);
        icon.Mask(20);
        icon.Image(40, 40);

        // What a frame does: ask for the mask and the image it already has. Ten thousand frames of it, after a few to
        // let the runtime settle; were either rasterised again, this would be megabytes.
        // The bar is 64 KB, between what the runtime itself allocates on this thread while the loop runs (a kilobyte or
        // two when it recompiles the loop under it: 1,688 bytes failed a bar of 1,024 on main after #291, on a tree
        // that had just passed) and what it guards against: the 40 px image alone is 6,400 bytes, so rasterising it
        // again once in a thousand frames is about the whole bar, and a 24-byte key allocated per call is 480 KB.
        long runs = 0;
        for (int frame = 0; frame < 100; frame++) runs += icon.Mask(20).Runs.Length + icon.Image(40, 40).Width;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < 10_000; frame++)
        {
            runs += icon.Mask(20).Runs.Length;
            runs += icon.Image(40, 40).Width;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        runs.ShouldBeGreaterThan(0);
        allocated.ShouldBeLessThan(64 * 1024);
    }

    [Fact]
    public void TheImage_IsTheDrawingInItsOwnColours()
    {
        VectorIcon icon = VectorIcon.FromSvg(Svg(
            "<rect width=\"12\" height=\"24\" fill=\"#c06030\"/><rect x=\"12\" width=\"12\" height=\"24\" fill=\"#0000ff\" fill-opacity=\"0.5\"/>"));
        RgbaImage image = icon.Image(24, 24);
        image.Pixels.AsSpan((10 * 24 + 3) * 4, 4).ToArray().ShouldBe([0xc0, 0x60, 0x30, 255]);
        image.Pixels.AsSpan((10 * 24 + 20) * 4, 4).ToArray().ShouldBe([0, 0, 255, 128]);

        // Fitted inside a box, proportions kept.
        VectorIcon wide = VectorIcon.FromSvg(Svg("<rect width=\"40\" height=\"20\"/>", "0 0 40 20"));
        RgbaImage fitted = wide.ImageFitting(100, 100);
        (fitted.Width, fitted.Height).ShouldBe((100, 50));
        (wide.ImageFitting(30, 100).Width, wide.ImageFitting(30, 100).Height).ShouldBe((30, 15));
    }

    [Fact]
    public void WhatTheReaderLeftOut_IsOnTheIcon_AndABadFileIsRefused()
    {
        VectorIcon icon = VectorIcon.FromSvg(Svg("<rect width=\"24\" height=\"24\"/><switch><g/></switch>"));
        icon.Issues.ShouldHaveSingleItem().What.ShouldBe("<switch> element");
        icon.Mask(16).IsEmpty.ShouldBeFalse();

        Should.Throw<InvalidDataException>(() => VectorIcon.FromSvg("<html/>"));
        Should.Throw<InvalidDataException>(() => VectorIcon.FromSvg("not xml"u8.ToArray()));
        new VectorIcon(new VectorScene(10, 10, [])).Issues.ShouldBeEmpty();
    }

    [Fact]
    public void AnIconLoadsFromASceneFileOrFromSvg_AndTheTwoDrawTheSame()
    {
        VectorIcon fromSvg = VectorIcon.Load(System.Text.Encoding.UTF8.GetBytes(Clock));
        byte[] sceneFile = fromSvg.ToSceneFile();
        VectorSceneFile.IsSceneFile(sceneFile).ShouldBeTrue();

        VectorIcon fromScene = VectorIcon.Load(sceneFile);
        (fromScene.Width, fromScene.Height).ShouldBe((24f, 24f));
        fromScene.Issues.ShouldBeEmpty();
        fromScene.Mask(20).Runs.ShouldBe(fromSvg.Mask(20).Runs);
        fromScene.Image(48, 48).Pixels.ShouldBe(fromSvg.Image(48, 48).Pixels);

        // A damaged scene file is refused the same way a bad SVG is.
        Should.Throw<InvalidDataException>(() => VectorIcon.Load(sceneFile[..(sceneFile.Length / 2)]));
        Should.Throw<InvalidDataException>(() => VectorIcon.Load("neither"u8.ToArray()));
    }

    [Fact]
    public void OnlyAHandfulOfSizesIsKept()
    {
        VectorIcon icon = VectorIcon.FromSvg(Clock);
        IconBaker.CoverageMask first = icon.Mask(8);
        for (int size = 9; size < 60; size++) icon.Mask(size);
        // The first size has been dropped and is baked afresh: equal runs, a different array.
        IconBaker.CoverageMask again = icon.Mask(8);
        again.Runs.ShouldBe(first.Runs);
        System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsArray(again.Runs)
            .ShouldNotBeSameAs(System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsArray(first.Runs));
    }

    [Fact]
    public void ReadingAndBakingAnIcon_IsCheapEnoughToDoAtStartUp_NotInAFrame()
    {
        // Not a benchmark: a ceiling loose enough for any machine, that says which side of a frame this belongs on.
        VectorIcon.FromSvg(Clock).Mask(20);     // warm up
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++) VectorIcon.FromSvg(Clock).Mask(20);
        double each = watch.Elapsed.TotalMilliseconds / 100;
        TestContext.Current.TestOutputHelper?.WriteLine($"read + bake at 20 px: {each:0.000} ms");
        each.ShouldBeLessThan(20);
    }
}
