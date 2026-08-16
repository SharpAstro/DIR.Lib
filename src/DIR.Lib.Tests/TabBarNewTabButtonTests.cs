using DIR.Lib;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// Covers <see cref="TabBar.ShowNewTabButton"/>: that the + lands where the tabs stop, that it is
/// reported by <see cref="TabBar.HitNewTabButton"/> and by nothing else, and that it stays off screen
/// rather than under the clip when the tabs have used the width up.
/// </summary>
/// <remarks>
/// Widths come from a stub oracle (half the font size per character) rather than a real face, so the
/// expected geometry is arithmetic instead of a baseline that moves with a font update — the same
/// approach <see cref="LayoutTextFitTests"/> takes.
/// </remarks>
public class TabBarNewTabButtonTests
{
    private const float Height = 30f;   // TabBar.BaseHeight at scale 1, and so the + button's side

    /// <summary>Half the font size per char, and no glyph rasterization — this is a geometry test.</summary>
    private sealed class StubRenderer(uint w, uint h) : RgbaImageRenderer(w, h)
    {
        public override (float Width, float Height) MeasureText(ReadOnlySpan<char> text, string fontFamily, float fontSize)
            => (text.Length * fontSize * 0.5f, fontSize);

        public override void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize,
            RGBAColor32 fontColor, in RectInt layout, TextAlign horizAlign = TextAlign.Near,
            TextAlign vertAlign = TextAlign.Center)
        { }
    }

    private static TabBar<RgbaImage> NewBar(Renderer<RgbaImage> renderer)
    {
        const string path = "stub.ttf";
        return new TabBar<RgbaImage>(renderer)
        {
            FontPath = path,
            FontFallback = new FontFallbackResolver(path, []),
            ShowNewTabButton = true,
        };
    }

    // Every title here measures well under TabBar's minimum, so each tab is exactly MinTabW wide.
    private const float MinTabW = 92f;

    [Fact]
    public void SitsImmediatelyAfterTheLastTab()
    {
        var renderer = new StubRenderer(600, 40);
        var bar = NewBar(renderer);
        bar.Render(contentStart: 0f, viewportEnd: 600f, ["a", "b"], activeIndex: 0);

        var afterTabs = MinTabW * 2;
        bar.HitNewTabButton(afterTabs + 1f, Height / 2f).ShouldBeTrue();
        bar.HitNewTabButton(afterTabs + Height - 1f, Height / 2f).ShouldBeTrue();
        // Not before the last tab's right edge, and not past its own square.
        bar.HitNewTabButton(afterTabs - 2f, Height / 2f).ShouldBeFalse();
        bar.HitNewTabButton(afterTabs + Height + 2f, Height / 2f).ShouldBeFalse();
    }

    [Fact]
    public void FollowsTheContentLeftOffset()
    {
        // A host with a sidebar hands the bar a left offset; the + has to move with the tabs, not stay
        // measured from the window edge.
        var renderer = new StubRenderer(600, 40);
        var bar = NewBar(renderer);
        bar.Render(contentStart: 120f, viewportEnd: 600f, ["a"], activeIndex: 0);

        bar.HitNewTabButton(120f + MinTabW + 1f, Height / 2f).ShouldBeTrue();
        bar.HitNewTabButton(MinTabW + 1f, Height / 2f).ShouldBeFalse();
    }

    [Fact]
    public void IsNotClaimedByTheTabHitTest()
    {
        var renderer = new StubRenderer(600, 40);
        var bar = NewBar(renderer);
        bar.Render(contentStart: 0f, viewportEnd: 600f, ["a"], activeIndex: 0);

        var onButton = MinTabW + Height / 2f;
        bar.HandleMouseDown(onButton, Height / 2f).ShouldBeNull();   // tabs only — the host asks separately
        bar.HitNewTabButton(onButton, Height / 2f).ShouldBeTrue();
    }

    [Fact]
    public void IsAbsentBelowTheBar()
    {
        var renderer = new StubRenderer(600, 40);
        var bar = NewBar(renderer);
        bar.Render(contentStart: 0f, viewportEnd: 600f, ["a"], activeIndex: 0);

        bar.HitNewTabButton(MinTabW + 2f, Height + 4f).ShouldBeFalse();
    }

    [Fact]
    public void IsAbsentWhenNotAskedFor()
    {
        var renderer = new StubRenderer(600, 40);
        var bar = NewBar(renderer);
        bar.ShowNewTabButton = false;
        bar.Render(contentStart: 0f, viewportEnd: 600f, ["a"], activeIndex: 0);

        bar.HitNewTabButton(MinTabW + 2f, Height / 2f).ShouldBeFalse();
    }

    [Fact]
    public void IsDroppedRatherThanDrawnUnderTheClipWhenTheTabsFillTheWidth()
    {
        // Two 92 px tabs in a 200 px strip leave 16 px — less than the button's 30 — so it is skipped.
        // Reporting a hit there would hand the host clicks on a control the clip has hidden.
        var renderer = new StubRenderer(200, 40);
        var bar = NewBar(renderer);
        bar.Render(contentStart: 0f, viewportEnd: 200f, ["a", "b"], activeIndex: 0);

        bar.HitNewTabButton(190f, Height / 2f).ShouldBeFalse();
        bar.HitNewTabButton(MinTabW * 2 + 1f, Height / 2f).ShouldBeFalse();
    }

    [Fact]
    public void MarksItselfActiveWithTheSameAccentAnActiveTabUses()
    {
        // The accent strip is what tells a reader which page is showing. With a new-tab page behind the
        // +, that has to be the + and not a tab, or the bar contradicts the window.
        var renderer = new StubRenderer(600, 40);
        var bar = NewBar(renderer);
        bar.NewTabActive = true;
        bar.Colors = new TabBarColors { ActiveAccent = new RGBAColor32(0xff, 0x00, 0x00, 0xff) };
        bar.Render(contentStart: 0f, viewportEnd: 600f, ["a"], activeIndex: 0);

        // Top-left pixel of the button, where the 2 px accent strip is painted.
        PixelAt(renderer.Surface, (int)MinTabW + 2, 0).ShouldBe(new RGBAColor32(0xff, 0x00, 0x00, 0xff));
    }

    // RgbaImage exposes its buffer rather than a pixel accessor (see DrawLineTests.IsLit).
    private static RGBAColor32 PixelAt(RgbaImage image, int x, int y)
    {
        var o = (y * image.Width + x) * 4;
        return new RGBAColor32(image.Pixels[o], image.Pixels[o + 1], image.Pixels[o + 2], image.Pixels[o + 3]);
    }
}
