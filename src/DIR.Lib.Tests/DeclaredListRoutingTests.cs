using System.Collections.Immutable;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// A layout node declares the <see cref="ListCursor"/> that owns its rows, and <see cref="InputRouter"/>
/// steps it for ANY <see cref="IPixelWidget"/>: here one that paints nothing and is not a
/// <c>PixelWidgetBase</c>, so only what it hands the router (regions and arranged nodes) can be the reason
/// the arrows work.
/// </summary>
public class DeclaredListRoutingTests
{
    /// <summary>The smallest IPixelWidget: it answers with what the test handed it and nothing else.</summary>
    private sealed class BareWidget : IPixelWidget
    {
        public List<ClickableRegion> Regions { get; } = [];
        public List<Layout.ArrangedNode<float>> Nodes { get; } = [];
        public WindowUiSettings Ui { get; } = new();
        public (float X, float Y)? Pointer { get; set; }

        public HitResult? HitTest(float x, float y) => null;
        public List<TextInputState> GetRegisteredTextInputs() => [];
        public int CaretIndexAt(HitResult.TextInputHit hit, float pointerX) => 0;
        public void CollectPaintedRegions(List<ClickableRegion> into) => into.AddRange(Regions);
        public void CollectPaintedNodes(List<Layout.ArrangedNode<float>> into) => into.AddRange(Nodes);
        public ListScrollController? ScrollTargetAt(float x, float y) => null;
        public CursorKind? HitTestCursor(float x, float y) => null;
    }

    private const float RowH = 10f;

    private sealed class Fixture
    {
        public BareWidget Widget { get; } = new();
        public ListCursor A { get; } = new();
        public ListCursor B { get; } = new();
        public List<int> Activated { get; } = [];
        public List<InputEvent> Unhandled { get; } = [];
        public InputRouter Router { get; }

        public Fixture(bool declareA = true, bool declareB = true)
        {
            // List A at x 0..100, list B at x 200..300; three rows of RowH each.
            if (declareA) Declare("A", A, 0f);
            if (declareB) Declare("B", B, 200f);
            Router = new InputRouter(Widget.Ui, new BackgroundTaskTracker(), static () => { })
            {
                Widgets = () => [Widget],
                Unhandled = e => { Unhandled.Add(e); return false; },
            };
        }

        private void Declare(string id, ListCursor cursor, float x)
        {
            var container = new Layout.Node.Stack(ImmutableArray<Layout.Node>.Empty).ListOf(cursor, id);
            Widget.Nodes.Add(new Layout.ArrangedNode<float>(container, new Rect<float>(x, 0f, 100f, 4 * RowH)));
            for (var i = 0; i < 3; i++)
            {
                var row = i;
                Widget.Regions.Add(new ClickableRegion(x, i * RowH, 100f, RowH, new HitResult.ListItemHit(id, row), null)
                {
                    OnActivate = _ => Activated.Add(id == "A" ? 100 + row : row),
                });
            }
        }

        public bool Press(float x, float y) => Router.Handle(new InputEvent.MouseDown(x, y, MouseButton.Left, InputModifier.None, 1));
        public bool Key(InputKey key) => Router.Handle(new InputEvent.KeyDown(key, InputModifier.None));
    }

    [Fact]
    public void Two_lists_a_press_on_B_makes_B_the_one_the_arrows_step()
    {
        var f = new Fixture();
        f.A.Open("A", 1);

        f.Press(210f, 5f).ShouldBeTrue(); // row 0 of B
        f.Key(InputKey.Down).ShouldBeTrue();
        f.Key(InputKey.Down).ShouldBeTrue();

        f.B.ListId.ShouldBe("B");
        f.B.Index.ShouldBe(2);
        f.A.Index.ShouldBe(1);
        f.Key(InputKey.Enter).ShouldBeTrue();
        f.Activated.ShouldBe([2]);
        f.Unhandled.OfType<InputEvent.KeyDown>().ShouldBeEmpty();
    }

    [Fact]
    public void A_press_inside_the_container_but_off_every_row_still_chooses_the_list()
    {
        var f = new Fixture();

        f.Press(250f, 3.5f * RowH); // inside the container, below its three rows
        f.Key(InputKey.Down).ShouldBeTrue();

        f.B.ListId.ShouldBe("B");
        f.B.Index.ShouldBe(0);
        f.A.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public void One_declared_list_and_no_press_is_the_active_one()
    {
        var f = new Fixture(declareA: false);

        f.Key(InputKey.Down).ShouldBeTrue();
        f.Key(InputKey.Down).ShouldBeTrue();

        f.B.ListId.ShouldBe("B");
        f.B.Index.ShouldBe(1);
        f.Key(InputKey.Enter).ShouldBeTrue();
        f.Activated.ShouldBe([1]);
    }

    [Fact]
    public void Two_lists_and_no_press_or_focus_step_neither_and_the_key_reaches_Unhandled()
    {
        var f = new Fixture();

        f.Key(InputKey.Down).ShouldBeFalse();

        f.A.IsOpen.ShouldBeFalse();
        f.B.IsOpen.ShouldBeFalse();
        f.Unhandled.OfType<InputEvent.KeyDown>().Count().ShouldBe(1);
    }

    [Fact]
    public void A_press_off_every_list_forgets_the_last_one()
    {
        var f = new Fixture();
        f.Press(210f, 5f);
        f.Press(500f, 500f);

        f.Key(InputKey.Down).ShouldBeFalse();
        f.B.Index.ShouldBe(0); // the press opened it on the row; the arrow after the forget moved nothing
    }

    [Fact]
    public void Active_rule_is_pure_focus_then_press_then_the_only_list_then_none()
    {
        var a = new DeclaredList(new ListCursor(), "A");
        var b = new DeclaredList(new ListCursor(), "B");

        DeclaredList.Active([a, b], "B", "A").ShouldBe(b);
        DeclaredList.Active([a, b], null, "A").ShouldBe(a);
        DeclaredList.Active([a], null, null).ShouldBe(a);
        DeclaredList.Active([a, b], null, null).ShouldBeNull();
        DeclaredList.Active([a], null, "gone").ShouldBe(a);
        DeclaredList.Active([a, b], null, "gone").ShouldBeNull();
    }
}
