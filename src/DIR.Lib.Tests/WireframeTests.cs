using System;
using System.Linq;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="WireframeRenderer"/> records what a frame puts where, and <see cref="Wireframe.Check"/> says what
/// of it a person would see as broken. Each rule is shown firing on the fault it is for and staying quiet on
/// the layout beside it that is sound.
/// </summary>
public class WireframeTests
{
    private const float FontSize = 12f;
    private static readonly RGBAColor32 Ink = new RGBAColor32(0xff, 0xff, 0xff, 0xff);
    private static readonly RGBAColor32 Panel = new RGBAColor32(0x20, 0x20, 0x20, 0xff);

    /// <summary>Half an em a character, so a test states a text's width by its length and loads no font.</summary>
    private sealed class FixedPitch(uint w, uint h) : WireframeRenderer(w, h)
    {
        public override (float Width, float Height) MeasureText(ReadOnlySpan<char> text, string fontFamily, float fontSize)
            => (text.Length * fontSize * 0.5f, fontSize);
    }

    private sealed class Widget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public void Render(Layout.Node root, RectF32 bounds, Action<Widget>? byHand = null)
        {
            BeginFrame();
            RenderLayout(root, bounds, fontPath: "wire", scale: DesignScale.One);
            byHand?.Invoke(this);
        }

        public void Text(string text, float x, float y, float w, float h, TextAlign align = TextAlign.Near)
            => DrawText(text.AsSpan(), "wire", x, y, w, h, FontSize, Ink, align, TextAlign.Center);

        public void Clip(float x, float y, float w, float h) => PushClip(x, y, w, h);

