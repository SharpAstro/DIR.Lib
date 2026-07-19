using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
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

        // Selectable-text regions registered this frame (paint order). Mirrors _tracker's lifecycle --
        // cleared in BeginFrame, appended by DrawSelectableText, snapshotted by a host that renders the
        // text as native selectable UI (web DOM span / terminal yank). Kept OUT of the clickable tracker
        // on purpose: a selectable-text rect must never shadow a button's click hit-test.
        private readonly List<SelectableTextRegion> _selectableText = [];

        // DEBUG-inspector capture of the arranged layout painted this frame. Null until
        // LayoutInspection is enabled (zero overhead in production); mirrors _tracker -- cleared in
        // BeginFrame, appended in PaintLayout, read by the inspector's describe_layout. Render-thread
        // only, like the region tracker.
        private List<Layout.ArrangedNode<float>>? _capturedLayout;

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
        /// Clears clickable regions (and the inspector layout capture, if enabled). Call at the start
        /// of each Render pass.
        /// </summary>
        protected void BeginFrame()
        {
            _tracker.BeginFrame();
            _selectableText.Clear();
            _capturedLayout?.Clear();
        }

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

        /// <summary>
        /// Measures the width of a shared value column sized to fit the widest of <paramref name="values"/>
        /// (plus <paramref name="horizontalPadding"/> on each side), clamped to
        /// [<paramref name="minWidth"/>, <paramref name="maxWidth"/>]. Intended for "[-] value [+]" stepper
        /// rows so every row aligns in one column and long values neither clip nor collide with the buttons.
        /// <paramref name="maxWidth"/> is floored to <paramref name="minWidth"/>, so a cramped panel collapses
        /// to the minimum rather than going negative. When no font is available (e.g. headless tests) the text
        /// cannot be measured, so <paramref name="minWidth"/> is returned unchanged.
        /// </summary>
        protected float MeasureValueColumnWidth(
            IReadOnlyList<string> values, string fontPath, float fontSize,
            float minWidth, float maxWidth, float horizontalPadding)
        {
            var clampMax = Math.Max(minWidth, maxWidth);

            if (string.IsNullOrEmpty(fontPath))
            {
                return Math.Min(minWidth, clampMax);
            }

            var width = minWidth;
            for (var i = 0; i < values.Count; i++)
            {
                var value = values[i];
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                var measured = Renderer.MeasureText(value.AsSpan(), fontPath, fontSize).Width + horizontalPadding * 2f;
                if (measured > width)
                {
                    width = measured;
                }
            }

            return Math.Min(width, clampMax);
        }

        /// <inheritdoc/>
        public List<TextInputState> GetRegisteredTextInputs() => _tracker.GetRegisteredTextInputs();

        /// <summary>
        /// Returns a snapshot of this widget's clickable regions from the last render pass.
        /// Surfaces the per-frame region set for the debug inspector (region bounds + role/label).
        /// </summary>
        public ClickableRegion[] GetRegisteredRegions() => _tracker.GetRegisteredRegions();

        /// <summary>
        /// Returns the arranged <see cref="Layout.ArrangedNode{T}"/> nodes this widget painted via the
        /// layout DSL since the last <c>BeginFrame</c> (each carries its tree <see cref="Layout.ArrangedNode{T}.Depth"/>),
        /// or empty when <see cref="LayoutInspection"/> is disabled or the widget draws without the
        /// layout DSL. Used by the DEBUG inspector's describe_layout to surface the full layout tree
        /// (not just the clickable subset). Render-thread only, read inside the inspector pump.
        /// </summary>
        public IReadOnlyList<Layout.ArrangedNode<float>> GetCapturedLayout()
            => _capturedLayout is { } captured ? captured : [];

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
            // +0.5px epsilon on the fit guard: when the items exactly fill dropdownH (= totalItems * rowH,
            // the unclamped case), the accumulated `itemY` (y + rowH + rowH + ...) can exceed `y + dropdownH`
            // (= y + N*rowH, computed by multiplication) by a sub-pixel float-rounding error, which would
            // silently clip the LAST item -- it bit the 3-entry Live Session mode dropdown ("Planetary" drew
            // no text). The epsilon is well under a row, so a genuinely overflowing item (maxHeight-clamped)
            // is still excluded.
            for (var i = 0; i < dropdown.Items.Length && itemY + rowH <= y + dropdownH + 0.5f; i++)
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

        // --- Declarative layout (Layout.Node tree -> arrange -> paint + auto-bind clicks) ---

        /// <summary>
        /// Arranges a declarative <see cref="Layout.Node"/> tree into <paramref name="bounds"/> using this
        /// widget's renderer as the text-width oracle. Returns the flat pre-order arranged tree (also handy
        /// for inspection / custom hit-testing).
        /// </summary>
        protected ImmutableArray<Layout.ArrangedNode<float>> ArrangeLayout(Layout.Node root, RectF32 bounds, string fontPath, float dpiScale = 1f)
        {
            var ctx = new PixelMeasureContext<TSurface>(Renderer, fontPath, dpiScale);
            return Layout.Engine.Arrange(root, new Rect<float>(bounds.X, bounds.Y, bounds.Width, bounds.Height), ctx);
        }

        /// <summary>
        /// Paints an already-arranged tree: each node's <see cref="Layout.Node.Background"/> fills first
        /// (parent-before-children emission = correct z-order), then leaf content draws, and any
        /// <see cref="Layout.Content.Hit"/> is bound to the node's arranged rect via
        /// <see cref="RegisterClickable"/> -- so draw-position and hit-region cannot drift.
        /// <paramref name="drawFill"/> handles <see cref="Layout.Content.Fill"/> escape-hatch leaves
        /// (charts, sky map, custom widgets).
        /// </summary>
        protected void PaintLayout(ImmutableArray<Layout.ArrangedNode<float>> arranged, string fontPath, float dpiScale = 1f,
            Action<Layout.Content.Fill, RectF32>? drawFill = null)
        {
            foreach (var (node, bounds) in arranged)
            {
                if (node.Background is { } bg)
                {
                    FillRect(bounds.X, bounds.Y, bounds.Width, bounds.Height, bg);
                }

                // Auto-bind the click region to the arranged rect. Any node can be a hit target -- a whole
                // slot row or panel, not just a leaf -- and inner nodes register later so they win the hit.
                if (node.Hit is { } hit)
                {
                    RegisterClickable(bounds.X, bounds.Y, bounds.Width, bounds.Height, hit, node.OnClick);
                }

                if (node is Layout.Node.Leaf leaf)
                {
                    switch (leaf.Content)
                    {
                        case Layout.Content.Text text:
                            DrawText(text.Value.AsSpan(), fontPath, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                                text.FontSize * dpiScale, text.Color, text.HAlign, text.VAlign);
                            break;
                        case Layout.Content.Box box when box.Color.Alpha > 0:
                            FillRect(bounds.X, bounds.Y, bounds.Width, bounds.Height, box.Color);
                            break;
                        case Layout.Content.Fill fill:
                            drawFill?.Invoke(fill, new RectF32(bounds.X, bounds.Y, bounds.Width, bounds.Height));
                            break;
                    }
                }
            }

            // Retain the arranged tree for the DEBUG inspector's describe_layout. Opt-in (null unless
            // LayoutInspection is on) so production paints pay nothing; appended across the frame's
            // multiple PaintLayout calls, exactly like the region tracker.
            if (LayoutInspection.Enabled)
            {
                (_capturedLayout ??= []).AddRange(arranged);
            }
        }

        /// <summary>Convenience: <see cref="ArrangeLayout"/> + <see cref="PaintLayout"/> in one call.</summary>
        protected ImmutableArray<Layout.ArrangedNode<float>> RenderLayout(Layout.Node root, RectF32 bounds, string fontPath,
            float dpiScale = 1f, Action<Layout.Content.Fill, RectF32>? drawFill = null)
        {
            var arranged = ArrangeLayout(root, bounds, fontPath, dpiScale);
            PaintLayout(arranged, fontPath, dpiScale, drawFill);
            return arranged;
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

        /// <summary>
        /// Draws a run of text AND registers it as a selectable region for this frame. Unless the host
        /// has opted into native text rendering
        /// (<see cref="Renderer{TSurface}.HostRendersSelectableText"/>, default off) this is
        /// <see cref="DrawText"/> plus a region registration; a host that HAS opted in (web with a DOM
        /// text layer) gets the region ONLY and paints a real, selectable DOM node over the rect itself.
        /// <para>
        /// Takes an immutable <see cref="string"/> (not a <c>ReadOnlySpan&lt;char&gt;</c>) on purpose: the
        /// region has to outlive the frame for the host to read after paint, so a durable reference is
        /// stored with ZERO copy -- the raster backend never allocates, and the web host hands the same
        /// string straight to JS. Selectable text is always string-backed (panel/detail readouts), so this
        /// costs nothing at the call site.
        /// </para>
        /// <para>
        /// Use for stable, read-only text worth selecting/copying -- info panels, detail readouts. Do NOT
        /// use for high-churn scene labels (sky-map star/constellation names reflow every pan frame); those
        /// stay on <see cref="DrawText"/> so they never spill into the host's DOM/selection layer.
        /// </para>
        /// </summary>
        protected void DrawSelectableText(string text, string fontPath, float x, float y, float w, float h,
            float fontSize, RGBAColor32 color, TextAlign horizAlign = TextAlign.Near, TextAlign vertAlign = TextAlign.Center)
        {
            if (string.IsNullOrEmpty(fontPath) || string.IsNullOrEmpty(text)) return;
            if (!Renderer.HostRendersSelectableText)
            {
                DrawText(text.AsSpan(), fontPath, x, y, w, h, fontSize, color, horizAlign, vertAlign);
            }
            _selectableText.Add(new SelectableTextRegion(
                x, y, w, h, text, fontPath, fontSize, color, horizAlign, vertAlign));
        }

        /// <summary>
        /// The selectable-text regions registered during the last render pass, in paint order, as a
        /// ZERO-COPY view over the internal frame list (no allocation, O(1) -- this API can carry
        /// thousands of runs per frame in a document viewer, so a defensive array copy is off the table).
        /// <para>
        /// Lifetime contract: the view stays valid until the widget's NEXT Render pass
        /// (<see cref="BeginFrame"/> clears the backing list) -- in a render-on-demand host that can be
        /// arbitrarily long, so a reader that skips a frame loses nothing; this is snapshot state (a
        /// stale overlay reconciles fully on the next read), not an event stream. The reader must be the
        /// same thread that runs Render (it is the frame driver itself), which makes a torn read
        /// structurally impossible; the ref-struct nature of <see cref="ReadOnlySpan{T}"/> additionally
        /// prevents stashing the view across frames. If a cross-thread consumer ever appears, switch this
        /// to a published immutable snapshot (CircularBuffer / ImmutableArray-CAS pattern) instead.
        /// </para>
        /// </summary>
        public ReadOnlySpan<SelectableTextRegion> SelectableTextRegions
            => CollectionsMarshal.AsSpan(_selectableText);

        /// <summary>
        /// Fills a horizontal text bar with <paramref name="bgColor"/> and draws a single line of
        /// <paramref name="text"/> inside it.  The text rect is inset by <paramref name="horizontalPadding"/>
        /// on each side; vertical alignment defaults to <see cref="TextAlign.Center"/> so the text is
        /// centred within the bar height without a manual y-nudge.
        /// </summary>
        /// <param name="text">The text to render.</param>
        /// <param name="fontPath">Path to the font file; no-op when null or empty.</param>
        /// <param name="x">Left edge of the bar, in pixels.</param>
        /// <param name="y">Top edge of the bar, in pixels.</param>
        /// <param name="w">Width of the bar, in pixels.</param>
        /// <param name="h">Height of the bar, in pixels.</param>
        /// <param name="fontSize">Font size in points/pixels.</param>
        /// <param name="bgColor">Background fill color.</param>
        /// <param name="textColor">Text color.</param>
        /// <param name="horizontalPadding">Pixels inset from left and right edges before drawing text (default 8).</param>
        /// <param name="alignX">Horizontal text alignment within the inset rect (default <see cref="TextAlign.Near"/>).</param>
        /// <param name="alignY">Vertical text alignment within the bar height (default <see cref="TextAlign.Center"/>).</param>
        protected void RenderTextBar(
            ReadOnlySpan<char> text,
            string fontPath,
            float x, float y, float w, float h,
            float fontSize,
            RGBAColor32 bgColor,
            RGBAColor32 textColor,
            float horizontalPadding = 8f,
            TextAlign alignX = TextAlign.Near,
            TextAlign alignY = TextAlign.Center)
        {
            FillRect(x, y, w, h, bgColor);
            DrawText(text, fontPath,
                x + horizontalPadding, y, w - horizontalPadding * 2f, h,
                fontSize, textColor, alignX, alignY);
        }
    }
}
