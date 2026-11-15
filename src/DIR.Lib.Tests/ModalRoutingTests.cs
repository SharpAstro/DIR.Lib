using System.Collections.Generic;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Declared modal routing (<see cref="Layout.Node.Overlay.Modal"/>): while a modal's Top is painted, the router
/// reaches Top and nothing else. In memory, against the stub renderer, with no clock: every case is one event fed to
/// <see cref="InputRouter"/> and one fact read back.
/// </summary>
public class ModalRoutingTests
{
    private sealed class Fixture
    {
        public Fixture()
        {
            var renderer = new DeclarationStubRenderer(200, 200);
            Back = new DeclarationWidget(renderer);
            Front = new DeclarationWidget(renderer);
            Back.ShareWith(Front);
            Router = new InputRouter(Back.Ui, new BackgroundTaskTracker(), static () => { })
            {
                Widgets = () => [Back, Front],
                Unhandled = e => { Unhandled.Add(e); return false; },
            };
        }

        public DeclarationWidget Back { get; }
        public DeclarationWidget Front { get; }
        public InputRouter Router { get; }
        public ListCursor Cursor { get; } = new();
        public List<string> Fired { get; } = [];
        public List<InputEvent> Unhandled { get; } = [];
        public bool Open { get; set; } = true;
        public bool Modal { get; set; } = true;
        public bool CanDismiss { get; set; } = true;

        public RectF32 Card => Back.Ui.PaintedModal!.Card;

        public void Paint()
        {
            Back.Render(Layout.Builder.Spacer().Stretch().Clickable(new HitResult.ChromeHit(), _ => Fired.Add("beneath")),
                new RectF32(0, 0, 200, 200));

            var rows = new Layout.Node[3];
            for (var i = 0; i < 3; i++)
            {
                var row = i;
                rows[i] = Layout.Builder.Spacer().RowH(10f)
                    .Clickable(new HitResult.ListItemHit("base", row), _ => Fired.Add("base-list-click"))
                    .Activatable(_ => Fired.Add("base-list-activate"));
            }

            var page = Layout.Builder.VStack(
                Layout.Builder.Spacer().RowH(20f).Clickable(new HitResult.ChromeHit(), _ => Fired.Add("base-row")),
                Layout.Builder.Spacer().RowH(5f).Clickable(new HitResult.ChromeHit(), _ => Fired.Add("base-shortcut"))
                    .WithShortcut(InputKey.B, InputModifier.Ctrl),
                Layout.Builder.VStack(rows).WFixed(60f).HFixed(30f).ListOf(Cursor, "base")).Stretch();

            Layout.Node? top = null;
            if (Open)
            {
                var card = Layout.Builder.VStack(
                    Layout.Builder.Box(50f, 20f).Clickable(new HitResult.ChromeHit(), _ => Fired.Add("top-row"))
                        .WithShortcut(InputKey.T, InputModifier.Ctrl))
                    .WFixed(50f).HFixed(20f);
                top = Layout.Builder.AnchoredTo(new RectF32(120f, 100f, 0f, 0f), card);
            }

            Layout.Node root = !Modal && top is not null
                ? Layout.Builder.Overlay(page, top)
                : Layout.Builder.Modal(page, top, CanDismiss ? () => { Open = false; Fired.Add("dismissed"); } : null);
            Front.Render(root, new RectF32(0, 0, 200, 200));
        }

        public bool Press(float x, float y) => Router.Handle(new InputEvent.MouseDown(x, y, MouseButton.Left, InputModifier.None, 1));

        public bool Key(InputKey key, InputModifier mods = InputModifier.None) => Router.Handle(new InputEvent.KeyDown(key, mods));
    }

    private static (float X, float Y) Inside(RectF32 card) => (card.X + card.Width / 2f, card.Y + card.Height / 2f);

    [Fact]
    public void A_press_on_a_Base_region_of_the_same_widget_fires_nothing_and_is_consumed()
    {
        var f = new Fixture { CanDismiss = false };
        f.Paint();

        f.Press(10f, 10f).ShouldBeTrue();

        f.Fired.ShouldBeEmpty();
    }

