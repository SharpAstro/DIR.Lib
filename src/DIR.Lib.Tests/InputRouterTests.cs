using System;
using System.Collections.Generic;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="InputRouter"/>: one fixed order for the whole frame's input, one branch per test.
///
/// <para>
/// One test per branch because the branches are what a host used to write out, and each of them was
/// written slightly differently on each surface. A test that drove several at once could not say WHICH
/// arm answered, which is the question every one of those divergences turned on.
/// </para>
/// <para>
/// The two <see cref="KeyChord.BeatsFocusedField"/> directions are pinned separately and deliberately.
/// Getting it wrong one way leaves Ctrl+F unable to reach a search box while another field has the
/// keyboard; getting it wrong the other way makes a bare letter typed into a field fire the application
/// binding for that letter instead of appearing in the box. A single test can only see one of those.
/// </para>
/// <para>
/// Everything is asserted against registered regions, focus and window state rather than pixels, for the
/// reason <see cref="LayoutPopoverTests"/> gives: a picture cannot tell a discharged obligation from a
/// forgotten one that happens to look right this frame.
/// </para>
/// </summary>
public class InputRouterTests
{
    private static readonly RGBAColor32 Lit = new(40, 40, 60, 255);

    /// <summary>A clock a test moves by hand, the hover delay being the one thing here that cannot be
    /// driven by feeding the router events.</summary>
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    /// <summary>
    /// Two widgets sharing one window's settings, and a router over both. Two rather than one because
    /// half of what the router owns is only expressible across them: which of them answers a press first,
    /// where Tab goes next, and whose fields count as painted.
    /// </summary>
    private sealed class Harness
    {
        public Harness()
        {
            var renderer = new DeclarationStubRenderer(200, 200);
            Back = new DeclarationWidget(renderer);
            Front = new DeclarationWidget(renderer);
            Back.ShareWith(Front);

            Router = new InputRouter(Back.Ui, new BackgroundTaskTracker(), () => Redraws++)
            {
                // Paint order: Back is drawn first, so Front is on top.
                Widgets = () => [Back, Front],
                Unhandled = evt =>
                {
                    UnhandledEvents.Add(evt);
                    return UnhandledConsumes;
                },
                Clock = Clock,
            };
        }

        public DeclarationWidget Back { get; }

        public DeclarationWidget Front { get; }

        public InputRouter Router { get; }

        public ManualClock Clock { get; } = new();

        public TextInputFocus Focus => Back.Ui.Focus;

        public int Redraws { get; private set; }

        public List<InputEvent> UnhandledEvents { get; } = [];

        public bool UnhandledConsumes { get; set; }

        /// <summary>Paints one frame: the back widget's tree, then the front one's.</summary>
        public void Paint(Layout.Node? back = null, Layout.Node? front = null)
        {
            Back.Render(back ?? Layout.Builder.Spacer(), new RectF32(0, 0, 200, 200));
            Front.Render(front ?? Layout.Builder.Spacer(), new RectF32(0, 0, 200, 200));
        }

        public bool Press(float x, float y, int clicks = 1, InputModifier mods = InputModifier.None)
            => Router.Handle(new InputEvent.MouseDown(x, y, MouseButton.Left, mods, clicks));

        public bool Move(float x, float y, MouseButton button = MouseButton.None)
            => Router.Handle(new InputEvent.MouseMove(x, y, button));

        public bool Release(float x, float y)
            => Router.Handle(new InputEvent.MouseUp(x, y, MouseButton.Left));

        public bool Key(InputKey key, InputModifier mods = InputModifier.None)
            => Router.Handle(new InputEvent.KeyDown(key, mods));

        public bool Type(string text) => Router.Handle(new InputEvent.TextInput(text));
    }

    private static Layout.Node Field(TextInputState state, bool focusOnOpen = false)
        => Layout.Builder.TextInput(state, 14f, focusOnOpen: focusOnOpen).Stretch();

    // ---- MouseDown ----------------------------------------------------------------------------

