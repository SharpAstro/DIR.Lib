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
/// DSN-1 v4 screens settings and equipment, composed from the family's controls as fixtures (both are tianwen pages, so the
/// session lock is shown as the locked state under a Warn banner and the steppers are themed Minus and Plus buttons). Only the
/// switch is the hit: a press on a row or a device's name does nothing, a press on a switch toggles. Pixel baselines
/// screen_settings_dark.png, screen_settings_locked_dark.png and screen_equipment_dark.png.
/// </summary>
public class SwitchFamilyScreensTests
{
    private static readonly bool UpdateBaselines = Environment.GetEnvironmentVariable("DIR_LIB_UPDATE_BASELINES") == "1";
    private static readonly string FontPath = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");
    private static readonly string BaselineDir = Path.Combine(AppContext.BaseDirectory, "Baselines");
    private static readonly string SourceBaselineDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Baselines");

    private static RGBAColor32 Hex(int rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 0xff);

    // tianwen's Dark palette as settings.json and equipment.json draw it.
    private static readonly UiPalette DarkPalette = new()
    {
        ContentBg = Hex(0x101318), PanelBg = Hex(0x171b22), HeaderBg = Hex(0x1e232c), Separator = Hex(0x2a3039),
        SeparatorStrong = Hex(0x3c444f), BodyText = Hex(0xe2e6ec), DimText = Hex(0x8b939f), Accent = Hex(0xff5e00),
        Selection = Hex(0x302820), Info = Hex(0x7cc4ff), Warn = Hex(0xccac18), Error = Hex(0xff7a70), Focus = Hex(0xff5e00),
        Success = Hex(0x4cc38a),
    };

    private static readonly UiTheme Dark = new() { Palette = DarkPalette, Metrics = new UiMetrics(14f, 6f, 30f, 24f, 28f) };

    private sealed class Widget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public Node Tree { get; set; } = Builder.Box(0, 0);

