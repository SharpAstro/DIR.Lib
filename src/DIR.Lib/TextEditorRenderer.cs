using System;

namespace DIR.Lib;

/// <summary>
/// Renders a multi-line <see cref="TextEditorState"/> onto any <see cref="Renderer{TSurface}"/>: the box, the
/// wrapped lines clipped to it, the selection across lines, the caret, the placeholder and the IME composition.
/// Works with the GPU renderers and <see cref="RgbaImageRenderer"/> alike, as <see cref="TextInputRenderer"/> does.
/// </summary>
/// <remarks>
/// <para>
/// <b>The paint is the authority on the box.</b> It wraps <see cref="TextInputState.Text"/> to the width it is
/// given (<see cref="TextEditorLayout"/>), tells the state how tall the text area is
/// (so the scroll that keeps the caret in view is the one on screen, even when a parent arranged the box shorter
/// than its content asked for), and hands the state the layout, from which every vertical motion and a click
/// resolve. The state therefore needs one frame before its visual-line motions apply; until then they follow
/// logical lines.
/// </para>
/// <para>
/// <b>Everything is measured through the chain the text is drawn with</b>, for the reason
/// <see cref="TextInputRenderer.CaretIndexAt"/> gives: with a fallback in play the primary face reports zero
/// advance for a glyph it lacks.
/// </para>
/// </remarks>
public static class TextEditorRenderer
{
    /// <summary>
    /// The palette every editor draws with when it passes none: the SAME one a single-line field uses
    /// (<see cref="TextInputRenderer.Colors"/>), because an app has one input style and an editor beside a field
    /// that came out a different colour would be the first thing noticed.
    /// </summary>
    public static TextInputColors Colors => TextInputRenderer.Colors;

    /// <summary>The inset between the box's left/right edge and its text: the single-line field's, so the two line up.</summary>
    public static float HorizontalPadding(float fontSize) => TextInputRenderer.HorizontalPadding(fontSize);

    /// <summary>
    /// The inset between the box's top/bottom edge and its text. A single line is centred in its row and needs
    /// none; an editor's first and last lines would otherwise touch the border.
    /// </summary>
    public static float VerticalPadding(float fontSize) => fontSize * 0.3f;

    /// <summary>The height of one visual line, in the units <paramref name="fontSize"/> is given in.</summary>
    public static float LineHeight(float fontSize) => TextBaseline.LineHeight(fontSize);

    /// <summary>How wide the caret is drawn.</summary>
    private const int CaretWidthPx = 2;

    /// <summary>How wide the scroll thumb of an overflowing editor is drawn.</summary>
    private const int ThumbWidthPx = 2;

    /// <summary>Where the editor's first glyph lands horizontally, given the box's left edge. The insets are stated once, here, for the paint and the hit test.</summary>
    public static int TextOriginX(int x, float fontSize) => x + (int)HorizontalPadding(fontSize);

    /// <summary>Where the first line's top lands, given the box's top edge.</summary>
    public static int TextOriginY(int y, float fontSize) => y + (int)VerticalPadding(fontSize);

