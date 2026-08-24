using System;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// The pixel painter fitting a text run to the rect the engine arranged for it, per the run's own
/// <see cref="Layout.Content.Text.Trim"/>.
///
/// <para>What these pin is the failure this closes: <c>DrawText</c> starts at its rect's edge and keeps
/// going, so before the painter fitted anything, a run wider than its rect drew straight over its
/// neighbour — and only at the surface sizes where it happened not to fit, which is the worst way to find
/// out. The engine already owned the rect; the run already declared which half of itself mattered; the
/// painter was the piece not honouring either.</para>
///
/// <para>Widths come from a stub oracle (half the font size per character) rather than a real face, so every
/// expectation here is arithmetic rather than a baseline that shifts with a font update.</para>
/// </summary>
public class LayoutTextFitTests
{
    private const string Font = "stub.ttf";

    /// <summary>Half the font size per char, and records what was finally drawn — so a test asserts on the
    /// string and size that reached the surface, not on an intermediate.</summary>
    private sealed class FitRenderer(uint w, uint h) : RgbaImageRenderer(w, h)
    {
        public List<(string Text, float FontSize)> Runs { get; } = [];

        public override (float Width, float Height) MeasureText(ReadOnlySpan<char> text, string fontFamily, float fontSize)
            => (text.Length * fontSize * 0.5f, fontSize);

