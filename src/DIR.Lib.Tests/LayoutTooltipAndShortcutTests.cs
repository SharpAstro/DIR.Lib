using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Two declarations that are inert until a router reads them, and are worth landing first because the
/// declaration is where the bug was: three separate tooltip painters in one consumer, none with a hover
/// delay, over two declarations this library owned and refused to paint; and a keyboard map written out
/// per key, per surface, with the "is this panel even on screen" guard beside each one.
/// </summary>
public class LayoutTooltipAndShortcutTests
{
    private static (DeclarationWidget Widget, DeclarationStubRenderer Renderer) Fixture()
    {
        var renderer = new DeclarationStubRenderer(100, 100);
        return (new DeclarationWidget(renderer), renderer);
    }

    [Fact]
    public void ATooltipRidesOnTheRegionItsNodeWasArrangedInto()
    {
        var (widget, _) = Fixture();
        var tree = Layout.Builder.Spacer().Stretch()
            .Clickable(new HitResult.ButtonHit("cool"), _ => { })
            .WithTooltip("Cool the camera to its setpoint");

        widget.Render(tree, new RectF32(0, 0, 100, 100));

        widget.GetRegisteredRegions().Single(r => r.Result is HitResult.ButtonHit)
            .Tooltip.ShouldBe("Cool the camera to its setpoint");
    }

    [Fact]
    public void ANodeWithATooltipAndNoHitStillGetsARegionAndStaysInertToPresses()
    {
        // A statement about what is under the pointer has nowhere else to live -- but it must not become
        // a click target by acquiring one, or a label would start swallowing presses meant for the row.
        var (widget, _) = Fixture();
        var tree = Layout.Builder.Text("14.2 C", 10f).Stretch().WithTooltip("Sensor temperature");

        widget.Render(tree, new RectF32(0, 0, 100, 100));

        var region = widget.GetRegisteredRegions().Single(r => r.Result is HitResult.ChromeHit);
        region.Tooltip.ShouldBe("Sensor temperature");
        region.OnClick.ShouldBeNull();
        region.OnPress.ShouldBeNull();
    }

    [Fact]
    public void ATreeThatDeclaresNoTooltipRegistersNoneAndNoExtraRegion()
    {
        var (widget, _) = Fixture();
        widget.Render(Layout.Builder.Text("14.2 C", 10f).Stretch(), new RectF32(0, 0, 100, 100));

        widget.GetRegisteredRegions().ShouldBeEmpty();
    }

    [Fact]
    public void AShortcutIsAChordOnTheNodeAndChangesNothingAboutThePaint()
    {
        // Inert in arrange and paint by design: the router matches it against the painted tree in wave
        // 3. What this pins is that declaring one costs a tree nothing in the meantime.
        var (widget, _) = Fixture();
        var declared = Layout.Builder.Spacer().Stretch()
            .Clickable(new HitResult.ButtonHit("search"), _ => { })
            .WithShortcut(InputKey.F, InputModifier.Ctrl);

        declared.Shortcut.ShouldBe(new KeyChord(InputKey.F, InputModifier.Ctrl));
        declared.Shortcut!.Value.BeatsFocusedField.ShouldBeTrue();

        widget.Render(declared, new RectF32(0, 0, 100, 100));

        var regions = widget.GetRegisteredRegions();
        regions.Length.ShouldBe(1);
        regions[0].Result.ShouldBeOfType<HitResult.ButtonHit>();
    }

    [Fact]
    public void AShortcutDefaultsToNoModifier()
    {
        Layout.Builder.Spacer().WithShortcut(InputKey.F3).Shortcut
            .ShouldBe(new KeyChord(InputKey.F3, InputModifier.None));
    }

    [Fact]
    public void ANodeDeclaresNoShortcutUnlessAskedTo()
    {
        Layout.Builder.Spacer().Shortcut.ShouldBeNull();
    }
}
