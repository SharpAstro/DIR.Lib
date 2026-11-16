using DIR.Lib;
using SharpAstro.Png;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// An SVG all the way to pixels on a DIR.Lib surface, through the pieces an app uses: a widget's
/// <c>DrawCoverageMask</c> on a real renderer for the single-colour form, and the surface's own blit for the
/// colour form. <see cref="VectorIconTests"/> checks the masks and images themselves; this checks that what an app
/// would write actually puts them on screen, tinted and placed.
/// </summary>
public sealed class VectorIconRenderTests
{
    /// <summary>A toolbar-like widget: it draws icons the way a consumer's widget does, and nothing else.</summary>
    private sealed class IconWidget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public void Draw(VectorIcon icon, float x, float y, int size, RGBAColor32 ink)
            => DrawCoverageMask(icon.Mask(size), x, y, size, ink);
    }

    private const string Outline = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\">";

    private static readonly (string Name, string Svg)[] Toolbar =
    [
        ("clock", Outline + "<path d=\"M12 2a10 10 0 1 0 10 10A10 10 0 0 0 12 2z\"/><path d=\"M12 6v6l4 2\"/></svg>"),
        ("search", Outline + "<circle cx=\"11\" cy=\"11\" r=\"8\"/><path d=\"M21 21l-4.35-4.35\"/></svg>"),
        ("folder", Outline + "<path d=\"M3 7a2 2 0 0 1 2-2h4l2 3h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z\"/></svg>"),
        ("check", Outline + "<path d=\"M4 12l5 5L20 6\"/></svg>"),
        ("square", "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><rect x=\"4\" y=\"4\" width=\"16\" height=\"16\"/></svg>"),
    ];

    private static (byte R, byte G, byte B, byte A) At(RgbaImage image, int x, int y)
    {
        int i = (y * image.Width + x) * 4;
        return (image.Pixels[i], image.Pixels[i + 1], image.Pixels[i + 2], image.Pixels[i + 3]);
    }

    [Fact]
    public void AnSvgIcon_IsDrawnByAWidget_InTheInkItIsGiven_WhereItIsPut()
    {
        using var renderer = new RgbaImageRenderer(120, 60);
        var panel = new RGBAColor32(0x1e, 0x22, 0x2a, 255);
        renderer.FillRectangle(new RectInt(new PointInt(120, 60), new PointInt(0, 0)), panel);
        var widget = new IconWidget(renderer);
        VectorIcon square = VectorIcon.FromSvg(Toolbar[4].Svg);

        // The filled square covers 4..20 of 24 units: at 24 px, pixels 4..19, placed at (10, 10) and at (60, 20).
        widget.Draw(square, 10, 10, 24, new RGBAColor32(0xe6, 0xe9, 0xef, 255));
        widget.Draw(square, 60, 20, 24, new RGBAColor32(0xff, 0x40, 0x40, 255));

        RgbaImage surface = renderer.Surface;
        At(surface, 10 + 12, 10 + 12).ShouldBe(((byte)0xe6, (byte)0xe9, (byte)0xef, (byte)255));
        At(surface, 10 + 4, 10 + 4).ShouldBe(((byte)0xe6, (byte)0xe9, (byte)0xef, (byte)255));
        At(surface, 10 + 19, 10 + 19).ShouldBe(((byte)0xe6, (byte)0xe9, (byte)0xef, (byte)255));
        At(surface, 10 + 3, 10 + 12).ShouldBe((panel.Red, panel.Green, panel.Blue, (byte)255));
        At(surface, 10 + 20, 10 + 12).ShouldBe((panel.Red, panel.Green, panel.Blue, (byte)255));
        // The same icon in another ink, elsewhere: one mask, tinted at draw time.
        At(surface, 60 + 12, 20 + 12).ShouldBe(((byte)0xff, (byte)0x40, (byte)0x40, (byte)255));
        At(surface, 60 + 3, 20 + 12).ShouldBe((panel.Red, panel.Green, panel.Blue, (byte)255));
    }

    [Fact]
    public void ADimmedInk_DimsTheIcon_AsItDoesABakedGlyph()
    {
        using var renderer = new RgbaImageRenderer(40, 40);
        renderer.FillRectangle(new RectInt(new PointInt(40, 40), new PointInt(0, 0)), new RGBAColor32(0, 0, 0, 255));
        new IconWidget(renderer).Draw(VectorIcon.FromSvg(Toolbar[4].Svg), 8, 8, 24, new RGBAColor32(255, 255, 255, 128));
        (byte r, byte g, byte b, _) = At(renderer.Surface, 20, 20);
        ((int)r).ShouldBeInRange(126, 130);
        (g, b).ShouldBe((r, r));
    }

    [Fact]
    public void AColourSvg_IsBlittedOntoTheSurface_OverWhatIsThere()
    {
        VectorIcon icon = VectorIcon.FromSvg(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 20 20\"><rect width=\"10\" height=\"20\" fill=\"#c06030\"/><rect x=\"10\" width=\"10\" height=\"10\" fill=\"#0000ff\" fill-opacity=\"0.5\"/></svg>");
        using var renderer = new RgbaImageRenderer(60, 40);
        renderer.FillRectangle(new RectInt(new PointInt(60, 40), new PointInt(0, 0)), new RGBAColor32(255, 255, 255, 255));

        RgbaImage image = icon.Image(20, 20);
        renderer.Surface.BlitRgba(30, 10, image.Pixels, image.Width, image.Height);

        At(renderer.Surface, 30 + 5, 10 + 5).ShouldBe(((byte)0xc0, (byte)0x60, (byte)0x30, (byte)255));
        // Half-transparent blue over the white that was there.
        (byte r, byte g, byte b, _) = At(renderer.Surface, 30 + 15, 10 + 5);
        ((int)r).ShouldBeInRange(126, 129);
        ((int)g).ShouldBeInRange(126, 129);
        b.ShouldBe((byte)255);
        // Where the drawing is empty the surface is untouched.
        At(renderer.Surface, 30 + 15, 10 + 15).ShouldBe(((byte)255, (byte)255, (byte)255, (byte)255));
        At(renderer.Surface, 5, 5).ShouldBe(((byte)255, (byte)255, (byte)255, (byte)255));
    }

    /// <summary>
    /// A row of SVG icons drawn by a widget at three DPI scales, on a dark and a light panel, compared with a
    /// committed baseline. It is the capture a person looks at (set <c>DIR_LIB_UPDATE_BASELINES=1</c> to rewrite it),
    /// and the guard that what a toolbar would show does not change unnoticed.
    /// </summary>
    [Fact]
    public void AToolbarOfSvgIcons_MatchesItsBaseline()
    {
        int[] sizes = [16, 24, 39];
        const int gap = 10;
        int width = gap + Toolbar.Length * (sizes.Max() + gap), rowHeight = sizes.Max() + gap;
        using var renderer = new RgbaImageRenderer((uint)width, (uint)(rowHeight * sizes.Length * 2 + gap));
        var widget = new IconWidget(renderer);
        (RGBAColor32 Panel, RGBAColor32 Ink)[] themes =
        [
            (new RGBAColor32(0x1e, 0x22, 0x2a, 255), new RGBAColor32(0xe6, 0xe9, 0xef, 255)),
            (new RGBAColor32(0xf4, 0xf5, 0xf7, 255), new RGBAColor32(0x1f, 0x29, 0x37, 255)),
        ];
        VectorIcon[] icons = [.. Toolbar.Select(item => VectorIcon.FromSvg(item.Svg))];
        icons.ShouldAllBe(icon => icon.Issues.IsEmpty);

        int y = 0;
        foreach ((RGBAColor32 panel, RGBAColor32 ink) in themes)
        {
            int bandHeight = rowHeight * sizes.Length + gap / 2;
            renderer.FillRectangle(new RectInt(new PointInt(width, y + bandHeight), new PointInt(0, y)), panel);
            foreach (int size in sizes)
            {
                for (int i = 0; i < icons.Length; i++)
                {
                    widget.Draw(icons[i], gap + i * (sizes.Max() + gap), y + gap / 2 + (rowHeight - size) / 2, size, ink);
                }
                y += rowHeight;
            }
            y += gap / 2;
        }

        RgbaImage surface = renderer.Surface;
        string baseline = Path.Combine(AppContext.BaseDirectory, "Baselines", "svg_icon_toolbar.png");
        string source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Baselines", "svg_icon_toolbar.png"));
        if (Environment.GetEnvironmentVariable("DIR_LIB_UPDATE_BASELINES") == "1")
        {
            PngWriter.Save(source, surface.Pixels, surface.Width, surface.Height);
            PngWriter.Save(baseline, surface.Pixels, surface.Width, surface.Height);
            return;
        }

        File.Exists(baseline).ShouldBeTrue($"No baseline at {baseline}. Run once with DIR_LIB_UPDATE_BASELINES=1 and look at the picture.");
        PngImageDecoder.TryDecodeRgba8(File.ReadAllBytes(baseline), out byte[]? expected, out int w, out int h).ShouldBeTrue();
        (w, h).ShouldBe((surface.Width, surface.Height));
        int worst = 0;
        for (int i = 0; i < expected!.Length; i++) worst = Math.Max(worst, Math.Abs(expected[i] - surface.Pixels[i]));
        worst.ShouldBeLessThanOrEqualTo(2);
    }
}
