using System;
using System.IO;
using System.Linq;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="PixelMenuWidget{TSurface}.HandleStandalone"/>: a menu shown without a host router takes a press on a
/// row, which <see cref="PixelMenuWidget{TSurface}.HandleInput"/> alone (keys-only since 10.0) never does. Painted
/// off-screen at an iPad's pixel size, where this was found.
/// </summary>
public sealed class PixelMenuWidgetStandaloneTests
{
    private static readonly string Font = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");

    private static (PixelMenuWidget<RgbaImage> Menu, ClickableRegion[] Rows) Painted(RgbaImageRenderer renderer)
    {
        var menu = new PixelMenuWidget<RgbaImage>(renderer, Font);
        menu.Reset("Title", "Pick one:", ["First", "Second", "Third"]);
        menu.Render();
        var rows = menu.GetRegisteredRegions().Where(r => r.OnClick is not null).ToArray();
        rows.Length.ShouldBe(3, "one clickable region per row");
        return (menu, rows);
    }

    private static InputEvent.MouseDown PressOn(ClickableRegion row) => new(row.X + row.Width / 2, row.Y + row.Height / 2);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void A_press_on_a_row_confirms_that_row(int row)
    {
        var (menu, rows) = Painted(new RgbaImageRenderer(2360, 1640));

        menu.HandleStandalone(PressOn(rows[row])).ShouldBeTrue();

        menu.IsConfirmed.ShouldBeTrue();
        menu.SelectedIndex.ShouldBe(row);
    }

    [Fact]
    public void A_press_beside_the_rows_confirms_nothing()
    {
        var (menu, _) = Painted(new RgbaImageRenderer(2360, 1640));

        menu.HandleStandalone(new InputEvent.MouseDown(5, 5));

        menu.IsConfirmed.ShouldBeFalse();
    }

    [Fact]
    public void Keys_still_reach_the_menu()
    {
        var (menu, _) = Painted(new RgbaImageRenderer(2360, 1640));

        menu.HandleStandalone(new InputEvent.KeyDown(InputKey.Down)).ShouldBeTrue();
        menu.SelectedIndex.ShouldBe(1);
        menu.HandleStandalone(new InputEvent.KeyDown(InputKey.Enter)).ShouldBeTrue();
        menu.IsConfirmed.ShouldBeTrue();
    }

    [Fact]
    public void HandleInput_alone_takes_no_press_which_is_why_this_exists()
    {
        var (menu, rows) = Painted(new RgbaImageRenderer(2360, 1640));

        menu.HandleInput(PressOn(rows[1])).ShouldBeFalse();
        menu.IsConfirmed.ShouldBeFalse();
    }
}
