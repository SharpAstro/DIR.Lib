using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// The <see cref="UiPalette"/> role model itself. The TabBar projection is NOT retested here --
/// <see cref="TabBarColorsTests"/> owns it, including the deliberate decision to leave
/// <see cref="TabBarColors.ActiveAccent"/> out of <see cref="TabBarColors.FromPalette"/>.
/// </summary>
public sealed class UiPaletteTests
{
    // The eleven roles every palette must state. Anything derived is deliberately left unset here,
    // so the tests below observe the defaults rather than values a fixture supplied.
    private static UiPalette Minimal(RGBAColor32? contentBg = null) => new()
    {
        ContentBg = contentBg ?? new RGBAColor32(0x10, 0x13, 0x18, 0xff),
        PanelBg = new RGBAColor32(0x17, 0x1b, 0x22, 0xff),
        HeaderBg = new RGBAColor32(0x1e, 0x23, 0x2c, 0xff),
        Separator = new RGBAColor32(0x2a, 0x30, 0x39, 0xff),
        BodyText = new RGBAColor32(0xe2, 0xe6, 0xec, 0xff),
        DimText = new RGBAColor32(0x8b, 0x93, 0x9f, 0xff),
        Accent = new RGBAColor32(0x7c, 0xc4, 0xff, 0xff),
        Selection = new RGBAColor32(0x3c, 0x44, 0x4f, 0xff),
        Info = new RGBAColor32(0x7c, 0xc4, 0xff, 0xff),
        Warn = new RGBAColor32(0xe8, 0xa3, 0x3c, 0xff),
        Error = new RGBAColor32(0xff, 0x7a, 0x70, 0xff),
    };

    // The five optional roles fall back to a stated one rather than to transparent black, so a
    // palette that has only one rule weight or one accent need not invent a second.
    [Fact]
    public void UnstatedRolesFallBackToTheRoleTheyExtend()
    {
        var p = Minimal();

        p.SeparatorStrong.ShouldBe(p.Separator);
        p.HeaderText.ShouldBe(p.Accent);
        p.AccentAlt.ShouldBe(p.Accent);
        p.Focus.ShouldBe(p.Accent);
        // Success defaults to Accent rather than to a green, because a palette that cannot spend
        // the green channel still needs a positive mark and the accent is the right one.
        p.Success.ShouldBe(p.Accent);
    }

    [Fact]
    public void RaisedBgAndEdgeDefaultToPanelBgAndSeparatorAndCanBeStated()
    {
        var p = Minimal();
        p.RaisedBg.ShouldBe(p.PanelBg);
        p.Edge.ShouldBe(p.Separator);

        var raised = new RGBAColor32(1, 2, 3, 255);
        var edge = new RGBAColor32(4, 5, 6, 255);
        var q = p with { RaisedBg = raised, Edge = edge };
        q.RaisedBg.ShouldBe(raised);
        q.Edge.ShouldBe(edge);
    }

    [Fact]
    public void MixIsExactAtZeroAndOneHundredPercent()
    {
        var a = new RGBAColor32(10, 20, 30, 40);
        var b = new RGBAColor32(200, 150, 100, 250);
        RGBAColor32.Mix(a, b, 0f).ShouldBe(a);
        RGBAColor32.Mix(a, b, 1f).ShouldBe(b);
        RGBAColor32.Mix(a, b, -3f).ShouldBe(a);
        RGBAColor32.Mix(a, b, 7f).ShouldBe(b);
        RGBAColor32.Mix(new RGBAColor32(0, 0, 0, 255), new RGBAColor32(200, 100, 50, 255), 0.5f)
            .ShouldBe(new RGBAColor32(100, 50, 25, 255));
    }

    [Fact]
    public void ContrastRatioIsTwentyOneForBlackOnWhiteAndOneForEqualColours()
    {
        RGBAColor32.ContrastRatio(new RGBAColor32(0, 0, 0, 255), new RGBAColor32(255, 255, 255, 255)).ShouldBe(21f, 0.01f);
        RGBAColor32.ContrastRatio(new RGBAColor32(9, 9, 9, 255), new RGBAColor32(9, 9, 9, 255)).ShouldBe(1f, 0.001f);
    }