    /// <summary>
    /// The rule that shipped in 9.1 and that no host wired: a click PLACES the caret. Every surface put it
    /// at the end of the value instead, because focusing was all any of them did.
    /// </summary>
    [Fact]
    public void APressOverAFieldFocusesItAndPlacesTheCaretWhereItLanded()
    {
        var harness = new Harness();
        var state = new TextInputState { Text = "hello world" };
        harness.Paint(front: Field(state));

        harness.Press(2f, 10f).ShouldBeTrue();

        harness.Focus.Current.ShouldBeSameAs(state);
        state.CursorPos.ShouldBe(0, "the press landed at the left edge");
        state.CursorPos.ShouldNotBe(state.Text.Length, "which is the answer every host gave instead");
    }

    [Fact]
    public void ASecondClickSelectsTheWordUnderIt()
    {
        var harness = new Harness();
        var state = new TextInputState { Text = "hello world" };
        harness.Paint(front: Field(state));

        harness.Press(2f, 10f, clicks: 2);

        state.SelectionStart.ShouldBe(0);
        state.SelectionEnd.ShouldBe("hello".Length, "the WORD, not the whole field");
    }

    /// <summary>
    /// The press takes the gesture, so dragging out of it extends the selection. Nothing declared that
    /// capture: a press over a field means this, so the router arms it.
    /// </summary>
    [Fact]
    public void ADragOutOfAFieldExtendsTheSelection()
    {
        var harness = new Harness();
        var state = new TextInputState { Text = "hello world" };
        harness.Paint(front: Field(state));

        harness.Press(2f, 10f);
        state.HasSelection.ShouldBeFalse("a plain press only places the caret");

        harness.Move(190f, 10f, MouseButton.Left).ShouldBeTrue();

        state.HasSelection.ShouldBeTrue();
        state.SelectionStart.ShouldBe(0);
        state.SelectionEnd.ShouldBe(state.Text.Length);
    }

    [Fact]
    public void APressOnALinkRaisesOpenUrl()
    {
        var harness = new Harness();
        var opened = new List<string>();
        harness.Router.OpenUrl += opened.Add;
        harness.Paint(front: Layout.Builder.Box(50f, 20f).Clickable(new HitResult.LinkHit("https://example.test")));

        harness.Press(10f, 10f).ShouldBeTrue();

        opened.ShouldBe(["https://example.test"]);
    }

    [Fact]
    public void APressOnANodeWithAPressHandlerGivesItTheWholeGesture()
    {
        var harness = new Harness();
        var moves = new List<PointerMove>();
        PointerMove? released = null;
        var tree = Layout.Builder.Box(50f, 20f).Pressable(
            new HitResult.ButtonHit("drag"),
            _ => new DragCapture(moves.Add, move => released = move));
        harness.Paint(front: tree);

        harness.Press(10f, 10f, mods: InputModifier.Shift);
        harness.Move(30f, 12f, MouseButton.Left);
        harness.Release(40f, 14f);

        moves.Count.ShouldBe(1);
        moves[0].X.ShouldBe(30f);
        moves[0].Button.ShouldBe(MouseButton.Left);
        moves[0].Modifiers.ShouldBe(InputModifier.Shift, "the gesture keeps the modifiers it was ARMED with");
        released.ShouldNotBeNull().X.ShouldBe(40f);
    }

    /// <summary>
    /// Returning null is the documented way to say "not this press", so it has to leave the node exactly
    /// as clickable as one that never declared a press handler.
    /// </summary>
    [Fact]
    public void APressHandlerThatDeclinesFallsThroughToTheClick()
    {
        var harness = new Harness();
        var clicked = 0;
        var tree = Layout.Builder.Box(50f, 20f)
            .Clickable(new HitResult.ButtonHit("go"), _ => clicked++)
            .Pressable(new HitResult.ButtonHit("go"), _ => null);
        harness.Paint(front: tree);

        harness.Press(10f, 10f);

        clicked.ShouldBe(1);
    }

    [Fact]
    public void APressOnAPlainClickableRunsItsClickWithTheModifiers()
    {
        var harness = new Harness();
        InputModifier? seen = null;
        harness.Paint(front: Layout.Builder.Box(50f, 20f)
            .Clickable(new HitResult.ButtonHit("go"), mods => seen = mods));

        harness.Press(10f, 10f, mods: InputModifier.Ctrl).ShouldBeTrue();

        seen.ShouldBe(InputModifier.Ctrl);
    }

