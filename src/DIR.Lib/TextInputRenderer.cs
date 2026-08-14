using System;
using DIR.Lib;

namespace DIR.Lib;

/// <summary>
/// Renders a single-line text input field onto any <see cref="Renderer{TSurface}"/>.
/// Works with VkRenderer (GPU) and RgbaImageRenderer (TUI).
/// </summary>
public static class TextInputRenderer
{
    /// <summary>
    /// The palette every field draws with. Defaults to the scheme this renderer has always used, so a
    /// consumer that sets nothing is unchanged.
    /// </summary>
    /// <remarks>
    /// Set it once when the theme MOVES, not per frame, the same contract <see cref="TabBar.Colors"/>
    /// carries: <see cref="TextInputColors.FromPalette"/> derives eight colours, and doing that per
    /// draw would rebuild them for every field on screen every frame.
    /// <para>
    /// A static because a text field is drawn from a dozen call sites across a consumer's tabs and an
    /// app has one input style, not one per caller — but it is the DEFAULT, not the only answer. A
    /// field that genuinely differs (one inlaid in a 33px toolbar chip, which cannot afford the fill,
    /// the border and a full-strength selection on top of each other) passes its own palette to the
    /// call. Before that was possible the only way through was to assign this static, draw, and assign
    /// it back — a global mutated around one draw, which is a race the moment anything else paints.
    /// </para>
    /// </remarks>
    public static TextInputColors Colors { get; set; } = new TextInputColors();

    /// <summary>
    /// The inset between the field's left/right edge and its text, in the units
    /// <paramref name="fontSize"/> is given in.
    /// <para>
    /// Stated once and read by both halves, because <see cref="Layout.Content.TextInput"/> made the two
    /// halves separate: the layout engine has to reserve room for the inset when it measures the field, and
    /// <see cref="Render"/> has to leave exactly that much when it draws. A literal in each is the shape
    /// where a later tweak to one silently mis-sizes the other -- the sample text fitting the box in the
    /// measure pass and being clipped in the paint.
    /// </para>
    /// </summary>
    public static float HorizontalPadding(float fontSize) => fontSize * 0.4f;

    /// <summary>
    /// Renders a text input field at the specified position.
    /// </summary>
    /// <param name="renderer">Target renderer.</param>
    /// <param name="state">Text input state.</param>
    /// <param name="x">Left edge in pixels.</param>
    /// <param name="y">Top edge in pixels.</param>
    /// <param name="width">Field width in pixels.</param>
    /// <param name="height">Field height in pixels.</param>
    /// <param name="fontFamily">Font path for text rendering.</param>
    /// <param name="fontSize">Font size in pixels.</param>
    /// <param name="frameCount">Frame counter for cursor blink (blinks every 30 frames).</param>
    /// <param name="colors">Palette for THIS field, or null for the shared <see cref="Colors"/>.</param>
    public static void Render<TSurface>(
        Renderer<TSurface> renderer,
        TextInputState state,
        int x, int y, int width, int height,
        string fontFamily, float fontSize,
        long frameCount = 0,
        TextInputColors? colors = null)
    {
        colors ??= Colors;
        var bgColor = state.IsActive ? colors.BackgroundActive : colors.Background;
        var borderColor = state.IsActive ? colors.BorderActive : colors.Border;

        // Background
        renderer.FillRectangle(
            new RectInt(new PointInt(x + width, y + height), new PointInt(x, y)),
            bgColor);

        // Border
        renderer.DrawRectangle(
            new RectInt(new PointInt(x + width, y + height), new PointInt(x, y)),
            borderColor, 1);

        // Text or placeholder
        var padding = (int)HorizontalPadding(fontSize);
        var textX = x + padding;
        var textY = y;
        var textW = width - padding * 2;
        var textH = height;

        // No font: draw the box and stop, rather than throwing. This is the same contract the layout text
        // helpers on PixelWidgetBase carry (an unconfigured widget draws no text), and it stopped being
        // merely nice-to-have when Layout.Content.TextInput started routing fields through here: a headless
        // render is how the layout tests check what was drawn, and a tree with a label in it would render
        // while the same tree with a FIELD in it threw. Every path below either draws or measures glyphs, so
        // the guard covers the caret and the selection too -- their positions are glyph measurements.
        if (string.IsNullOrEmpty(fontFamily))
        {
            return;
        }

        var displayText = state.Text.Length > 0 ? state.Text : (state.IsActive ? "" : state.Placeholder);
        var textColor = state.Text.Length > 0 ? colors.Text : colors.Placeholder;

        if (displayText.Length > 0)
        {
            var layoutRect = new RectInt(
                new PointInt(textX + textW, textY + textH),
                new PointInt(textX, textY));

            renderer.DrawText(
                displayText.AsSpan(),
                fontFamily,
                fontSize,
                textColor,
                layoutRect,
                TextAlign.Near,
                TextAlign.Center);
        }

        // Selection highlight
        if (state.IsActive && state.HasSelection)
        {
            var selStartText = state.Text[..state.SelectionStart];
            var selEndText = state.Text[..state.SelectionEnd];
            var selStartX = textX + (int)renderer.MeasureText(selStartText.AsSpan(), fontFamily, fontSize).Width;
            var selEndX = textX + (int)renderer.MeasureText(selEndText.AsSpan(), fontFamily, fontSize).Width;
            var selY = y + (int)(height * 0.1f);
            var selH = (int)(height * 0.8f);

            renderer.FillRectangle(
                new RectInt(new PointInt(selEndX, selY + selH), new PointInt(selStartX, selY)),
                colors.Selection);
        }

        // Cursor (blinking)
        if (state.IsActive && (frameCount / 30) % 2 == 0)
        {
            // Measure text up to cursor position to find cursor X
            var textBeforeCursor = state.Text.Length > 0 && state.CursorPos > 0
                ? state.Text[..state.CursorPos]
                : "";
            var cursorX = textX + (int)renderer.MeasureText(textBeforeCursor.AsSpan(), fontFamily, fontSize).Width;
            var cursorY = y + (int)(height * 0.15f);
            var cursorH = (int)(height * 0.7f);

            renderer.FillRectangle(
                new RectInt(new PointInt(cursorX + 2, cursorY + cursorH), new PointInt(cursorX, cursorY)),
                colors.Cursor);
        }
    }

    /// <summary>
    /// Hit-tests whether a click is inside the text field.
    /// </summary>
    public static bool HitTest(int clickX, int clickY, int fieldX, int fieldY, int fieldWidth, int fieldHeight)
    {
        return clickX >= fieldX && clickX < fieldX + fieldWidth
            && clickY >= fieldY && clickY < fieldY + fieldHeight;
    }
}