    [Fact]
    public void OneHueIsTrueForARedOnBlackNightPaletteAndFalseForDarkAndLightMultiHue()
    {
        static RGBAColor32 C(byte r, byte g, byte b) => new(r, g, b, 0xff);
        var night = new UiPalette
        {
            ContentBg = C(0, 0, 0), PanelBg = C(0x10, 0, 0), HeaderBg = C(0x18, 0, 0), Separator = C(0x40, 0, 0),
            BodyText = C(0xc0, 0x20, 0x20), DimText = C(0x80, 0x10, 0x10), Accent = C(0xff, 0x30, 0x30), Selection = C(0x50, 0, 0),
            Info = C(0xc0, 0x20, 0x20), Warn = C(0xff, 0x40, 0x30), Error = C(0xff, 0x20, 0x20),
        };
        night.IsOneHue.ShouldBeTrue();

        Minimal().IsOneHue.ShouldBeFalse();

        var light = Minimal(new RGBAColor32(0xf5, 0xf6, 0xf8, 0xff)) with
        {
            BodyText = new RGBAColor32(0x20, 0x24, 0x2c, 0xff),
            Info = new RGBAColor32(0x1a, 0x6f, 0xd0, 0xff),
            Warn = new RGBAColor32(0xb3, 0x6b, 0x00, 0xff),
            Error = new RGBAColor32(0xc6, 0x28, 0x28, 0xff),
            Success = new RGBAColor32(0x1e, 0x8e, 0x3e, 0xff),
        };
        light.IsOneHue.ShouldBeFalse();
    }

    [Fact]
    public void AStatedOptionalRoleWins()
    {
        var strong = new RGBAColor32(0x3c, 0x44, 0x4f, 0xff);
        var p = Minimal() with { SeparatorStrong = strong };

        p.SeparatorStrong.ShouldBe(strong);
        p.HeaderText.ShouldBe(p.Accent);  // still defaulted
    }

    // `with` clones through the copy constructor, which copies the nullable BACKING fields. If it
    // copied the resolved property values instead, an unstated role would silently become stated
    // on the first clone and stop tracking the role it extends.
    [Fact]
    public void CloningKeepsAnUnstatedRoleUnstated()
    {
        var recoloured = Minimal() with { Accent = new RGBAColor32(0xff, 0x6a, 0x00, 0xff) };

        recoloured.AccentAlt.ShouldBe(recoloured.Accent);
        recoloured.HeaderText.ShouldBe(recoloured.Accent);
        recoloured.Focus.ShouldBe(recoloured.Accent);
    }

    // IsDark is computed from ContentBg rather than stored, so it cannot disagree with the colours
    // it describes -- which a hand-set flag can, and eventually does.
    [Theory]
    [InlineData(0x00, 0x00, 0x00, true)]   // a dark-adaptation palette
    [InlineData(0x10, 0x13, 0x18, true)]   // a dark chrome
    [InlineData(0xf2, 0xf4, 0xf6, false)]  // a light chrome
    [InlineData(0xff, 0xff, 0xff, false)]
    public void IsDarkFollowsTheContentBackground(byte r, byte g, byte b, bool expected)
        => Minimal(new RGBAColor32(r, g, b, 0xff)).IsDark.ShouldBe(expected);

    [Fact]
    public void MenuColorsFromPaletteTakesTheChromeRoles()
    {
        var p = Minimal();
        var m = MenuColors.FromPalette(p);

        m.TitleColor.ShouldBe(p.Accent);
        m.PromptColor.ShouldBe(p.BodyText);
        m.ItemColor.ShouldBe(p.DimText);
        m.SelectedBackground.ShouldBe(p.Selection);
        m.SelectedForeground.ShouldBe(p.BodyText);
    }

    // Same override path TabBarColors has: take the roles, then swap the one thing the app knows
    // better. A record, so `with` is the mechanism in both cases.
    [Fact]
    public void MenuColorsCanBeAdjustedAfterProjection()
    {
        var gold = new RGBAColor32(0xff, 0xd7, 0x00, 0xff);
        var m = MenuColors.FromPalette(Minimal()) with { SelectedForeground = gold };

        m.SelectedForeground.ShouldBe(gold);
        m.TitleColor.ShouldBe(Minimal().Accent);
    }

    private static UiPalette Light() => Minimal(new RGBAColor32(0xf2, 0xf4, 0xf6, 0xff)) with
    {
        PanelBg = new RGBAColor32(0xff, 0xff, 0xff, 0xff),
        BodyText = new RGBAColor32(0x1a, 0x1d, 0x23, 0xff),
        DimText = new RGBAColor32(0x5c, 0x64, 0x70, 0xff),
        Accent = new RGBAColor32(0x0b, 0x5c, 0xc4, 0xff),
    };