    /// <summary>
    /// The blur happens AFTER the dispatch, which is the whole reason the order is written down: a button
    /// that reads the field it sits beside has to see what the reader saw when they pressed it.
    /// </summary>
    [Fact]
    public void APressElsewhereBlursTheFocusedFieldAfterTheHandlerHasRun()
    {
        var harness = new Harness();
        var state = new TextInputState { Text = "typed" };
        TextInputState? seenByHandler = null;
        harness.Paint(
            back: Layout.Builder.Box(40f, 20f).Clickable(
                new HitResult.ButtonHit("save"), _ => seenByHandler = harness.Focus.Current),
            front: Layout.Builder.Box(40f, 20f).WFixed(40f).HFixed(20f));
        harness.Focus.Focus(state);

        harness.Press(10f, 10f);

        seenByHandler.ShouldBeSameAs(state, "the handler still had the field");
        harness.Focus.Current.ShouldBeNull("and the press took the keyboard off it");
    }

    [Fact]
    public void APressOnNothingBlursAndReachesTheHostsOwnRouting()
    {
        var harness = new Harness();
        var state = new TextInputState();
        harness.Paint();
        harness.Focus.Focus(state);

        harness.Press(150f, 150f);

        harness.Focus.Current.ShouldBeNull();
        harness.UnhandledEvents.ShouldHaveSingleItem().ShouldBeOfType<InputEvent.MouseDown>();
    }

    [Fact]
    public void TheTopMostWidgetAnswersAPressFirst()
    {
        var harness = new Harness();
        var answered = new List<string>();
        harness.Paint(
            back: Layout.Builder.Box(60f, 60f).Clickable(new HitResult.ButtonHit("back"), _ => answered.Add("back")),
            front: Layout.Builder.Box(60f, 60f).Clickable(new HitResult.ButtonHit("front"), _ => answered.Add("front")));

        harness.Press(10f, 10f);

        answered.ShouldBe(["front"], "the widget painted last is the one on top");
    }

    /// <summary>
    /// A disabled region is registered precisely so the press is SWALLOWED. Letting it through to the host
    /// is how a disabled row over a menu backdrop dismisses the menu, which is to say behaves like a
    /// working one.
    /// </summary>
    [Fact]
    public void ADisabledRegionSwallowsThePressRatherThanPassingItOn()
    {
        var harness = new Harness();
        var clicked = 0;
        harness.Paint(front: Layout.Builder.Box(50f, 20f)
            .Clickable(new HitResult.ButtonHit("go"), _ => clicked++)
            .Disabled("no camera connected"));

        harness.Press(10f, 10f).ShouldBeTrue();

        clicked.ShouldBe(0);
        harness.UnhandledEvents.ShouldBeEmpty();
    }

    // ---- MouseMove ----------------------------------------------------------------------------

    [Fact]
    public void AMoveWithNoGestureTellsEveryWidgetWhereThePointerIs()
    {
        var harness = new Harness();
        harness.Paint();

        harness.Move(33f, 44f);

        harness.Back.Pointer.ShouldBe((33f, 44f));
        harness.Front.Pointer.ShouldBe((33f, 44f), "hover is resolved per widget, during ITS paint");
    }

    /// <summary>
    /// Motion is not a reason to draw a frame. It becomes one when it changed something the frame shows,
    /// which for the router is a lit background or a tooltip, and nothing else.
    /// </summary>
    [Fact]
    public void AMoveOntoAndOffAHoverBackgroundAsksForARedrawAndAMoveAcrossInertChromeDoesNot()
    {
        var harness = new Harness();
        harness.Paint(front: Layout.Builder.VStack(
            Layout.Builder.Box(200f, 20f).RowH(20f).BgHover(Lit),
            Layout.Builder.Box(200f, 20f).RowH(20f)).Stretch());

        harness.Move(10f, 10f);
        var onto = harness.Redraws;
        harness.Move(20f, 10f);
        var acrossTheSameNode = harness.Redraws;
        harness.Move(20f, 30f);
        var offIt = harness.Redraws;
        harness.Move(40f, 30f);

        onto.ShouldBe(1, "the row lit");
        acrossTheSameNode.ShouldBe(1, "still the same row, so nothing changed");
        offIt.ShouldBe(2, "the row went out");
        harness.Redraws.ShouldBe(2, "and nothing below it lights at all");
    }

    [Fact]
    public void AMoveWithNoGestureReachesTheHostsOwnRouting()
    {
        var harness = new Harness();
        harness.Paint();

        harness.Move(10f, 10f);

        harness.UnhandledEvents.ShouldHaveSingleItem().ShouldBeOfType<InputEvent.MouseMove>();
    }

