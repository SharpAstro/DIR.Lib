using System.Linq;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// A node declared <see cref="Layout.Node.Opens"/> is a popover's trigger, and a press on the popover's
/// backdrop that lands on a trigger reaches it. <see cref="PopoverGroup"/> keeps a bar of them to one open
/// at a time.
/// </summary>
/// <remarks>
/// <para>
/// The case the file exists for is the one-press switch. With one card open, a press on the next chip
/// hit the backdrop, which closed the card and consumed the press, and the reader pressed again -- on
/// every consumer with a bar of cards, one of which had re-ordered its own dispatcher so the chips were
/// tested before the cards, a rule about popovers written into a host. It is pinned against the state the
/// press leaves rather than a picture, for the reason <see cref="LayoutPopoverTests"/> gives.
/// </para>
/// <para>
/// The two negatives are the ones worth the file: the dismissed popover's OWN trigger must not toggle it
/// back open (a lit chip's press means close), and a trigger under the card must not be reached through
/// the card's padding.
/// </para>
/// </remarks>
public class PopoverTriggerTests
{
    private const float ChipW = 40f;
    private const float ChipH = 20f;

    /// <summary>Two chips across the top, each opening its own popover; the popovers hang below them.</summary>
    private sealed class Bar
    {
        public Bar(bool grouped = true)
        {
            var group = grouped ? new PopoverGroup() : null;
            A = new PopoverState { Group = group };
            B = new PopoverState { Group = group };
            Group = group;
            Widget = new DeclarationWidget(new DeclarationStubRenderer(200, 200));
        }

        public PopoverState A { get; }

        public PopoverState B { get; }

        public PopoverGroup? Group { get; }

        public DeclarationWidget Widget { get; }

        /// <summary>The chip row and, over it, both popovers -- each painted only while open.</summary>
        public void Paint(Layout.Node? chipA = null, Layout.Node? chipB = null, Layout.Node? content = null)
        {
            var chips = Layout.Builder.HStack(chipA ?? Chip("a", A), chipB ?? Chip("b", B)).RowH(ChipH);
            var page = Layout.Builder.VStack(chips, Layout.Builder.Spacer().Stretch()).Stretch();
            var tree = Layout.Builder.Overlay(
                Layout.Builder.Overlay(page, Layout.Builder.Popover(ChipRect(0), content ?? Content(), A)),
                Layout.Builder.Popover(ChipRect(1), content ?? Content(), B));
            Widget.Render(tree, new RectF32(0, 0, 200, 200));
        }

        public bool Press(float x, float y, int clicks = 1) => Routing.Press(Widget, x, y, clicks: clicks);

        public static RectF32 ChipRect(int index) => new(index * ChipW, 0f, ChipW, ChipH);

        public static (float X, float Y) OnChip(int index) => (index * ChipW + ChipW / 2f, ChipH / 2f);
    }

    private static Layout.Node Chip(string action, PopoverState opens)
        => Layout.Builder.Box(ChipW, ChipH).WFixed(ChipW).HFixed(ChipH)
            .Clickable(new HitResult.ButtonHit(action)).Opens(opens);

    /// <summary>Card content with no region of its own, so a press inside it reaches the backdrop.</summary>
    private static Layout.Node Content() => Layout.Builder.Box(50f, 30f);

    // ---- the group ----------------------------------------------------------------------------

    [Fact]
    public void OpeningAMemberClosesTheRestOfItsGroup()
    {
        var bar = new Bar();
        bar.A.Open();
        bar.B.Open();

        bar.A.IsOpen.ShouldBeFalse();
        bar.B.IsOpen.ShouldBeTrue();
        bar.Group!.Open.ShouldBeSameAs(bar.B);
        bar.Group.Members.ShouldBe([bar.A, bar.B], "joined in construction order");
    }

    [Fact]
    public void TheSiblingClosesBeforeTheNewcomerOpens()
    {
        var bar = new Bar();
        var order = new System.Collections.Generic.List<string>();
        bar.A.Closed += () => order.Add("a closed");
        bar.B.Opened += () => order.Add("b opened");

        bar.A.Open();
        bar.B.Open();

        order.ShouldBe(["a closed", "b opened"], "a card committing on close has done so before the next reads it");
    }

    [Fact]
    public void OpenedFiresOnATransitionOnly()
    {
        var state = new PopoverState();
        var opened = 0;
        state.Opened += () => opened++;

        state.Open();
        state.Open();
        opened.ShouldBe(1);

        state.Close();
        state.Toggle();
        opened.ShouldBe(2);
    }

    [Fact]
    public void CloseAllIsIdempotent()
    {
        var bar = new Bar();
        bar.A.Open();
        bar.Group!.CloseAll();
        bar.Group.CloseAll();
        bar.Group.Open.ShouldBeNull();
    }

