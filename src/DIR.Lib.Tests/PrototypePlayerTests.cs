using DIR.Lib.Prototyping;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// The player driven the way a host drives a widget: painted through <see cref="RgbaImageRenderer"/>, then pressed
/// and released through <see cref="InputRouter"/> at points read off the regions the paint registered.
/// </summary>
public sealed class PrototypePlayerTests
{
    private sealed class Rig
    {
        private readonly RectF32 _bounds;

        public Rig(uint width = 360, uint height = 640)
        {
            _bounds = new RectF32(0, 0, width, height);
            Player = new PrototypePlayer<RgbaImage>(new DeclarationStubRenderer(width, height),
                PrototypeKit.Sample(), PrototypeKit.Theme, "stub.ttf");
            Router = new InputRouter(Player.Ui, new BackgroundTaskTracker(), () => { }) { Widgets = () => [Player] };
            Paint();
        }

        public PrototypePlayer<RgbaImage> Player { get; }

        public InputRouter Router { get; }

        public void Paint() => Player.Render(_bounds);

        public ClickableRegion Region(Func<HitResult, bool> match)
            => Player.GetRegisteredRegions().Single(r => r.Result is { } hit && match(hit));

        public ClickableRegion Button(string action)
            => Region(h => h is HitResult.ButtonHit b && b.Action == action);

        /// <summary>A press and release at the centre of a region, then the next frame.</summary>
        public void Click(ClickableRegion region)
        {
            var x = region.X + region.Width / 2f;
            var y = region.Y + region.Height / 2f;
            Router.Handle(new InputEvent.MouseDown(x, y, MouseButton.Left, InputModifier.None, 1));
            Router.Handle(new InputEvent.MouseUp(x, y, MouseButton.Left));
            Paint();
        }

        public void Click(string action) => Click(Button(action));
    }

    [Fact]
    public void ThePlayerStartsOnTheEntryScreen()
    {
        var rig = new Rig();
        rig.Player.CurrentScreen.Id.ShouldBe("home");
        rig.Player.State.Has("dialog").ShouldBeFalse();
    }

    [Fact]
    public void GotoMovesBetweenTheSamplesScreens()
    {
        var rig = new Rig();
        var seen = new List<string>();
        rig.Player.ScreenChanged += s => seen.Add(s.Id);

        rig.Click("toForm");
        rig.Player.CurrentScreen.Id.ShouldBe("form");
        rig.Click("formBack");
        rig.Player.CurrentScreen.Id.ShouldBe("home");
        rig.Click("toAbout");
        rig.Player.CurrentScreen.Id.ShouldBe("about");
        rig.Click("aboutBack");

        seen.ShouldBe(["form", "home", "about", "home"]);
    }

    [Fact]
    public void GotoByIdRefusesAScreenThatDoesNotExist()
    {
        var rig = new Rig();
        rig.Player.Goto("nowhere").ShouldBeFalse();
        rig.Player.CurrentScreen.Id.ShouldBe("home");
        rig.Player.Goto("about").ShouldBeTrue();
        rig.Player.CurrentScreen.Id.ShouldBe("about");
    }

    [Fact]
    public void ATabSetsStateAndTheWhenContentFollows()
    {
        var rig = new Rig();
        PrototypeKit.Texts(rig.Player.BuildTree()).ShouldContain("Summary content");
        PrototypeKit.Texts(rig.Player.BuildTree()).ShouldNotContain("Details content");

        rig.Click(rig.Region(h => h is HitResult.ButtonHit { Action: "tabDetails" }));
        rig.Player.State.Get("details").ShouldBe("true");
        PrototypeKit.Texts(rig.Player.BuildTree()).ShouldContain("Details content");
        PrototypeKit.Texts(rig.Player.BuildTree()).ShouldNotContain("Summary content");

        rig.Click(rig.Region(h => h is HitResult.ButtonHit { Action: "tabSummary" }));
        rig.Player.State.Get("details").ShouldBe("false");
        PrototypeKit.Texts(rig.Player.BuildTree()).ShouldContain("Summary content");
    }

    [Fact]
    public void ToggleOpensTheModalAndItDismissesFromItsButtonAndItsScrim()
    {
        var rig = new Rig();
        rig.Player.Ui.PaintedModal.ShouldBeNull();

        rig.Click(rig.Region(h => h is HitResult.ButtonHit { Action: "openDialog" }));
        rig.Player.State.GetBool("dialog").ShouldBeTrue();
        rig.Player.Ui.PaintedModal.ShouldNotBeNull("the card is a declared modal");

        // Open: the page behind is out of reach, the card's own button is not.
        rig.Click(rig.Region(h => h is HitResult.ButtonHit { Action: "closeDialog" }));
        rig.Player.State.GetBool("dialog").ShouldBeFalse();
        rig.Player.Ui.PaintedModal.ShouldBeNull();

        // Reopen and dismiss by pressing the scrim, outside the card.
        rig.Click(rig.Region(h => h is HitResult.ButtonHit { Action: "openDialog" }));
        rig.Player.State.GetBool("dialog").ShouldBeTrue();
        rig.Router.Handle(new InputEvent.MouseDown(359f, 639f, MouseButton.Left, InputModifier.None, 1));
        rig.Router.Handle(new InputEvent.MouseUp(359f, 639f, MouseButton.Left));
        rig.Paint();
        rig.Player.State.GetBool("dialog").ShouldBeFalse();
    }

    [Fact]
    public void TypingIntoTheBoundTextInputUpdatesState()
    {
        var rig = new Rig();
        rig.Player.Goto("form");
        rig.Paint();

        rig.Click(rig.Region(h => h is HitResult.TextInputHit));
        rig.Router.Handle(new InputEvent.TextInput("Ada")).ShouldBeTrue();
        rig.Paint();

        rig.Player.State.Get("name").ShouldBe("Ada");
        rig.Player.TreeBuilder.InputFor("name").Text.ShouldBe("Ada");
    }

    [Fact]
    public void ABoundCheckboxChangesStateAndRevealsItsWhenNode()
    {
        var rig = new Rig();
        rig.Player.Goto("form");
        rig.Paint();
        PrototypeKit.Texts(rig.Player.BuildTree()).ShouldNotContain("Notifications are on");

        rig.Click("Notify me");

        rig.Player.State.GetBool("notify").ShouldBeTrue();
        PrototypeKit.Texts(rig.Player.BuildTree()).ShouldContain("Notifications are on");
    }

    [Fact]
    public void TheDesignSizeScalesToALargerSurfaceAndClicksStillLand()
    {
        var rig = new Rig(720, 1280);
        var (rect, scale) = rig.Player.Fit(new RectF32(0, 0, 720, 1280));
        scale.X.ShouldBe(2f);
        rect.Width.ShouldBe(720f);

        var region = rig.Button("toForm");
        region.X.ShouldBeGreaterThan(0f);
        region.Height.ShouldBeGreaterThan(14f, "design units were scaled to surface units");
        rig.Click(region);
        rig.Player.CurrentScreen.Id.ShouldBe("form");
    }

    [Fact]
    public void ASurfaceOfAnotherShapeIsLetterboxed()
    {
        var rig = new Rig();
        var (rect, scale) = rig.Player.Fit(new RectF32(0, 0, 720, 640));
        scale.X.ShouldBe(1f);
        rect.Width.ShouldBe(360f);
        rect.X.ShouldBe(180f, "centred");
    }
}