    [Fact]
    public void A_press_on_a_widget_beneath_fires_nothing()
    {
        var f = new Fixture { CanDismiss = false };
        f.Paint();

        // Beyond the page's rows, where only the widget beneath has a region.
        f.Press(190f, 190f).ShouldBeTrue();

        f.Fired.ShouldBeEmpty();
    }

    [Fact]
    public void A_press_on_a_Top_row_fires_it()
    {
        var f = new Fixture();
        f.Paint();
        var (x, y) = Inside(f.Card);

        f.Press(x, y).ShouldBeTrue();

        f.Fired.ShouldBe(["top-row"]);
    }

    [Fact]
    public void A_Base_shortcut_does_not_take_the_key_and_a_Top_one_does()
    {
        var f = new Fixture();
        f.Paint();

        f.Key(InputKey.B, InputModifier.Ctrl).ShouldBeTrue("a modal keeps the key from the host as well");
        f.Fired.ShouldBeEmpty();

        f.Key(InputKey.T, InputModifier.Ctrl).ShouldBeTrue();
        f.Fired.ShouldBe(["top-row"]);
    }

    [Fact]
    public void A_Base_declared_list_does_not_take_Up_Down_or_Enter()
    {
        var f = new Fixture();
        f.Paint();
        f.Cursor.Open("base", 0);

        f.Key(InputKey.Down);
        f.Key(InputKey.Enter);

        f.Cursor.Index.ShouldBe(0);
        f.Fired.ShouldBeEmpty();
        f.Unhandled.ShouldBeEmpty("the host's own handler is Base's too");
    }

    [Fact]
    public void An_outside_press_with_a_declared_close_closes_it_and_is_still_consumed()
    {
        var f = new Fixture();
        f.Paint();

        f.Press(10f, 10f).ShouldBeTrue();

        f.Open.ShouldBeFalse();
        f.Fired.ShouldBe(["dismissed"]);
        f.Unhandled.ShouldBeEmpty();
    }

    [Fact]
    public void Escape_closes_a_modal_that_declared_a_close()
    {
        var f = new Fixture();
        f.Paint();

        f.Key(InputKey.Escape).ShouldBeTrue();

        f.Fired.ShouldBe(["dismissed"]);
    }

    [Fact]
    public void An_outside_press_with_no_declared_close_is_consumed_and_closes_nothing()
    {
        var f = new Fixture { CanDismiss = false };
        f.Paint();

        f.Press(10f, 10f).ShouldBeTrue();

        f.Open.ShouldBeTrue();
        f.Fired.ShouldBeEmpty();
    }

    [Fact]
    public void A_press_on_the_card_beyond_its_rows_is_consumed_and_does_not_close_it()
    {
        var f = new Fixture();
        f.Paint();
        var card = f.Card;

        f.Press(card.X + 1f, card.Y + 1f).ShouldBeTrue();

        f.Open.ShouldBeTrue();
    }

    [Fact]
    public void With_Top_absent_Base_gets_the_press_again()
    {
        var f = new Fixture { Open = false };
        f.Paint();

        f.Press(10f, 10f).ShouldBeTrue();

        f.Fired.ShouldBe(["base-row"]);
        f.Back.Ui.PaintedModal.ShouldBeNull();
    }

    [Fact]
    public void A_modal_closed_by_its_own_dismiss_hands_input_back_on_the_next_paint()
    {
        var f = new Fixture();
        f.Paint();
        f.Press(10f, 10f);
        f.Fired.Clear();

        f.Paint();
        f.Press(10f, 10f);

        f.Fired.ShouldBe(["base-row"]);
    }

    [Fact]
    public void An_overlay_not_declared_modal_leaves_Base_reachable()
    {
        var f = new Fixture { Modal = false };
        f.Paint();

        f.Press(10f, 10f);
        f.Key(InputKey.B, InputModifier.Ctrl);

        f.Fired.ShouldBe(["base-row", "base-shortcut"]);
        f.Back.Ui.PaintedModal.ShouldBeNull();
    }
}
