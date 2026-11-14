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
    public void A_popover_over_a_declared_list_claims_a_click_inside_it_and_the_row_cursor_stays()
    {
        var f = new Fixture(declareB: false);
        f.A.Open("A", 1);
        var popover = new PopoverState();
        popover.Open();

        // A non-pixel surface paints an open popover over list A (x 0..100, rows 0..30) through public API only.
        var content = new RectF32(10f, 0f, 60f, 20f);
        f.Widget.Ui.NoteFrameBegin(f.Widget);
        f.Widget.Ui.NotePopoverPainted(popover);
        f.Widget.Ui.ClaimPointer(content, popover);
        var contentHits = 0;
        f.Widget.Regions.Add(new ClickableRegion(0f, 0f, 400f, 400f, new HitResult.ButtonHit("backdrop"), null) { Dismisses = popover });
        f.Widget.Regions.Add(new ClickableRegion(content.X, content.Y, content.Width, content.Height,
            new HitResult.ButtonHit("content"), null) { OnActivate = _ => contentHits++ });

        f.Widget.Ui.PointerOwner.ShouldBe(content);
        f.Press(20f, 5f).ShouldBeTrue(); // inside the content, over row 0 of A

        contentHits.ShouldBe(1, "the popover's region took the press, not the row beneath");
        f.Activated.ShouldBeEmpty();
        popover.IsOpen.ShouldBeTrue("a press inside the content does not dismiss");
        f.A.Index.ShouldBe(1);
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

    // ---- the same tree painted by a PixelWidgetBase ------------------------------------------

    private static readonly RGBAColor32 Rest = new(0x20, 0x40, 0x60, 0xff);
    private static readonly RGBAColor32 Lit = new(0xc0, 0x30, 0x10, 0xff);

    private sealed class StubRenderer(uint w, uint h) : RgbaImageRenderer(w, h)
    {
        public override (float Width, float Height) MeasureText(ReadOnlySpan<char> text, string fontFamily, float fontSize)
            => (text.Length * fontSize * 0.5f, fontSize);

        public override void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize,
            RGBAColor32 fontColor, in RectInt layout, TextAlign horizAlign = TextAlign.Near,
            TextAlign vertAlign = TextAlign.Center)
        { }
    }

    private sealed class PaintedWidget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public void Render(Layout.Node root, RectF32 bounds)
        {
            BeginFrame();
            RenderLayout(root, bounds, fontPath: "stub.ttf", scale: DesignScale.One);
        }
    }

    private sealed class PixelFixture
    {
        public StubRenderer Renderer { get; } = new(300, 40);
        public PaintedWidget Widget { get; }
        public ListCursor A { get; } = new();
        public ListCursor B { get; } = new();
        public List<int> Activated { get; } = [];
        public InputRouter Router { get; }
        public List<InputEvent> Unhandled { get; } = [];
        public int HostSteps { get; private set; }

        public PixelFixture()
        {
            Widget = new PaintedWidget(Renderer);
            Widget.Render(Layout.Builder.HStack(List("A", A), Layout.Builder.Spacer().WFixed(100f).HStar(), List("B", B)).Stretch(),
                new RectF32(0f, 0f, 300f, 40f));
            Router = new InputRouter(Widget.Ui, new BackgroundTaskTracker(), static () => { })
            {
                Widgets = () => [Widget],
                Unhandled = e =>
                {
                    Unhandled.Add(e);
                    if (e is InputEvent.KeyDown k && Widget.HandleListKey(k.Key, k.Modifiers)) { HostSteps++; return true; }
                    return false;
                },
            };
        }

        private Layout.Node List(string id, ListCursor cursor)
        {
            var rows = new Layout.Node[3];
            for (var i = 0; i < 3; i++)
            {
                var row = i;
                rows[i] = Layout.Builder.Spacer().RowH(10f).Bg(Rest).BgFocus(Lit)
                    .Clickable(new HitResult.ListItemHit(id, row), _ => { })
                    .Activatable(_ => Activated.Add(id == "A" ? 100 + row : row));
            }

            return Layout.Builder.VStack(rows).WFixed(100f).HStar().ListOf(cursor, id);
        }

        public RGBAColor32 PixelAt(int x, int y)
        {
            var img = Renderer.Surface;
            var o = (y * img.Width + x) * 4;
            return new RGBAColor32(img.Pixels[o], img.Pixels[o + 1], img.Pixels[o + 2], img.Pixels[o + 3]);
        }
    }

    [Fact]
    public void Pixel_widget_routed_by_InputRouter_matches_the_bare_widget()
    {
        var f = new PixelFixture();
        f.A.Open("A", 1);

        f.Router.Handle(new InputEvent.MouseDown(210f, 5f, MouseButton.Left, InputModifier.None, 1)).ShouldBeTrue();
        f.Router.Handle(new InputEvent.KeyDown(InputKey.Down, InputModifier.None)).ShouldBeTrue();
        f.Router.Handle(new InputEvent.KeyDown(InputKey.Down, InputModifier.None)).ShouldBeTrue();

        f.B.Index.ShouldBe(2);
        f.A.Index.ShouldBe(1);
        f.Router.Handle(new InputEvent.KeyDown(InputKey.Enter, InputModifier.None)).ShouldBeTrue();
        f.Activated.ShouldBe([2]);
        // The router consumed every key, so the host's own HandleListKey never stepped a second time.
        f.Unhandled.OfType<InputEvent.KeyDown>().ShouldBeEmpty();
        f.HostSteps.ShouldBe(0);
    }

    [Fact]
    public void Pixel_widget_HandleListKey_steps_the_declared_list_and_not_its_own_cursor()
    {
        var f = new PixelFixture();
        f.B.Open("B", 0); // the only list with an open cursor is the one the reader is in

        f.Widget.HandleListKey(InputKey.Down).ShouldBeTrue();
        f.Widget.HandleListKey(InputKey.Down).ShouldBeTrue();

        f.B.Index.ShouldBe(2);
        f.A.IsOpen.ShouldBeFalse();
        f.Widget.ListCursor.IsOpen.ShouldBeFalse();
        f.Widget.HandleListKey(InputKey.Enter).ShouldBeTrue();
        f.Activated.ShouldBe([2]);
    }

    [Fact]
    public void Pixel_widget_two_declared_lists_with_no_hint_step_neither()
    {
        var f = new PixelFixture();

        f.Widget.HandleListKey(InputKey.Down).ShouldBeFalse();

        f.A.IsOpen.ShouldBeFalse();
        f.B.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public void Pixel_widget_FocusBackground_lights_the_row_of_the_declared_cursor()
    {
        var f = new PixelFixture();
        f.B.Open("B", 2);
        f.Widget.Render(Layout.Builder.HStack(
            Layout.Builder.Spacer().WFixed(100f).HStar(), Layout.Builder.Spacer().WFixed(100f).HStar(),
            Layout.Builder.VStack(Row(0), Row(1), Row(2)).WFixed(100f).HStar().ListOf(f.B, "B")).Stretch(),
            new RectF32(0f, 0f, 300f, 40f));

        f.PixelAt(250, 25).ShouldBe(Lit);
        f.PixelAt(250, 5).ShouldBe(Rest);
    }

    private static Layout.Node Row(int i)
        => Layout.Builder.Spacer().RowH(10f).Bg(Rest).BgFocus(Lit).Clickable(new HitResult.ListItemHit("B", i), _ => { });
}