        public override void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize,
            RGBAColor32 fontColor, in RectInt layout, TextAlign horizAlign = TextAlign.Near,
            TextAlign vertAlign = TextAlign.Center)
            => Runs.Add((text.ToString(), fontSize));
    }

    private sealed class Widget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public void Render(Layout.Node root, RectF32 bounds)
        {
            BeginFrame();
            RenderLayout(root, bounds, Font, dpiScale: 1f);
        }

        public new float FitFontSize(ReadOnlySpan<char> text, float preferred, float maxWidth,
            float minFontSize = TextFit.DefaultMinFontSize)
            => base.FitFontSize(text, Font, preferred, maxWidth, minFontSize);
    }

    private static (string Text, float FontSize) Paint(string text, float fontSize, float width, TextTrim trim)
    {
        using var renderer = new FitRenderer(400, 40);
        new Widget(renderer).Render(
            Layout.Builder.Text(text, fontSize, trim: trim).Stretch(), new RectF32(0, 0, width, 40));
        return renderer.Runs.ShouldHaveSingleItem();
    }

    // "abcdefghij" at 10f measures 10 * 10 * 0.5 = 50; a 30-wide rect fits six characters.

    [Fact]
    public void ARunThatFits_IsDrawnExactlyAsAuthored()
    {
        // The common case, and the one that must stay free: no truncation, no rescale, one measurement.
        Paint("abcdefghij", 10f, 100f, TextTrim.End).ShouldBe(("abcdefghij", 10f));
    }

    [Fact]
    public void End_DropsTheTailAndKeepsTheHead()
    {
        var (text, fontSize) = Paint("abcdefghij", 10f, 30f, TextTrim.End);
        text.ShouldBe("abcde…");           // 6 chars * 5 = 30, exactly the rect
        fontSize.ShouldBe(10f, "End trims the string; it must not also rescale");
    }

    [Fact]
    public void Start_DropsTheHeadAndKeepsTheTail()
    {
        // The case TextTrim exists for: a path or URL identifies itself at the END.
        Paint("abcdefghij", 10f, 30f, TextTrim.Start).Text.ShouldBe("…fghij");
    }

    [Fact]
    public void Middle_KeepsBothEndsAndDropsTheMiddle()
    {
        // Symmetric by construction: 2k+1 characters must fit six, so k = 2 from each end.
        // The case neither Start nor End covers -- a path needs its root AND its leaf.
        Paint("abcdefghij", 10f, 30f, TextTrim.Middle).Text.ShouldBe("ab…ij");
    }

    [Fact]
    public void Middle_OnARunThatFits_IsUntouched()
    {
        // Same free path as every other policy: one measurement, no cut.
        Paint("abcdefghij", 10f, 100f, TextTrim.Middle).ShouldBe(("abcdefghij", 10f));
    }

    [Fact]
    public void Middle_WithRoomForNothing_IsJustTheEllipsis()
    {
        // 5px fits one character, and one character cannot show two ends, so it shows neither
        // rather than picking an end arbitrarily -- Start and End are the policies that pick.
        Paint("abcdefghij", 10f, 5f, TextTrim.Middle).Text.ShouldBe("…");
    }

    [Fact]
    public void Shrink_KeepsEveryCharacterAndScalesDown()
    {
        // 10 chars must fit 30px: 10 * size * 0.5 <= 30 => size <= 6.
        var (text, fontSize) = Paint("abcdefghij", 10f, 30f, TextTrim.Shrink);
        text.ShouldBe("abcdefghij", "Shrink is for runs where every character carries meaning");
        fontSize.ShouldBe(6f, 0.01);
    }

    [Fact]
    public void None_DrawsTheRunWholeAndLetsItOverflow()
    {
        // The compatibility statement, and the painter's behaviour for EVERY run before it learned to fit:
        // a deliberately overhanging label says so rather than being silently ellipsized.
        Paint("abcdefghij", 10f, 30f, TextTrim.None).ShouldBe(("abcdefghij", 10f));
    }

    [Fact]
    public void Shrink_StopsAtItsFloorAndOverflowsVisiblyRatherThanVanishing()
    {
        // A rect too narrow for the run at ANY sane size: text scaled towards nothing is a worse outcome
        // than text a reader can see is too big for its box, so the floor wins over the fit.
        var (_, fontSize) = Paint("abcdefghij", 10f, 1f, TextTrim.Shrink);
        fontSize.ShouldBe(TextFit.DefaultMinFontSize);
    }

    [Fact]
    public void AZeroWidthRect_LeavesTheRunAlone()
    {
        // Nothing is known about the space (a slot that resolved to nothing, a pre-layout frame), so nothing
        // is given up — the alternative is silently replacing content with "…" on a transient frame.
        Paint("abcdefghij", 10f, 0f, TextTrim.End).ShouldBe(("abcdefghij", 10f));
    }

    [Fact]
    public void FitFontSize_IsTheSameAnswerTheShrinkPolicyGives()
    {
        // The hand-placed draw helpers (status bars, two-ended strips) and the layout painter must not
        // disagree about what fits, or a widget that uses both gets two different sizes for one width.
        using var renderer = new FitRenderer(400, 40);
        var widget = new Widget(renderer);

        widget.FitFontSize("abcdefghij", 10f, 30f).ShouldBe(6f, 0.01);
        widget.FitFontSize("abcdefghij", 10f, 100f).ShouldBe(10f, "a run that fits keeps its size");
        widget.FitFontSize("abcdefghij", 10f, 0f).ShouldBe(10f, "unconstrained means unchanged");
        widget.FitFontSize("", 10f, 1f).ShouldBe(10f, "an empty run always fits");
    }

    /// <summary>
    /// The reason this belongs in the painter rather than in each consumer: two runs sharing a strip. The
    /// trailing control takes its measured width and the label takes the remainder — and the label must then
    /// stay inside that remainder, which is exactly what an un-fitted painter would not do.
    /// </summary>
    [Fact]
    public void TwoRunsSharingAStrip_TheLabelStaysOutOfTheControl()
    {
        using var renderer = new FitRenderer(400, 40);
        // Label 20 chars (100px at 10f) docked beside a 40px control in a 100px strip: the label's rect is
        // 60px, so an unfitted paint would run 40px into the control.
        new Widget(renderer).Render(
            Layout.Builder.Dock(
                Layout.Builder.Text("a-very-long-label-xx", 10f, trim: TextTrim.Shrink).Stretch(),
                Layout.Builder.Right(Layout.Builder.Text("GO", 10f).Stretch(), 40f)),
            new RectF32(0, 0, 100, 40));

        // Dock arranges its pinned strips before the fill, so the control is painted first.
        renderer.Runs[0].ShouldBe(("GO", 10f), "the control was never the thing that had to give");

        var label = renderer.Runs[1];
        label.Text.ShouldBe("a-very-long-label-xx");
        (label.Text.Length * label.FontSize * 0.5f).ShouldBeLessThanOrEqualTo(60f,
            "the label must stay inside the rect the dock left it");
    }

    /// <summary>
    /// A real rasterizer quantizes the pixel size, so measured width is a STEP function of the requested
    /// size — and <see cref="FitRenderer"/>'s continuous oracle above cannot express that, which is exactly
    /// why it never caught the bug below. Rounds the size to whole pixels before measuring, as a glyph raster
    /// does.
    /// </summary>
    private sealed class QuantizedRenderer(uint w, uint h) : RgbaImageRenderer(w, h)
    {
        public override (float Width, float Height) MeasureText(ReadOnlySpan<char> text, string fontFamily, float fontSize)
            => (text.Length * MathF.Round(fontSize) * 0.5f, fontSize);
    }

    /// <summary>
    /// <see cref="TextFit.ShrinkToWidth"/> must return a size it has MEASURED to fit, never an unverified
    /// estimate.
    ///
    /// <para>The ratio refinement assumes width scales continuously with the size. Against a quantizing
    /// rasterizer it does not: successive estimates land inside one step, measure identically, and the ratio
    /// reapplies the same factor — converging to a fixed point still above the budget, which was then returned
    /// as the answer. Chess found it as a 12-glyph panel header drawn ~1px past the column it had just been
    /// fitted to, on one surface aspect, every frame.</para>
    ///
    /// <para>Swept across budgets so what is asserted is the INVARIANT (what comes back fits) rather than one
    /// arithmetic answer — a step function has plateaus, and a budget landing mid-plateau is the whole
    /// hazard.</para>
    /// </summary>
    [Theory]
    [InlineData(182f)]
    [InlineData(183f)]
    [InlineData(100f)]
    [InlineData(61f)]
    [InlineData(60f)]
    [InlineData(59.5f)]
    [InlineData(36f)]   // exactly the floor's own width — fits, but only just
    public void ShrinkToWidth_ReturnsASizeThatWasMeasuredToFit(float maxWidth)
    {
        using var renderer = new QuantizedRenderer(400, 40);

        var size = TextFit.ShrinkToWidth(renderer, "Move History".AsSpan(), Font, null,
            fontSize: 30.25f, maxWidth: maxWidth);

        size.ShouldBeLessThanOrEqualTo(30.25f, "the preferred size is a ceiling, never exceeded");
        renderer.MeasureText("Move History".AsSpan(), Font, size).Width
            .ShouldBeLessThanOrEqualTo(maxWidth,
                $"ShrinkToWidth returned {size}, which does not fit {maxWidth} — an unverified estimate");
    }

    /// <summary>The floor still wins: a budget nothing can satisfy comes back AT the floor and overflows
    /// visibly, rather than scaling the run away to nothing.</summary>
    [Fact]
    public void ShrinkToWidth_KeepsTheFloorWhenNothingFits()
    {
        using var renderer = new QuantizedRenderer(400, 40);

        TextFit.ShrinkToWidth(renderer, "Move History".AsSpan(), Font, null,
                fontSize: 30.25f, maxWidth: 1f, minFontSize: 6f)
            .ShouldBe(6f, "the floor is the answer when even it overflows");
    }
}
