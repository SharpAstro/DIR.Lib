using System;
using System.Collections.Immutable;
using System.Linq;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// F1 shows the menu's keys on the card in place of its rows (<see cref="MenuModel.ShowingHelp"/>), listing only the
/// keys this menu answers to, and the host's own after them. A footer hint was the alternative and was turned down:
/// it says the same thing on every card, to readers who mostly know it already.
/// </summary>
public class MenuHelpTests
{
    private static MenuModel Model(Action? back = null, Action? close = null, int rows = 3)
    {
        var m = new MenuModel { Back = back, Close = close };
        m.Reset("Title", "Pick", [.. Enumerable.Range(1, rows).Select(i => $"row {i}")]);
        return m;
    }

    [Fact]
    public void F1OpensTheListAndF1OrEscClosesIt()
    {
        var m = Model();
        m.ShowingHelp.ShouldBeFalse();
        m.HandleKey(InputKey.F1).ShouldBeTrue();
        m.ShowingHelp.ShouldBeTrue();
        m.HandleKey(InputKey.F1).ShouldBeTrue();
        m.ShowingHelp.ShouldBeFalse();

        m.HandleKey(InputKey.F1);
        m.HandleKey(InputKey.Escape).ShouldBeTrue();
        m.ShowingHelp.ShouldBeFalse();
    }

    [Fact]
    public void WhileOpenEveryOtherKeyIsSwallowedAndChangesNothing()
    {
        var backs = 0;
        var m = Model(back: () => backs++);
        m.HandleKey(InputKey.F1);

        foreach (var key in new[] { InputKey.Down, InputKey.Up, InputKey.Enter, InputKey.D2 })
            m.HandleKey(key).ShouldBeTrue($"{key} is consumed, so nothing behind the list acts on it");
        m.HandleKey(InputKey.Tab, InputModifier.None).ShouldBeTrue();

        m.SelectedIndex.ShouldBe(0);
        m.IsConfirmed.ShouldBeFalse();
        m.Focus.ShouldBe(MenuFocus.Rows);
        backs.ShouldBe(0);
        m.ShowingHelp.ShouldBeTrue();
    }

    [Fact]
    public void ResetLeavesTheListOpen()
    {
        // A host that refreshes its rows while the list is open (a lobby's peer list) must not close it under the reader.
        var m = Model();
        m.HandleKey(InputKey.F1);
        m.Reset("Title", "Pick", ["x", "y"]);
        m.ShowingHelp.ShouldBeTrue();
    }

    [Fact]
    public void TheListNamesOnlyTheKeysThisMenuHas()
    {
        var plain = Model(rows: 3).HelpLines.Select(l => l.Keys).ToArray();
        plain.ShouldBe(["↑ ↓", "Enter", "1–3"]);

        Model(rows: 12).HelpLines.ShouldContain(l => l.Keys == "1–9", "digits stop at nine, as the rows' shortcuts do");
        Model(rows: 1).HelpLines.ShouldContain(l => l.Keys == "1");

        var both = Model(back: () => { }, close: () => { }).HelpLines;
        both.ShouldContain(new MenuHelpLine("Tab, Shift+Tab", "Reach Back and Close"));
        Model(back: () => { }).HelpLines.ShouldContain(new MenuHelpLine("Tab, Shift+Tab", "Reach Back"));
        Model(close: () => { }).HelpLines.ShouldContain(new MenuHelpLine("Tab, Shift+Tab", "Reach Close"));
    }

    [Fact]
    public void HostLinesComeAfterTheMenusOwn()
    {
        var m = Model(back: () => { });
        m.HostHelp = [new("Esc, Backspace", "Go back a step")];
        m.HelpLines[^1].ShouldBe(new MenuHelpLine("Esc, Backspace", "Go back a step"));
    }

