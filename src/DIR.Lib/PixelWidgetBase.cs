using System;
using System.Collections.Generic;
using DIR.Lib;

namespace DIR.Lib
{
    /// <summary>
    /// Renderer-agnostic widget for hit testing and click dispatch.
    /// </summary>
    /// <summary>
    /// Pixel-coordinate widget interface. Extends <see cref="IWidget"/> with
    /// hit testing, click dispatch, and text input discovery.
    /// </summary>
    public interface IPixelWidget : IWidget
    {
        /// <summary>Hit-tests the last rendered frame. Returns null for no hit.</summary>
        HitResult? HitTest(float x, float y);

        /// <summary>Hit-tests and invokes the <see cref="ClickableRegion.OnClick"/> handler if present.</summary>
        HitResult? HitTestAndDispatch(float x, float y, InputModifier modifiers = InputModifier.None);

        /// <summary>Returns all registered text inputs in order (for Tab cycling).</summary>
        List<TextInputState> GetRegisteredTextInputs();
    }

    /// <summary>
    /// Base class for pixel-coordinate widgets. Provides the clickable region system
    /// (RegisterClickable / HitTest / HitTestAndDispatch) and common drawing helpers.
    /// Generic over <typeparamref name="TSurface"/> so it works with any <see cref="Renderer{TSurface}"/>.
    /// </summary>
    public abstract class PixelWidgetBase<TSurface>(Renderer<TSurface> renderer) : IPixelWidget
    {
        private readonly ClickableRegionTracker _tracker = new();

        protected Renderer<TSurface> Renderer { get; } = renderer;

        /// <summary>
        /// Optional signal bus for deferred inter-component communication.
        /// Set via object initializer at construction time.
        /// </summary>
        public SignalBus? Bus { get; init; }

        /// <summary>
        /// Posts a signal to the bus for delivery at the next <see cref="SignalBus.ProcessPending"/> call.
        /// No-op if <see cref="Bus"/> is null.
        /// </summary>
        protected void PostSignal<T>(T signal) where T : notnull => Bus?.Post(signal);

        /// <summary>Frame counter for cursor blink etc.</summary>
        public long FrameCount { get; set; }

        /// <summary>
        /// Clears clickable regions. Call at the start of each Render pass.
        /// </summary>
        protected void BeginFrame() => _tracker.BeginFrame();

        /// <summary>
        /// Registers a clickable region with an optional direct click handler.
        /// </summary>
        protected void RegisterClickable(float x, float y, float w, float h, HitResult result, Action<InputModifier>? onClick = null)
            => _tracker.Register(x, y, w, h, result, onClick);

        /// <summary>
        /// Registers a text input field — renders it and registers the clickable region.
        /// </summary>
        protected void RenderTextInput(TextInputState state, int x, int y, int width, int height, string fontPath, float fontSize)
        {
            TextInputRenderer.Render(Renderer, state, x, y, width, height, fontPath, fontSize, FrameCount);
            RegisterClickable(x, y, width, height, new HitResult.TextInputHit(state));
        }

        /// <summary>
        /// Renders a button and registers the clickable region with an optional direct handler.
        /// </summary>
        protected void RenderButton(string label, float x, float y, float w, float h, string fontPath, float fontSize,
            RGBAColor32 bgColor, RGBAColor32 textColor, string action, Action<InputModifier>? onClick = null)
        {
            FillRect(x, y, w, h, bgColor);
            DrawText(label.AsSpan(), fontPath, x, y, w, h, fontSize, textColor, TextAlign.Center, TextAlign.Center);
            RegisterClickable(x, y, w, h, new HitResult.ButtonHit(action), onClick);
        }

        /// <summary>
        /// Measures text width for button sizing.
        /// </summary>
        protected float MeasureButtonWidth(string label, string fontPath, float fontSize, float padding)
        {
            return Renderer.MeasureText(label.AsSpan(), fontPath, fontSize).Width + padding * 2f;
        }

        /// <inheritdoc/>
        public List<TextInputState> GetRegisteredTextInputs() => _tracker.GetRegisteredTextInputs();

        /// <inheritdoc/>
        public HitResult? HitTest(float x, float y) => _tracker.HitTest(x, y);

        /// <inheritdoc/>
        public HitResult? HitTestAndDispatch(float x, float y, InputModifier modifiers = InputModifier.None) => _tracker.HitTestAndDispatch(x, y, modifiers);

        /// <summary>
        /// Handles an input event. Returns true if consumed.
        /// Override in tabs to pattern match on <see cref="InputEvent"/> subtypes.
        /// </summary>
        public virtual bool HandleInput(InputEvent evt) => false;

        // --- Dropdown menu ---

