using DIR.Lib;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// Covers <see cref="TabBar{TSurface}.Colors"/>: that the defaults still are what the bar has always drawn, and
/// that an override actually reaches the pixels rather than only the property.
/// </summary>
public class TabBarColorsTests
{
    private static string Font(string name) => Path.Combine(AppContext.BaseDirectory, "Fonts", name);

    private static TabBar<RgbaImage> NewBar(Renderer<RgbaImage> renderer)
    {
        var path = Font("DejaVuSans.ttf");
        return new TabBar<RgbaImage>(renderer) { FontPath = path, FontFallback = new FontFallbackResolver(path, []) };
    }

    // Sampled just inside the bar's right end, past the last tab: that region is painted with
    // BarBackground alone, so it isolates the palette from tab fills, labels and separators.
    private static RGBAColor32 SampleEmptyBarArea(RgbaImageRenderer r) => PixelAt(r.Surface, 390, 8);

    // RgbaImage exposes its buffer rather than a pixel accessor (see DrawLineTests.IsLit).
    private static RGBAColor32 PixelAt(RgbaImage image, int x, int y)
    {
        var o = (y * image.Width + x) * 4;
        return new RGBAColor32(image.Pixels[o], image.Pixels[o + 1], image.Pixels[o + 2], image.Pixels[o + 3]);
    }

    [Fact]
    public void Defaults_are_the_bar_original_dark_palette()
    {
        // Pinned so a sync round or a well-meant tidy cannot restyle every consumer silently. These are
        // the values that were inline constants before the palette existed.
        var c = new TabBarColors();
        c.BarBackground.ShouldBe(new RGBAColor32(0x14, 0x14, 0x1c, 0xff));
        c.ActiveBackground.ShouldBe(new RGBAColor32(0x2c, 0x2c, 0x3c, 0xff));
        c.InactiveBackground.ShouldBe(new RGBAColor32(0x1c, 0x1c, 0x26, 0xff));
        c.Separator.ShouldBe(new RGBAColor32(0x3a, 0x3a, 0x48, 0xff));
        c.ActiveAccent.ShouldBe(new RGBAColor32(0x44, 0x88, 0xff, 0xff));
        c.ActiveText.ShouldBe(new RGBAColor32(0xf0, 0xf0, 0xf0, 0xff));
        c.InactiveText.ShouldBe(new RGBAColor32(0x9a, 0x9a, 0xa6, 0xff));
        c.CloseMark.ShouldBe(new RGBAColor32(0xc0, 0xc0, 0xc8, 0xff));
    }

    [Fact]
    public void A_bar_that_sets_nothing_paints_the_default_background()
    {
        var renderer = new RgbaImageRenderer(400, 40);
        var bar = NewBar(renderer);

        bar.Render(contentLeft: 0, viewportW: 400, ["One", "Two"], activeIndex: 0);

        SampleEmptyBarArea(renderer).ShouldBe(new TabBarColors().BarBackground);
    }

    [Fact]
    public void An_overridden_background_reaches_the_pixels()
    {
        var renderer = new RgbaImageRenderer(400, 40);
        var bar = NewBar(renderer);
        var paper = new RGBAColor32(0xf2, 0xf2, 0xf4, 0xff);   // a light-theme strip
        bar.Colors = new TabBarColors { BarBackground = paper };

        bar.Render(contentLeft: 0, viewportW: 400, ["One", "Two"], activeIndex: 0);

        SampleEmptyBarArea(renderer).ShouldBe(paper);
    }

    [Fact]
    public void The_palette_is_settable_after_construction_so_a_theme_can_change_live()
    {
        // Not init-only on purpose: the host flips a theme while the bar is alive. Rendering twice with
        // different palettes has to give different pixels from the same instance.
        var renderer = new RgbaImageRenderer(400, 40);
        var bar = NewBar(renderer);

        bar.Render(0, 400, ["One"], 0);
        var darkPixel = SampleEmptyBarArea(renderer);

        bar.Colors = new TabBarColors { BarBackground = new RGBAColor32(0xff, 0xff, 0xff, 0xff) };
        bar.Render(0, 400, ["One"], 0);

        SampleEmptyBarArea(renderer).ShouldNotBe(darkPixel);
        SampleEmptyBarArea(renderer).ShouldBe(new RGBAColor32(0xff, 0xff, 0xff, 0xff));
    }