    /// <summary>
    /// Renders the editor at the given position and returns the caret's rect in surface pixels, or
    /// <c>default</c> when the editor is not active, its caret is scrolled out of the box, or no font is
    /// configured -- what a host feeds to its platform's "where is the caret" call, as for
    /// <see cref="TextInputRenderer.Render"/>.
    /// </summary>
    /// <param name="lineHeight">The height of one visual line in surface pixels; the layout engine measures the
    /// box with the same number (<see cref="LineHeight"/> at the surface's font size).</param>
    /// <param name="caretVisible">Whether the caret is in the ON half of its blink; ignored while composing.</param>
    /// <param name="colors">Palette for THIS editor, or null for <see cref="Colors"/>.</param>
    /// <param name="fallback">Per-script fallback chain, or null to draw with <paramref name="fontFamily"/> alone.</param>
    public static RectInt Render<TSurface>(
        Renderer<TSurface> renderer,
        TextEditorState state,
        int x, int y, int width, int height,
        string fontFamily, float fontSize, float lineHeight,
        bool caretVisible = true,
        TextInputColors? colors = null,
        FontFallbackResolver? fallback = null)
    {
        colors ??= Colors;
        var active = state.IsActive;

        renderer.FillRectangle(
            new RectInt(new PointInt(x + width, y + height), new PointInt(x, y)),
            active ? colors.BackgroundActive : colors.Background);
        renderer.DrawRectangle(
            new RectInt(new PointInt(x + width, y + height), new PointInt(x, y)),
            active ? colors.BorderActive : colors.Border, 1);

        var hp = (int)HorizontalPadding(fontSize);
        var vp = (int)VerticalPadding(fontSize);
        var textX = x + hp;
        var textY = y + vp;
        var textW = width - hp * 2;
        var viewH = height - vp * 2;

        // No font, or no room: the box alone, as the single-line field does. A headless render is how the layout
        // tests check what was drawn, and a tree with an editor in it must not throw where one with a label does not.
        if (string.IsNullOrEmpty(fontFamily) || textW <= 0 || viewH <= 0 || lineHeight <= 0f)
        {
            return default;
        }

        float Measure(ReadOnlySpan<char> text) => fallback is not null
            ? fallback.Measure(renderer, text.ToString(), fontSize).Width
            : renderer.MeasureText(text, fontFamily, fontSize).Width;

        // The wrap is rebuilt only when something it depends on moved: a frame that changed none of width, line
        // height, face or chain keeps the layout (the state re-wraps it on its own when the text changed).
        var key = (fontFamily, fontSize, (object?)fallback);
        var layout = state.VisualLayout;
        if (layout is null || layout.Width != textW || layout.LineHeight != lineHeight || state.PaintKey != key)
        {
            layout = new TextEditorLayout(state.Text, textW, Measure, lineHeight);
            state.PaintKey = key;
        }

        // A box that is not being edited has no caret to chase: it keeps the scroll it was given (the wheel's, or
        // the top on its first frame), where SetLayout would reveal a caret the reader is not looking at.
        var held = state.ScrollOffsetY;
        state.PaintedViewportHeight = viewH;
        state.SetLayout(layout);
        if (!active)
        {
            state.ScrollOffsetY = Math.Clamp(held, 0f, layout.MaxScroll(viewH));
        }

        // The IME's preedit is drawn INSIDE the text at the caret, where it will land on commit, and is not part of
        // state.Text -- the input method owns those characters until it commits them. So the lines drawn while
        // composing are a wrap of the text with the preedit in it, and the state's own layout is left alone.
        var composing = active && state.IsComposing;
        var caretChars = composing ? state.CursorPos + state.CompositionCursor : state.CursorPos;
        var drawn = layout;
        var drawnText = state.Text;
        if (composing)
        {
            drawnText = string.Concat(state.Text.AsSpan(0, state.CursorPos), state.Composition, state.Text.AsSpan(state.CursorPos));
            drawn = new TextEditorLayout(drawnText, textW, Measure, lineHeight);
            state.ScrollOffsetY = drawn.RevealLine(drawn.LineOf(caretChars), state.ScrollOffsetY, viewH);
        }

        var placeholder = drawnText.Length == 0 && !active && state.Placeholder.Length > 0;
        if (placeholder)
        {
            drawn = new TextEditorLayout(state.Placeholder, textW, Measure, lineHeight);
            drawnText = state.Placeholder;
        }

        var scroll = placeholder ? 0 : (int)MathF.Round(state.ScrollOffsetY);
        var textColor = placeholder ? colors.Placeholder : colors.Text;
        var lineH = (int)MathF.Round(lineHeight);

        // Clipped to the box inside its border: a line the scroll has half moved out must not paint over the border
        // or past the box into whatever sits beside it. Horizontally the clip is the box rather than the text
        // area, so a caret at the very end of a full line (which sits in the padding) is not cut.
        renderer.PushClip(new RectInt(new PointInt(x + width - 1, textY + viewH), new PointInt(x + 1, textY)));

        int LineTop(int line) => textY + (int)MathF.Round(drawn.YOf(line)) - scroll;

        var first = Math.Clamp(scroll / Math.Max(1, lineH), 0, drawn.LineCount - 1);
        var last = Math.Clamp((scroll + viewH) / Math.Max(1, lineH), 0, drawn.LineCount - 1);

        // Selection and underline run over [from, to) across the visible lines. The highlight is painted BEFORE the
        // glyphs, for the reason TextInputRenderer gives: over them it would hide the selected characters.
        void ForSpan(int from, int to, Action<int, int, int> paint)
        {
            for (var i = first; i <= last; i++)
            {
                var line = drawn.Lines[i];
                var a = Math.Max(from, line.Start);
                var b = Math.Min(to, line.End);
                var carriesBreak = !line.IsSoftWrapped && to > line.End && from <= line.End;
                if (a > b || (a == b && !carriesBreak))
                {
                    continue;
                }

                var x0 = textX + (int)Measure(drawnText.AsSpan(line.Start, a - line.Start));
                var x1 = textX + (int)Measure(drawnText.AsSpan(line.Start, b - line.Start));

                // A selection that runs on past a hard line break shows the break as a space's width, so an empty
                // line inside a selection is not a gap in it.
                if (carriesBreak)
                {
                    x1 += Math.Max(3, (int)Measure(" "));
                }

                paint(LineTop(i), x0, x1);
            }
        }

        if (active && state.HasSelection && !composing)
        {
            ForSpan(state.SelectionStart, state.SelectionEnd,
                (top, x0, x1) => renderer.FillRectangle(
                    new RectInt(new PointInt(x1, top + lineH), new PointInt(x0, top)), colors.Selection));
        }

        for (var i = first; i <= last; i++)
        {
            var line = drawn.Lines[i];
            if (line.End <= line.Start)
            {
                continue;
            }

            var top = LineTop(i);
            var run = drawnText.Substring(line.Start, line.End - line.Start);
            var rect = new RectInt(new PointInt(textX + textW + hp, top + lineH), new PointInt(textX, top));
            if (fallback is not null)
            {
                fallback.Draw(renderer, run, fontSize, textColor, rect, TextAlign.Near, TextAlign.Center);
            }
            else
            {
                renderer.DrawText(run.AsSpan(), fontFamily, fontSize, textColor, rect, TextAlign.Near, TextAlign.Center);
            }
        }

        // The near-universal convention for "the input method still owns this", in the text's own colour.
        if (composing)
        {
            var thickness = Math.Max(1, (int)(fontSize * 0.06f));
            ForSpan(state.CursorPos, state.CursorPos + state.Composition.Length,
                (top, x0, x1) => renderer.FillRectangle(
                    new RectInt(new PointInt(x1, top + lineH - 1), new PointInt(x0, top + lineH - 1 - thickness)), textColor));
        }

        var caretRect = default(RectInt);
        if (active)
        {
            var caretLine = drawn.LineOf(caretChars);
            var caretTop = LineTop(caretLine);

            // A caret the wheel has scrolled out of the box is not drawn, and not reported either: an input
            // method anchoring its candidate window there would float it over whatever is beside the editor.
            if (caretTop + lineH > textY && caretTop < textY + viewH)
            {
                var caretX = textX + (int)drawn.XOf(caretChars);
                var inset = (int)(lineH * 0.1f);
                var caretH = (int)(lineH * 0.8f);
                caretRect = new RectInt(
                    new PointInt(caretX + CaretWidthPx, caretTop + inset + caretH), new PointInt(caretX, caretTop + inset));

                // It stops blinking while composing: it is tracking the input method.
                if (composing || caretVisible)
                {
                    renderer.FillRectangle(caretRect, colors.Cursor);
                }
            }
        }

        renderer.PopClip();

        // A box that holds more than it shows says so: a thumb on its right edge, where the padding is. Without
        // one a max-height box simply ends mid-sentence and nothing says the rest is there.
        var content = (int)MathF.Round(drawn.ContentHeight);
        if (!placeholder && content > viewH + 1 && hp >= ThumbWidthPx + 2)
        {
            var thumbH = Math.Clamp(viewH * viewH / content, Math.Min(viewH, 12), viewH);
            var maxScroll = Math.Max(1, content - viewH);
            var thumbTop = textY + (viewH - thumbH) * Math.Clamp(scroll, 0, maxScroll) / maxScroll;
            var thumbX = x + width - 2 - ThumbWidthPx;
            renderer.FillRectangle(
                new RectInt(new PointInt(thumbX + ThumbWidthPx, thumbTop + thumbH), new PointInt(thumbX, thumbTop)),
                colors.Placeholder);
        }

        return caretRect;
    }
}
