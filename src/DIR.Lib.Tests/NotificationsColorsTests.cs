using DIR.Lib.Notifications;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>The tint roles of <see cref="NotificationsColors"/> and how a <see cref="UiTheme"/> fills them.</summary>
public sealed class NotificationsColorsTests
{
    private static RGBAColor32 C(byte r, byte g, byte b) => new(r, g, b, 0xff);

    private static UiPalette MultiHue() => new()
    {
        ContentBg = C(0x10, 0x13, 0x18),
        PanelBg = C(0x17, 0x1b, 0x22),
        HeaderBg = C(0x1e, 0x23, 0x2c),
        Separator = C(0x2a, 0x30, 0x39),
        BodyText = C(0xe2, 0xe6, 0xec),
        DimText = C(0x8b, 0x93, 0x9f),
        Accent = C(0x7c, 0xc4, 0xff),
        Selection = C(0x3c, 0x44, 0x4f),
        Info = C(0x7c, 0xc4, 0xff),
        Warn = C(0xe8, 0xa3, 0x3c),
        Error = C(0xff, 0x7a, 0x70),
        Success = C(0x5c, 0xd0, 0x7a),
    };

    private static UiPalette NightRed() => new()
    {
        ContentBg = C(0, 0, 0),
        PanelBg = C(0x10, 0, 0),
        HeaderBg = C(0x18, 0, 0),
        Separator = C(0x40, 0, 0),
        BodyText = C(0xc0, 0x20, 0x20),
        DimText = C(0x80, 0x10, 0x10),
        Accent = C(0xff, 0x30, 0x30),
        Selection = C(0x50, 0, 0),
        Info = C(0xc0, 0x20, 0x20),
        Warn = C(0xff, 0x40, 0x30),
        Error = C(0xff, 0x20, 0x20),
    };

    private static UiMetrics Metrics(float radius = 6f) => new UiMetrics(14, 8, 28, 24, 28) with { CornerRadius = radius };

    [Fact]
    public void FromThemeFillsEveryRole()
    {
        var p = MultiHue() with { RaisedBg = C(0x22, 0x28, 0x31), Edge = C(0x55, 0x5d, 0x68) };
        var shadow = new DIR.Lib.Layout.ShadowStyle { OffsetY = 4, Blur = 12, Color = new RGBAColor32(0, 0, 0, 100) };
        var c = NotificationsColors.From(new UiTheme { Palette = p, Metrics = Metrics(10) with { Shadow = shadow } });

        c.Info.ShouldBe(p.Info);
        c.Warn.ShouldBe(p.Warn);
        c.Error.ShouldBe(p.Error);
        c.Success.ShouldBe(p.Success);
        c.Surface.ShouldBe(p.HeaderBg);
        c.Card.ShouldBe(p.PanelBg);
        c.Text.ShouldBe(p.BodyText);
        c.Dim.ShouldBe(p.DimText);
        c.Separator.ShouldBe(p.Separator);
        c.Raised.ShouldBe(p.RaisedBg);
        c.Edge.ShouldBe(p.Edge);
        c.CornerRadius.ShouldBe(10f);
        c.Shadow.ShouldBe(shadow);
        c.OneHue.ShouldBeFalse();
        c.ToastShadow.ShouldBe(shadow);
    }

    [Fact]
    public void FromPaletteAloneKeepsWorking()
    {
        var c = NotificationsColors.From(MultiHue());
        c.Raised.ShouldBe(C(0x17, 0x1b, 0x22));
        c.CornerRadius.ShouldBe(6f);
    }

