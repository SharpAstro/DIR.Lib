using System;
using System.Collections.Generic;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="TextInputState.IsMasked"/>: a password field. The value is drawn as bullets, everything
/// measured against the drawing measures the bullets, and nothing reaches the clipboard.
/// <para>
/// The metrics here make a bullet a different width from every letter on purpose. The renderer tests'
/// usual stand-in measures by length alone, under which a bullet and the letter it hides are the same width,
/// and a caret that measured the hidden value instead of the bullets would pass every test written with it.
/// </para>
/// </summary>
public class TextInputMaskTests
{
    private const float FontSize = 14f;
    private const float LetterW = 5f;
    private const float BulletW = 11f;
    private const int FieldX = 10;

    /// <summary>A bullet is 11px and every other character 5px, and every DrawText is recorded.</summary>
    private sealed class ContentMetricsRenderer(uint w, uint h) : RgbaImageRenderer(w, h)
    {
        public List<string> Drawn { get; } = [];

        public override (float Width, float Height) MeasureText(ReadOnlySpan<char> text, string fontFamily, float fontSize)
        {
            var width = 0f;
            foreach (var c in text) width += c == TextInputState.MaskChar ? BulletW : LetterW;
            return (width, fontSize);
        }

        public override void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize,
            RGBAColor32 fontColor, in RectInt layout,
            TextAlign horizAlignment = TextAlign.Center, TextAlign vertAlignment = TextAlign.Near)
            => Drawn.Add(text.ToString());
    }

    private sealed class FieldWidget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public void Paint(TextInputState field)
        {
            BeginFrame();
            RenderLayout(Layout.Builder.TextInput(field, 1f).RowH(10f), new RectF32(0f, 0f, 100f, 10f),
                fontPath: string.Empty, scale: DesignScale.One);
        }
    }

    private sealed class Harness
    {
        public TextInputFocus Focus { get; } = new();
        public BackgroundTaskTracker Tracker { get; } = new();
        public string? Clipboard { get; set; }
        private readonly FieldWidget _widget = new(new RgbaImageRenderer(100, 100));

        public Harness(TextInputState field)
        {
            _widget.Paint(field);
            Focus.Focus(field);
        }

        public bool Key(InputKey key, InputModifier modifiers = InputModifier.None)
            => TextInputInteraction.HandleKey(key, modifiers, new TextInputInteraction.KeyContext(
                Tracker, Focus, () => { },
                TabFields: _widget.GetRegisteredTextInputs,
                GetClipboardText: () => Clipboard,
                SetClipboardText: t => Clipboard = t));
    }

    private static TextInputState Masked(string text) => new() { Text = text, IsActive = true, IsMasked = true };

    [Fact]
    public void AMaskedField_DrawsOneBulletPerCharacter_AndNeverTheValue()
    {
        var renderer = new ContentMetricsRenderer(200, 40);

        TextInputRenderer.Render(renderer, Masked("hunter2"), FieldX, 0, 150, 30, "font.ttf", FontSize);

        renderer.Drawn.ShouldContain(new string(TextInputState.MaskChar, 7));
        renderer.Drawn.ShouldAllBe(s => !s.Contains("hunter"));
    }

    [Fact]
    public void AnUnmaskedField_StillDrawsItsValue()
    {
        var renderer = new ContentMetricsRenderer(200, 40);

        TextInputRenderer.Render(renderer, new TextInputState { Text = "hunter2", IsActive = true },
            FieldX, 0, 150, 30, "font.ttf", FontSize);

        renderer.Drawn.ShouldContain("hunter2");
    }

    /// <summary>
    /// A click resolves against the bullets that were drawn. Measured against the hidden letters (5px
    /// each, where the bullets are 11), a click over the third bullet would land past the sixth letter.
    /// </summary>
    [Fact]
    public void AClickOnAMaskedField_LandsOnTheBulletUnderThePointer()
    {
        var state = Masked("abcdefgh");
        var boundary3 = TextInputRenderer.TextOriginX(FieldX, FontSize, 0f) + 3 * BulletW;

        var index = TextInputRenderer.CaretIndexAt(new ContentMetricsRenderer(200, 40), state, FieldX, "font.ttf",
            FontSize, boundary3 + 1f);

        index.ShouldBe(3);
    }

    [Fact]
    public void CtrlC_OnAMaskedField_LeavesTheClipboardAlone()
    {
        var field = Masked("secret words");
        field.SelectAll();
        var harness = new Harness(field) { Clipboard = "previous" };

        harness.Key(InputKey.C, InputModifier.Ctrl).ShouldBeTrue();

        harness.Clipboard.ShouldBe("previous");
    }

    /// <summary>
    /// Cut does nothing at all, the delete included: removing the selection without copying it would lose
    /// what the user was moving, and copying it is exactly what a masked field refuses.
    /// </summary>
    [Fact]
    public void CtrlX_OnAMaskedField_NeitherCopiesNorDeletes()
    {
        var field = Masked("secret words");
        field.SelectAll();
        var harness = new Harness(field) { Clipboard = "previous" };

        harness.Key(InputKey.X, InputModifier.Ctrl).ShouldBeTrue();

        harness.Clipboard.ShouldBe("previous");
        field.Text.ShouldBe("secret words");
    }

    [Fact]
    public void CtrlV_IntoAMaskedField_StillPastes()
    {
        var field = Masked("");
        var harness = new Harness(field) { Clipboard = "from a manager" };

        harness.Key(InputKey.V, InputModifier.Ctrl);

        field.Text.ShouldBe("from a manager");
    }

    /// <summary>
    /// Word motions cross the whole value. A caret that stopped at "secret|" would say where the space is,
    /// which the bullets exist to hide.
    /// </summary>
    [Fact]
    public void WordMotions_OnAMaskedField_CrossTheWholeValue()
    {
        var field = Masked("secret words");
        field.CursorPos = 12;

        field.MoveCaretToWordBoundary(-1);
        field.CursorPos.ShouldBe(0);

        field.MoveCaretToWordBoundary(1);
        field.CursorPos.ShouldBe(12);
    }

    [Fact]
    public void SelectingAWord_InAMaskedField_SelectsAllOfIt()
    {
        var field = Masked("secret words");

        field.SelectWordAt(2);

        (field.SelectionStart, field.SelectionEnd).ShouldBe((0, 12));
    }
}
