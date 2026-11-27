using System;
using System.Collections.Generic;
using System.IO;
using DIR.Lib.Layout;
using SharpAstro.Png;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="Content.Text.Weight"/> and <see cref="Content.Text.Face"/>, in memory through <see cref="RgbaImageRenderer"/>.
/// Fixtures: DejaVuSans is the regular face, DejaVuSansMono the monospace one (every glyph one advance wide, so a test can
/// tell it from a proportional face by widths alone), and STIX2Math.otf stands in for the bold face (the repo keeps no true bold
/// fixture, and LithosBold_subset.cff draws nothing for ASCII; a test of routing needs only a face that is visibly another one). Baseline:
/// <c>text_faces.png</c>; <c>DIR_LIB_UPDATE_BASELINES=1</c> with a filter on this class writes it.
/// </summary>
public class TextFaceTests
{
    private static readonly bool UpdateBaselines = Environment.GetEnvironmentVariable("DIR_LIB_UPDATE_BASELINES") == "1";
    private static readonly string Regular = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");
    private static readonly string Mono = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSansMono.ttf");
    private static readonly string Bold = Path.Combine(AppContext.BaseDirectory, "Fonts", "STIX2Math.otf");
    private static readonly string BaselineDir = Path.Combine(AppContext.BaseDirectory, "Baselines");
    private static readonly string SourceBaselineDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Baselines");

    private static readonly RGBAColor32 Ink = new(0x20, 0x20, 0x20, 0xff);
    private static readonly RGBAColor32 Paper = new(0xff, 0xff, 0xff, 0xff);

    private static PixelMeasureContext<RgbaImage> Context(bool withFaces) => new(new RgbaImageRenderer(200, 60), Regular)
    {
        BoldFontPath = withFaces ? Bold : null,
        MonoFontPath = withFaces ? Mono : null,
    };

    [Fact]
    public void ARunStatesNoFaceByDefault()
    {
        var text = new Content.Text("x");
        text.Weight.ShouldBe(TextWeight.Regular);
        text.Face.ShouldBe(TextFace.Ui);
    }

    [Fact]
    public void FluentSettersSetTheRunAndIgnoreOtherNodes()
    {
        var run = Builder.Text("Heading").Bold().Mono();
        var content = ((Node.Leaf)run).Content.ShouldBeOfType<Content.Text>();
        content.Weight.ShouldBe(TextWeight.Bold);
        content.Face.ShouldBe(TextFace.Mono);
        ((Node.Leaf)run.Bold(false).Mono(false)).Content.ShouldBe(new Content.Text("Heading"));

        var stack = Builder.VStack(run);
        stack.Bold().ShouldBeSameAs(stack);
    }

    [Fact]
    public void WithoutFacesEveryRunMeasuresAsRegular()
    {
        var ctx = Context(withFaces: false);
        var regular = ctx.MeasureText("Hello world".AsSpan(), 14f);
        ctx.MeasureText("Hello world".AsSpan(), 14f, TextFace.Ui, TextWeight.Bold).ShouldBe(regular);
        ctx.MeasureText("Hello world".AsSpan(), 14f, TextFace.Mono, TextWeight.Regular).ShouldBe(regular);
        ctx.ResolveFontPath(TextFace.Mono, TextWeight.Bold).ShouldBe(Regular);
    }

    [Fact]
    public void AMonospaceRunIsMeasuredInTheMonospaceFace()
    {
        var ctx = Context(withFaces: true);
        ctx.ResolveFontPath(TextFace.Mono, TextWeight.Regular).ShouldBe(Mono);
        var narrow = ctx.MeasureText("iiii".AsSpan(), 14f, TextFace.Mono, TextWeight.Regular);
        var wide = ctx.MeasureText("WWWW".AsSpan(), 14f, TextFace.Mono, TextWeight.Regular);
        narrow.Width.ShouldBe(wide.Width, 0.5f);

        // The proportional face tells them apart, so the monospace one really was used.
        var proportionalNarrow = ctx.MeasureText("iiii".AsSpan(), 14f);
        var proportionalWide = ctx.MeasureText("WWWW".AsSpan(), 14f);
        proportionalNarrow.Width.ShouldBeLessThan(proportionalWide.Width - 1f);
    }

