using System.Collections.Generic;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="Layout.Node.WithContextMenu"/>: a right press on a node that declares a menu runs the menu, not
/// the click -- and a node that declares nothing keeps the old behaviour, in which the router never looked at
/// the button. Driven through <see cref="InputRouter"/>, which is where the button is read.
/// </summary>
public class LayoutContextMenuTests
{
    private sealed class Harness
    {
        private readonly DeclarationWidget _widget = new(new DeclarationStubRenderer(200, 200));

        public Harness()
            => Router = new InputRouter(_widget.Ui, new BackgroundTaskTracker(), () => { })
            {
                Widgets = () => [_widget],
            };

        public InputRouter Router { get; }

        public void Paint(Layout.Node tree) => _widget.Render(tree, new RectF32(0, 0, 200, 200));

        public bool Press(MouseButton button, float x = 50f, float y = 50f)
            => Router.Handle(new InputEvent.MouseDown(x, y, button, InputModifier.None, 1));
    }

    private static Layout.Node Row(List<string> log)
        => Layout.Builder.Spacer()
            .Clickable(new HitResult.ButtonHit("row"), _ => log.Add("click"))
            .WithContextMenu(p => log.Add($"menu@{p.X:0},{p.Y:0}"));

    [Fact]
    public void ARightPressRunsTheMenuInsteadOfTheClick_AtThePointer()
    {
        var log = new List<string>();
        var harness = new Harness();
        harness.Paint(Row(log));

        harness.Press(MouseButton.Right, 30f, 40f).ShouldBeTrue();
        harness.Press(MouseButton.Left).ShouldBeTrue();

        log.ShouldBe(["menu@30,40", "click"]);
    }

    [Fact]
    public void WithoutADeclaredMenuARightPressIsStillAClick()
    {
        var log = new List<string>();
        var harness = new Harness();
        harness.Paint(Layout.Builder.Spacer().Clickable(new HitResult.ButtonHit("row"), _ => log.Add("click")));

        harness.Press(MouseButton.Right);

        log.ShouldBe(["click"], "no existing tree changes meaning");
    }

    // The twirl caret, a cell's button: the reader right-clicked the ROW, so its menu answers.
    [Fact]
    public void ARightPressOnAPartWithoutAMenuReachesTheRowsMenu()
    {
        var log = new List<string>();
        var harness = new Harness();
        harness.Paint(Layout.Builder.Overlay(
            Row(log),
            Layout.Builder.Spacer().Clickable(new HitResult.ButtonHit("caret"), _ => log.Add("toggle"))));

        harness.Press(MouseButton.Right);

        log.ShouldBe(["menu@50,50"]);
    }

    [Fact]
    public void ADisabledNodeSwallowsTheRightPress()
    {
        var log = new List<string>();
        var harness = new Harness();
        harness.Paint(Row(log).Disabled("not now"));

        harness.Press(MouseButton.Right);

        log.ShouldBeEmpty();
    }

    [Fact]
    public void ABackdropStopsTheSearch()
    {
        var log = new List<string>();
        var popover = new PopoverState();
        popover.Open();
        var harness = new Harness();
        harness.Paint(Layout.Builder.Overlay(
            Row(log),
            Layout.Builder.Popover(new RectF32(150, 150, 10, 10), Layout.Builder.Spacer().RowH(10f), popover)));

        harness.Press(MouseButton.Right);

        log.ShouldBeEmpty();
        popover.IsOpen.ShouldBeFalse("the scrim's press closed it");
    }
}
