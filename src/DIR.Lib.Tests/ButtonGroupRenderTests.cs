using System;
using System.IO;
using DIR.Lib;
using DIR.Lib.Layout;
using SharpAstro.Png;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// Pixel baseline for a <see cref="Builder.ButtonGroup{T}"/> in the equal-to-widest mode
/// (<see cref="ButtonGroupStyle.EqualToWidest"/>): every segment as wide as the widest word plus padding, at Auto
/// width beside a Star spacer, as a consumer's header row places it. Baseline button_group_equal_to_widest.png.
/// </summary>
public class ButtonGroupRenderTests
{
    private enum Alert { Off, Errors, All }

    private static readonly bool UpdateBaselines = Environment.GetEnvironmentVariable("DIR_LIB_UPDATE_BASELINES") == "1";
    private static readonly string FontPath = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");
    private static readonly string BaselineDir = Path.Combine(AppContext.BaseDirectory, "Baselines");
    private static readonly string SourceBaselineDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Baselines");

    private static readonly UiPalette Palette = NotificationsRenderTests.Dark;

    private static ButtonGroupOption<Alert>[] Options() =>
    [
        new(Alert.Off, "Off"),
        new(Alert.Errors, "Errors and safety"),
        new(Alert.All, "All warnings"),
    ];

    private static ButtonGroupStyle StyleOf() => new(Palette.Accent, Palette.HeaderBg, Palette.ContentBg, Palette.BodyText, Palette.Separator)
    {
        EqualToWidest = true,
        SegmentPadding = 8f,
    };

    private static Node Row(Alert chosen, ButtonGroupStyle style)
        => Builder.HStack(
                Builder.Spacer().WFixed(12f),
                Builder.ButtonGroup<Alert>(Options(), chosen, _ => { }, style),
                Builder.Spacer().WStar())
            .HFixed(32f);

    private sealed class Widget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public Node Tree { get; set; } = Builder.Box(0, 0);

        public void Render(float width, float height)
        {
            BeginFrame();
            RenderLayout(Tree, new RectF32(0, 0, width, height), FontPath, DesignScale.One);
        }
    }

    private static RgbaImage Draw()
    {
        const int width = 480, height = 90;
        var renderer = new RgbaImageRenderer(width, height);
        renderer.Surface.Clear(Palette.ContentBg);
        var widget = new Widget(renderer) { FontPath = FontPath };
        widget.Tree = Builder.VStack(
            Builder.Spacer().HFixed(8f),
            Row(Alert.Off, StyleOf()),
            Builder.Spacer().HFixed(8f),
            Row(Alert.Errors, StyleOf() with { InsetFraction = 0.7f, CornerRadius = 4f })
        ).Stretch();
        widget.Render(width, height);
        return renderer.Surface;
    }

    [Fact]
    public void Equal_to_widest() => CompareBaseline(Draw(), "button_group_equal_to_widest.png");

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
        for (var i = 0; i < baseline.Pixels.Length; i++)
        {
            maxDiff = Math.Max(maxDiff, Math.Abs(img.Pixels[i] - baseline.Pixels[i]));
        }

        if (maxDiff > 2)
        {
            var actualPath = Path.ChangeExtension(baselinePath, ".actual.png");
            PngWriter.Save(actualPath, img.Pixels, img.Width, img.Height);
            Assert.Fail($"Baseline mismatch for '{name}': max diff={maxDiff}. Actual saved to '{actualPath}'.");
        }
    }
}