    [Fact]
    public void AMoveDuringAGestureGoesToTheCaptureAndNowhereElse()
    {
        var harness = new Harness();
        var moves = 0;
        harness.Paint(front: Layout.Builder.Box(50f, 20f).Pressable(
            new HitResult.ButtonHit("drag"), _ => new DragCapture(_ => moves++, _ => { })));

        harness.Press(10f, 10f);
        harness.Move(120f, 120f, MouseButton.Left).ShouldBeTrue();

        moves.ShouldBe(1);
        harness.Back.Pointer.ShouldBeNull("a drag is not a hover, so nothing lights under it");
        harness.UnhandledEvents.ShouldBeEmpty();
    }

    // ---- MouseUp ------------------------------------------------------------------------------

    [Fact]
    public void AReleaseEndsTheGestureAndTheMoveAfterItIsAHoverAgain()
    {
        var harness = new Harness();
        var moves = 0;
        harness.Paint(front: Layout.Builder.Box(50f, 20f).Pressable(
            new HitResult.ButtonHit("drag"), _ => new DragCapture(_ => moves++, _ => { })));

        harness.Press(10f, 10f);
        harness.Release(20f, 10f).ShouldBeTrue();
        harness.Move(30f, 10f, MouseButton.Left);

        moves.ShouldBe(0, "the move came after the release, so the gesture was over");
        harness.Front.Pointer.ShouldBe((30f, 10f));
    }

    [Fact]
    public void AReleaseWithNoGestureReachesTheHostsOwnRouting()
    {
        var harness = new Harness();
        harness.Paint();

        harness.Release(10f, 10f);

        harness.UnhandledEvents.ShouldHaveSingleItem().ShouldBeOfType<InputEvent.MouseUp>();
    }

    // ---- Scroll -------------------------------------------------------------------------------

    /// <summary>
    /// Seventeen wheel handlers across eleven files each opened with "is the pointer over my rect". The
    /// region list already knew, and now answers.
    /// </summary>
    [Fact]
    public void TheWheelGoesToTheScrollableUnderThePointer()
    {
        var harness = new Harness();
        var scroll = new ListScrollController();
        scroll.SetExtent(new RectF32(0, 0, 100, 50), atomExtentPx: 10f, totalAtoms: 40, DesignScale.One);
        harness.Paint(front: Layout.Builder.VStack(
            Layout.Builder.Spacer().RowH(100f),
            Layout.Builder.Spacer().RowH(100f).WithScroll(scroll)).Stretch());
        var before = scroll.Offset;

        harness.Router.Handle(new InputEvent.Scroll(-3f, 50f, 150f)).ShouldBeTrue();

        scroll.Offset.ShouldBeGreaterThan(before);
        harness.UnhandledEvents.ShouldBeEmpty();
    }

    [Fact]
    public void AWheelWhereNothingScrollsReachesTheHostsOwnRouting()
    {
        var harness = new Harness();
        var scroll = new ListScrollController();
        scroll.SetExtent(new RectF32(0, 0, 100, 50), atomExtentPx: 10f, totalAtoms: 40, DesignScale.One);
        harness.Paint(front: Layout.Builder.VStack(
            Layout.Builder.Spacer().RowH(100f),
            Layout.Builder.Spacer().RowH(100f).WithScroll(scroll)).Stretch());

        harness.Router.Handle(new InputEvent.Scroll(-3f, 50f, 10f));

        scroll.Offset.ShouldBe(0f, "the wheel landed above the list");
        harness.UnhandledEvents.ShouldHaveSingleItem().ShouldBeOfType<InputEvent.Scroll>();
    }

    // ---- KeyDown: the claimant ----------------------------------------------------------------

    [Fact]
    public void AnOpenPopoverReadsTheKeyBeforeAnyShortcutDoes()
    {
        var harness = new Harness();
        var popover = new PopoverState();
        popover.Open();
        var shortcutFired = 0;
        harness.Paint(
            back: Layout.Builder.Box(10f, 10f)
                .Clickable(new HitResult.ButtonHit("x"), _ => shortcutFired++)
                .WithShortcut(InputKey.Escape),
            front: Layout.Builder.Popover(new RectF32(40f, 20f, 20f, 10f), Layout.Builder.Box(50f, 30f), popover));

        harness.Key(InputKey.Escape).ShouldBeTrue();

        popover.IsOpen.ShouldBeFalse();
        shortcutFired.ShouldBe(0, "the overlay on screen owns the key");
    }

