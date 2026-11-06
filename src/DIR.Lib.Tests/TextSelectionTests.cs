using System.Text;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="TextSelection{TSurface}"/> driven against <see cref="RgbaImageRenderer"/>. DejaVu Sans is the fixture's plain Latin
/// face; every x below is derived from the renderer's own <c>MeasureText</c>, so no test hard-codes a glyph width.
/// </summary>
public class TextSelectionTests : IDisposable
{
    private static readonly string Font = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");
    private static readonly RGBAColor32 Ground = new(255, 255, 255, 255);
    private static readonly RGBAColor32 Bar = new(255, 0, 0, 255);
    private static readonly RGBAColor32 Ink = new(0, 0, 255, 255);

    private const float Size = 16f;
    private readonly RgbaImageRenderer _renderer = new(400, 200);
    private readonly TextSelection<RgbaImage> _selection;

    public TextSelectionTests() => _selection = new TextSelection<RgbaImage>(_renderer);

    public void Dispose() => _renderer.Dispose();

    private float Width(string text) => text.Length == 0 ? 0f : _renderer.MeasureText(text, Font, Size).Width;

    private SelectableTextRegion Region(string text, float x, float y, float width = -1f, TextAlign align = TextAlign.Near) =>
        new(x, y, width < 0f ? Width(text) : width, 20f, text, Font, Size, Ink, align, TextAlign.Center);

    private void Frame(params SelectableTextRegion[] regions) => _selection.Frame(regions, default);

    private static bool Always(float x, float y) => true;

    private void Drag(float x0, float y0, float x1, float y1)
    {
        _selection.HandleInput(new InputEvent.MouseDown(x0, y0), Always).ShouldBeTrue();
        _selection.HandleInput(new InputEvent.MouseMove(x1, y1), Always);
        _selection.HandleInput(new InputEvent.MouseUp(x1, y1), Always);
    }

    [Fact]
    public void PressDragReleaseSelectsTheCoveredCharacters()
    {
        Frame(Region("hello world", 10, 10));
        Drag(10, 20, 10 + Width("hello"), 20);
        _selection.HasSelection.ShouldBeTrue();
        _selection.Dragging.ShouldBeFalse();
        _selection.Text.ShouldBe("hello");
    }

    [Fact]
    public void DraggingBackwardsSelectsTheSameText()
    {
        Frame(Region("hello world", 10, 10));
        Drag(10 + Width("hello"), 20, 10, 20);
        _selection.Text.ShouldBe("hello");
    }

    [Theory]
    [InlineData(TextAlign.Center)]
    [InlineData(TextAlign.Far)]
    public void AlignedTextIsHitWhereItsGlyphsAre(TextAlign align)
    {
        const string text = "abcdef";
        var region = Region(text, 20, 10, 300, align);
        var origin = align == TextAlign.Center ? 20 + (300 - Width(text)) / 2f : 20 + 300 - Width(text);
        Frame(region);
        Drag(origin + Width("ab"), 20, origin + Width("abcd"), 20);
        _selection.Text.ShouldBe("cd");
    }

    [Fact]
    public void ASurrogatePairIsNeverSplit()
    {
        const string text = "a\U0001F600b";
        Frame(Region(text, 10, 10));
        _selection.HandleInput(new InputEvent.MouseDown(10, 20), Always).ShouldBeTrue();
        for (var x = 10f; x <= 10 + Width(text) + 4; x += 1f)
        {
            _selection.HandleInput(new InputEvent.MouseMove(x, 20), Always);
            if (_selection.Text is { } said)
            {
                said.EnumerateRunes().ShouldNotContain(Rune.ReplacementChar);
            }
        }
        _selection.HandleInput(new InputEvent.MouseUp(10 + Width(text) + 4, 20), Always);
        _selection.Text.ShouldBe(text);
    }

    [Fact]
    public void ADoubleClickSelectsTheWord()
    {
        Frame(Region("hello world", 10, 10));
        var x = 10 + Width("hello w") + 1;
        _selection.HandleInput(new InputEvent.MouseDown(x, 20, ClickCount: 2), Always).ShouldBeTrue();
        _selection.Text.ShouldBe("world");
        _selection.Dragging.ShouldBeFalse();
    }

