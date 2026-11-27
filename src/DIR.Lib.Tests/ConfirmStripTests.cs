using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DIR.Lib;
using DIR.Lib.Layout;
using SharpAstro.Png;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="Builder.ConfirmStrip"/> and <see cref="Builder.ConfirmChoices"/>, driven through <see cref="InputRouter"/> events
/// and repainted between them as a host does: the first press only opens the question, the strip keeps the rest button's right
/// edge so Cancel is last and the confirming button is never under the first press, the second press of a double-click runs no
/// choice, Cancel / Escape / a press elsewhere close it and run nothing, the confirming button holds the keyboard and Enter
/// confirms once. Pixel baselines confirm_states_dark.png (the screen's States panel) and confirm_camera_choices.png
/// (the camera's strip under its row).
/// </summary>
public class ConfirmStripTests
{
    private static readonly bool UpdateBaselines = Environment.GetEnvironmentVariable("DIR_LIB_UPDATE_BASELINES") == "1";
    private static readonly string FontPath = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");
    private static readonly string BaselineDir = Path.Combine(AppContext.BaseDirectory, "Baselines");
    private static readonly string SourceBaselineDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Baselines");

    private static RGBAColor32 Hex(int rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 0xff);

    // tianwen's Dark palette as confirm.json draws it.
    private static readonly UiPalette DarkPalette = new()
    {
        ContentBg = Hex(0x101318), PanelBg = Hex(0x171b22), HeaderBg = Hex(0x1e232c), Separator = Hex(0x2a3039),
        SeparatorStrong = Hex(0x3c444f), BodyText = Hex(0xe2e6ec), DimText = Hex(0x8b939f), Accent = Hex(0xff5e00),
        Selection = Hex(0x302820), Info = Hex(0x5aa0ff), Warn = Hex(0xccac18), Error = Hex(0xff7a70), Focus = Hex(0xff5e00),
    };

    private static readonly UiTheme Dark = new() { Palette = DarkPalette, Metrics = new UiMetrics(14f, 6f, 30f, 24f, 28f) };

