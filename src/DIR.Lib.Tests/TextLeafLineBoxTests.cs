using System.Text;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// A pixel layout's text leaf measures to its face's LINE BOX (<see cref="TextBaseline.LineHeight"/>, 1.3 em at
/// the scaled size) for its height, whatever glyphs it holds, while the renderer's own
/// <see cref="Renderer{TSurface}.MeasureText"/> keeps answering ink.
/// </summary>
/// <remarks>
/// <para>Before 11.20 the leaf took the renderer's ink: "LOG" measured its cap height and "gjpq" its x-height plus
/// descenders, so two buttons side by side measured differently by what they said, and every declared text
/// control stated its height by hand (<c>.RowH(fontSize + gap)</c>). Every renderer's <c>DrawText</c> already lays a
/// line out in the 1.3 em box, which is why a leaf of that height drawn top-aligned lands where a hand-centred
/// row did.</para>
/// <para>Real faces here, not a stub: a stub that answers "the font size tall" for every run cannot tell ink from a
/// line box, which is the whole difference under test.</para>
/// </remarks>
public sealed class TextLeafLineBoxTests
{
    private static string Font(string name) => Path.Combine(AppContext.BaseDirectory, "Fonts", name);

    private const string DejaVu = "DejaVuSans.ttf";          // broad Latin; has no emoji
    private const string Emoji = "NotoColorEmoji.ttf";       // emoji only

    private const float FontSize = 18f;