    // Object-initializer rather than the positional form this used to take: UiPalette became a
    // sealed record with required roles (see UiTheme.cs and MIGRATION.md). HeaderText is stated
    // explicitly even though it is now derivable, because these tests assert on it directly and a
    // fixture that leaned on the fallback would be testing the default rather than the mapping.
    private static readonly UiPalette LightChrome = new()
    {
        ContentBg = new RGBAColor32(0xff, 0xff, 0xff, 0xff),
        PanelBg = new RGBAColor32(0xf2, 0xf2, 0xf4, 0xff),
        HeaderBg = new RGBAColor32(0xff, 0xff, 0xff, 0xff),
        HeaderText = new RGBAColor32(0x1a, 0x1a, 0x1e, 0xff),
        BodyText = new RGBAColor32(0x33, 0x33, 0x38, 0xff),
        DimText = new RGBAColor32(0x6a, 0x6a, 0x72, 0xff),
        Separator = new RGBAColor32(0xc8, 0xc8, 0xd0, 0xff),
        Selection = new RGBAColor32(0x20, 0x60, 0xff, 0xff),
        Accent = new RGBAColor32(0x20, 0x60, 0xff, 0xff),
        Info = new RGBAColor32(0x0a, 0x63, 0xa8, 0xff),
        Warn = new RGBAColor32(0x8a, 0x50, 0x00, 0xff),
        Error = new RGBAColor32(0xb0, 0x2a, 0x20, 0xff),
    };

    [Fact]
    public void FromPalette_takes_every_surface_and_text_colour_from_the_shared_roles()
    {
        var c = TabBarColors.FromPalette(LightChrome);

        c.BarBackground.ShouldBe(LightChrome.PanelBg);
        c.InactiveBackground.ShouldBe(LightChrome.PanelBg);
        c.ActiveBackground.ShouldBe(LightChrome.HeaderBg);
        c.Separator.ShouldBe(LightChrome.Separator);
        c.ActiveText.ShouldBe(LightChrome.HeaderText);
        c.InactiveText.ShouldBe(LightChrome.DimText);
        c.CloseMark.ShouldBe(LightChrome.BodyText);
    }

    [Fact]
    public void FromPalette_leaves_the_accent_alone_rather_than_theming_it()
    {
        // Selection is the nearest role and mapping it would look reasonable, which is the trap: the
        // accent means "the tab you are on" and must not drift with the theme. See TabBarColors remarks.
        TabBarColors.FromPalette(LightChrome).ActiveAccent.ShouldBe(new TabBarColors().ActiveAccent);
    }

    [Fact]
    public void FromPalette_can_be_adjusted_afterwards_for_the_third_surface()
    {
        // The bar draws three surfaces where UiPalette names two, so a consumer that wants idle tabs
        // distinct from the strip says so here rather than DIR.Lib inventing a blended tone.
        var idle = new RGBAColor32(0xe4, 0xe4, 0xe8, 0xff);

        var c = TabBarColors.FromPalette(LightChrome) with { InactiveBackground = idle };

        c.InactiveBackground.ShouldBe(idle);
        c.BarBackground.ShouldBe(LightChrome.PanelBg);
    }

    [Fact]
    public void A_palette_derived_bar_paints_the_themed_strip()
    {
        var renderer = new RgbaImageRenderer(400, 40);
        var bar = NewBar(renderer);
        bar.Colors = TabBarColors.FromPalette(LightChrome);

        bar.Render(contentLeft: 0, viewportW: 400, ["One", "Two"], activeIndex: 0);

        SampleEmptyBarArea(renderer).ShouldBe(LightChrome.PanelBg);
    }

    [Fact]
    public void An_override_leaves_the_colours_it_does_not_name_at_their_defaults()
    {
        // A `record` with init properties is why a consumer can theme the surfaces and say nothing about
        // the accent — which is the intended usage, the accent being semantic rather than decorative.
        var themed = new TabBarColors { BarBackground = new RGBAColor32(0xf2, 0xf2, 0xf4, 0xff) };

        themed.ActiveAccent.ShouldBe(new TabBarColors().ActiveAccent);
        themed.CloseMark.ShouldBe(new TabBarColors().CloseMark);
    }
}