    [Fact]
    public void ABoldRunIsMeasuredInTheBoldFace()
    {
        var ctx = Context(withFaces: true);
        ctx.ResolveFontPath(TextFace.Ui, TextWeight.Bold).ShouldBe(Bold);
        ctx.ResolveFontPath(TextFace.Mono, TextWeight.Bold).ShouldBe(Mono, "no bold mono face given: the mono face");
        ctx.MeasureText("0123".AsSpan(), 14f, TextFace.Ui, TextWeight.Bold)
            .Width.ShouldNotBe(ctx.MeasureText("0123".AsSpan(), 14f).Width);
    }

    [Fact]
    public void TheEngineMeasuresALeafInItsOwnFace()
    {
        var ctx = Context(withFaces: true);
        var available = new Size<float>(400f, 40f);
        var plain = Engine.Measure(Builder.Text("iiii iiii").WAuto(), available, ctx).Width;
        var mono = Engine.Measure(Builder.Text("iiii iiii").Mono().WAuto(), available, ctx).Width;
        mono.ShouldBeGreaterThan(plain + 1f);
    }

    [Fact]
    public void WithoutFacesABoldRunPaintsExactlyAsARegularOne()
    {
        var plain = Draw(Builder.Text("Section label", 16f, Ink), withFaces: false);
        var bold = Draw(Builder.Text("Section label", 16f, Ink).Bold().Mono(), withFaces: false);
        bold.Pixels.AsSpan().SequenceEqual(plain.Pixels).ShouldBeTrue();
    }

    [Fact]
    public void WeightChangesWhatIsDamaged()
    {
        var ctx = Context(withFaces: true);
        var bounds = new Rect<float>(0, 0, 200, 30);
        var before = Engine.Arrange(Builder.Text("0123", 14f, Ink).WAuto(), bounds, ctx);
        var after = Engine.Arrange(Builder.Text("0123", 14f, Ink).WAuto().Bold(), bounds, ctx);
        var damage = new List<Rect<float>>();
        LayoutDamage.Compute(before, after, null, null, null, damage);
        damage.ShouldNotBeEmpty();
    }

    [Fact]
    public void RegularBoldAndMonospaceBaseline()
    {
        var tree = Builder.VStack(
                Builder.Text("Regular heading", 18f, Ink),
                Builder.Text("Bold ABCabc 0123 .,-", 18f, Ink).Bold(),
                Builder.Text("$ dotnet test --filter iiWW", 14f, Ink).Mono(),
                Builder.Text("iiiiiiiiii", 14f, Ink).Mono(),
                Builder.Text("WWWWWWWWWW", 14f, Ink).Mono())
            .WithGap(6f).Pad(12f);
        CompareBaseline(Draw(tree, withFaces: true, 280, 170), "text_faces.png");
    }

    private sealed class Widget(Renderer<RgbaImage> renderer, uint w, uint h) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public void Render(Node root) { BeginFrame(); RenderLayout(root, new RectF32(0, 0, w, h), Regular, DesignScale.One); }
    }

    private static RgbaImage Draw(Node tree, bool withFaces, uint w = 200, uint h = 60)
    {
        var renderer = new RgbaImageRenderer(w, h);
        renderer.Surface.Clear(Paper);
        var widget = new Widget(renderer, w, h) { FontPath = Regular };
        if (withFaces)
        {
            widget.Ui.BoldFontPath = Bold;
            widget.Ui.MonoFontPath = Mono;
        }
        widget.Render(tree);
        return renderer.Surface;
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