        public void Render(float width, float height)
        {
            BeginFrame();
            RenderLayout(Tree, new RectF32(0, 0, width, height), FontPath, DesignScale.One);
        }
    }

    // ---- the settings screen ----

    private sealed class Settings
    {
        public int MinAltitude = 20, Wait = 30, Steps = 9;
        public bool Unpark = true, Refocus = true, Save;
        public bool Running;
        public List<string> Log { get; } = [];
    }

    private const string Locked = "Locked while the session runs";

    private static Node Stepper(string value, bool locked, Action minus, Action plus)
        => Builder.HStack(
                Builder.Button("−", ButtonKind.Standard, minus, Dark, disabledReason: locked ? Locked : null, hit: new HitResult.ButtonHit("minus:" + value)).WFixed(30f),
                Builder.Text(value, 14f, DarkPalette.BodyText, TextAlign.Center).WFixed(64f).HStar(),
                Builder.Button("+", ButtonKind.Standard, plus, Dark, disabledReason: locked ? Locked : null, hit: new HitResult.ButtonHit("plus:" + value)).WFixed(30f))
            .WithGap(4f).CrossCenter();

    private static Node Toggle(string name, bool on, bool locked, Action<bool> set)
        => Builder.Switch(on, set, Dark, SwitchSize.Regular, lockedReason: locked ? Locked : null, hit: new HitResult.ButtonHit("switch:" + name));

    // The screens draw a group as one card with hairlines between its rows, so a row takes the card's own fill and radius.
    private static readonly UiTheme CardRows = Dark with
    {
        Controls = Dark.Controls with { SettingRow = Dark.Controls.SettingRowOrDerived with { RestFill = DarkPalette.PanelBg, Radius = 8f } },
    };

    private static Node Row(string title, string subtitle, Node control)
        => Builder.SettingRow(title, subtitle, SettingTrailing.Of(control), CardRows).WStar();

    private static Node Hairline() => Builder.Spacer().HFixed(1f).WStar().Bg(DarkPalette.Separator);

    // One card: the rows with a hairline between each two.
    private static Node Card(params Node[] rows)
    {
        var parts = new List<Node>(rows.Length * 2);
        foreach (var row in rows)
        {
            if (parts.Count > 0) parts.Add(Hairline());
            parts.Add(row);
        }
        return Builder.VStack([.. parts]).WStar().Bg(DarkPalette.PanelBg).Radius(8f);
    }

    private static Node Group(string title, params Node[] rows)
        => Builder.VStack(
                Builder.Text(title, 15f, DarkPalette.Accent, TextAlign.Near).HFixed(28f).WStar(),
                Card(rows))
            .WithGap(6f).WStar();

    private static Node SettingsPage(Settings s)
    {
        var locked = s.Running;
        var banner = locked
            ? Builder.HStack(Builder.Text("The session is running: its settings are locked until it stops.", 13f, DarkPalette.Warn, TextAlign.Near, TextAlign.Center).WStar())
                .CrossCenter().WStar().Pad(12f, 0f).Bg(RGBAColor32.Mix(DarkPalette.PanelBg, DarkPalette.Warn, 0.14f)).Border(DarkPalette.Warn, 1f, 6f).Radius(6f).HFixed(40f)
            : Builder.Spacer().HFixed(0f);
        return Builder.VStack(
                banner,
                Group("Horizon",
                    Row("Minimum altitude", "Targets below this are not started", Stepper($"{s.MinAltitude}°", locked, () => s.MinAltitude -= 5, () => s.MinAltitude += 5)),
                    Row("Wait for a rising target", "Longest wait before moving on", Stepper($"{s.Wait} min", locked, () => s.Wait -= 5, () => s.Wait += 5)),
                    Row("Unpark mount", "Unpark a parked mount once the wait for dark is over", Toggle("unpark", s.Unpark, locked, v => { s.Unpark = v; s.Log.Add("unpark"); }))),
                Group("Focusing",
                    Row("Autofocus steps", "Positions sampled in one autofocus run", Stepper($"{s.Steps}", locked, () => s.Steps--, () => s.Steps++)),
                    Row("Refocus on a new target", "Run autofocus before the first frame of each target", Toggle("refocus", s.Refocus, locked, v => { s.Refocus = v; s.Log.Add("refocus"); })),
                    Row("Save intermediate frames", "Keep every autofocus frame on disk", Toggle("save", s.Save, locked, v => { s.Save = v; s.Log.Add("save"); }))))
            .WithGap(10f).Pad(24f).WFixed(660f);
    }

    // ---- the equipment screen ----

    private sealed class Devices
    {
        public Dictionary<string, bool> On = new() { ["Mount"] = true, ["Camera"] = true, ["Filter wheel"] = false, ["Guider"] = false };
        public readonly ConfirmState Ask = new() { ListId = "camera" };
        public List<string> Log { get; } = [];
    }

    private static Node DeviceRow(Devices d, string kind, string model, string? extra = null, bool changing = false, bool asking = false)
    {
        var on = d.On.GetValueOrDefault(kind);
        var (word, ink) = changing ? ("Connecting…", DarkPalette.Info)
            : on ? (extra is null ? "Connected" : "Connected · " + extra, DarkPalette.Success)
            : ("Not connected", DarkPalette.DimText);
        Action<bool> toggle = kind == "Camera"
            ? v => { if (!v) d.Ask.Ask(); else { d.On[kind] = true; d.Log.Add("camera on"); } }
            : v => { d.On[kind] = v; d.Log.Add(kind); };
        return Builder.HStack(
                Builder.Text(kind, 14f, DarkPalette.DimText, TextAlign.Near, TextAlign.Center).WFixed(100f).HStar(),
                Builder.Text(model, 14f, DarkPalette.BodyText, TextAlign.Near, TextAlign.Center).WStar().HStar(),
                Builder.Text(word, 12f, ink, TextAlign.Far, TextAlign.Center).WFixed(170f).HStar(),
                Builder.Switch(on, toggle, Dark, SwitchSize.Small, changing: changing, asking: asking, hit: new HitResult.ButtonHit("connect:" + kind)))
            .WithGap(12f).CrossCenter().Pad(12f, 6f).HFixed(48f).WStar();
    }

    private static Node EquipmentPage(Devices d)
    {
        var asking = d.Ask.IsAsking;
        var strip = Builder.ConfirmChoices(d.Ask, Dark, "The sensor is at −10 °C. Warm it before disconnecting?",
        [
            new ConfirmChoice("Warm, then off", ConfirmChoiceLook.Accent, () => { d.On["Camera"] = false; d.Log.Add("warm"); }),
            new ConfirmChoice("Off now", ConfirmChoiceLook.Outlined, () => { d.On["Camera"] = false; d.Log.Add("now"); }),
        ]);
        var list = Card(
                DeviceRow(d, "Mount", "Sky-Watcher EQ6-R Pro"),
                Builder.VStack(DeviceRow(d, "Camera", "ZWO ASI2600MM Pro", "−10 °C", asking: asking), strip).WStar(),
                DeviceRow(d, "Focuser", "ZWO EAF", changing: true),
                DeviceRow(d, "Filter wheel", "ZWO EFW 7 × 36 mm"),
                DeviceRow(d, "Guider", "Built-in guider"));
        return Builder.VStack(
                Builder.Text("Profile: Backyard rig", 15f, DarkPalette.Accent, TextAlign.Near).HFixed(28f),
                list,
                Builder.Text("Only the switch connects or disconnects. The cooled camera asks first; its switch stays on until you choose.", 12f, DarkPalette.DimText, TextAlign.Near))
            .WithGap(10f).Pad(24f).WFixed(808f);
    }

    // ---- input ----

    private sealed class Rig
    {
        private readonly Func<Node> _build;
        private readonly Widget _widget;
        private readonly InputRouter _router;
        private readonly float _w, _h;

        public Rig(Func<Node> build, int width, int height)
        {
            _build = build;
            _w = width;
            _h = height;
            _widget = new Widget(new RgbaImageRenderer((uint)width, (uint)height)) { FontPath = FontPath };
            _router = new InputRouter(_widget.Ui, new BackgroundTaskTracker(), static () => { }) { Widgets = () => [_widget] };
            Repaint();
        }

        public void Repaint()
        {
            _widget.Tree = _build();
            _widget.Render(_w, _h);
            _router.AfterPaint();
        }

        public void Press(float x, float y)
        {
            _router.Handle(new InputEvent.MouseDown(x, y, MouseButton.Left, InputModifier.None, 1));
            _router.Handle(new InputEvent.MouseUp(x, y, MouseButton.Left));
            Repaint();
        }

        public ClickableRegion RegionOf(string hit)
            => _widget.GetRegisteredRegions().Last(r => r.Result is HitResult.ButtonHit b && b.Action == hit);

        public void PressOn(string hit)
        {
            var r = RegionOf(hit);
            Press(r.X + r.Width / 2f, r.Y + r.Height / 2f);
        }
    }

    [Fact]
    public void A_press_on_a_settings_row_its_name_or_its_words_does_nothing_and_a_press_on_a_switch_toggles()
    {
        var s = new Settings();
        var rig = new Rig(() => SettingsPage(s), 660, 470);
        var save = rig.RegionOf("switch:save");

        rig.Press(60f, save.Y + save.Height / 2f);
        rig.Press(300f, save.Y + save.Height / 2f);
        rig.Press(save.X - 60f, save.Y + save.Height / 2f);
        s.Log.ShouldBeEmpty();
        s.Save.ShouldBeFalse();

        rig.PressOn("switch:save");
        s.Save.ShouldBeTrue();
        rig.PressOn("switch:unpark");
        s.Unpark.ShouldBeFalse();
        s.Log.ShouldBe(["save", "unpark"]);
    }

    [Fact]
    public void A_stepper_button_steps_its_value()
    {
        var s = new Settings();
        var rig = new Rig(() => SettingsPage(s), 660, 470);
        rig.PressOn("plus:20°");
        s.MinAltitude.ShouldBe(25);
    }

    [Fact]
    public void A_locked_settings_page_keeps_every_value_and_takes_no_press()
    {
        var s = new Settings { Running = true };
        var rig = new Rig(() => SettingsPage(s), 660, 520);
        rig.PressOn("switch:save");
        rig.PressOn("switch:refocus");
        rig.PressOn("plus:20°");
        s.Log.ShouldBeEmpty();
        s.Save.ShouldBeFalse();
        s.Refocus.ShouldBeTrue();
        s.MinAltitude.ShouldBe(20);
    }

    [Fact]
    public void A_press_on_a_device_row_or_name_does_nothing_and_a_press_on_its_switch_toggles()
    {
        var d = new Devices();
        var rig = new Rig(() => EquipmentPage(d), 808, 420);
        var guider = rig.RegionOf("connect:Guider");
        var y = guider.Y + guider.Height / 2f;

        rig.Press(40f, y);
        rig.Press(200f, y);
        rig.Press(guider.X - 80f, y);
        d.Log.ShouldBeEmpty();
        d.On["Guider"].ShouldBeFalse();

        rig.PressOn("connect:Guider");
        d.On["Guider"].ShouldBeTrue();
        rig.PressOn("connect:Filter wheel");
        d.On["Filter wheel"].ShouldBeTrue();
        d.Log.ShouldBe(["Guider", "Filter wheel"]);
    }

    [Fact]
    public void The_focuser_that_is_connecting_takes_no_press()
    {
        var d = new Devices();
        var rig = new Rig(() => EquipmentPage(d), 808, 420);
        rig.PressOn("connect:Focuser");
        d.Log.ShouldBeEmpty();
    }

    [Fact]
    public void Turning_the_cooled_camera_off_asks_first_and_its_switch_stays_on_until_a_choice_is_made()
    {
        var d = new Devices();
        var rig = new Rig(() => EquipmentPage(d), 808, 420);
        rig.PressOn("connect:Camera");
        d.Ask.IsAsking.ShouldBeTrue();
        d.On["Camera"].ShouldBeTrue();
        d.Log.ShouldBeEmpty();
    }

    [Fact]
    public void While_the_camera_asks_a_press_on_a_switch_below_it_toggles_nothing_and_closes_the_question()
    {
        foreach (var other in new[] { "connect:Filter wheel", "connect:Guider", "connect:Mount" })
        {
            var d = new Devices();
            var rig = new Rig(() => EquipmentPage(d), 808, 420);
            rig.PressOn("connect:Camera");
            d.Ask.IsAsking.ShouldBeTrue();

            rig.PressOn(other);
            d.Log.ShouldBeEmpty($"{other} must not toggle while the question is open");
            d.Ask.IsAsking.ShouldBeFalse($"a press on {other} closes the question");
            d.On["Camera"].ShouldBeTrue();
        }
    }

    // ---- baselines ----

    private static RgbaImage Draw(Node tree, int width, int height)
    {
        var renderer = new RgbaImageRenderer((uint)width, (uint)height);
        renderer.Surface.Clear(DarkPalette.ContentBg);
        var widget = new Widget(renderer) { FontPath = FontPath, Tree = tree };
        widget.Render(width, height);
        return renderer.Surface;
    }

    [Fact] public void Settings_dark() => CompareBaseline(Draw(SettingsPage(new Settings()), 660, 470), "screen_settings_dark.png");

    [Fact] public void Settings_locked_dark() => CompareBaseline(Draw(SettingsPage(new Settings { Running = true }), 660, 520), "screen_settings_locked_dark.png");

    [Fact]
    public void Equipment_dark()
    {
        var d = new Devices();
        d.Ask.Ask();
        CompareBaseline(Draw(EquipmentPage(d), 808, 420), "screen_equipment_dark.png");
    }

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
