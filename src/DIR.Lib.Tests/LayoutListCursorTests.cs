using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// The keyboard cursor over a list a layout tree has already declared: the arrows move it, Enter acts
/// on it, and <see cref="Layout.Node.FocusBackground"/> shows where it is.
///
/// <para>
/// The duplication this exists to remove: every card with a list kept its own highlight index, its own
/// move-and-clamp, and — the part that actually broke — its own answer to "can this row be acted on",
/// beside a tree that had already said so by making the row clickable or not. The two drift, and the
/// symptom is a card that looks drawn and stops responding, because the cursor is parked on a row
/// nothing will act on.
/// </para>
///
/// <para>
/// Here the rows ARE the list. A row states <c>ListItemHit(list, i)</c> once, for the click it needs
/// anyway; a row a reader cannot act on simply is not clickable, registers no region, and is therefore
/// unreachable without anything saying so.
/// </para>
/// </summary>
public class LayoutListCursorTests
{
    private static readonly RGBAColor32 Rest = new(0x20, 0x40, 0x60, 0xff);
    private static readonly RGBAColor32 Focus = new(0xc0, 0x30, 0x10, 0xff);

    /// <summary>Answers MeasureText itself so no real font file is loaded.</summary>
    private sealed class StubRenderer(uint w, uint h) : RgbaImageRenderer(w, h)
    {
        public override (float Width, float Height) MeasureText(ReadOnlySpan<char> text, string fontFamily, float fontSize)
            => (text.Length * fontSize * 0.5f, fontSize);

