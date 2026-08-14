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
    /// <returns>
    /// The caret's rect in surface pixels, or <c>default</c> when the field is not active or no font is
    /// configured. A host feeds this to its platform's "where is the caret" call
    /// (<c>SDL_SetTextInputArea</c>) so an input method can place its candidate window beside the text
    /// instead of over it -- there is no other way for the platform to know, and without it a CJK IME
    /// has nothing to anchor to.
    /// </returns>
    /// <param name="fallback">
    /// Per-script fallback chain, or null to draw with <paramref name="fontFamily"/> alone.
    /// <b>Without it a field can only display what its primary face covers</b>: the layout painter's
    /// coverage-run splitting stops at the field's edge, because everything inside is drawn here rather
    /// than as a text leaf. That is what made a field holding correct CJK text look empty -- the
    /// characters were there, the face had no glyphs for them, and nothing said so.
    /// </param>
    public static RectInt Render<TSurface>(
        Renderer<TSurface> renderer,
        TextInputState state,
        int x, int y, int width, int height,
        string fontFamily, float fontSize,
        long frameCount = 0,
        TextInputColors? colors = null,
        FontFallbackResolver? fallback = null)
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
            return default;
        }

        // The preedit is drawn INSIDE the field at the caret, because that is where the characters will
        // land once the IME commits. It is deliberately not part of state.Text: the input method owns
        // those characters until it commits them, and merging them early would let a cancelled
        // composition survive in the field.
        var composing = state.IsActive && state.IsComposing;
        var visibleText = composing
            ? string.Concat(state.Text[..state.CursorPos], state.Composition, state.Text[state.CursorPos..])
            : state.Text;

        var displayText = visibleText.Length > 0 ? visibleText : (state.IsActive ? "" : state.Placeholder);
        var textColor = visibleText.Length > 0 ? colors.Text : colors.Placeholder;

        if (displayText.Length > 0)
        {
            var layoutRect = new RectInt(
                new PointInt(textX + textW, textY + textH),
                new PointInt(textX, textY));

            if (fallback is not null)
            {
                fallback.Draw(renderer, displayText, fontSize, textColor, layoutRect, TextAlign.Near, TextAlign.Center);
            }
            else
            {
                renderer.DrawText(
                    displayText.AsSpan(),
                    fontFamily,
                    fontSize,
                    textColor,
                    layoutRect,
                    TextAlign.Near,
                    TextAlign.Center);
            }
        }

        // Measured through the SAME chain the text was drawn with, or the caret and selection would be
        // positioned for a face that did not render it -- with a fallback in play the primary reports zero
        // advance for a glyph it lacks, so every measurement past the first CJK character would be short.
        int XOf(int chars) => textX + (int)(fallback is not null
            ? fallback.Measure(renderer, visibleText[..chars], fontSize).Width
            : renderer.MeasureText(visibleText[..chars].AsSpan(), fontFamily, fontSize).Width);

        // Selection highlight. Suppressed while composing: the selection indices address state.Text,
        // which is not what is on screen, so drawing it would highlight the wrong characters.
        if (state.IsActive && state.HasSelection && !composing)
        {
            var selY = y + (int)(height * 0.1f);
            var selH = (int)(height * 0.8f);

            renderer.FillRectangle(
                new RectInt(new PointInt(XOf(state.SelectionEnd), selY + selH), new PointInt(XOf(state.SelectionStart), selY)),
                colors.Selection);
        }

        if (!state.IsActive)
        {
            return default;
        }

        // Underline the composition, the near-universal convention for "the input method still owns
        // this". Drawn in the text colour rather than a new palette entry, since it IS that text's own
        // decoration and a separate colour would be one more thing every theme has to state.
        if (composing)
        {
            var underlineY = y + (int)(height * 0.78f);
            renderer.FillRectangle(
                new RectInt(
                    new PointInt(XOf(state.CursorPos + state.Composition.Length), underlineY + Math.Max(1, (int)(fontSize * 0.06f))),
                    new PointInt(XOf(state.CursorPos), underlineY)),
                textColor);
        }

        // While composing, the caret belongs to the IME's position inside the preedit, not to the
        // field's own CursorPos.
        var caretChars = composing ? state.CursorPos + state.CompositionCursor : state.CursorPos;
        var caretX = XOf(caretChars);
        var caretY = y + (int)(height * 0.15f);
        var caretH = (int)(height * 0.7f);
        var caretRect = new RectInt(new PointInt(caretX + 2, caretY + caretH), new PointInt(caretX, caretY));

        // The caret stops blinking while composing: it is tracking the input method, and a blink there
        // reads as the field being unresponsive rather than as a text cursor.
        if (composing || (frameCount / 30) % 2 == 0)
        {
            renderer.FillRectangle(caretRect, colors.Cursor);
        }

        return caretRect;
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
