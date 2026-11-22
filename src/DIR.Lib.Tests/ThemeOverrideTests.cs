using System;
using System.Collections.Generic;
using System.Linq;
using DIR.Lib;
using DIR.Lib.Layout;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// One <c>with</c> on <see cref="UiMetrics.ControlRadius"/> and <see cref="UiTheme.Controls"/> restyles every button: with
/// ControlRadius 0 and a Standard style with no edge the button renders square and ringless, the default does not.
/// </summary>
public class ThemeOverrideTests
{
    private static readonly UiTheme Default = new()
    {
        Palette = NotificationsRenderTests.Dark,
        Metrics = new UiMetrics(14f, 6f, 28f, 24f, 28f),
    };

    // The radius comes from the metric alone: no Radius is set by hand on any style.
    private static readonly UiTheme ZeroRadius = Default with { Metrics = Default.Metrics with { ControlRadius = 0f } };

    private static readonly UiTheme Square = ZeroRadius with
    {
        Controls = ZeroRadius.Controls with { Standard = ZeroRadius.Controls.Standard with { Edge = null } },
    };

    private sealed class StubRenderer(uint w, uint h) : RgbaImageRenderer(w, h)
    {
        public override (float Width, float Height) MeasureText(ReadOnlySpan<char> text, string fontFamily, float fontSize)
            => (text.Length * 8f, 14f);

        public override void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize,
            RGBAColor32 fontColor, in RectInt layout, TextAlign horizAlign = TextAlign.Near,
            TextAlign vertAlign = TextAlign.Center)
        { }
    }

    private sealed class TestWidget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public void Render(Node root)
        {
            BeginFrame();
            RenderLayout(root, new RectF32(0, 0, 200, 60), fontPath: "stub.ttf", scale: DesignScale.One);
        }
    }

    private static RgbaImage Draw(UiTheme theme)
    {
        var r = new StubRenderer(200, 60);
        r.Surface.Clear(default);
        new TestWidget(r).Render(Builder.HStack(Builder.Button("Save", ButtonKind.Standard, () => { }, theme)).HFixed(28f));
        return r.Surface;
    }

    private static RGBAColor32 At(RgbaImage img, int x, int y)
    {
        var i = (y * img.Width + x) * 4;
        return new RGBAColor32(img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2], img.Pixels[i + 3]);
    }

    [Fact]
    public void The_override_is_square_and_ringless_and_the_default_is_neither()
    {
        var def = Draw(Default);
        var sq = Draw(Square);
        var fill = Default.Controls.Standard.RestFill!.Value;

        // The corner pixel: rounded leaves it clear, square paints the fill.
        At(def, 0, 0).ShouldNotBe(fill);
        At(sq, 0, 0).ShouldBe(fill);

        // The middle of the left edge: the default draws a ring there, the override is the plain fill.
        At(def, 0, 14).ShouldNotBe(fill);
        At(sq, 0, 14).ShouldBe(fill);
    }

    [Fact]
    public void The_node_carries_the_override_and_the_default_carries_radius_and_edge()
    {
        var def = Builder.Button("Save", ButtonKind.Standard, () => { }, Default);
        var sq = Builder.Button("Save", ButtonKind.Standard, () => { }, Square);
        def.BorderStyle.ShouldNotBeNull();
        def.CornerRadius.ShouldBe(Default.Metrics.ControlRadius);
        def.CornerRadius.ShouldBeGreaterThan(0f);
        sq.BorderStyle.ShouldBeNull();
        sq.CornerRadius.ShouldBe(0f);
    }

    [Fact]
    public void Overriding_one_kind_leaves_the_others_derived_from_the_new_metrics()
    {
        Square.Controls.Accent.ShouldBe(ZeroRadius.Controls.Accent);
        Square.Controls.Accent.Radius.ShouldBe(0f);
        Square.Controls.Subtle.Radius.ShouldBe(0f);
        Default.Controls.Accent.Radius.ShouldBe(4f);
    }

    [Fact]
    public void A_with_after_Controls_was_read_derives_afresh_not_from_the_copied_cache()
    {
        var t = new UiTheme { Palette = NotificationsRenderTests.Dark, Metrics = new UiMetrics(14f, 6f, 28f, 24f, 28f) };
        _ = t.Controls;
        (t with { Metrics = t.Metrics with { ControlRadius = 0f } }).Controls.Standard.Radius.ShouldBe(0f);
        var light = NotificationsRenderTests.Light;
        (t with { Palette = light }).Controls.Standard.RestFill.ShouldBe(light.ControlFill);
        t.Controls.Standard.Radius.ShouldBe(4f);
    }

    [Fact]
    public void Reading_Controls_does_not_change_equality()
    {
        UiTheme Make() => new() { Palette = NotificationsRenderTests.Dark, Metrics = new UiMetrics(14f, 6f, 28f, 24f, 28f) };
        var a = Make();
        var b = Make();
        a.ShouldBe(b);
        _ = a.Controls;
        a.ShouldBe(b);
        a.Controls.ShouldBeSameAs(a.Controls);
    }

    [Fact]
    public void Edge_width_and_link_gap_come_from_the_metrics()
    {
        var thick = Default with { Metrics = Default.Metrics with { ControlEdgeWidth = 3f, LinkArrowGap = 9f } };
        thick.Controls.Standard.EdgeWidth.ShouldBe(3f);
        thick.Controls.Hyperlink.ArrowGap.ShouldBe(9f);
        Builder.Button("Save", ButtonKind.Standard, () => { }, thick).BorderStyle!.Width.ShouldBe(3f);
    }
}