        public override void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize,
            RGBAColor32 fontColor, in RectInt layout, TextAlign horizAlign = TextAlign.Near,
            TextAlign vertAlign = TextAlign.Center)
        { }
    }

    private sealed class ListWidget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public void Render(Layout.Node root, RectF32 bounds)
        {
            BeginFrame();
            RenderLayout(root, bounds, fontPath: "stub.ttf", dpiScale: 1f);
        }
    }

    private static (ListWidget Widget, StubRenderer Renderer) Fixture()
    {
        var renderer = new StubRenderer(100, 100);
        return (new ListWidget(renderer), renderer);
    }

    private static RGBAColor32 PixelAt(StubRenderer r, int x, int y)
    {
        var img = r.Surface;
        var i = (y * img.Width + x) * 4;
        return new RGBAColor32(img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2], img.Pixels[i + 3]);
    }

    /// <summary>One row of the list <c>views</c>, clickable unless <paramref name="reachable"/> is false.</summary>
    private static Layout.Node Row(int index, List<int>? chosen = null, bool reachable = true)
    {
        var row = Layout.Builder.Spacer().RowH(10f).Bg(Rest).BgFocus(Focus);
        return reachable
            ? row.Clickable(new HitResult.ListItemHit("views", index), _ => chosen?.Add(index))
            : row;
    }

    /// <summary>Three rows of ten in a hundred-wide card, so row <c>i</c> is centred at y = 10i + 5.</summary>
    private static Layout.Node Rows(List<int>? chosen = null, params bool[] reachable)
    {
        var rows = new Layout.Node[reachable.Length];
        for (var i = 0; i < rows.Length; i++) rows[i] = Row(i, chosen, reachable[i]);
        return Layout.Builder.VStack(rows).Stretch();
    }

    /// <summary>
    /// Rows that DECLARE themselves -- so the cursor reaches them, and a debug inspector can name them
    /// -- while carrying no handler, because their host takes the action somewhere else.
    /// </summary>
    private static Layout.Node RowsWithNoHandler(int count)
    {
        var rows = new Layout.Node[count];
        for (var i = 0; i < rows.Length; i++)
        {
            rows[i] = Layout.Builder.Spacer().RowH(10f).Bg(Rest).BgFocus(Focus)
                .Clickable(new HitResult.ListItemHit("views", i));
        }
        return Layout.Builder.VStack(rows).Stretch();
    }

    private static int RowCentre(int index) => 10 * index + 5;

    [Fact]
    public void TheCursorsRowPaintsItsFocusFillAndTheOthersDoNot()
    {
        var (widget, renderer) = Fixture();
        widget.ListCursor.Open("views", 1);

        widget.Render(Rows(null, true, true, true), new RectF32(0, 0, 100, 30));

        PixelAt(renderer, 50, RowCentre(1)).ShouldBe(Focus);
        PixelAt(renderer, 50, RowCentre(0)).ShouldBe(Rest);
        PixelAt(renderer, 50, RowCentre(2)).ShouldBe(Rest);
    }

    [Fact]
    public void ACursorInNoListLeavesEveryRowAlone()
    {
        // Every consumer written before this existed is in exactly this state and must be unaffected.
        var (widget, renderer) = Fixture();

        widget.Render(Rows(null, true, true, true), new RectF32(0, 0, 100, 30));

        widget.ListCursor.IsOpen.ShouldBeFalse();
        PixelAt(renderer, 50, RowCentre(1)).ShouldBe(Rest);
    }

    [Fact]
    public void AnOpenCursorThatHasNotMovedYetLightsNothing()
    {
        // -1 is a real state: a card that has just opened carries no highlight the reader did not ask for.
        var (widget, renderer) = Fixture();
        widget.ListCursor.Open("views");

        widget.Render(Rows(null, true, true, true), new RectF32(0, 0, 100, 30));

        widget.ListCursor.Index.ShouldBe(-1);
        PixelAt(renderer, 50, RowCentre(0)).ShouldBe(Rest);
    }

    [Fact]
    public void TheCursorOfAnotherListDoesNotMatchTheseRows()
    {
        var (widget, renderer) = Fixture();
        widget.ListCursor.Open("layers", 1);

        widget.Render(Rows(null, true, true, true), new RectF32(0, 0, 100, 30));

        PixelAt(renderer, 50, RowCentre(1)).ShouldBe(Rest);
    }

    [Fact]
    public void DownFromNowhereLandsOnTheFirstRowAndUpOnTheLast()
    {
        var (widget, _) = Fixture();
        widget.ListCursor.Open("views");
        widget.Render(Rows(null, true, true, true), new RectF32(0, 0, 100, 30));

        widget.MoveListCursor(1).ShouldBeTrue();
        widget.ListCursor.Index.ShouldBe(0);

        widget.ListCursor.Open("views");
        widget.MoveListCursor(-1).ShouldBeTrue();
        widget.ListCursor.Index.ShouldBe(2);
    }

    [Fact]
    public void TheArrowsStepOneRowAtATime()
    {
        var (widget, _) = Fixture();
        widget.ListCursor.Open("views", 0);
        widget.Render(Rows(null, true, true, true), new RectF32(0, 0, 100, 30));

        widget.MoveListCursor(1);
        widget.ListCursor.Index.ShouldBe(1);
        widget.MoveListCursor(1);
        widget.ListCursor.Index.ShouldBe(2);
        widget.MoveListCursor(-1);
        widget.ListCursor.Index.ShouldBe(1);
    }

    /// <summary>
    /// The headline property. The middle row is not clickable — a view with no camera, a locked layer —
    /// so it registers no region and the cursor cannot stop on it. Nothing beside the list says so.
    /// </summary>
    [Fact]
    public void ARowThatIsNotClickableCannotBeReached()
    {
        var (widget, _) = Fixture();
        widget.ListCursor.Open("views", 0);
        widget.Render(Rows(null, true, false, true), new RectF32(0, 0, 100, 30));

        widget.MoveListCursor(1).ShouldBeTrue();
        widget.ListCursor.Index.ShouldBe(2);          // straight past the gap, not onto it
        widget.MoveListCursor(-1).ShouldBeTrue();
        widget.ListCursor.Index.ShouldBe(0);
    }

    [Fact]
    public void TheEndOfTheListIsAnEndRatherThanAWrapOrAMoveToNothing()
    {
        var (widget, _) = Fixture();
        widget.ListCursor.Open("views", 2);
        widget.Render(Rows(null, true, true, true), new RectF32(0, 0, 100, 30));

        widget.MoveListCursor(1).ShouldBeFalse();
        widget.ListCursor.Index.ShouldBe(2);
    }

    [Fact]
    public void AMultiRowStepMovesAsFarAsItCanAndSaysWhetherItMovedAtAll()
    {
        var (widget, _) = Fixture();
        widget.ListCursor.Open("views", 0);
        widget.Render(Rows(null, true, true, true), new RectF32(0, 0, 100, 30));

        widget.MoveListCursor(5).ShouldBeTrue();
        widget.ListCursor.Index.ShouldBe(2);
        widget.MoveListCursor(5).ShouldBeFalse();
        widget.ListCursor.Index.ShouldBe(2);
    }

    [Fact]
    public void EnterInvokesTheCursorsRowExactlyAsAClickWould()
    {
        var (widget, _) = Fixture();
        var chosen = new List<int>();
        widget.ListCursor.Open("views", 1);
        widget.Render(Rows(chosen, true, true, true), new RectF32(0, 0, 100, 30));

        widget.ActivateListCursor().ShouldBeTrue();

        chosen.ShouldBe([1]);
    }

    [Fact]
    public void EnterBeforeAnyArrowActsOnTheFirstRow()
    {
        // Or a card that deliberately opens unlit would need a press of Down to do anything at all.
        var (widget, _) = Fixture();
        var chosen = new List<int>();
        widget.ListCursor.Open("views");
        widget.Render(Rows(chosen, true, true, true), new RectF32(0, 0, 100, 30));

        widget.ActivateListCursor().ShouldBeTrue();

        chosen.ShouldBe([0]);
        widget.ListCursor.Index.ShouldBe(0);
    }

    [Fact]
    public void ADeclaredRowWithNoHandlerIsReachedButNotActivated()
    {
        // A row may register its region and still carry no handler, because its host takes the action
        // somewhere else. TianWen's FITS viewer file list is the case: every row registers, so the
        // cursor and the debug inspector can both see it, but OnClick stays null because the press has
        // to reach the scroll controller underneath -- selection fires on the tap RELEASE, and an
        // OnClick here would open whichever row a touch drag happened to start on.
        //
        // The arrows still reach such a row, which is right: it is a row. What must not happen is
        // reporting that it was ACTED on, because the host then never reaches its own Enter binding and
        // the key is swallowed with nothing to show for it -- silently, there being no handler to
        // notice it was missing.
        var (widget, _) = Fixture();
        widget.ListCursor.Open("views", 1);
        widget.Render(RowsWithNoHandler(3), new RectF32(0, 0, 100, 30));

        widget.MoveListCursor(1).ShouldBeTrue("a handler-less row is still a row the arrows reach");
        widget.ListCursor.Index.ShouldBe(2);

        widget.ActivateListCursor().ShouldBeFalse("nothing ran, so nothing was activated");
        widget.HandleListKey(InputKey.Enter)
            .ShouldBeFalse("so a forwarding host can fall through to its own binding");
    }

    [Fact]
    public void HandleListKeyClaimsTheArrowsAndEnterAndNothingElse()
    {
        var (widget, _) = Fixture();
        var chosen = new List<int>();
        widget.ListCursor.Open("views");
        widget.Render(Rows(chosen, true, true, true), new RectF32(0, 0, 100, 30));

        widget.HandleListKey(InputKey.Down).ShouldBeTrue();
        widget.ListCursor.Index.ShouldBe(0);
        widget.HandleListKey(InputKey.Enter).ShouldBeTrue();
        chosen.ShouldBe([0]);

        // Escape is the widget's own business — a menu dismisses, a panel may commit — so it goes through.
        widget.HandleListKey(InputKey.Escape).ShouldBeFalse();
        widget.HandleListKey(InputKey.Left).ShouldBeFalse();
    }

    [Fact]
    public void AListNothingPaintedCannotBeNavigated()
    {
        // The regions are last frame's, so a card that is not on screen has none — and a cursor left
        // open over it must not move or act rather than moving over rows nobody can see.
        var (widget, _) = Fixture();
        var chosen = new List<int>();
        widget.ListCursor.Open("views", 0);
        widget.Render(Layout.Builder.Spacer().Stretch().Bg(Rest), new RectF32(0, 0, 100, 30));

        widget.MoveListCursor(1).ShouldBeFalse();
        widget.ActivateListCursor().ShouldBeFalse();
        chosen.ShouldBeEmpty();
    }

    [Fact]
    public void TheFocusFillFollowsTheRectTheEngineArranged()
    {
        // The same guarantee the hover fill has: the row sits under a header and a gap, so its top edge
        // is nowhere near the card's, which is exactly the offset a hand-computed rect gets wrong.
        var (widget, renderer) = Fixture();
        widget.ListCursor.Open("views", 0);

        var tree = Layout.Builder.VStack(
                Layout.Builder.Spacer().RowH(10f).Bg(Rest),
                Layout.Builder.Spacer().RowH(10f).Bg(Rest),
                Row(0).RowH(10f))
            .Stretch();
        widget.Render(tree, new RectF32(0, 0, 100, 30));

        PixelAt(renderer, 50, 25).ShouldBe(Focus);     // the third row, where the cursor's row was arranged
        PixelAt(renderer, 50, 5).ShouldBe(Rest);       // the first, which must not have lit
    }
}