    [Fact]
    public void AnInitializerWrittenFor1186StillCompilesAndDefaultsTheNewRoles()
    {
        var c = new NotificationsColors
        {
            Info = C(1, 2, 3), Warn = C(4, 5, 6), Error = C(7, 8, 9), Success = C(10, 11, 12),
            Surface = C(20, 20, 20), Card = C(30, 30, 30), Text = C(250, 250, 250), Dim = C(120, 120, 120),
            Separator = C(60, 60, 60), Track = C(70, 70, 70),
        };
        c.Raised.ShouldBe(c.Card);
        c.Edge.ShouldBe(c.Separator);
        c.OneHue.ShouldBeFalse();
        c.OnSeverity.ShouldBeNull();
    }

    [Fact]
    public void TintedFillAndEdgeMixTheSeverityIntoRaised()
    {
        var c = NotificationsColors.From(MultiHue());
        c.TintedFill(NotificationSeverity.Error).ShouldBe(RGBAColor32.Mix(c.Raised, c.Error, 0.15f));
        c.TintedEdge(NotificationSeverity.Warn).ShouldBe(c.Warn);
        c.TintedFill(NotificationSeverity.Error).ShouldNotBe(c.Raised);
    }

    [Fact]
    public void OneHuePaletteFallsBackToPlainRaisedAnEdgeAt3To1AndNoShadow()
    {
        var c = NotificationsColors.From(NightRed());
        c.OneHue.ShouldBeTrue();
        foreach (var s in new[] { NotificationSeverity.Info, NotificationSeverity.Warn, NotificationSeverity.Error, NotificationSeverity.Success })
        {
            c.TintedFill(s).ShouldBe(c.Raised);
            c.TintedEdge(s).ShouldBe(c.EdgeOnPage());
            RGBAColor32.ContrastRatio(c.TintedEdge(s), c.Page).ShouldBeGreaterThanOrEqualTo(3f);
        }
        c.ToastShadow.ShouldBeNull();
    }

    [Fact]
    public void AMultiHueEdgeThatMissesThreeToOneAgainstThePageIsMovedTowardText()
    {
        var c = NotificationsColors.From(NightRed()) with { OneHue = false };
        var yellow = c with { Page = c.Warn, Text = new RGBAColor32(0, 0, 0, 255) };
        RGBAColor32.ContrastRatio(yellow.Warn, yellow.Page).ShouldBeLessThan(3f);
        var edge = yellow.TintedEdge(NotificationSeverity.Warn);
        edge.ShouldNotBe(yellow.Warn);
        RGBAColor32.ContrastRatio(edge, yellow.Page).ShouldBeGreaterThanOrEqualTo(3f);
    }

    [Fact]
    public void AFullyTransparentShadowIsNone()
    {
        var c = NotificationsColors.From(MultiHue()) with { Shadow = new DIR.Lib.Layout.ShadowStyle { Color = new RGBAColor32(0, 0, 0, 0) } };
        c.ToastShadow.ShouldBeNull();
    }

    [Fact]
    public void InkOnFilledSeverityContrastsAndAStatedOneWins()
    {
        var c = NotificationsColors.From(MultiHue());
        foreach (var s in new[] { NotificationSeverity.Info, NotificationSeverity.Warn, NotificationSeverity.Error, NotificationSeverity.Success })
            RGBAColor32.ContrastRatio(c.InkOn(s), c.Of(s)).ShouldBeGreaterThanOrEqualTo(4.5f);
        var ink = C(9, 9, 9);
        (c with { OnSeverity = ink }).InkOn(NotificationSeverity.Info).ShouldBe(ink);
    }

    [Fact]
    public void UiMetricsDefaultsSurviveDefaultStruct()
    {
        var m = default(UiMetrics);
        m.CornerRadius.ShouldBe(6f);
        m.Shadow.ShouldBe(UiMetrics.DefaultShadow);
        UiMetrics.DefaultShadow.OffsetX.ShouldBe(0f);
        UiMetrics.DefaultShadow.OffsetY.ShouldBe(1f);
        UiMetrics.DefaultShadow.Blur.ShouldBe(2f);
        UiMetrics.DefaultShadow.Color.Alpha.ShouldBe((byte)140);
    }
}