    // ---- the trigger --------------------------------------------------------------------------

    [Fact]
    public void ThePainterCarriesTheDeclarationsOntoTheRegions()
    {
        var bar = new Bar();
        bar.A.Open();
        bar.Paint();

        var regions = bar.Widget.GetRegisteredRegions();
        regions.Single(r => r.Result is HitResult.ButtonHit { Action: "a" }).Opens.ShouldBeSameAs(bar.A);
        regions.Single(r => r.Result is HitResult.ButtonHit { Action: "b" }).Opens.ShouldBeSameAs(bar.B);
        regions.Single(r => r.Dismisses is not null).Dismisses.ShouldBeSameAs(bar.A, "only the open popover paints a backdrop");
    }

    [Fact]
    public void APressOnATriggerOpensItsPopover()
    {
        var bar = new Bar();
        bar.Paint();

        var (x, y) = Bar.OnChip(0);
        bar.Press(x, y).ShouldBeTrue();

        bar.A.IsOpen.ShouldBeTrue();
        bar.B.IsOpen.ShouldBeFalse();
    }

    /// <summary>The gap. One press, not two.</summary>
    [Fact]
    public void APressOnASiblingTriggerSwitchesInOnePress()
    {
        var bar = new Bar();
        bar.A.Open();
        bar.Paint();

        var (x, y) = Bar.OnChip(1);
        bar.Press(x, y).ShouldBeTrue();

        bar.A.IsOpen.ShouldBeFalse("the backdrop closed it");
        bar.B.IsOpen.ShouldBeTrue("and the press still reached the chip beneath");
    }