    // ---- KeyDown: both directions of the precedence rule ---------------------------------------

    /// <summary>
    /// The user's own example, and the reason the rule is on the chord: Ctrl+F reaches the search box
    /// while you are typing in another field.
    /// </summary>
    [Fact]
    public void ACtrlChordReachesItsNodeWhileAFieldHasTheKeyboard()
    {
        var harness = new Harness();
        var typing = new TextInputState { Text = "abc" };
        var fired = 0;
        harness.Paint(front: Layout.Builder.VStack(
            Field(typing).RowH(20f),
            Layout.Builder.Box(50f, 20f).RowH(20f)
                .Clickable(new HitResult.ButtonHit("find"), _ => fired++)
                .WithShortcut(InputKey.F, InputModifier.Ctrl)).Stretch());
        harness.Focus.Focus(typing);

        harness.Key(InputKey.F, InputModifier.Ctrl).ShouldBeTrue();

        fired.ShouldBe(1);
        typing.Text.ShouldBe("abc", "and nothing was typed into the field");
    }

    /// <summary>
    /// The other direction, which is the one a host cannot express at all today: while a field has the
    /// keyboard, a bare letter is a letter. This is the `F` that meant a rating-filter cycle on one panel
    /// and a typed character on another.
    /// </summary>
    [Fact]
    public void ABareLetterDoesNotReachItsNodeWhileAFieldHasTheKeyboard()
    {
        var harness = new Harness();
        var typing = new TextInputState { Text = "abc" };
        var fired = 0;
        harness.Paint(front: Layout.Builder.VStack(
            Field(typing).RowH(20f),
            Layout.Builder.Box(50f, 20f).RowH(20f)
                .Clickable(new HitResult.ButtonHit("filter"), _ => fired++)
                .WithShortcut(InputKey.F)).Stretch());
        harness.Focus.Focus(typing);

        harness.Key(InputKey.F).ShouldBeTrue("the focused field swallows it, which is what a field does");

        fired.ShouldBe(0);
    }

    [Fact]
    public void ABareLetterDoesReachItsNodeWithNoFieldFocused()
    {
        var harness = new Harness();
        var fired = 0;
        harness.Paint(front: Layout.Builder.Box(50f, 20f)
            .Clickable(new HitResult.ButtonHit("filter"), _ => fired++)
            .WithShortcut(InputKey.F));

        harness.Key(InputKey.F).ShouldBeTrue();

        fired.ShouldBe(1);
    }

    // ---- KeyDown: what a match does ------------------------------------------------------------

    [Fact]
    public void AShortcutOnAFieldFocusesItAndSelectsWhatIsInIt()
    {
        var harness = new Harness();
        var search = new TextInputState { Text = "M 42" };
        harness.Paint(front: Field(search).WithShortcut(InputKey.F3));

        harness.Key(InputKey.F3).ShouldBeTrue();

        harness.Focus.Current.ShouldBeSameAs(search);
        search.SelectionStart.ShouldBe(0);
        search.SelectionEnd.ShouldBe("M 42".Length, "so the next keystroke searches for something else");
    }

    [Fact]
    public void AShortcutPrefersTheNodesActivateOverItsClick()
    {
        var harness = new Harness();
        var acted = new List<string>();
        harness.Paint(front: Layout.Builder.Box(50f, 20f)
            .Clickable(new HitResult.ListItemHit("targets", 0), _ => acted.Add("click"))
            .Activatable(_ => acted.Add("activate"))
            .WithShortcut(InputKey.Enter, InputModifier.Ctrl));

        harness.Key(InputKey.Enter, InputModifier.Ctrl);

        acted.ShouldBe(["activate"]);
    }