    [Fact]
    public void ControlRolesStepLighterOnDarkAndDarkerOnLight()
    {
        var d = Minimal();
        d.ControlFill.Luminance.ShouldBeGreaterThan(d.PanelBg.Luminance);
        d.ControlFillHover.Luminance.ShouldBeGreaterThan(d.ControlFill.Luminance);
        d.ControlFillPressed.Luminance.ShouldBeGreaterThan(d.ControlFillHover.Luminance);
        d.ControlEdge.Luminance.ShouldBeGreaterThan(d.ControlFill.Luminance);

        var l = Light();
        l.ControlFill.Luminance.ShouldBeLessThan(l.PanelBg.Luminance);
        l.ControlFillHover.Luminance.ShouldBeLessThan(l.ControlFill.Luminance);
        l.ControlFillPressed.Luminance.ShouldBeLessThan(l.ControlFillHover.Luminance);
        l.ControlEdge.Luminance.ShouldBeLessThan(l.ControlFill.Luminance);
    }

    [Fact]
    public void ControlEdgeReachesThreeToOneAgainstPanelBgAndNoRoleIsTheAccent()
    {
        foreach (var p in new[] { Minimal(), Light() })
        {
            RGBAColor32.ContrastRatio(p.ControlEdge, p.PanelBg).ShouldBeGreaterThanOrEqualTo(3f);
            p.ControlFill.ShouldNotBe(p.Accent);
            p.ControlFillHover.ShouldNotBe(p.Accent);
            p.ControlFillPressed.ShouldNotBe(p.Accent);
            p.ControlEdge.ShouldNotBe(p.Accent);
        }
    }

    [Fact]
    public void ControlRolesCanBeStated()
    {
        var c = new RGBAColor32(1, 2, 3, 0xff);
        var p = Minimal() with { ControlFill = c, ControlEdge = c };
        p.ControlFill.ShouldBe(c);
        p.ControlEdge.ShouldBe(c);
    }

    [Fact]
    public void ControlRadiusDefaultsToFourAndCanBeStated()
    {
        default(UiMetrics).ControlRadius.ShouldBe(4f);
        (default(UiMetrics) with { ControlRadius = 9f }).ControlRadius.ShouldBe(9f);
    }

    [Fact]
    public void ThemeControlsDeriveFromPaletteAndMetrics()
    {
        var p = Light();
        var theme = new UiTheme { Palette = p, Metrics = new UiMetrics(14, 8, 30, 24, 28) };
        var c = theme.Controls;

        c.ShouldBeSameAs(theme.Controls);
        c.Accent.RestFill.ShouldBe(p.Accent);
        c.Accent.Ink.ShouldBe(p.InkOn(p.Accent));
        c.Accent.Edge.ShouldBeNull();
        c.Standard.RestFill.ShouldBe(p.ControlFill);
        c.Standard.Edge.ShouldBe(p.ControlEdge);
        c.Subtle.RestFill.ShouldBeNull();
        c.Subtle.HoverFill.ShouldBe(p.ControlFillHover);
        c.Standard.Radius.ShouldBe(theme.Metrics.ControlRadius);
        c.Standard.PaddingX.ShouldBe(8f);
        RGBAColor32.ContrastRatio(c.Hyperlink.Color, p.PanelBg).ShouldBeGreaterThanOrEqualTo(4.5f);
        c.Hyperlink.UnderlineOnHover.ShouldBeTrue();
    }

    [Fact]
    public void ThemeControlsOverrideOneKindWithWith()
    {
        var theme = new UiTheme { Palette = Minimal(), Metrics = new UiMetrics(14, 8, 30, 24, 28) };
        var std = theme.Controls.Standard with { Radius = 12f };
        var t2 = theme with { Controls = theme.Controls with { Standard = std } };

        t2.Controls.Standard.Radius.ShouldBe(12f);
        t2.Controls.Accent.ShouldBe(theme.Controls.Accent);
    }

    // design-language section 6 and the UI-changes rule: a control edge, so the focus ring, reads at 3:1 on the ground.
    [Theory]
    [InlineData("dark")]
    [InlineData("light")]
    public void Focus_reads_at_3_to_1_against_ContentBg_in_each_built_in_palette(string which)
    {
        var p = which == "dark" ? NotificationsRenderTests.Dark : NotificationsRenderTests.Light;
        RGBAColor32.ContrastRatio(p.Focus, p.ContentBg).ShouldBeGreaterThanOrEqualTo(3f, which);
    }
}
