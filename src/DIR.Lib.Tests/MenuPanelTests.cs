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
                widest = Math.Max(widest, ("▶  " + item).Length * f * 0.5f);
            var inner = plan.CardWidth - 2f * Style.Border!.Width - 2f * Style.PadX * f;
            widest.ShouldBeLessThanOrEqualTo(inner + 0.01f);
        }
    }

    [Fact]
    public void FontIsRelativeToTheCardNotTheSurfaceHeight()
    {
        var tall = MenuLayout.PlanPanel(Short, Style, Ctx, 1200, 800).FontSize;
        var taller = MenuLayout.PlanPanel(Short, Style, Ctx, 1200, 4000).FontSize;
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
        public Fixture()
        {
            Menu = new PixelMenuWidget<RgbaImage>(new DeclarationStubRenderer(1200, 800), "stub.ttf");
            Menu.Reset("Title", "Pick", ["a", "b", "c"]);
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
}