    [Fact]
    public void APressWithNoDragSelectsNothing()
    {
        Frame(Region("hello world", 10, 10));
        _selection.HandleInput(new InputEvent.MouseDown(30, 20), Always).ShouldBeTrue();
        _selection.Dragging.ShouldBeTrue();
        _selection.HandleInput(new InputEvent.MouseUp(30, 20), Always).ShouldBeTrue();
        _selection.HasSelection.ShouldBeFalse();
        _selection.Text.ShouldBeNull();
    }

    [Fact]
    public void TextAcrossTwoRowsHasALineBreak()
    {
        Frame(Region("first", 10, 10), Region("second", 10, 40));
        Drag(10, 20, 10 + Width("second") + 30, 50);
        _selection.Text.ShouldBe("first\nsecond");
    }

    [Fact]
    public void TextAcrossAGapOnOneRowHasASpace()
    {
        Frame(Region("ab", 10, 10), Region("cd", 200, 10));
        Drag(10, 20, 300, 20);
        _selection.Text.ShouldBe("ab cd");
    }

    [Fact]
    public void AFrameWhoseRegionsMovedKeepsTheSelection()
    {
        Frame(Region("one", 10, 10), Region("hello world", 10, 40), Region("three", 10, 70));
        Drag(10, 50, 10 + Width("hello"), 50);
        _selection.Text.ShouldBe("hello");

        // A line inserted above, and the page scrolled up a little.
        Frame(Region("new", 10, 0), Region("one", 10, 25), Region("hello world", 10, 55), Region("three", 10, 85));
        _selection.Text.ShouldBe("hello");
    }

    [Fact]
    public void AFrameWithoutTheTextDropsTheSelection()
    {
        Frame(Region("hello world", 10, 10));
        Drag(10, 20, 10 + Width("hello"), 20);
        _selection.HasSelection.ShouldBeTrue();

        Frame(Region("something else", 10, 10));
        _selection.HasSelection.ShouldBeFalse();
        _selection.Text.ShouldBeNull();
    }

    [Fact]
    public void APressTheInertPredicateRefusesIsNotTaken()
    {
        Frame(Region("hello world", 10, 10));
        _selection.HandleInput(new InputEvent.MouseDown(30, 20), (_, _) => false).ShouldBeFalse();
        _selection.Dragging.ShouldBeFalse();
        _selection.HasSelection.ShouldBeFalse();
    }

    [Fact]
    public void ContainsIsTrueOnlyOverSelectedText()
    {
        Frame(Region("hello world", 10, 10));
        Drag(10, 20, 10 + Width("hello"), 20);
        _selection.Contains(10 + Width("hel"), 20).ShouldBeTrue();
        _selection.Contains(10 + Width("hello wor"), 20).ShouldBeFalse();
        _selection.Clear();
        _selection.Contains(12, 20).ShouldBeFalse();
    }

    [Fact]
    public void PaintPutsTheBackgroundUnderTheSelectedStretchOnly()
    {
        var region = Region("hello world", 10, 10);
        Frame(region);
        Drag(10, 20, 10 + Width("hello"), 20);
        _renderer.Surface.Clear(Ground);

        _selection.Paint(
            (text, r, x, y, w, h, color) => _renderer.DrawText(text, r.FontPath, r.FontSize, color,
                new RectInt(new PointInt((int)(x + w), (int)(y + h)), new PointInt((int)x, (int)y)), TextAlign.Near, TextAlign.Center),
            Bar, Ink);

        var selectedRight = 10 + (int)MathF.Floor(Width("hello"));
        CountColour(Bar, 10, 10, selectedRight, 30).ShouldBeGreaterThan((selectedRight - 10) * 20 / 4);
        CountColour(Bar, selectedRight + 2, 0, 400, 200).ShouldBe(0);
        CountColour(Bar, 0, 0, 9, 200).ShouldBe(0);
        CountColour(Bar, 0, 0, 400, 9).ShouldBe(0);
        CountColour(Bar, 0, 31, 400, 200).ShouldBe(0);
    }

    private int CountColour(RGBAColor32 colour, int x0, int y0, int x1, int y1)
    {
        var image = _renderer.Surface;
        var count = 0;
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var i = (y * image.Width + x) * 4;
                if (image.Pixels[i] == colour.Red && image.Pixels[i + 1] == colour.Green && image.Pixels[i + 2] == colour.Blue)
                {
                    count++;
                }
            }
        }
        return count;
    }
}