    /// <summary>Without a group the switch still happens -- the backdrop is what closes the first one.</summary>
    [Fact]
    public void TheSwitchDoesNotDependOnTheGroup()
    {
        var bar = new Bar(grouped: false);
        bar.A.Open();
        bar.Paint();

        var (x, y) = Bar.OnChip(1);
        bar.Press(x, y);

        bar.A.IsOpen.ShouldBeFalse();
        bar.B.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public void APressOnTheLitTriggerClosesAndDoesNotReopen()
    {
        var bar = new Bar();
        bar.A.Open();
        bar.Paint();

        var (x, y) = Bar.OnChip(0);
        bar.Press(x, y).ShouldBeTrue();

        bar.A.IsOpen.ShouldBeFalse("the backdrop closed it, and its own trigger must not toggle it straight back");
    }

    [Fact]
    public void ABackdropPressWithNoTriggerBeneathOnlyCloses()
    {
        var bar = new Bar();
        bar.A.Open();
        bar.Paint();

        bar.Press(150f, 150f).ShouldBeTrue();

        bar.A.IsOpen.ShouldBeFalse();
        bar.B.IsOpen.ShouldBeFalse();
    }

    /// <summary>
    /// The card's padding is the card. A trigger the card happens to cover is not reachable through it,
    /// or a press on the card's empty space would open whatever the bar keeps underneath.
    /// </summary>
    [Fact]
    public void ATriggerUnderTheCardIsNotReachedThroughIt()
    {
        var bar = new Bar();
        var under = new PopoverState();
        bar.A.Open();
        // Chip A's card hangs at (0, 20) 50x30; a trigger with the same footprint sits in the page below it.
        var covered = Layout.Builder.Box(50f, 30f).WFixed(50f).HFixed(30f)
            .Clickable(new HitResult.ButtonHit("under")).Opens(under);
        var chips = Layout.Builder.HStack(Chip("a", bar.A), Chip("b", bar.B)).RowH(ChipH);
        var page = Layout.Builder.VStack(chips, Layout.Builder.HStack(covered).RowH(30f), Layout.Builder.Spacer().Stretch()).Stretch();
        var tree = Layout.Builder.Overlay(page, Layout.Builder.Popover(Bar.ChipRect(0), Content(), bar.A));
        bar.Widget.Render(tree, new RectF32(0, 0, 200, 200));

        bar.Press(10f, 30f).ShouldBeTrue();

        bar.A.IsOpen.ShouldBeFalse("the press was on the card's padding, which the backdrop takes");
        under.IsOpen.ShouldBeFalse("and nothing beneath the card was reached");
    }

    /// <summary>A trigger covered by some OTHER region is not what the reader pressed either.</summary>
    [Fact]
    public void ATriggerCoveredByAnotherRegionIsNotReachedThroughIt()
    {
        var bar = new Bar();
        bar.A.Open();
        // A plain button painted over chip B, after it, so it is the topmost thing beneath the backdrop.
        var cover = Layout.Builder.Box(ChipW, ChipH).WFixed(ChipW).HFixed(ChipH)
            .Clickable(new HitResult.ButtonHit("cover"));
        var chips = Layout.Builder.HStack(Chip("a", bar.A), Chip("b", bar.B)).RowH(ChipH);
        var covers = Layout.Builder.HStack(Layout.Builder.Spacer().WFixed(ChipW), cover).RowH(ChipH);
        var page = Layout.Builder.Overlay(
            Layout.Builder.VStack(chips, Layout.Builder.Spacer().Stretch()).Stretch(),
            Layout.Builder.VStack(covers, Layout.Builder.Spacer().Stretch()).Stretch());
        var tree = Layout.Builder.Overlay(page, Layout.Builder.Popover(Bar.ChipRect(0), Content(), bar.A));
        bar.Widget.Render(tree, new RectF32(0, 0, 200, 200));

        var (x, y) = Bar.OnChip(1);
        bar.Press(x, y);

        bar.A.IsOpen.ShouldBeFalse();
        bar.B.IsOpen.ShouldBeFalse("the region under the backdrop was the cover, not the chip");
    }

    [Fact]
    public void ADisabledTriggerOpensNothing()
    {
        var bar = new Bar();
        bar.Paint(chipA: Chip("a", bar.A).Disabled("not now"));

        var (x, y) = Bar.OnChip(0);
        bar.Press(x, y).ShouldBeTrue("swallowed, as a disabled region's press is");

        bar.A.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public void AShortcutOnATriggerToggles()
    {
        var bar = new Bar();
        bar.Paint(chipA: Chip("a", bar.A).WithShortcut(InputKey.A));

        Routing.Key(bar.Widget, InputKey.A).ShouldBeTrue();
        bar.A.IsOpen.ShouldBeTrue();

        bar.Paint(chipA: Chip("a", bar.A).WithShortcut(InputKey.A));
        Routing.Key(bar.Widget, InputKey.A).ShouldBeTrue();
        bar.A.IsOpen.ShouldBeFalse();
    }

    /// <summary>
    /// The one-click-opens, two-clicks-edits control: the second press arrives with the popover open, so
    /// it goes through the backdrop. The trigger's own press still sees the count, and the popover closes
    /// once rather than closing and reopening.
    /// </summary>
    [Fact]
    public void TheTriggersOwnPressSeesTheSecondClickWhileThePopoverCloses()
    {
        var bar = new Bar();
        var clicks = 0;
        Layout.Node ChipA() => Layout.Builder.Box(ChipW, ChipH).WFixed(ChipW).HFixed(ChipH)
            .Pressable(new HitResult.ButtonHit("a"), press => { clicks = press.Clicks; return null; })
            .Opens(bar.A);

        bar.Paint(chipA: ChipA());
        var (x, y) = Bar.OnChip(0);
        bar.Press(x, y, clicks: 1);
        clicks.ShouldBe(1);
        bar.A.IsOpen.ShouldBeTrue();

        bar.Paint(chipA: ChipA());
        bar.Press(x, y, clicks: 2);
        clicks.ShouldBe(2, "the press reached the trigger through the backdrop");
        bar.A.IsOpen.ShouldBeFalse("closed by the backdrop, and not toggled back by its own trigger");
    }

    /// <summary>
    /// The other half of that control: the second press opens an EDITOR, which is a field taking the
    /// keyboard from inside the press. The rule that a press elsewhere blurs the focused field must not
    /// undo what the press itself just did.
    /// </summary>
    [Fact]
    public void ATriggerWhosePressFocusesAFieldKeepsItFocused()
    {
        var bar = new Bar();
        var field = new TextInputState();
        Layout.Node ChipA() => Layout.Builder.Box(ChipW, ChipH).WFixed(ChipW).HFixed(ChipH)
            .Pressable(new HitResult.ButtonHit("a"), press =>
            {
                if (press.Clicks >= 2) bar.Widget.Ui.Focus.Focus(field, "100");
                return null;
            })
            .Opens(bar.A);

        bar.Paint(chipA: ChipA());
        var (x, y) = Bar.OnChip(0);
        bar.Press(x, y, clicks: 1);
        bar.Paint(chipA: ChipA());
        bar.Press(x, y, clicks: 2);

        bar.Widget.Ui.Focus.Current.ShouldBeSameAs(field, "the press gave it the keyboard, and the same press does not take it back");
        bar.A.IsOpen.ShouldBeFalse();
    }

    /// <summary>A press that claims a drag is a gesture, not a toggle.</summary>
    [Fact]
    public void APressThatClaimsADragDoesNotToggle()
    {
        var bar = new Bar();
        var chipA = Layout.Builder.Box(ChipW, ChipH).WFixed(ChipW).HFixed(ChipH)
            .Pressable(new HitResult.ButtonHit("a"), _ => new DragCapture(_ => { }, _ => { }))
            .Opens(bar.A);
        bar.Paint(chipA: chipA);

        var (x, y) = Bar.OnChip(0);
        bar.Press(x, y);

        bar.A.IsOpen.ShouldBeFalse();
    }
}