    private sealed class CellCtx : Layout.IMeasureContext<int>
    {
        public Layout.Size<int> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length, 1);
        public int ToSurface(float designUnits) => (int)MathF.Round(designUnits);
    }

    // --- (1) different ink, one height ---

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void CapsAndDescendersMeasureTheSameLineBox(float dpiScale)
    {
        using var renderer = new RgbaImageRenderer(64, 64);
        var ctx = new PixelMeasureContext<RgbaImage>(renderer, Font(DejaVu), dpiScale);
        var lineBox = TextBaseline.LineHeight(FontSize * dpiScale);

        var caps = ctx.MeasureText("LOG".AsSpan(), FontSize);
        var descenders = ctx.MeasureText("gjpq".AsSpan(), FontSize);

        caps.Height.ShouldBe(lineBox);
        descenders.Height.ShouldBe(lineBox);
    }

    /// <summary>
    /// An empty run and a context with no font at all (headless) keep the same line box, so an empty label
    /// holds its row open at the height a drawn one would. It used to be one em here, a third answer beside
    /// the two inks.
    /// </summary>
    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void AnEmptyRunAndAFontlessContextMeasureTheSameLineBox(float dpiScale)
    {
        using var renderer = new RgbaImageRenderer(64, 64);
        var lineBox = TextBaseline.LineHeight(FontSize * dpiScale);

        var empty = new PixelMeasureContext<RgbaImage>(renderer, Font(DejaVu), dpiScale)
            .MeasureText(ReadOnlySpan<char>.Empty, FontSize);
        var fontless = new PixelMeasureContext<RgbaImage>(renderer, string.Empty, dpiScale)
            .MeasureText("LOG".AsSpan(), FontSize);

        empty.ShouldBe(new Layout.Size<float>(0f, lineBox));
        fontless.ShouldBe(new Layout.Size<float>(0f, lineBox));
    }

    /// <summary>Width is still the advance the renderer measures: only the height changed.</summary>
    [Fact]
    public void WidthIsStillTheRenderersAdvance()
    {
        using var renderer = new RgbaImageRenderer(64, 64);
        var ctx = new PixelMeasureContext<RgbaImage>(renderer, Font(DejaVu), 1.5f);

        ctx.MeasureText("gjpq".AsSpan(), FontSize).Width
            .ShouldBe(renderer.MeasureText("gjpq".AsSpan(), Font(DejaVu), FontSize * 1.5f).Width);
    }

    /// <summary>Through the engine, two text buttons side by side now measure one height, padding alone.</summary>
    [Fact]
    public void TwoButtonsOfDifferentInkArrangeToOneHeight()
    {
        using var renderer = new RgbaImageRenderer(64, 64);
        var ctx = new PixelMeasureContext<RgbaImage>(renderer, Font(DejaVu), 1.5f);

        var log = Layout.Engine.Measure(Layout.Builder.Text("LOG", FontSize).Pad(0f, 3f), new Layout.Size<float>(400f, 400f), ctx);
        var gjpq = Layout.Engine.Measure(Layout.Builder.Text("gjpq", FontSize).Pad(0f, 3f), new Layout.Size<float>(400f, 400f), ctx);

        log.Height.ShouldBe(TextBaseline.LineHeight(FontSize * 1.5f) + 2f * 3f * 1.5f);
        gjpq.Height.ShouldBe(log.Height);
    }

    // --- (2) the renderer's own MeasureText still answers ink ---

    /// <summary>
    /// Pinned to the pixel, so a change to the renderer's ink measure is a decision and not a side effect of this
    /// one. The two runs differ by what they hold, and neither is the line box.
    /// </summary>
    [Fact]
    public void TheRenderersMeasureTextStillAnswersInk()
    {
        using var renderer = new RgbaImageRenderer(64, 64);

        var caps = renderer.MeasureText("LOG".AsSpan(), Font(DejaVu), FontSize);
        var descenders = renderer.MeasureText("gjpq".AsSpan(), Font(DejaVu), FontSize);

        caps.Height.ShouldBe(13f);
        descenders.Height.ShouldBe(18f);
        caps.Height.ShouldNotBe(descenders.Height);
        caps.Height.ShouldBeLessThan(TextBaseline.LineHeight(FontSize));
    }

    // --- (3) a cell-authored tree does not move ---

    /// <summary>
    /// A terminal measures in cells: one row per line, whatever the ink or the font size. That is an
    /// <see cref="Layout.IMeasureContext{T}"/> of <c>int</c> of its own (Console.Lib's <c>CellMeasureContext</c>),
    /// which this release does not touch, so the same two labels stack to exactly two rows.
    /// </summary>
    [Fact]
    public void ACellAuthoredTreeMeasuresInCellsAsBefore()
    {
        var tree = Layout.Builder.VStack(
            Layout.Builder.Text("LOG", FontSize),
            Layout.Builder.Text("gjpq", FontSize));

        Layout.Engine.Measure(tree, new Layout.Size<int>(80, 24), new CellCtx())
            .ShouldBe(new Layout.Size<int>(4, 2));
    }

    // --- (4) a fallback-run text gets the line box too ---

    /// <summary>
    /// A run the primary cannot draw is measured across its coverage runs, where the height used to be the
    /// tallest run's ink -- a colour emoji's bitmap, here. It is the primary face's line box like any other run,
    /// because that is the box <c>DrawText</c> lays the line out in.
    /// </summary>
    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void AFallbackRunMeasuresTheLineBox(float dpiScale)
    {
        using var renderer = new RgbaImageRenderer(64, 64);
        var fallback = FontFallbackResolver.FromRoles(Font(DejaVu), emojiFontPath: Font(Emoji));
        var ctx = new PixelMeasureContext<RgbaImage>(renderer, Font(DejaVu), dpiScale) { Fallback = fallback };
        var text = "A" + new Rune(0x1F680); // a rocket, which DejaVu lacks

        fallback.PrimaryCoversAll(text.AsSpan()).ShouldBeFalse("else this is the single-face path");

        var size = ctx.MeasureText(text.AsSpan(), FontSize);

        size.Height.ShouldBe(TextBaseline.LineHeight(FontSize * dpiScale));
        size.Height.ShouldBe(ctx.MeasureText("LOG".AsSpan(), FontSize).Height);
    }
}