        public void Unclip() => PopClip();
    }

    private static Layout.Node Button(string label)
        => Layout.Builder.Text(label, FontSize, Ink, TextAlign.Center, TextAlign.Center)
            .WFixed(60f).HStar().Clickable(new HitResult.ButtonHit(label), _ => { });

    private static string[] Check(FixedPitch renderer, WireframeOptions? options = null)
        => [.. Wireframe.Check(renderer.Frame, options).Select(f => $"{f.Rule}: {f.Message}")];

    // The row the atlas's strip was: a readout drawn by hand from the left into a slot, controls packed from the
    // right. Wide, the two are apart; narrow, the readout runs across the controls.
    private static FixedPitch ReadoutAndControls(uint width, bool clipped = false)
    {
        var renderer = new FixedPitch(width, 24);
        var widget = new Widget(renderer);
        renderer.Begin();
        widget.Render(
            Layout.Builder.HStack(Layout.Builder.Spacer().WStar(), Button("-1h"), Button("+1h")),
            new RectF32(0, 0, width, 24),
            w =>
            {
                var slot = width - 120f;
                if (clipped)
                {
                    w.Clip(0, 0, slot, 24);
                }
                // 40 characters at half an em of 12 px: 240 px of ink.
                w.Text(new string('x', 40), 0, 0, slot, 24);
                if (clipped)
                {
                    w.Unclip();
                }
            });
        return renderer;
    }

    [Fact]
    public void ARowWithRoomForBothEndsIsSound()
    {
        using var renderer = ReadoutAndControls(width: 400);
        Check(renderer).ShouldBeEmpty();
    }

    [Fact]
    public void TextDrawnByHandAcrossAControlIsFound()
    {
        using var renderer = ReadoutAndControls(width: 300);

        var findings = Check(renderer);
        findings.ShouldContain(f => f.StartsWith("TextOverRegion") && f.Contains("-1h"));
        findings.ShouldContain(f => f.StartsWith("TextOverText"), "the readout also runs over the button's own label");
    }

    [Fact]
    public void TextAClipCutWithoutAnEllipsisIsFound()
    {
        using var renderer = ReadoutAndControls(width: 300, clipped: true);

        var findings = Check(renderer);
        findings.Length.ShouldBe(1, string.Join("\n", findings));
        findings[0].ShouldStartWith("CutText");
    }

    [Fact]
    public void TextThatEndsInAnEllipsisIsNotACut()
    {
        using var renderer = new FixedPitch(200, 24);
        var widget = new Widget(renderer);
        renderer.Begin();
        widget.Render(Layout.Builder.Spacer(), new RectF32(0, 0, 200, 24), w =>
        {
            w.Clip(0, 0, 100, 24);
            w.Text(new string('x', 39) + "…", 0, 0, 100, 24);
            w.Unclip();
        });

        Check(renderer).ShouldBeEmpty();
    }

    [Fact]
    public void TwoLabelsSharingAPlaceAreFoundAndAShadowIsNot()
    {
        using var renderer = new FixedPitch(300, 24);
        var widget = new Widget(renderer);
        renderer.Begin();
        widget.Render(Layout.Builder.Spacer(), new RectF32(0, 0, 300, 24), w =>
        {
            // 10 characters: 60 px each, 40 px apart.
            w.Text("0123456789", 10, 0, 60, 24);
            w.Text("abcdefghij", 50, 0, 60, 24);
            // A shadow: the same words a pixel off.
            w.Text("shadow", 200, 0, 40, 24);
            w.Text("shadow", 201, 1, 40, 24);
        });

        var findings = Check(renderer);
        findings.Length.ShouldBe(1, string.Join("\n", findings));
        findings[0].ShouldStartWith("TextOverText");
        findings[0].ShouldContain("abcdefghij");
    }

    [Fact]
    public void WhatAnOpaquePanelWasPaintedOverCollidesWithNothing()
    {
        using var renderer = new FixedPitch(300, 100);
        var widget = new Widget(renderer);
        renderer.Begin();
        // The page: a button and a label. Then a popover over both, with a label and a button of its own.
        widget.Render(
            Layout.Builder.HStack(Button("page"), Layout.Builder.Text("under the popover", FontSize, Ink).WStar().HStar()),
            new RectF32(0, 40, 300, 24));
        var popover = Layout.Builder.HStack(
                Layout.Builder.Text("popover", FontSize, Ink).WStar().HStar(), Button("ok"))
            .Bg(Panel);
        var page = renderer.Frame;
        widget.Render(popover, new RectF32(30, 30, 200, 44));

        // The second Render began a new frame for the widget's regions, not for the renderer: both are recorded.
        renderer.Frame.Texts.Length.ShouldBeGreaterThan(page.Texts.Length);
        Check(renderer).ShouldBeEmpty();

        // The same two layers with no panel between them do collide.
        renderer.Begin();
        widget.Render(
            Layout.Builder.HStack(Button("page"), Layout.Builder.Text("under the popover", FontSize, Ink).WStar().HStar()),
            new RectF32(0, 40, 300, 24));
        widget.Render(
            Layout.Builder.HStack(Layout.Builder.Text("popover", FontSize, Ink).WStar().HStar(), Button("ok")),
            new RectF32(30, 30, 200, 44));
        Check(renderer).ShouldNotBeEmpty();
    }

    [Fact]
    public void TwoControlsSharingAPlaceAreFoundAndOneInsideAnotherIsNot()
    {
        using var renderer = new FixedPitch(300, 60);
        var widget = new Widget(renderer);
        renderer.Begin();
        // A row that is itself a control, holding a button: nested, sound.
        widget.Render(
            Layout.Builder.HStack(Layout.Builder.Spacer().WStar(), Button("in")).Clickable(new HitResult.ButtonHit("row"), _ => { }),
            new RectF32(0, 0, 300, 24));
        Check(renderer, new WireframeOptions { Rules = WireframeRules.RegionOverRegion }).ShouldBeEmpty();

        // Two rows 30 px wide of overlap.
        renderer.Begin();
        widget.Render(Layout.Builder.HStack(Button("a")), new RectF32(0, 30, 60, 24));
        widget.Render(Layout.Builder.HStack(Button("b")), new RectF32(30, 30, 60, 24));
        var findings = Check(renderer, new WireframeOptions { Rules = WireframeRules.RegionOverRegion });
        findings.Length.ShouldBe(1, string.Join("\n", findings));
    }

    [Fact]
    public void WhatReachesPastTheSurfaceIsFound()
    {
        using var renderer = new FixedPitch(100, 24);
        var widget = new Widget(renderer);
        renderer.Begin();
        widget.Render(Layout.Builder.HStack(Button("a"), Button("b")), new RectF32(0, 0, 120, 24));

        var findings = Check(renderer);
        findings.ShouldAllBe(f => f.StartsWith("OutsideBounds"));
        findings.ShouldContain(f => f.Contains("Action = b"));
    }

    [Fact]
    public void TextTheCallerNamesIsLeftAlone()
    {
        using var renderer = ReadoutAndControls(width: 300);

        Check(renderer, new WireframeOptions { IgnoreText = t => t.Text.StartsWith('x') }).ShouldBeEmpty();
    }
}