    /// <summary>Half the font size per character, as <see cref="DeclarationStubRenderer"/> measures.</summary>
    private sealed class HalfEmCtx : Layout.IMeasureContext<float>
    {
        public Layout.Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length * fontSize * 0.5f, fontSize);
        public float ToSurface(float designUnits) => designUnits;
    }

    [Theory]
    [InlineData(1200f, 800f)]
    [InlineData(420f, 900f)] // portrait: the card is full width less the margins
    public void TheListIsTwoAlignedColumnsThatFitTheCard(float w, float h)
    {
        var ctx = new HalfEmCtx();
        var m = Model(back: () => { }, close: () => { });
        m.HostHelp = [new("Esc", "Go back a step"), new("Backspace", "Go back a step")];
        m.HandleKey(InputKey.F1);

        var arranged = Layout.Engine.Arrange(MenuLayout.BuildPanel(m, new MenuColors(), new MenuPanelStyle(), ctx, w, h),
            new Rect<float>(0, 0, w, h), ctx);
        var rows = arranged.Where(a => a.Node is Layout.Node.Stack { Children: [Layout.Node.Leaf { Content: Layout.Content.Text }, Layout.Node.Leaf { Content: Layout.Content.Text }] }).ToArray();
        rows.Length.ShouldBe(m.HelpLines.Length);

        var cells = rows.SelectMany(r => arranged.Where(a => a.Node is Layout.Node.Leaf { Content: Layout.Content.Text }
                && ((Layout.Node.Stack)r.Node).Children.Any(c => ReferenceEquals(c, a.Node))))
            .Select(a => (a.Bounds, Text: (Layout.Content.Text)((Layout.Node.Leaf)a.Node).Content)).ToArray();
        cells.Length.ShouldBe(2 * m.HelpLines.Length);
        foreach (var (bounds, text) in cells)
            ctx.MeasureText(text.Value, text.FontSize).Width.ShouldBeLessThanOrEqualTo(bounds.Width + 0.01f, $"'{text.Value}' fits its column");

        var actionColumns = cells.Where(c => m.HelpLines.Any(l => l.Action == c.Text.Value)).Select(c => c.Bounds.X).Distinct().ToArray();
        actionColumns.Length.ShouldBe(1, "every description starts at the same x");

        arranged.ShouldNotContain(a => a.Node.Hit is HitResult.ButtonHit, "no Back or close while the list is open");
    }

    [Fact]
    public void BuildTreeShowsEachKeyAsOneCentredLineInPlaceOfTheRows()
    {
        // No measure there, and the column fills the surface, so two columns would stretch across the whole window.
        var m = Model();
        m.HandleKey(InputKey.F1);
        var texts = Texts(MenuLayout.BuildTree(m, new MenuColors(), 16f));

        texts.ShouldContain(MenuModel.HelpTitle);
        texts.ShouldContain(MenuModel.HelpPrompt);
        texts.ShouldContain("↑ ↓   Move the selection");
        texts.ShouldNotContain(t => t.Contains("row 1"), "the rows are not shown under the list");
    }

    private static string[] Texts(Layout.Node node) => node switch
    {
        Layout.Node.Leaf { Content: Layout.Content.Text t } => [t.Value],
        Layout.Node.Stack s => [.. s.Children.SelectMany(Texts)],
        _ => [],
    };

    // The same keys through a painted panel and the real router: the path the GUI and Web hosts take, where a key the
    // card does not declare is swallowed by the modal panel. F1 and Esc have to be declared to reach the model at all.
    private sealed class Panel
    {
        public Panel(Action? back)
        {
            Menu = new PixelMenuWidget<RgbaImage>(new DeclarationStubRenderer(1200, 800), "stub.ttf");
            Menu.Reset("Title", "Pick", ["a", "b", "c"]);
            Menu.Back = back;
            Router = new InputRouter(Menu.Ui, new BackgroundTaskTracker(), static () => { })
            {
                Widgets = () => [Menu],
            };
            Menu.RenderPanel();
        }

        public PixelMenuWidget<RgbaImage> Menu { get; }
        public InputRouter Router { get; }

        public void Key(InputKey key)
        {
            Router.Handle(new InputEvent.KeyDown(key, InputModifier.None));
            Menu.RenderPanel();
        }
    }

    [Fact]
    public void RoutedF1OpensAndEscClosesWithNothingChosenBetween()
    {
        var backs = 0;
        var p = new Panel(() => backs++);
        p.Key(InputKey.F1);
        p.Menu.ShowingHelp.ShouldBeTrue();

        p.Key(InputKey.Down);
        p.Key(InputKey.Enter);
        p.Menu.IsConfirmed.ShouldBeFalse();
        p.Menu.SelectedIndex.ShouldBe(0);

        p.Key(InputKey.Escape);
        p.Menu.ShowingHelp.ShouldBeFalse();
        backs.ShouldBe(0, "Esc closed the list; it did not go back");

        p.Key(InputKey.Down);
        p.Menu.SelectedIndex.ShouldBe(1, "the rows answer again once the list is closed");
    }
}
