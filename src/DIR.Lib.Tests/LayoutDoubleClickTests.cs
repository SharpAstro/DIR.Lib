using System.Collections.Generic;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="Layout.Node.DoubleClickable"/>: one click selects, two open, declared on the node and applied
/// by the router. Driven through <see cref="InputRouter"/> because the click count reaches a node only
/// there, which is what every consumer used to re-read by hand through a press handler that had to
/// remember to decline.
/// </summary>
public class LayoutDoubleClickTests
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

        public bool Press(int clicks, InputModifier mods = InputModifier.None)
            => Router.Handle(new InputEvent.MouseDown(50f, 50f, MouseButton.Left, mods, clicks));
    }

    private static Layout.Node Row(List<string> log, bool withDoubleClick = true)
    {
        var row = Layout.Builder.Spacer()
            .Clickable(new HitResult.ButtonHit("row"), _ => log.Add("select"));
        return withDoubleClick ? row.DoubleClickable(_ => log.Add("open")) : row;
    }

    [Fact]
    public void OneClickSelectsAndTheSecondPressOpensInsteadOfClickingAgain()
    {
        var log = new List<string>();
        var harness = new Harness();
        harness.Paint(Row(log));

        harness.Press(clicks: 1).ShouldBeTrue();
        harness.Press(clicks: 2).ShouldBeTrue();

        log.ShouldBe(["select", "open"], "a click that toggles would be undone by a second run");
    }

    [Fact]
    public void WithoutADeclaredDoubleClickTheSecondPressIsAnotherClick()
    {
        var log = new List<string>();
        var harness = new Harness();
        harness.Paint(Row(log, withDoubleClick: false));

        harness.Press(clicks: 1);
        harness.Press(clicks: 2);

        log.ShouldBe(["select", "select"], "nothing changes for a node that declares nothing");
    }

    [Fact]
    public void TheDoubleClickIsHandedTheModifiers()
    {
        InputModifier? seen = null;
        var harness = new Harness();
        harness.Paint(Layout.Builder.Spacer()
            .Clickable(new HitResult.ButtonHit("row"))
            .DoubleClickable(mods => seen = mods));

        harness.Press(clicks: 2, InputModifier.Ctrl);

        seen.ShouldBe(InputModifier.Ctrl);
    }

    // A press claimed for a drag owns the gesture, double or not, exactly as it owns a single click.
    [Fact]
    public void APressThatClaimsADragWinsOverTheDoubleClick()
    {
        var log = new List<string>();
        var harness = new Harness();
        harness.Paint(Layout.Builder.Spacer()
            .Pressable(new HitResult.ButtonHit("row"), _ => new DragCapture(_ => { }, _ => { }))
            .DoubleClickable(_ => log.Add("open")));

        harness.Press(clicks: 2).ShouldBeTrue();

        log.ShouldBeEmpty();
    }

    [Fact]
    public void ADisabledNodeSwallowsTheDoubleClick()
    {
        var log = new List<string>();
        var harness = new Harness();
        harness.Paint(Row(log).Disabled("not now"));

        harness.Press(clicks: 1);
        harness.Press(clicks: 2);

        log.ShouldBeEmpty();
    }
}