        /// <summary>
        /// Renders a dropdown menu overlay. <b>Must be called last</b> in the render pass
        /// so that its clickable regions win hit testing (paint order = z-order).
        /// Registers a full-screen backdrop that dismisses the dropdown on click-outside.
        /// </summary>
        protected void RenderDropdownMenu(
            DropdownMenuState dropdown,
            string fontPath,
            float fontSize,
            RGBAColor32 bgColor,
            RGBAColor32 highlightColor,
            RGBAColor32 textColor,
            RGBAColor32 borderColor,
            float viewportWidth,
            float viewportHeight,
            float maxHeight = 0f)
        {
            if (!dropdown.IsOpen || dropdown.Items.Length == 0)
            {
                return;
            }

            var rowH = fontSize * 1.8f;
            var padding = fontSize * 0.5f;
            var totalItems = dropdown.Items.Length + (dropdown.HasCustomEntry ? 1 : 0);
            var dropdownH = totalItems * rowH;
            if (maxHeight > 0f && dropdownH > maxHeight)
            {
                dropdownH = maxHeight;
            }

            var x = dropdown.AnchorX;
            var y = dropdown.AnchorY;
            var w = dropdown.AnchorWidth;

            // Full-screen backdrop — closes dropdown on click-outside
            RegisterClickable(0, 0, viewportWidth, viewportHeight, new HitResult.ButtonHit("DropdownBackdrop"),
                _ => dropdown.Close());

            // Border
            FillRect(x - 1f, y - 1f, w + 2f, dropdownH + 2f, borderColor);
            // Background
            FillRect(x, y, w, dropdownH, bgColor);

            // Items
            var itemY = y;
            for (var i = 0; i < dropdown.Items.Length && itemY + rowH <= y + dropdownH; i++)
            {
                if (i == dropdown.HighlightIndex)
                {
                    FillRect(x, itemY, w, rowH, highlightColor);
                }

                DrawText(dropdown.Items[i].AsSpan(), fontPath,
                    x + padding, itemY, w - padding * 2f, rowH,
                    fontSize, textColor, TextAlign.Near, TextAlign.Center);

                var capturedI = i;
                var capturedItem = dropdown.Items[i];
                RegisterClickable(x, itemY, w, rowH, new HitResult.ListItemHit("Dropdown", i),
                    _ =>
                    {
                        dropdown.OnSelect?.Invoke(capturedI, capturedItem);
                        dropdown.Close();
                    });

                itemY += rowH;
            }

            // "Custom..." entry
            if (dropdown.HasCustomEntry && itemY + rowH <= y + dropdownH)
            {
                var customIdx = dropdown.Items.Length;
                if (customIdx == dropdown.HighlightIndex)
                {
                    FillRect(x, itemY, w, rowH, highlightColor);
                }

                // Slightly dimmed, blue-shifted text for the "Custom..." entry
                var customColor = new RGBAColor32(
                    (byte)((textColor.Red * 3 + 2) / 4),
                    (byte)((textColor.Green * 3 + 2) / 4),
                    (byte)Math.Min(255, textColor.Blue + 40),
                    textColor.Alpha);
                DrawText(dropdown.CustomEntryLabel.AsSpan(), fontPath,
                    x + padding, itemY, w - padding * 2f, rowH,
                    fontSize, customColor, TextAlign.Near, TextAlign.Center);

                RegisterClickable(x, itemY, w, rowH, new HitResult.ListItemHit("Dropdown", customIdx),
                    _ =>
                    {
                        dropdown.OnCustom?.Invoke();
                        dropdown.Close();
                    });
            }
        }

        // --- Drawing helpers ---

        protected void FillRect(float x, float y, float w, float h, RGBAColor32 color)
        {
            if (w <= 0 || h <= 0) return;
            Renderer.FillRectangle(
                new RectInt(new PointInt((int)(x + w), (int)(y + h)), new PointInt((int)x, (int)y)),
                color);
        }

        /// <summary>
        /// Draws a line between two points with the given color and thickness.
        /// </summary>
        protected void DrawLine(float x0, float y0, float x1, float y1, RGBAColor32 color, int thickness = 1)
            => Renderer.DrawLine(x0, y0, x1, y1, color, thickness);

        /// <summary>
        /// Fills a circle centered at (<paramref name="cx"/>, <paramref name="cy"/>).
        /// </summary>
        protected void FillCircle(float cx, float cy, float radius, RGBAColor32 color)
        {
            if (radius <= 0) return;
            var r = (int)radius;
            Renderer.FillEllipse(
                new RectInt(new PointInt((int)(cx + r), (int)(cy + r)), new PointInt((int)(cx - r), (int)(cy - r))),
                color);
        }

        /// <summary>
        /// Draws a circle outline centered at (<paramref name="cx"/>, <paramref name="cy"/>).
        /// Delegates to <see cref="Renderer{TSurface}.DrawEllipse"/> for GPU-efficient rendering
        /// when available.
        /// </summary>
        protected void DrawCircle(float cx, float cy, float radius, RGBAColor32 color, float strokeWidth = 1f)
        {
            if (radius <= 0) return;
            var r = (int)radius;
            Renderer.DrawEllipse(
                new RectInt(new PointInt((int)(cx + r), (int)(cy + r)), new PointInt((int)(cx - r), (int)(cy - r))),
                color, strokeWidth);
        }

        /// <summary>
        /// Draws an ellipse outline bounded by the given rectangle.
        /// </summary>
        protected void DrawEllipse(float x, float y, float w, float h, RGBAColor32 color, float strokeWidth = 1f)
        {
            if (w <= 0 || h <= 0) return;
            Renderer.DrawEllipse(
                new RectInt(new PointInt((int)(x + w), (int)(y + h)), new PointInt((int)x, (int)y)),
                color, strokeWidth);
        }

        /// <summary>
        /// Fills an axis-aligned ellipse bounded by the given rectangle.
        /// </summary>
        protected void FillEllipse(float x, float y, float w, float h, RGBAColor32 color)
        {
            if (w <= 0 || h <= 0) return;
            Renderer.FillEllipse(
                new RectInt(new PointInt((int)(x + w), (int)(y + h)), new PointInt((int)x, (int)y)),
                color);
        }

        protected void DrawText(ReadOnlySpan<char> text, string fontPath, float x, float y, float w, float h,
            float fontSize, RGBAColor32 color, TextAlign horizAlign = TextAlign.Near, TextAlign vertAlign = TextAlign.Center)
        {
            if (string.IsNullOrEmpty(fontPath)) return;
            Renderer.DrawText(text, fontPath, fontSize, color,
                new RectInt(new PointInt((int)(x + w), (int)(y + h)), new PointInt((int)x, (int)y)),
                horizAlign, vertAlign);
        }
    }
}
