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
    /// A static rather than a per-call parameter because a text field is drawn from a dozen call sites
    /// across a consumer's tabs, and threading a palette through all of them buys nothing: an app has
    /// one input style, not one per caller.
    /// </para>
    /// </remarks>
    public static TextInputColors Colors { get; set; } = new TextInputColors();

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
    public static void Render<TSurface>(
        Renderer<TSurface> renderer,
        TextInputState state,
        int x, int y, int width, int height,
        string fontFamily, float fontSize,
        long frameCount = 0)
    {
        var colors = Colors;
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
        var padding = (int)(fontSize * 0.4f);
        var textX = x + padding;
        var textY = y;
        var textW = width - padding * 2;
        var textH = height;

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
