using System;
using System.Collections.Immutable;
using System.Linq;
using DIR.Lib;
using DIR.Lib.Layout;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Headless arrange tests for <see cref="Builder.Progress"/> -- the declarative fractional progress bar
/// (promoted here from TianWen's <c>FormRowLayout.ProgressBar</c>). Pins the load-bearing invariant: the
/// fill spans exactly the requested fraction of the track width (via two Star-weighted spacers), the 0/1
/// ends collapse to a single coloured box, and an optional label arranges as a centred text leaf over the
/// bar. This file imports <c>DIR.Lib.Layout</c> for bareword brevity (see <see cref="LayoutBuilderTests"/>).
/// </summary>
public class LayoutProgressTests
{
    private static readonly RGBAColor32 Track = new(0x2a, 0x2a, 0x3a, 0xff);
    private static readonly RGBAColor32 Fill = new(0x30, 0x88, 0x30, 0xff);
    private static readonly RGBAColor32 White = new(0xff, 0xff, 0xff, 0xff);

    /// <summary>Minimal pixel measure context: design units -&gt; px x dpi, glyph metrics = a crude box.</summary>
    private sealed class StubCtx(float dpi) : IMeasureContext<float>
    {
        public Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize)
            => new(text.Length * fontSize * 0.5f, fontSize);

        public float ToSurface(float designUnits) => designUnits * dpi;
    }

    private static ImmutableArray<ArrangedNode<float>> Arrange(
        Node node, float width = 200f, float height = 14f, float dpi = 1f)
        => Engine.Arrange(node, new Rect<float>(0f, 0f, width, height), new StubCtx(dpi));

    [Theory]
    [InlineData(0.25f, 50f)]
    [InlineData(0.5f, 100f)]
    [InlineData(0.75f, 150f)]
    public void Progress_Partial_FillSpansFractionOfWidth(float fraction, float expectedFillWidth)
    {
        var arranged = Arrange(Builder.Progress(fraction, Track, Fill));

        // Track spans the full width; the fill spans exactly `fraction` of it.
        var trackNode = arranged.First(a => a.Node.Background is { } bg && bg.Equals(Track));
        trackNode.Bounds.Width.ShouldBe(200f, 0.5f);

        var fillNode = arranged.First(a => a.Node.Background is { } bg && bg.Equals(Fill));
        fillNode.Bounds.Width.ShouldBe(expectedFillWidth, 0.5f);
        fillNode.Bounds.Height.ShouldBe(14f, 0.5f);
    }

    [Fact]
    public void Progress_Empty_IsSingleTrackBox_NoFill()
    {
        var arranged = Arrange(Builder.Progress(0f, Track, Fill));

        arranged.Any(a => a.Node.Background is { } bg && bg.Equals(Track)).ShouldBeTrue();
        arranged.Any(a => a.Node.Background is { } bg && bg.Equals(Fill)).ShouldBeFalse();
    }

    [Fact]
    public void Progress_Full_IsSingleFillBox_NoTrack()
    {
        var arranged = Arrange(Builder.Progress(1f, Track, Fill));

        var fillNode = arranged.First(a => a.Node.Background is { } bg && bg.Equals(Fill));
        fillNode.Bounds.Width.ShouldBe(200f, 0.5f);
        arranged.Any(a => a.Node.Background is { } bg && bg.Equals(Track)).ShouldBeFalse();
    }

    [Fact]
    public void Progress_OutOfRange_ClampsToEnds()
    {
        // Negative clamps to empty (track only), > 1 clamps to full (fill only).
        Arrange(Builder.Progress(-0.5f, Track, Fill))
            .Any(a => a.Node.Background is { } bg && bg.Equals(Fill)).ShouldBeFalse();
        Arrange(Builder.Progress(2f, Track, Fill))
            .First(a => a.Node.Background is { } bg && bg.Equals(Fill)).Bounds.Width.ShouldBe(200f, 0.5f);
    }

    [Fact]
    public void Progress_Label_ArrangesCentredTextOverBar()
    {
        var arranged = Arrange(Builder.Progress(0.4f, Track, Fill, "12s", 10f, White));

        var label = arranged
            .Select(a => a.Node)
            .OfType<Node.Leaf>()
            .Select(l => l.Content)
            .OfType<Content.Text>()
            .SingleOrDefault(t => t.Value == "12s");
        label.ShouldNotBeNull();
        label.HAlign.ShouldBe(TextAlign.Center);
        label.VAlign.ShouldBe(TextAlign.Center);

        // The fill is still present under the label.
        arranged.Any(a => a.Node.Background is { } bg && bg.Equals(Fill)).ShouldBeTrue();
    }
}
