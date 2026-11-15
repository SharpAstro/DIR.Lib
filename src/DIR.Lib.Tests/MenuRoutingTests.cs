using System.Collections.Generic;
using System.Linq;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>A menu routes its own input: driven through <see cref="InputRouter"/> with Unhandled left null.</summary>
public class MenuRoutingTests
{
    private static readonly string[] Items = ["a", "b", "c"];

    // A real PixelMenuWidget on the router, as a host lists it.
    private sealed class Fixture
    {
        public Fixture(bool wireUnhandled = false)
        {
            Menu = new PixelMenuWidget<RgbaImage>(new DeclarationStubRenderer(300, 300), "stub.ttf");
            Menu.Reset("Title", "Pick", [.. Items]);
            Router = new InputRouter(Menu.Ui, new BackgroundTaskTracker(), static () => { })
            {
                Widgets = () => [Menu],
            };
            if (wireUnhandled)
            {
                Router.Unhandled = e => { UnhandledCalls++; return Menu.HandleInput(e); };
            }

            Paint();
        }

        public PixelMenuWidget<RgbaImage> Menu { get; }
        public InputRouter Router { get; }
        public int UnhandledCalls { get; private set; }

        public void Paint() => Menu.Render();

        public bool Key(InputKey key)
        {
            var r = Router.Handle(new InputEvent.KeyDown(key, InputModifier.None));
            Paint();
            return r;
        }
    }

    [Fact]
    public void DownAndUpMoveWithNoUnhandled()
    {
        var f = new Fixture();
        f.Key(InputKey.Down).ShouldBeTrue();
        f.Menu.SelectedIndex.ShouldBe(1);
        f.Key(InputKey.Up).ShouldBeTrue();
        f.Menu.SelectedIndex.ShouldBe(0);
        f.Menu.IsConfirmed.ShouldBeFalse();
    }

    [Fact]
    public void EnterConfirms()
    {
        var f = new Fixture();
        f.Key(InputKey.Down);
        f.Key(InputKey.Enter);
        f.Menu.IsConfirmed.ShouldBeTrue();
        f.Menu.SelectedIndex.ShouldBe(1);
    }

    [Fact]
    public void DigitConfirmsItsRow()
    {
        var f = new Fixture();
        f.Key(InputKey.D3);
        f.Menu.IsConfirmed.ShouldBeTrue();
        f.Menu.SelectedIndex.ShouldBe(2);
    }

    [Fact]
    public void DigitPastTheLastRowIsNotConsumed()
    {
        var f = new Fixture();
        f.Key(InputKey.D5).ShouldBeFalse();
        f.Menu.IsConfirmed.ShouldBeFalse();
    }

    [Fact]
    public void PressConfirmsARow()
    {
        var f = new Fixture();
        var regions = new List<ClickableRegion>();
        f.Menu.CollectPaintedRegions(regions);
        var row = regions.First(r => r.Result is HitResult.ListItemHit { Index: 1 });
        f.Router.Handle(new InputEvent.MouseDown(row.X + 2, row.Y + 2, MouseButton.Left, InputModifier.None, 1));
        f.Menu.IsConfirmed.ShouldBeTrue();
        f.Menu.SelectedIndex.ShouldBe(1);
    }

    [Fact]
    public void WiredUnhandledStepsOncePerKey()
    {
        var f = new Fixture(wireUnhandled: true);
        f.Key(InputKey.Down).ShouldBeTrue();
        f.Menu.SelectedIndex.ShouldBe(1);
        f.UnhandledCalls.ShouldBe(0);
        f.Key(InputKey.Down);
        f.Menu.SelectedIndex.ShouldBe(2);
    }

    [Fact]
    public void DirectHandleInputStillWorksWithoutARouter()
    {
        var w = new PixelMenuWidget<RgbaImage>(new DeclarationStubRenderer(300, 300), "stub.ttf");
        w.Reset("T", "P", ["a", "b"]);
        w.HandleInput(new InputEvent.KeyDown(InputKey.Down, InputModifier.None)).ShouldBeTrue();
        w.SelectedIndex.ShouldBe(1);
    }

    [Fact]
    public void KeysAndPressWorkInsideAModalTop()
    {
        var model = new MenuModel();
        model.Reset("T", "P", [.. Items]);
        var widget = new DeclarationWidget(new DeclarationStubRenderer(300, 300));
        var router = new InputRouter(widget.Ui, new BackgroundTaskTracker(), static () => { })
        {
            Widgets = () => [widget],
        };

        void Paint() => widget.Render(
            Layout.Builder.Modal(Layout.Builder.Spacer().Stretch(), MenuLayout.BuildTree(model, new MenuColors(), 16f)),
            new RectF32(0, 0, 300, 300));

        Paint();
        router.Handle(new InputEvent.KeyDown(InputKey.Down, InputModifier.None)).ShouldBeTrue();
        model.SelectedIndex.ShouldBe(1);
        Paint();
        router.Handle(new InputEvent.KeyDown(InputKey.Up, InputModifier.None)).ShouldBeTrue();
        model.SelectedIndex.ShouldBe(0);
        Paint();

        var regions = new List<ClickableRegion>();
        widget.CollectPaintedRegions(regions);
        var row = regions.First(r => r.Result is HitResult.ListItemHit { Index: 2 });
        router.Handle(new InputEvent.MouseDown(row.X + 2, row.Y + 2, MouseButton.Left, InputModifier.None, 1));
        model.IsConfirmed.ShouldBeTrue();
        model.SelectedIndex.ShouldBe(2);
    }

    [Fact]
    public void EnterConfirmsInsideAModalTop()
    {
        var model = new MenuModel();
        model.Reset("T", "P", [.. Items]);
        var widget = new DeclarationWidget(new DeclarationStubRenderer(300, 300));
        var router = new InputRouter(widget.Ui, new BackgroundTaskTracker(), static () => { })
        {
            Widgets = () => [widget],
        };
        widget.Render(
            Layout.Builder.Modal(Layout.Builder.Spacer().Stretch(), MenuLayout.BuildTree(model, new MenuColors(), 16f)),
            new RectF32(0, 0, 300, 300));
        router.Handle(new InputEvent.KeyDown(InputKey.Enter, InputModifier.None)).ShouldBeTrue();
        model.IsConfirmed.ShouldBeTrue();
    }
}