    private sealed class FloatCtx : IMeasureContext<float>
    {
        public Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length * 8f, 14f);
        public float ToSurface(float designUnits) => designUnits;
        public float ToSurfaceX(float designUnits) => designUnits;
        public float ToSurfaceY(float designUnits) => designUnits;
    }

    private static List<ArrangedNode<float>> Arrange(Node root)
        => [.. Engine.Arrange(root, new Rect<float>(0, 0, 400f, 60f), new FloatCtx())];

    private static Node RowOf(Node cell) => Builder.HStack(Builder.Spacer().WStar(), cell).CrossCenter().Stretch();

    private static Node Remove(ConfirmState state, List<string> log, Consequence c = Consequence.Destroys)
        => Builder.ConfirmStrip(state, Dark, open => Builder.Button("Remove", ButtonKind.Destructive, open, Dark),
            "Remove ZWO EAF?", "Remove", () => log.Add("remove"), c);

    // ---- geometry ----

    [Fact]
    public void The_strip_keeps_the_rest_buttons_right_edge_so_cancel_is_last_and_confirm_is_not_under_the_first_press()
    {
        var state = new ConfirmState();
        var log = new List<string>();
        var rest = Arrange(RowOf(Remove(state, log)));
        var restButton = rest.Single(n => n.Node.Hit is HitResult.ButtonHit).Bounds;

        state.Ask();
        var asking = Arrange(RowOf(Remove(state, log)));
        var confirm = asking.Single(n => n.Node.Hit is HitResult.ListItemHit { Index: 0 }).Bounds;
        var cancel = asking.Single(n => n.Node.Hit is HitResult.ListItemHit { Index: 1 }).Bounds;

        var strip = asking.Single(n => n.Node.DeclaredList is not null).Bounds;
        (strip.X + strip.Width).ShouldBe(restButton.X + restButton.Width, "the strip keeps the rest button's right edge");
        (cancel.X + cancel.Width).ShouldBeGreaterThan(restButton.X + restButton.Width - 6f, "Cancel sits last, within the strip's padding of that edge");
        cancel.X.ShouldBeGreaterThan(confirm.X, "Cancel comes after the confirming button");

        var pressX = restButton.X + restButton.Width / 2f;
        var pressY = restButton.Y + restButton.Height / 2f;
        (pressX >= confirm.X && pressX < confirm.X + confirm.Width).ShouldBeFalse("the first press would land on the confirming button");
    }

    // ---- input ----

    private sealed class Widget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public Node Tree { get; set; } = Builder.Box(0, 0);

        public void Render(float width, float height)
        {
            BeginFrame();
            RenderLayout(Tree, new RectF32(0, 0, width, height), FontPath, DesignScale.One);
        }
    }

    private sealed class Rig
    {
        private readonly Func<Node> _build;
        private readonly float _height;
        public Widget Widget { get; }
        public InputRouter Router { get; }

        public Rig(Func<Node> build, float height = 60f)
        {
            _build = build;
            _height = height;
            Widget = new Widget(new RgbaImageRenderer(400u, (uint)height)) { FontPath = FontPath };
            Router = new InputRouter(Widget.Ui, new BackgroundTaskTracker(), static () => { }) { Widgets = () => [Widget] };
            Repaint();
        }

        public void Repaint()
        {
            Widget.Tree = _build();
            Widget.Render(400f, _height);
            Router.AfterPaint();
        }

        public List<ClickableRegion> Regions()
        {
            var regions = new List<ClickableRegion>();
            Widget.CollectPaintedRegions(regions);
            return regions;
        }

        public ClickableRegion Region(Func<HitResult, bool> match) => Regions().Last(r => r.Result is { } h && match(h));

        public void Press(float x, float y, int clicks = 1)
        {
            Router.Handle(new InputEvent.MouseDown(x, y, MouseButton.Left, InputModifier.None, clicks));
            Router.Handle(new InputEvent.MouseUp(x, y, MouseButton.Left, InputModifier.None));
            Repaint();
        }

        public void PressCentre(ClickableRegion r, int clicks = 1) => Press(r.X + r.Width / 2f, r.Y + r.Height / 2f, clicks);

        public void Key(InputKey key)
        {
            Router.Handle(new InputEvent.KeyDown(key, InputModifier.None));
            Repaint();
        }
    }

    private static (Rig Rig, ConfirmState State, List<string> Log) StripRig()
    {
        var state = new ConfirmState();
        var log = new List<string>();
        return (new Rig(() => RowOf(Remove(state, log))), state, log);
    }

    [Fact]
    public void The_first_press_only_opens_the_question()
    {
        var (rig, state, log) = StripRig();
        rig.PressCentre(rig.Region(h => h is HitResult.ButtonHit));

        state.IsAsking.ShouldBeTrue();
        log.ShouldBeEmpty();
        rig.Regions().Any(r => r.Result is HitResult.ListItemHit { Index: 0 }).ShouldBeTrue("the confirming button is on screen");
    }

    [Fact]
    public void The_second_press_of_a_double_click_runs_no_choice_and_leaves_the_question_open()
    {
        var (rig, state, log) = StripRig();
        var rest = rig.Region(h => h is HitResult.ButtonHit);
        var x = rest.X + rest.Width / 2f;
        var y = rest.Y + rest.Height / 2f;

        rig.Press(x, y, clicks: 1);
        state.IsAsking.ShouldBeTrue();
        rig.Press(x, y, clicks: 2);

        state.IsAsking.ShouldBeTrue("the second press must not cancel it either");
        log.ShouldBeEmpty();
    }

    [Fact]
    public void Cancel_puts_the_button_back_and_runs_nothing()
    {
        var (rig, state, log) = StripRig();
        rig.PressCentre(rig.Region(h => h is HitResult.ButtonHit));
        rig.PressCentre(rig.Region(h => h is HitResult.ListItemHit { Index: 1 }));

        state.IsAsking.ShouldBeFalse();
        log.ShouldBeEmpty();
        rig.Regions().Any(r => r.Result is HitResult.ButtonHit).ShouldBeTrue();
    }

    [Fact]
    public void Escape_closes_it_and_runs_nothing()
    {
        var (rig, state, log) = StripRig();
        rig.PressCentre(rig.Region(h => h is HitResult.ButtonHit));
        rig.Key(InputKey.Escape);

        state.IsAsking.ShouldBeFalse();
        log.ShouldBeEmpty();
    }

    [Fact]
    public void A_press_anywhere_outside_the_strip_closes_it_and_runs_nothing()
    {
        var (rig, state, log) = StripRig();
        rig.PressCentre(rig.Region(h => h is HitResult.ButtonHit));
        rig.Press(5f, 5f);

        state.IsAsking.ShouldBeFalse();
        log.ShouldBeEmpty();
    }

    private static Node Row(string name, List<string> log)
        => Builder.HStack(Builder.Spacer().WStar(),
                Builder.Button(name, ButtonKind.Standard, () => log.Add(name), Dark)).CrossCenter().WStar().HFixed(34f);

    [Fact]
    public void A_strip_asking_swallows_a_press_on_a_row_above_and_a_row_below_and_closes()
    {
        foreach (var rowToPress in new[] { "Above", "Below" })
        {
            var state = new ConfirmState();
            var log = new List<string>();
            var rig = new Rig(() => Builder.VStack(Row("Above", log), RowOf(Remove(state, log)).HFixed(34f), Row("Below", log)).Stretch(), 140f);
            rig.PressCentre(rig.Regions().Where(r => r.Result is HitResult.ButtonHit).OrderBy(r => r.Y).ElementAt(1));
            state.IsAsking.ShouldBeTrue();

            var target = rig.Regions().Where(r => r.Result is HitResult.ButtonHit).OrderBy(r => r.Y).ToList();
            var row = rowToPress == "Above" ? target.First() : target.Last();
            rig.PressCentre(row);

            log.ShouldBeEmpty($"a press on the {rowToPress} row must run nothing while the question is open");
            state.IsAsking.ShouldBeFalse($"a press on the {rowToPress} row closes the question");
        }
    }

    [Fact]
    public void Choices_asking_swallow_a_press_on_a_row_below_and_close()
    {
        var state = new ConfirmState { ListId = "camera" };
        var log = new List<string>();
        var rig = new Rig(() => Builder.VStack(Row("Above", log), CameraChoices(state, log).HFixed(40f), Row("Below", log)).Stretch(), 140f);
        state.Ask();
        rig.Repaint();

        var rows = rig.Regions().Where(r => r.Result is HitResult.ButtonHit).OrderBy(r => r.Y).ToList();
        rig.PressCentre(rows.Last());
        log.ShouldBeEmpty();
        state.IsAsking.ShouldBeFalse();

        state.Ask();
        rig.Repaint();
        rig.PressCentre(rig.Regions().Where(r => r.Result is HitResult.ButtonHit).OrderBy(r => r.Y).First());
        log.ShouldBeEmpty();
        state.IsAsking.ShouldBeFalse();
    }

    [Fact]
    public void The_confirming_button_holds_the_keyboard_and_Enter_runs_it_once_and_closes()
    {
        var (rig, state, log) = StripRig();
        rig.PressCentre(rig.Region(h => h is HitResult.ButtonHit));

        state.Cursor.ListId.ShouldBe(state.ListId);
        state.Cursor.Index.ShouldBe(0);

        rig.Key(InputKey.Enter);
        log.ShouldBe(["remove"]);
        state.IsAsking.ShouldBeFalse();

        rig.Key(InputKey.Enter);
        log.ShouldBe(["remove"], "a second Enter has nothing open to confirm");
    }

    [Fact]
    public void Pressing_the_confirming_button_runs_the_action_once_and_closes()
    {
        var (rig, state, log) = StripRig();
        rig.PressCentre(rig.Region(h => h is HitResult.ButtonHit));
        rig.PressCentre(rig.Region(h => h is HitResult.ListItemHit { Index: 0 }));

        log.ShouldBe(["remove"]);
        state.IsAsking.ShouldBeFalse();
    }

    [Fact]
    public void The_strip_is_tinted_and_edged_in_the_consequence_colour_and_the_confirm_ink_is_InkOn()
    {
        foreach (var (consequence, colour) in new[] { (Consequence.Destroys, DarkPalette.Error), (Consequence.Hazard, DarkPalette.Warn) })
        {
            var state = new ConfirmState();
            state.Ask();
            var arranged = Arrange(RowOf(Remove(state, [], consequence)));
            var strip = arranged.Single(n => n.Node.DeclaredList is not null);
            strip.Node.BorderStyle.ShouldNotBeNull().Color.ShouldBe(colour);
            var confirm = arranged.Single(n => n.Node.Hit is HitResult.ListItemHit { Index: 0 });
            confirm.Node.Background.ShouldBe(colour);
        }
    }

    // ---- choices ----

    private static Node CameraChoices(ConfirmState state, List<string> log)
        => Builder.ConfirmChoices(state, Dark, "The sensor is at −10 °C. Warm it before disconnecting?",
        [
            new ConfirmChoice("Warm, then off", ConfirmChoiceLook.Accent, () => log.Add("warm")),
            new ConfirmChoice("Off now", ConfirmChoiceLook.Outlined, () => log.Add("now")),
        ]);

    private static Node ThreeChoices(ConfirmState state)
        => Builder.ConfirmChoices(state, Dark, "Which way?",
        [
            new ConfirmChoice("Warm, then off", ConfirmChoiceLook.Accent, () => { }),
            new ConfirmChoice("Off now", ConfirmChoiceLook.Outlined, () => { }),
            new ConfirmChoice("Keep it on", ConfirmChoiceLook.Consequence, () => { }),
        ]);

    private static (Rig Rig, ConfirmState State, List<string> Log) ChoicesRig()
    {
        var state = new ConfirmState { ListId = "camera" };
        var log = new List<string>();
        var rig = new Rig(() => Builder.VStack(Builder.Spacer().HFixed(10f), CameraChoices(state, log)).Stretch());
        state.Ask();
        rig.Repaint();
        return (rig, state, log);
    }

    [Fact]
    public void Choices_are_not_painted_until_asking_and_take_no_room()
    {
        var state = new ConfirmState();
        var node = CameraChoices(state, []);
        node.ShouldBeOfType<Node.Leaf>().Content.ShouldBeOfType<Content.Box>().Width.ShouldBe(0f);
    }

    [Fact]
    public void Three_choices_then_Cancel_last_each_in_its_look()
    {
        var state = new ConfirmState { ListId = "camera" };
        state.Ask();
        var arranged = Arrange(Builder.VStack(Builder.Spacer().HFixed(10f), ThreeChoices(state)).Stretch());
        var buttons = arranged.Where(n => n.Node.Hit is HitResult.ListItemHit).OrderBy(n => n.Bounds.X).ToList();

        buttons.Count.ShouldBe(4, "three choices and Cancel");
        buttons.Select(b => ((HitResult.ListItemHit)b.Node.Hit!).Index).ShouldBe([0, 1, 2, 3]);
        buttons[0].Node.Background.ShouldBe(DarkPalette.Accent);
        buttons[1].Node.Background.ShouldBe(DarkPalette.HeaderBg);
        buttons[1].Node.BorderStyle.ShouldNotBeNull().Color.ShouldBe(DarkPalette.Warn);
        buttons[2].Node.Background.ShouldBe(DarkPalette.Warn);
    }

    [Fact]
    public void A_choice_runs_once_and_closes()
    {
        var (rig, state, log) = ChoicesRig();
        rig.PressCentre(rig.Region(h => h is HitResult.ListItemHit { Index: 1 }));

        log.ShouldBe(["now"]);
        state.IsAsking.ShouldBeFalse();
    }

    [Fact]
    public void The_first_choice_holds_the_keyboard_and_Enter_runs_it()
    {
        var (rig, state, log) = ChoicesRig();
        state.Cursor.Index.ShouldBe(0);
        rig.Key(InputKey.Enter);

        log.ShouldBe(["warm"]);
        state.IsAsking.ShouldBeFalse();
    }

    [Fact]
    public void The_choices_close_on_Cancel_Escape_and_a_press_elsewhere_and_run_nothing()
    {
        var (rig, state, log) = ChoicesRig();
        rig.PressCentre(rig.Region(h => h is HitResult.ListItemHit { Index: 2 }));
        state.IsAsking.ShouldBeFalse();

        state.Ask();
        rig.Repaint();
        rig.Key(InputKey.Escape);
        state.IsAsking.ShouldBeFalse();

        state.Ask();
        rig.Repaint();
        rig.Press(5f, 3f);
        state.IsAsking.ShouldBeFalse();

        log.ShouldBeEmpty();
    }

    [Fact]
    public void The_second_press_of_a_double_click_on_a_choice_runs_nothing()
    {
        var (rig, state, log) = ChoicesRig();
        rig.PressCentre(rig.Region(h => h is HitResult.ListItemHit { Index: 0 }), clicks: 2);

        log.ShouldBeEmpty();
        state.IsAsking.ShouldBeTrue();
    }

    [Fact]
    public void A_choices_strip_needs_two_choices()
    {
        var state = new ConfirmState();
        Should.Throw<ArgumentException>(() => Builder.ConfirmChoices(state, Dark, "q", [new ConfirmChoice("Only", ConfirmChoiceLook.Accent, () => { })]));
    }

    // ---- baselines ----

    private static Node Card(UiTheme t, Node content, string caption, float width)
        => Builder.VStack(
                Builder.HStack(Builder.Spacer().WStar(), content).CrossCenter().WStar().HFixed(30f),
                Builder.Text(caption, 12f, t.Palette.DimText, TextAlign.Center).WStar())
            .WithGap(10f).Pad(12f).WFixed(width).HFixed(84f).Bg(t.Palette.PanelBg).Radius(8f);

    private static UiTheme HoverLook(UiTheme t)
    {
        var d = t.Controls.DestructiveOrDerived;
        return t with { Controls = t.Controls with { Destructive = d with { RestFill = d.HoverFill } } };
    }

    // Deliberate difference from confirm.png: the Rest and Hover buttons are 84 units wide, as screens/confirm.json declares
    // ("width": 84), where the still render draws one 126 units wide filling the card. The fixture follows the tree.
    private static Node StatesPanel(UiTheme t)
    {
        Node Rest(UiTheme theme) => Builder.ConfirmStrip(new ConfirmState(), theme, open => Builder.Button("Remove", ButtonKind.Destructive, open, theme),
            "Remove ZWO EAF?", "Remove", () => { });
        var removing = new ConfirmState { ListId = "a" };
        removing.Ask();
        var hazard = new ConfirmState { ListId = "b" };
        hazard.Ask();
        return Builder.HStack(
                Card(t, Rest(t).WFixed(84f), "Rest", 150f),
                Card(t, Rest(HoverLook(t)).WFixed(84f), "Hover", 150f),
                Card(t, Builder.ConfirmStrip(removing, t, open => Builder.Button("Remove", ButtonKind.Destructive, open, t),
                    "Remove ZWO EAF?", "Remove", () => { }, width: 356f), "Asking: destroys something", 380f),
                Card(t, Builder.ConfirmStrip(hazard, t, open => Builder.Button("Goto", ButtonKind.Standard, open, t),
                    "Goto the unfiltered Sun?", "Goto", () => { }, Consequence.Hazard, 356f), "Asking: a hazard", 380f))
            .WithGap(10f).Pad(20f);
    }

    private static Node CameraPanel(UiTheme t)
    {
        var state = new ConfirmState { ListId = "camera" };
        state.Ask();
        return Builder.VStack(CameraChoices(state, [])).WFixed(760f).Pad(20f);
    }

    private static RgbaImage Draw(UiTheme theme, Node tree, int width, int height)
    {
        var renderer = new RgbaImageRenderer((uint)width, (uint)height);
        renderer.Surface.Clear(theme.Palette.ContentBg);
        var widget = new Widget(renderer) { FontPath = FontPath };
        widget.Tree = tree;
        widget.Render(width, height);
        return renderer.Surface;
    }

    [Fact] public void States_dark() => CompareBaseline(Draw(Dark, StatesPanel(Dark), 1220, 124), "confirm_states_dark.png");

    [Fact] public void Camera_choices() => CompareBaseline(Draw(Dark, CameraPanel(Dark), 800, 92), "confirm_camera_choices.png");

    private static void CompareBaseline(RgbaImage img, string name)
    {
        var baselinePath = Path.Combine(BaselineDir, name);
        if (UpdateBaselines)
        {
            Directory.CreateDirectory(SourceBaselineDir);
            Directory.CreateDirectory(BaselineDir);
            var png = PngWriter.Encode(img.Pixels, img.Width, img.Height);
            File.WriteAllBytes(Path.Combine(SourceBaselineDir, name), png);
            File.WriteAllBytes(baselinePath, png);
            return;
        }

        File.Exists(baselinePath).ShouldBeTrue($"Baseline '{name}' is missing; generate it with DIR_LIB_UPDATE_BASELINES=1.");
        var baseline = PngReader.Decode(File.ReadAllBytes(baselinePath));
        baseline.Width.ShouldBe(img.Width);
        baseline.Height.ShouldBe(img.Height);
        var maxDiff = 0;
        for (var i = 0; i < baseline.Pixels.Length; i++)
        {
            maxDiff = Math.Max(maxDiff, Math.Abs(img.Pixels[i] - baseline.Pixels[i]));
        }

        if (maxDiff > 2)
        {
            var actualPath = Path.ChangeExtension(baselinePath, ".actual.png");
            PngWriter.Save(actualPath, img.Pixels, img.Width, img.Height);
            Assert.Fail($"Baseline mismatch for '{name}': max diff={maxDiff}. Actual saved to '{actualPath}'.");
        }
    }
}