    [Fact]
    public void AShortcutOnAnOpenPopoverDismissesIt()
    {
        var harness = new Harness();
        var popover = new PopoverState();
        popover.Open();
        harness.Paint(front: Layout.Builder
            .Popover(new RectF32(40f, 20f, 20f, 10f), Layout.Builder.Box(50f, 30f), popover)
            .WithShortcut(InputKey.W, InputModifier.Ctrl));

        harness.Key(InputKey.W, InputModifier.Ctrl).ShouldBeTrue();

        popover.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public void ShortcutFiredNamesTheChordAndTheNodeThatAnswered()
    {
        var harness = new Harness();
        var fired = new List<(KeyChord Chord, Layout.Node Node)>();
        harness.Router.ShortcutFired += (chord, node) => fired.Add((chord, node));
        var button = Layout.Builder.Box(50f, 20f)
            .Clickable(new HitResult.ButtonHit("go"), _ => { })
            .WithShortcut(InputKey.G, InputModifier.Alt);
        harness.Paint(front: button);

        harness.Key(InputKey.G, InputModifier.Alt);

        var one = fired.ShouldHaveSingleItem();
        one.Chord.ShouldBe(new KeyChord(InputKey.G, InputModifier.Alt));
        one.Node.Shortcut.ShouldBe(new KeyChord(InputKey.G, InputModifier.Alt));
    }

    // ---- KeyDown: matched against the PAINTED tree ---------------------------------------------

    /// <summary>
    /// The headline rule. A binding on a panel that is not on screen is inert with nobody saying so, which
    /// is the guard a hand-written key map needs beside every arm and never has.
    /// </summary>
    [Fact]
    public void AShortcutOnAPanelThatIsNoLongerPaintedIsInert()
    {
        var harness = new Harness();
        var fired = 0;
        var panel = Layout.Builder.Box(50f, 20f)
            .Clickable(new HitResult.ButtonHit("go"), _ => fired++)
            .WithShortcut(InputKey.G, InputModifier.Ctrl);

        harness.Paint(front: panel);
        harness.Key(InputKey.G, InputModifier.Ctrl);
        fired.ShouldBe(1, "while it was on screen");

        harness.Paint(front: Layout.Builder.Spacer());
        harness.Key(InputKey.G, InputModifier.Ctrl).ShouldBeFalse();
        fired.ShouldBe(1);
    }

    /// <summary>
    /// The case that separates PAINTED from arranged. A closed popover is still measured and still
    /// arranged, and only the paint skips it, so a shortcut inside one would fire from a panel nobody can
    /// see if the router read the arranged tree instead.
    /// </summary>
    [Fact]
    public void AShortcutInsideAClosedPopoverIsInertAndTheSameOneInsideAnOpenPopoverFires()
    {
        var harness = new Harness();
        var popover = new PopoverState();
        var fired = 0;
        var content = Layout.Builder.Box(50f, 30f)
            .Clickable(new HitResult.ButtonHit("reset"), _ => fired++)
            .WithShortcut(InputKey.R, InputModifier.Ctrl);

        harness.Paint(front: Layout.Builder.Popover(new RectF32(40f, 20f, 20f, 10f), content, popover));
        harness.Key(InputKey.R, InputModifier.Ctrl).ShouldBeFalse("a closed popover is not on screen");
        fired.ShouldBe(0);

        popover.Open();
        harness.Paint(front: Layout.Builder.Popover(new RectF32(40f, 20f, 20f, 10f), content, popover));
        harness.Key(InputKey.R, InputModifier.Ctrl).ShouldBeTrue();
        fired.ShouldBe(1);
    }

    [Fact]
    public void AKeyNothingClaimedReachesTheHostsOwnRouting()
    {
        var harness = new Harness();
        harness.Paint();

        harness.Key(InputKey.Down);

        harness.UnhandledEvents.ShouldHaveSingleItem().ShouldBeOfType<InputEvent.KeyDown>();
    }

    // ---- TextInput ----------------------------------------------------------------------------

    [Fact]
    public void TypedTextGoesToTheFocusedField()
    {
        var harness = new Harness();
        var state = new TextInputState();
        harness.Paint(front: Field(state));
        harness.Focus.Focus(state);

        harness.Type("Ori").ShouldBeTrue();

        state.Text.ShouldBe("Ori");
        harness.UnhandledEvents.ShouldBeEmpty();
    }

    [Fact]
    public void TypedTextWithNoFieldFocusedReachesTheHostsOwnRouting()
    {
        var harness = new Harness();
        harness.Paint();

        harness.Type("Ori");

        harness.UnhandledEvents.ShouldHaveSingleItem().ShouldBeOfType<InputEvent.TextInput>();
    }

    [Fact]
    public void TabCyclesTheFieldsOfEveryWidgetInPaintOrder()
    {
        var harness = new Harness();
        TextInputState first = new(), second = new();
        harness.Paint(back: Field(first), front: Field(second));
        harness.Focus.Focus(first);

        harness.Key(InputKey.Tab).ShouldBeTrue();

        harness.Focus.Current.ShouldBeSameAs(second, "paint order is the visual order and needs no maintaining");
    }

    /// <summary>
    /// A widget the host stopped drawing contributes NO fields to the ring, so Tab cannot put the keyboard
    /// in a box nobody can see.
    /// </summary>
    /// <remarks>
    /// The ring is every field every widget registered, which is right only because a widget that did not
    /// paint reports none. That gate is <see cref="WindowUiSettings.FrameId"/>, and it is opt-in: a host
    /// that never moves the counter leaves every widget answering with whatever it last painted. The first
    /// consumer to adopt the router hit exactly that, because its chrome composes ALL its tabs rather than
    /// only the visible one, so the ring spanned every tab that had ever been on screen. Nothing here
    /// caught it: every other test in this file paints both widgets every frame, which is the one shape in
    /// which the bug cannot appear.
    /// </remarks>
    [Fact]
    public void TabSkipsAWidgetTheHostHasStoppedPainting()
    {
        var harness = new Harness();
        TextInputState hidden = new(), shown = new();
        harness.Paint(back: Field(hidden), front: Field(shown));

        // The back widget is not drawn AT ALL next frame, which is what switching away from a tab looks
        // like. Painting it with an empty tree instead proves nothing: that still runs its BeginFrame,
        // which clears its regions regardless, so the test passes with the frame gate deleted. It was
        // written that way first and did exactly that.
        harness.Front.Ui.FrameId++;
        harness.Front.Render(Field(shown), new RectF32(0, 0, 200, 200));
        harness.Focus.Focus(shown);

        harness.Key(InputKey.Tab);

        harness.Focus.Current.ShouldBeSameAs(shown,
            "the only painted field, so Tab has nowhere else to go and must not reach the undrawn one");
    }

    // ---- AfterPaint ---------------------------------------------------------------------------

    [Fact]
    public void AFieldThatStoppedBeingPaintedLosesTheKeyboard()
    {
        var harness = new Harness();
        var state = new TextInputState();
        harness.Paint(front: Field(state));
        harness.Focus.Focus(state);
        harness.Router.AfterPaint();
        harness.Focus.Current.ShouldBeSameAs(state, "still on screen");

        harness.Paint(front: Layout.Builder.Spacer());
        harness.Router.AfterPaint();

        harness.Focus.Current.ShouldBeNull();
    }

    [Fact]
    public void AFieldThatAsksForTheKeyboardGetsItOnceWhileItStaysPainted()
    {
        var harness = new Harness();
        var state = new TextInputState();

        harness.Paint(front: Field(state, focusOnOpen: true));
        harness.Router.AfterPaint();
        harness.Focus.Current.ShouldBeSameAs(state);

        harness.Focus.Blur();
        harness.Paint(front: Field(state, focusOnOpen: true));
        harness.Router.AfterPaint();
        harness.Focus.Current.ShouldBeNull("the request was answered once, and the panel never left");
    }

    [Fact]
    public void AFieldAsksAgainOnceItHasLeftTheScreenAndComeBack()
    {
        var harness = new Harness();
        var state = new TextInputState();
        harness.Paint(front: Field(state, focusOnOpen: true));
        harness.Router.AfterPaint();
        harness.Focus.Blur();

        harness.Paint(front: Layout.Builder.Spacer());
        harness.Router.AfterPaint();
        harness.Paint(front: Field(state, focusOnOpen: true));
        harness.Router.AfterPaint();

        harness.Focus.Current.ShouldBeSameAs(state, "gone and back is a fresh request");
    }

    [Fact]
    public void AFocusOnOpenRequestNeverStealsFromAFieldBeingTypedIn()
    {
        var harness = new Harness();
        TextInputState typing = new(), asking = new();
        harness.Paint(front: Layout.Builder.VStack(
            Field(typing).RowH(20f),
            Field(asking, focusOnOpen: true).RowH(20f)).Stretch());
        harness.Focus.Focus(typing);

        harness.Router.AfterPaint();

        harness.Focus.Current.ShouldBeSameAs(typing);
    }

    // ---- tooltip and cursor -------------------------------------------------------------------

    /// <summary>
    /// The delay is the half three hand-written tooltip painters were all missing. Without one a tooltip
    /// appears under the pointer on its way past, which is why a reader learns to steer around them.
    /// </summary>
    [Fact]
    public void ATooltipIsDueOnlyOnceThePointerHasRestedOnIt()
    {
        var harness = new Harness();
        harness.Paint(front: Layout.Builder.VStack(
            Layout.Builder.Box(200f, 20f).RowH(20f).WithTooltip("Cool the camera"),
            Layout.Builder.Spacer().Stretch()).Stretch());

        harness.Move(10f, 10f);
        harness.Router.Tooltip.ShouldBeNull("the pointer has only just arrived");

        harness.Clock.Advance(harness.Router.TooltipDelay);
        var due = harness.Router.Tooltip.ShouldNotBeNull();
        due.Text.ShouldBe("Cool the camera");
        due.Anchor.Y.ShouldBe(0f, 0.01f, "anchored to the region it belongs to");
        due.Anchor.Height.ShouldBe(20f, 0.01f);
    }

    [Fact]
    public void ATooltipGoesTheMomentThePointerLeavesItsRegion()
    {
        var harness = new Harness();
        harness.Paint(front: Layout.Builder.VStack(
            Layout.Builder.Box(200f, 20f).RowH(20f).WithTooltip("Cool the camera"),
            Layout.Builder.Box(200f, 20f).RowH(20f)).Stretch());

        harness.Move(10f, 10f);
        harness.Clock.Advance(harness.Router.TooltipDelay);
        harness.Router.Tooltip.ShouldNotBeNull();

        harness.Move(10f, 30f);

        harness.Router.Tooltip.ShouldBeNull();
    }

    /// <summary>
    /// The pointer has not moved and the tooltip is no longer true, which is the case only the painted set
    /// can answer.
    /// </summary>
    [Fact]
    public void ATooltipExpiresWhenItsRegionStopsBeingPainted()
    {
        var harness = new Harness();
        harness.Paint(front: Layout.Builder.Box(50f, 20f).WithTooltip("Cool the camera"));
        harness.Move(10f, 10f);
        harness.Clock.Advance(harness.Router.TooltipDelay);
        harness.Router.Tooltip.ShouldNotBeNull();

        harness.Paint(front: Layout.Builder.Spacer());
        harness.Router.AfterPaint();

        harness.Router.Tooltip.ShouldBeNull();
    }

    /// <summary>
    /// A disabled node's reason IS its tooltip, the answer to "why can't I press this" belonging where the
    /// press was refused.
    /// </summary>
    [Fact]
    public void ADisabledNodesReasonIsWhatTheHoverShows()
    {
        var harness = new Harness();
        harness.Paint(front: Layout.Builder.Box(50f, 20f)
            .Clickable(new HitResult.ButtonHit("go"))
            .Disabled("no camera connected"));

        harness.Move(10f, 10f);
        harness.Clock.Advance(harness.Router.TooltipDelay);

        harness.Router.Tooltip.ShouldNotBeNull().Text.ShouldBe("no camera connected");
    }

    [Fact]
    public void CursorAtAnswersTheTopMostRegionThatStatesOne()
    {
        var harness = new Harness();
        harness.Paint(
            back: Layout.Builder.VStack(
                Layout.Builder.Spacer().RowH(60f).Clickable(new HitResult.ChromeHit(), null, CursorKind.Crosshair),
                Layout.Builder.Spacer().Stretch()).Stretch(),
            front: Layout.Builder.VStack(
                Layout.Builder.Spacer().RowH(60f).Clickable(new HitResult.ButtonHit("go"), null, CursorKind.Pointer),
                Layout.Builder.Spacer().Stretch()).Stretch());

        harness.Router.CursorAt(10f, 10f).ShouldBe(CursorKind.Pointer);
        harness.Router.CursorAt(150f, 150f).ShouldBeNull("nothing there had a view, so the host's default stands");
    }

    [Fact]
    public void ARouterWithNoWidgetsRoutesEverythingToTheHost()
    {
        var harness = new Harness();
        harness.Router.Widgets = () => [];

        harness.Press(10f, 10f);
        harness.Key(InputKey.G, InputModifier.Ctrl);

        harness.UnhandledEvents.Count.ShouldBe(2);
    }
}
