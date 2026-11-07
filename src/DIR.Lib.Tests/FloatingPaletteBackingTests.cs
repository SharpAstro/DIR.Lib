using System.Linq;
using DIR.Lib;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="PaletteColors.Backing"/>: an opaque fill under the whole panel, not faded, so content beneath
/// never shows through the grip or the rows. Rendered through <see cref="RgbaImageRenderer"/> with text drawing
/// stubbed out, since the claim is about fills.
/// </summary>
public class FloatingPaletteBackingTests
{
    private static readonly RGBAColor32 Surface = new(0xF0, 0xF0, 0xF0, 0xFF);
    private static readonly RGBAColor32 BackingColor = new(0x10, 0x10, 0x18, 0xFF);

    private sealed class StubRenderer(uint w, uint h) : RgbaImageRenderer(w, h)
    {
        public override (float Width, float Height) MeasureText(ReadOnlySpan<char> text, string fontFamily, float fontSize)
            => (text.Length * fontSize * 0.5f, fontSize);

        public override void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize,
            RGBAColor32 fontColor, in RectInt layout, TextAlign horizAlign = TextAlign.Near,
            TextAlign vertAlign = TextAlign.Center)
        { }
    }

    private sealed class Pane(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public void Paint(Layout.Node root, RectF32 bounds)
        {
            BeginFrame();
            RenderLayout(root, bounds, fontPath: "stub.ttf", scale: DesignScale.One);
        }
    }

    private static PaletteColors Colors(RGBAColor32? backing) => new(
        PanelBg: new(0x14, 0x14, 0x1C, 0x9C),
        GripBg: new(0x2A, 0x2A, 0x36, 0xB4),
        TitleInk: new(0xE8, 0xDC, 0xB4, 0xFF),
        CountInk: new(0xFF, 0xEE, 0x60, 0xFF),
        RowOnBg: new(0x37, 0x48, 0x5C, 0xDC),
        RowOffBg: new(0x20, 0x20, 0x2A, 0xA0),
        RowHoverBg: new(0x44, 0x56, 0x6A, 0xE6),
        KeyChipBg: new(0x2C, 0x2C, 0x36, 0xC8),
        OnInk: new(0xE8, 0xE8, 0xF0, 0xFF),
        OffInk: new(0x9A, 0x9A, 0xA6, 0xFF),
        DisabledInk: new(0x5A, 0x5A, 0x64, 0xFF))
    { Backing = backing };

    private static readonly PaletteItem[] Items =
        [.. Enumerable.Range(0, 3).Select(i => new PaletteItem($"Row {i}", $"row:{i}", IsOn: i == 0, KeyHint: null, IsAvailable: true))];

    /// <summary>Paints an idle palette over <see cref="Surface"/>; returns the image.</summary>
    private static RgbaImage Render(RGBAColor32? backing)
    {
        var state = new FloatingPaletteState { OffsetAlong = 20f };
        state.AgeEngagement(60f);
        state.Fade.ShouldBe(FloatingPalette.IdleAlpha);

        var colors = Colors(backing);
        var tree = FloatingPalette.Build(state, "LAYERS", Items, in colors, 12f, "grip", _ => { }, () => { },
            panelWidth: 124f, side: Layout.DockSide.Left);

        using var renderer = new StubRenderer(300, 200);
        renderer.Surface.Clear(Surface);
        var pane = new Pane(renderer) { FontPath = "stub.ttf" };
        pane.Paint(tree, new RectF32(0, 0, 300, 200));

        return renderer.Surface;
    }

    private static bool IsSurface(RgbaImage img, int x, int y)
    {
        var o = (y * img.Width + x) * 4;
        return img.Pixels[o] == Surface.Red && img.Pixels[o + 1] == Surface.Green && img.Pixels[o + 2] == Surface.Blue;
    }

    private static (int X0, int Y0, int X1, int Y1) Bounds(RgbaImage img)
    {
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        for (var y = 0; y < img.Height; y++)
        {
            for (var x = 0; x < img.Width; x++)
            {
                if (!IsSurface(img, x, y))
                {
                    x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
                }
            }
        }

        return (x0, y0, x1, y1);
    }

    [Fact]
    public void Backing_hides_the_surface_under_an_idle_panel()
    {
        var backed = Render(BackingColor);
        var (x0, y0, x1, y1) = Bounds(backed);
        (x1 - x0).ShouldBeGreaterThan(100);

        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                IsSurface(backed, x, y).ShouldBeFalse($"surface colour at {x},{y}");
                // Dark backing under faded dark fills: nothing near the light surface.
                backed.Pixels[(y * backed.Width + x) * 4].ShouldBeLessThan((byte)0x80, $"light pixel at {x},{y}");
            }
        }
    }

    [Fact]
    public void Without_backing_the_surface_shows_through()
    {
        var backed = Render(BackingColor);
        var (x0, y0, x1, y1) = Bounds(backed);
        var plain = Render(null);

        // The panel padding, where only the faded PanelBg lies over the surface.
        var o = ((y0 + 1) * plain.Width + x0 + 1) * 4;
        plain.Pixels[o].ShouldBeGreaterThan((byte)0x80);
        Bounds(plain).ShouldBe((x0, y0, x1, y1));
    }
}
