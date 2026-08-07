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
}
