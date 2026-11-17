using System;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Tab and Shift+Tab reach the panel's Back and close controls (<see cref="MenuModel.Focus"/>). Arrows stay on the
/// rows, as in dialog/whiptail and the Windows keyboard guidelines: a header control is a Tab stop, never a row.
/// </summary>
public class MenuFocusTests
{
    private static MenuModel Model(Action? back, Action? close)
    {
        var m = new MenuModel { Back = back, Close = close };
        m.Reset("Title", "Pick", ["a", "b", "c"]);
        return m;
    }

    private static bool Tab(MenuModel m, bool shift = false)
        => m.HandleKey(InputKey.Tab, shift ? InputModifier.Shift : InputModifier.None);

    [Fact]
    public void FocusStartsOnTheRows()
        => Model(() => { }, () => { }).Focus.ShouldBe(MenuFocus.Rows);

    [Fact]
    public void TabCyclesRowsBackCloseInReadingOrder()
    {
        var m = Model(() => { }, () => { });
        Tab(m).ShouldBeTrue();
        m.Focus.ShouldBe(MenuFocus.Back);
        Tab(m);
        m.Focus.ShouldBe(MenuFocus.Close);
        Tab(m);
        m.Focus.ShouldBe(MenuFocus.Rows);
    }

    [Fact]
    public void ShiftTabCyclesBackwards()
    {
        var m = Model(() => { }, () => { });
        Tab(m, shift: true).ShouldBeTrue();
        m.Focus.ShouldBe(MenuFocus.Close);
        Tab(m, shift: true);
        m.Focus.ShouldBe(MenuFocus.Back);
        Tab(m, shift: true);
        m.Focus.ShouldBe(MenuFocus.Rows);
    }

    [Fact]
    public void TabSkipsAControlTheMenuDoesNotHave()
    {
        var m = Model(() => { }, close: null);
        Tab(m);
        m.Focus.ShouldBe(MenuFocus.Back);
        Tab(m);
        m.Focus.ShouldBe(MenuFocus.Rows);
    }

    [Fact]
    public void TabIsNotConsumedWithNoHeaderControl()
    {
        // The host's own Tab handling keeps the key when the menu has nothing to focus.
        var m = Model(back: null, close: null);
        Tab(m).ShouldBeFalse();
        m.Focus.ShouldBe(MenuFocus.Rows);
    }

    [Fact]
    public void EnterOnFocusedBackRunsItAndConfirmsNoRow()
    {
        var backs = 0;
        var m = Model(() => backs++, () => { });
        Tab(m);
        m.HandleKey(InputKey.Enter).ShouldBeTrue();
        backs.ShouldBe(1);
        m.IsConfirmed.ShouldBeFalse();
    }

    [Fact]
    public void EnterOnFocusedCloseRunsIt()
    {
        var closes = 0;
        var m = Model(() => { }, () => closes++);
        Tab(m, shift: true);
        m.HandleKey(InputKey.Enter).ShouldBeTrue();
        closes.ShouldBe(1);
        m.IsConfirmed.ShouldBeFalse();
    }

    [Fact]
    public void ArrowsReturnFocusToTheRowsAndMoveThere()
    {
        var m = Model(() => { }, () => { });
        Tab(m);
        m.HandleKey(InputKey.Down).ShouldBeTrue();
        m.Focus.ShouldBe(MenuFocus.Rows);
        m.SelectedIndex.ShouldBe(1);
    }

    [Fact]
    public void ResetReturnsFocusToTheRows()
    {
        var m = Model(() => { }, () => { });
        Tab(m);
        m.Reset("T", "P", ["x"]);
        m.Focus.ShouldBe(MenuFocus.Rows);
    }

    [Fact]
    public void ARemovedControlReadsAsRows()
    {
        // A host that drops Back while it is focused must not leave Enter pointing at nothing.
        var m = Model(() => { }, close: null);
        Tab(m);
        m.Back = null;
        m.Focus.ShouldBe(MenuFocus.Rows);
        m.HandleKey(InputKey.Enter).ShouldBeTrue();
        m.IsConfirmed.ShouldBeTrue();
    }

    [Fact]
    public void TabWorksOnAMenuWithNoRows()
    {
        // A step with only Back (rows still loading) can still be left by keyboard.
        var backs = 0;
        var m = new MenuModel { Back = () => backs++ };
        m.Reset("T", "P", []);
        Tab(m).ShouldBeTrue();
        m.HandleKey(InputKey.Enter).ShouldBeTrue();
        backs.ShouldBe(1);
    }

    // The same keys through a painted panel and the real router: the path the GUI and Web hosts take, where the
    // keys are the tree's declared shortcuts and no Unhandled is wired.
    private sealed class Panel
    {
        public Panel(Action? back, Action? close)
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

        public bool Key(InputKey key, InputModifier mods = InputModifier.None)
        {
            var r = Router.Handle(new InputEvent.KeyDown(key, mods));
            Menu.RenderPanel();
            return r;
        }
    }

    [Fact]
    public void RoutedTabThenEnterRunsBack()
    {
        var backs = 0;
        var p = new Panel(() => backs++, () => { });
        p.Key(InputKey.Tab).ShouldBeTrue();
        p.Key(InputKey.Enter).ShouldBeTrue();
        backs.ShouldBe(1);
        p.Menu.IsConfirmed.ShouldBeFalse();
    }

    [Fact]
    public void RoutedShiftTabThenEnterRunsClose()
    {
        var closes = 0;
        var p = new Panel(() => { }, () => closes++);
        p.Key(InputKey.Tab, InputModifier.Shift).ShouldBeTrue();
        p.Key(InputKey.Enter).ShouldBeTrue();
        closes.ShouldBe(1);
    }

    [Fact]
    public void RoutedTabDoesNothingToAMenuWithNoHeaderControl()
    {
        // The panel is modal, so the router consumes a key nothing on the card claims (it must not reach the page
        // beneath); with no Back or close the menu declares no Tab, and the rows keep focus and selection.
        var p = new Panel(back: null, close: null);
        p.Key(InputKey.Tab);
        p.Menu.SelectedIndex.ShouldBe(0);
        p.Menu.IsConfirmed.ShouldBeFalse();
        p.Key(InputKey.Down);
        p.Menu.SelectedIndex.ShouldBe(1);
    }
}
