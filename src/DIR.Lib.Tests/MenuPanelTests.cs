using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>The menu panel: a modal card sized to its content, centred over a scrim, with the menu's own rows.</summary>
public class MenuPanelTests
{
    /// <summary>Half the font size per character, as <see cref="DeclarationStubRenderer"/> measures.</summary>
    private sealed class HalfEmCtx : Layout.IMeasureContext<float>
    {
        public Layout.Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length * fontSize * 0.5f, fontSize);
        public float ToSurface(float designUnits) => designUnits;
    }

    private static readonly HalfEmCtx Ctx = new();
    private static readonly MenuPanelStyle Style = new();

    private static MenuModel Model(string title, string prompt, params string[] items)
    {
        var m = new MenuModel();
        m.Reset(title, prompt, [.. items]);
        return m;
    }

    private static readonly MenuModel Short = Model("Hi", "Pick", "a", "b");
    private static readonly MenuModel Medium = Model("Choose a telescope", "Which one?", "Refractor 80mm", "Newtonian 200mm", "Cassegrain 250mm");
    private static readonly MenuModel Long = Model("A very long title that keeps going and going", "A prompt that is also quite long indeed",
        "An item whose label is long enough to need the font to shrink right down", "b", "c");

    private static (Rect<float> Card, Layout.Node Tree) CardIn(MenuModel m, float w, float h)
    {
        var tree = MenuLayout.BuildPanel(m, new MenuColors(), Style, Ctx, w, h);
        var arranged = Layout.Engine.Arrange(tree, new Rect<float>(0, 0, w, h), Ctx);
        var card = arranged.First(a => a.Node.BorderStyle is not null);
        return (card.Bounds, tree);
    }

    [Fact]
    public void ShortContentClampsUpToMinWidthAndCentres()
    {
        var (card, _) = CardIn(Short, 1200, 800);
        card.Width.ShouldBe(Style.MinWidth);
        (card.X + card.Width / 2f).ShouldBe(600f, 0.5f);
        (card.Y + card.Height / 2f).ShouldBe(400f, 0.5f);
    }

    [Fact]
    public void LongContentClampsDownToMaxWidth()
    {
        var (card, _) = CardIn(Long, 1200, 800);
        card.Width.ShouldBe(Style.MaxWidth);
        (card.X + card.Width / 2f).ShouldBe(600f, 0.5f);
    }

    [Fact]
    public void MediumContentSitsBetweenMinAndMax()
    {
        var (card, _) = CardIn(Medium, 1200, 800);
        card.Width.ShouldBeGreaterThanOrEqualTo(Style.MinWidth);
        card.Width.ShouldBeLessThanOrEqualTo(Style.MaxWidth);
    }

    [Fact]
    public void CardGrowsWithContentHeight()
    {
        CardIn(Medium, 1200, 800).Card.Height.ShouldBeGreaterThan(CardIn(Short, 1200, 800).Card.Height);
    }

    [Theory]
    [InlineData(400f, 800f)]
    [InlineData(360f, 900f)]
    public void PortraitIsFullWidthWithMargins(float w, float h)
    {
        foreach (var m in new[] { Short, Medium, Long })
        {
            var (card, _) = CardIn(m, w, h);
            card.X.ShouldBe(Style.SideMargin, 0.01f);
            card.Width.ShouldBe(w - 2f * Style.SideMargin, 0.01f);
            MenuLayout.PlanPanel(m, Style, Ctx, w, h).Portrait.ShouldBeTrue();
        }
    }

    [Fact]
    public void LandscapeIsNotPortrait()
        => MenuLayout.PlanPanel(Short, Style, Ctx, 1200, 800).Portrait.ShouldBeFalse();

    [Theory]
    [InlineData(1200f, 800f)]
    [InlineData(400f, 800f)]
    [InlineData(250f, 700f)]
    public void WidestLineFitsTheCard(float w, float h)
    {
        foreach (var m in new[] { Short, Medium, Long })
        {
            var plan = MenuLayout.PlanPanel(m, Style, Ctx, w, h);
            var f = plan.FontSize;
            var widest = Math.Max(m.Title.Length * f * 1.6f * 0.5f, m.Prompt.Length * f * 0.5f);
            foreach (var item in m.Items)
                widest = Math.Max(widest, (MenuLayout.SelectedPrefix + item).Length * f * 0.5f);
            var inner = plan.CardWidth - 2f * Style.Border!.Width - 2f * Style.PadX * f;
            widest.ShouldBeLessThanOrEqualTo(inner + 0.01f);
        }
    }

    [Fact]
    public void FontIsRelativeToTheCardNotTheSurfaceHeight()
    {
        var tall = MenuLayout.PlanPanel(Short, Style, Ctx, 1200, 800).FontSize;
        var taller = MenuLayout.PlanPanel(Short, Style, Ctx, 1200, 1150).FontSize; // still landscape: only the height differs
        taller.ShouldBe(tall, 0.001f);
    }

    [Fact]
    public void BorderIsDeclaredOnTheCard()
    {
        var (_, tree) = CardIn(Medium, 1200, 800);
        var top = ((Layout.Node.Overlay)tree).Top;
        var card = ((Layout.Node.Anchored)top).Child;
        card.BorderStyle.ShouldBe(Style.Border);
    }

    [Fact]
    public void PanelIsAModalOverlay()
    {
        var (_, tree) = CardIn(Short, 1200, 800);
        ((Layout.Node.Overlay)tree).Modal.ShouldBeTrue();
    }

    private sealed class Fixture
    {
        public Fixture(Action? back = null, Action? close = null)
        {
            Menu = new PixelMenuWidget<RgbaImage>(new DeclarationStubRenderer(1200, 800), "stub.ttf");
            Menu.Reset("Title", "Pick", ["a", "b", "c"]);
            Menu.Back = back;
            Menu.Close = close;
            Router = new InputRouter(Menu.Ui, new BackgroundTaskTracker(), static () => { })
            {
                Widgets = () => [Menu],
            };
            Menu.RenderPanel();
        }

        public PixelMenuWidget<RgbaImage> Menu { get; }
        public InputRouter Router { get; }

        public bool Press(float x, float y)
        {
            var r = Router.Handle(new InputEvent.MouseDown(x, y, MouseButton.Left, InputModifier.None, 1));
            Menu.RenderPanel();
            return r;
        }
    }

    [Fact]
    public void PressOnARowConfirms()
    {
        var f = new Fixture();
        var regions = new List<ClickableRegion>();
        f.Menu.CollectPaintedRegions(regions);
        var row = regions.First(r => r.Result is HitResult.ListItemHit { Index: 2 });
        f.Press(row.X + 2, row.Y + 2);
        f.Menu.IsConfirmed.ShouldBeTrue();
        f.Menu.SelectedIndex.ShouldBe(2);
    }

    [Fact]
    public void PressOnTheScrimIsConsumedAndConfirmsNothing()
    {
        var f = new Fixture();
        f.Press(5, 5).ShouldBeTrue();
        f.Menu.IsConfirmed.ShouldBeFalse();
        f.Menu.SelectedIndex.ShouldBe(0);
    }

    [Fact]
    public void KeysReachTheModelWithNoUnhandled()
    {
        var f = new Fixture();
        f.Router.Handle(new InputEvent.KeyDown(InputKey.Down, InputModifier.None)).ShouldBeTrue();
        f.Menu.SelectedIndex.ShouldBe(1);
        f.Router.Handle(new InputEvent.KeyDown(InputKey.Enter, InputModifier.None)).ShouldBeTrue();
        f.Menu.IsConfirmed.ShouldBeTrue();
    }

    // DIR.Lib #246: Back top-left and close top-right, each present only when the host supplies its action.
    private static MenuModel WithActions(MenuModel src, Action? back, Action? close)
    {
        var m = Model(src.Title, src.Prompt, [.. src.Items]);
        m.Back = back;
        m.Close = close;
        return m;
    }

    private static Rect<float> ButtonBounds(MenuModel m, string id, float w, float h, out Rect<float> card)
    {
        var tree = MenuLayout.BuildPanel(m, new MenuColors(), Style, Ctx, w, h);
        var arranged = Layout.Engine.Arrange(tree, new Rect<float>(0, 0, w, h), Ctx);
        card = arranged.First(a => a.Node.BorderStyle is not null).Bounds;
        return arranged.First(a => a.Node.Hit is HitResult.ButtonHit { Action: var x } && x == id).Bounds;
    }

    private static bool HasButtons(MenuModel m)
    {
        var tree = MenuLayout.BuildPanel(m, new MenuColors(), Style, Ctx, 1200, 800);
        return Layout.Engine.Arrange(tree, new Rect<float>(0, 0, 1200, 800), Ctx).Any(a => a.Node.Hit is HitResult.ButtonHit);
    }

    [Fact]
    public void NoActionMeansNoHeaderAndTheSameCard()
    {
        HasButtons(Medium).ShouldBeFalse();
        var plain = MenuLayout.PlanPanel(Medium, Style, Ctx, 1200, 800);
        var again = MenuLayout.PlanPanel(WithActions(Medium, null, null), Style, Ctx, 1200, 800);
        again.ShouldBe(plain);
    }

    [Fact]
    public void BackIsInTheTopLeftQuadrant()
    {
        var r = ButtonBounds(WithActions(Medium, () => { }, null), "MenuBack", 1200, 800, out var card);
        r.X.ShouldBeLessThan(card.X + card.Width / 2f);
        r.Y.ShouldBeLessThan(card.Y + card.Height / 2f);
        r.X.ShouldBeGreaterThanOrEqualTo(card.X + Style.Border!.Width);
        HasButtons(WithActions(Medium, () => { }, null)).ShouldBeTrue();
    }

    [Fact]
    public void CloseIsInTheTopRightQuadrant()
    {
        var r = ButtonBounds(WithActions(Medium, null, () => { }), "MenuClose", 1200, 800, out var card);
        (r.X + r.Width / 2f).ShouldBeGreaterThan(card.X + card.Width / 2f);
        r.Y.ShouldBeLessThan(card.Y + card.Height / 2f);
        (r.X + r.Width).ShouldBeLessThanOrEqualTo(card.X + card.Width - Style.Border!.Width + 0.01f);
    }

    [Fact]
    public void BothControlsDoNotOverlap()
    {
        var m = WithActions(Medium, () => { }, () => { });
        var back = ButtonBounds(m, "MenuBack", 1200, 800, out _);
        var close = ButtonBounds(m, "MenuClose", 1200, 800, out _);
        (back.X + back.Width).ShouldBeLessThanOrEqualTo(close.X + 0.01f);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void HeaderGrowsTheCardByExactlyOneHeaderLine(bool back, bool close)
    {
        var plain = MenuLayout.PlanPanel(Medium, Style, Ctx, 1200, 800);
        var m = WithActions(Medium, back ? () => { } : null, close ? () => { } : null);
        var plan = MenuLayout.PlanPanel(m, Style, Ctx, 1200, 800);
        plan.FontSize.ShouldBe(plain.FontSize, 0.001f);
        (plan.CardHeight - plain.CardHeight).ShouldBe(2f * plan.FontSize, 0.01f);
    }

    [Fact]
    public void PortraitKeepsBothControlsInsideTheCard()
    {
        var m = WithActions(Long, () => { }, () => { });
        var back = ButtonBounds(m, "MenuBack", 360, 640, out var card);
        var close = ButtonBounds(m, "MenuClose", 360, 640, out _);
        back.X.ShouldBeGreaterThanOrEqualTo(card.X);
        (close.X + close.Width).ShouldBeLessThanOrEqualTo(card.X + card.Width + 0.01f);
        (back.X + back.Width).ShouldBeLessThanOrEqualTo(close.X + 0.01f);
    }

    [Fact]
    public void PressOnBackRunsItOnceAndConfirmsNothing()
    {
        var n = 0;
        var f = new Fixture(back: () => n++);
        var regions = new List<ClickableRegion>();
        f.Menu.CollectPaintedRegions(regions);
        var r = regions.First(x => x.Result is HitResult.ButtonHit { Action: "MenuBack" });
        f.Press(r.X + 2, r.Y + 2);
        n.ShouldBe(1);
        f.Menu.IsConfirmed.ShouldBeFalse();
    }

    [Fact]
    public void PressOnCloseRunsItOnceAndConfirmsNothing()
    {
        var n = 0;
        var f = new Fixture(close: () => n++);
        var regions = new List<ClickableRegion>();
        f.Menu.CollectPaintedRegions(regions);
        var r = regions.First(x => x.Result is HitResult.ButtonHit { Action: "MenuClose" });
        f.Press(r.X + 2, r.Y + 2);
        n.ShouldBe(1);
        f.Menu.IsConfirmed.ShouldBeFalse();
    }

    private sealed class PanelWidget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public void Render(Layout.Node root, RectF32 bounds, string fontPath)
        {
            BeginFrame();
            RenderLayout(root, bounds, fontPath, scale: DesignScale.One);
        }
    }

    // DIR.Lib #245: the card's fill followed its square bounds, so it showed outside the rounded border.
    [Fact]
    public void CardFillStaysInsideItsRoundedBorder()
    {
        const int w = 600, h = 400;
        var font = System.IO.Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");
        using var renderer = new RgbaImageRenderer(w, h);
        var backdrop = new RGBAColor32(200, 40, 40, 255);
        renderer.Surface.Clear(backdrop);
        var ctx = new PixelMeasureContext<RgbaImage>(renderer, font, 1f);
        var model = Model("Name", "Pick", "a", "b");
        var colors = new MenuColors();
        var tree = MenuLayout.BuildPanel(model, colors, Style, ctx, w, h);
        new PanelWidget(renderer).Render(tree, new RectF32(0, 0, w, h), font);
        var card = Layout.Engine.Arrange(tree, new Rect<float>(0, 0, w, h), ctx).First(a => a.Node.BorderStyle is not null).Bounds;

        RGBAColor32 Px(int x, int y)
        {
            var at = (y * w + x) * 4;
            return new RGBAColor32(renderer.Surface.Pixels[at], renderer.Surface.Pixels[at + 1], renderer.Surface.Pixels[at + 2], renderer.Surface.Pixels[at + 3]);
        }

        var scrimOverBackdrop = Px(2, 2);
        scrimOverBackdrop.ShouldNotBe(backdrop, "the scrim is painted");
        scrimOverBackdrop.ShouldNotBe(Style.CardBackground);
        Px((int)card.X + 1, (int)card.Y + 1).ShouldBe(scrimOverBackdrop, "outside the curve shows the scrim, not the card fill");
        Px((int)(card.X + card.Width) - 2, (int)(card.Y + card.Height) - 2).ShouldBe(scrimOverBackdrop);
        Px((int)(card.X + card.Width / 2f), (int)card.Y + 30).ShouldBe(Style.CardBackground, "well inside is the card fill");
    }

    // DIR.Lib #246: how the header controls look with a real font (DejaVuSans, the fixture for general UI text).
    // Each label's ink must sit inside its control and the card's inner rect, uncut, and read against the card fill
    // as well as the rows do. Set DIR_LIB_MENU_CAPTURES to write the four renders as PNGs under the temp dir.
    [Theory]
    [InlineData(true, 1200, 800)]
    [InlineData(false, 1200, 800)]
    [InlineData(true, 360, 640)]
    [InlineData(false, 360, 640)]
    public void HeaderControlLabelIsDrawnInsideTheCardWithContrast(bool isBack, int w, int h)
    {
        var font = System.IO.Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");
        using var renderer = new RgbaImageRenderer((uint)w, (uint)h);
        renderer.Surface.Clear(new RGBAColor32(20, 20, 20, 255));
        var ctx = new PixelMeasureContext<RgbaImage>(renderer, font, 1f);
        var model = WithActions(Medium, isBack ? () => { } : null, isBack ? null : () => { });
        var colors = new MenuColors();
        var tree = MenuLayout.BuildPanel(model, colors, Style, ctx, w, h);
        new PanelWidget(renderer).Render(tree, new RectF32(0, 0, w, h), font);

        var arranged = Layout.Engine.Arrange(tree, new Rect<float>(0, 0, w, h), ctx);
        var card = arranged.First(a => a.Node.BorderStyle is not null).Bounds;
        var id = isBack ? "MenuBack" : "MenuClose";
        var r = arranged.First(a => a.Node.Hit is HitResult.ButtonHit { Action: var x } && x == id).Bounds;

        var pixels = renderer.Surface.Pixels;
        var fill = Style.CardBackground;
        static int Dist(byte[] p, int at, RGBAColor32 c)
            => Math.Max(Math.Abs(p[at] - c.Red), Math.Max(Math.Abs(p[at + 1] - c.Green), Math.Abs(p[at + 2] - c.Blue)));

        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, peak = 0;
        for (var y = (int)MathF.Floor(r.Y); y < (int)MathF.Ceiling(r.Y + r.Height); y++)
            for (var x = (int)MathF.Floor(r.X); x < (int)MathF.Ceiling(r.X + r.Width); x++)
            {
                var d = Dist(pixels, (y * w + x) * 4, fill);
                if (d < 8) continue;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                peak = Math.Max(peak, d);
            }

        (maxX >= 0).ShouldBeTrue($"{id} draws a label");
        var border = Style.Border!.Width;
        minX.ShouldBeGreaterThan((int)MathF.Floor(card.X + border), $"{id} ink clear of the left border");
        minY.ShouldBeGreaterThan((int)MathF.Floor(card.Y + border), $"{id} ink clear of the top border");
        maxX.ShouldBeLessThan((int)MathF.Ceiling(card.X + card.Width - border), $"{id} ink clear of the right border");
        minX.ShouldBeGreaterThanOrEqualTo((int)MathF.Floor(r.X), "not cut on the left");
        maxX.ShouldBeLessThan((int)MathF.Ceiling(r.X + r.Width), "not cut on the right");
        minY.ShouldBeGreaterThanOrEqualTo((int)MathF.Floor(r.Y), "not cut on top");
        maxY.ShouldBeLessThan((int)MathF.Ceiling(r.Y + r.Height), "not cut below");
        // one middle: the ink is centred on the row's height, within a fraction of a line
        ((minY + maxY) / 2f).ShouldBe(r.Y + r.Height / 2f, r.Height * 0.25f);

        var rowContrast = Math.Max(Math.Abs(colors.ItemColor.Red - fill.Red),
            Math.Max(Math.Abs(colors.ItemColor.Green - fill.Green), Math.Abs(colors.ItemColor.Blue - fill.Blue)));
        peak.ShouldBeGreaterThanOrEqualTo((int)(rowContrast * 0.9f), "label reads against the card as the rows do");

        if (Environment.GetEnvironmentVariable("DIR_LIB_MENU_CAPTURES") is { Length: > 0 })
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dirlib-menu-captures");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"menu-{(isBack ? "back" : "close")}-{w}x{h}.png"),
                SharpAstro.Png.PngWriter.Encode(renderer.Surface.Pixels, renderer.Surface.Width, renderer.Surface.Height));
        }
    }

    // The plan measures at size 1 and scales; a renderer's glyph advances are not linear in size, so the row
    // drawn at the plan's font must be measured at that font (DIR.Lib #243, found in sebgod/chess#84).
    [Theory]
    [InlineData(1f, 1200, 800, "Type a name, then press Enter")]
    [InlineData(2f, 1200, 800, "Type a name, then press Enter")]
    [InlineData(1f, 1200, 800, "Type a name, then press Enter, then wait for the engine to answer politely and at length")]
    [InlineData(2f, 1200, 800, "Type a name, then press Enter, then wait for the engine to answer politely and at length")]
    [InlineData(1f, 360, 640, "Type a name, then press Enter")]
    [InlineData(1f, 1200, 800, "Type a name, then press Enter, then wait for the engine to answer politely and at length, and then some more and more")]
    public void EveryRowFitsTheCardAtTheDrawnFont(float scale, int w, int h, string item)
    {
        var font = System.IO.Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");
        using var renderer = new RgbaImageRenderer((uint)(w * scale), (uint)(h * scale));
        var ctx = new PixelMeasureContext<RgbaImage>(renderer, font, scale);
        var model = Model("Name", "Who plays white?", item, "b");
        var tree = MenuLayout.BuildPanel(model, new MenuColors(), Style, ctx, w, h);
        var plan = MenuLayout.PlanPanel(model, Style, ctx, w, h);
        var arranged = Layout.Engine.Arrange(tree, new Rect<float>(0, 0, w * scale, h * scale), ctx);
        var card = arranged.First(a => a.Node.BorderStyle is not null).Bounds;
        var border = Style.Border!.Width * scale;
        var inner = card.Width - 2f * border - 2f * Style.PadX * plan.FontSize * scale;
        var rows = new[]
        {
            (model.Title, plan.FontSize * 1.6f), (model.Prompt, plan.FontSize),
            (MenuLayout.SelectedPrefix + model.Items[0], plan.FontSize), (MenuLayout.UnselectedPrefix + model.Items[1], plan.FontSize),
        };
        foreach (var (text, size) in rows)
            ctx.MeasureText(text.AsSpan(), size).Width.ShouldBeLessThanOrEqualTo(inner + 0.01f, text);
    }
}
