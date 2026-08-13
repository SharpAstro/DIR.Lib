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
    /// Chrome colours for <see cref="PixelWidgetBase{TSurface}.DrawTrackSlider"/>: the unfilled track
    /// bar background and the draggable handle marker. The per-slider accent fill is passed separately.
    /// </summary>
    public readonly record struct TrackSliderChrome(RGBAColor32 TrackBackground, RGBAColor32 Handle);

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
        /// The window's DPI scale (device pixels per design unit), owned per widget instance -- a widget
        /// belongs to exactly one window/renderer, so the host sets this at startup and on resize (SDL
        /// <c>DisplayScale</c>, web <c>devicePixelRatio</c>; a terminal stays at 1). Layout helpers
        /// (<see cref="RenderLayout"/> / <see cref="ArrangeLayout"/> / <see cref="PaintLayout"/>) and
        /// pixel controls (<see cref="DrawTrackSlider(float,float,float,float,float,RGBAColor32,RectF32,HitResult,TrackSliderChrome,float?)"/>)
        /// default to it when their <c>dpiScale</c> argument is omitted, and input handlers can read it
        /// directly (input events carry no DPI). Pass an explicit value only to override -- e.g.
        /// <c>dpiScale: 1f</c> for a tree whose sizes are already device pixels.
        /// Virtual so a composite chrome widget can override the setter to propagate the new scale to the
        /// child widgets it hosts (one set-point at startup/resize instead of per-frame pushes).
        /// <para>
        /// <b>This is the PRE-map scale, and it is deliberately not <see cref="Renderer{TSurface}.ContentTransform"/>.</b>
        /// The two are the two halves of the same ordering rule, not duplicates of each other. This one is
        /// applied to design units BEFORE they are mapped to surface units (it reaches the engine through
        /// <see cref="PixelMeasureContext{TSurface}"/>), so it REFLOWS: text is measured and rasterized at
        /// the size it will occupy, which is what keeps glyphs sharp at 2x. The renderer's transform is
        /// folded into the projection AFTER layout, which is what makes it free for a safe-area shift or a
        /// hot-seat flip -- and is exactly why DPI must not move there: layout would resolve at design size
        /// and the GPU would scale the raster, blurring every glyph. See <see cref="ContentTransform"/> for
        /// the rule in full.
        /// </para>
        /// </summary>
        public virtual float DpiScale { get; set; } = 1f;

        /// <summary>
        /// The window's primary text font (an absolute path or a family name the
        /// <see cref="Renderer{TSurface}"/> resolves), owned per widget instance like <see cref="DpiScale"/> --
        /// a widget belongs to exactly one window, so the host resolves the font once and sets this at startup
        /// (and again if the font changes). The layout helpers
        /// (<see cref="RenderLayout"/> / <see cref="ArrangeLayout"/> / <see cref="PaintLayout"/>) default to it
        /// when their <c>fontPath</c> argument is omitted; the lower-level draw helpers (<see cref="DrawText"/>,
        /// <see cref="RenderButton"/>, ...) still take an explicit font so a caller can draw a run in a
        /// different face (e.g. the emoji font). Empty = unresolved: the text helpers no-op on an empty font,
        /// so an unconfigured widget (headless test, pre-resolve frame) draws no text rather than throwing.
        /// Virtual so a composite chrome widget can override the setter to push the font to the child widgets
        /// it hosts (one set-point at startup instead of per-frame pushes).
        /// </summary>
        public virtual string FontPath { get; set; } = string.Empty;

        /// <summary>
        /// Optional emoji/symbol fallback font for glyphs the primary <see cref="FontPath"/> lacks (colour
        /// emoji, weather/planet symbols). Null = none (callers fall back to <see cref="FontPath"/>). Owned and
        /// propagated exactly like <see cref="FontPath"/>. DIR.Lib's own helpers do not consume it -- it rides
        /// here so a consumer's widgets share one owner for the whole per-window font set.
        /// </summary>
        public virtual string? EmojiFontPath { get; set; }

        /// <summary>
        /// Optional per-codepoint font fallback covering the whole font set — the general form of
        /// <see cref="EmojiFontPath"/>, which handles exactly one extra face and only for the emoji
        /// ranges. When set, <c>DrawText</c> splits a string into runs each drawn with a font that
        /// actually covers it, so a symbol or a foreign script inside an ordinary label renders instead
        /// of coming out as <c>.notdef</c>. The layout helpers propagate it into the measure context, so
        /// text is sized for the fonts it will be drawn with.
        ///
        /// <para>Null (the default) leaves drawing exactly as it was, including the emoji path.</para>
        /// </summary>
        public virtual FontFallbackResolver? FontFallback { get; set; }

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
        protected void RegisterClickable(float x, float y, float w, float h, HitResult result, Action<InputModifier>? onClick = null, CursorKind? cursor = null)
            => _tracker.Register(x, y, w, h, result, onClick, cursor);

        /// <summary>Registers a region that only states a cursor -- a card, a bar -- with no action.</summary>
        protected void RegisterCursor(float x, float y, float w, float h, CursorKind cursor)
            => _tracker.RegisterCursor(x, y, w, h, cursor);

        /// <summary>
        /// Registers a text input field — renders it and registers the clickable region.
        /// </summary>
        protected void RenderTextInput(TextInputState state, int x, int y, int width, int height, string fontPath,
            float fontSize, TextInputColors? colors = null)
        {
            TextInputRenderer.Render(Renderer, state, x, y, width, height, fontPath, fontSize, FrameCount, colors);
            // A field is where text is edited, so the I-beam comes with it rather than being arranged for
            // separately by whatever happens to enclose it.
            RegisterClickable(x, y, width, height, new HitResult.TextInputHit(state), cursor: CursorKind.Text);
        }

        /// <summary>
        /// Arranged-rect overload of
        /// <see cref="RenderTextInput(TextInputState,int,int,int,int,string,float)"/> for layout-driven
        /// callers that hold a float <see cref="RectF32"/> (a Fill leaf's arranged bounds) rather than
        /// integer pixel positions. Rounds to whole pixels once here -- the text-input renderer is
        /// integer-grid (RectInt) internally -- so call sites stop repeating the four-way (int) cast.
        /// </summary>
        protected void RenderTextInput(TextInputState state, RectF32 rect, string fontPath, float fontSize,
            TextInputColors? colors = null) =>
            RenderTextInput(state,
                (int)MathF.Round(rect.X), (int)MathF.Round(rect.Y),
                (int)MathF.Round(rect.Width), (int)MathF.Round(rect.Height),
                fontPath, fontSize, colors);

        // -------------------------------------------------------------------------------------------------
        // TrackSlider -- the one horizontal press/drag/release track (WB / wavelet / scrub / ...).
        //
        // A horizontal track (unfilled bar + played/value fill + a draggable handle) plus a cursor-X ->
        // fraction mapping against a captured hit-band rect. Generic: the track + handle colours arrive as a
        // TrackSliderChrome, the accent fill + fraction + geometry + hit payload per call, and dpiScale scales
        // the bar/handle thickness (the only DPI-dependent bit). Consumers pass their own chrome so no widget
        // re-triplicates the bar/fill/handle/clamp math or the drag arithmetic.
        // -------------------------------------------------------------------------------------------------

        /// <summary>
        /// Draws a <see cref="Layout.IconKind"/> into <paramref name="rect"/> out of rectangles, which is the
        /// whole point of naming an icon rather than spelling it: nothing has to carry the codepoint, so a
        /// machine missing a symbol face cannot turn the icon into a .notdef box. Sized off the rect's short
        /// side, so it scales with whatever the engine arranged and takes no DPI argument.
        /// <para>
        /// <b>Every kind inks the FULL square it is given</b>, so a row of different marks at one declared
        /// size comes out one height. That is not free -- each drawing is tuned to reach its bounding box --
        /// and it is the property that makes the set usable as a family: before it, the same declared size
        /// produced ink spanning 63% (a half-disc) to 100% (the grid) of it, a 1.6x spread that no amount of
        /// centring can hide. A new kind owes the same, and the LayoutIconTests measure it.
        /// <para>
        /// A kind with no drawing here paints nothing rather than throwing -- a blank button is visible on
        /// the first frame, which is the right way for a forgotten kind to announce itself.
        /// </para>
        /// </summary>
        protected void DrawLayoutIcon(Layout.IconKind kind, RectF32 rect, RGBAColor32 ink)
        {
            var side = MathF.Min(rect.Width, rect.Height);
            if (side <= 0f)
            {
                return;
            }

            // 5.4 units across the icon: two 2.2-unit cells plus the 1-unit gutter between them. The bars
            // below reuse the same unit, so the two icons read as one family at any size.
            var unit = MathF.Max(1f, side / 5.4f);

            switch (kind)
            {
                case Layout.IconKind.Grid:
                    var cell = unit * 2.2f;
                    var quad = cell * 2f + unit;
                    var ox = rect.X + (rect.Width - quad) / 2f;
                    var oy = rect.Y + (rect.Height - quad) / 2f;
                    for (var r = 0; r < 2; r++)
                    {
                        for (var c = 0; c < 2; c++)
                        {
                            FillRect(ox + c * (cell + unit), oy + r * (cell + unit), cell, cell, ink);
                        }
                    }

                    break;

                case Layout.IconKind.CaretUp:
                case Layout.IconKind.CaretDown:
                {
                    // Filled from rows of rectangles, because the surface has no triangle primitive and
                    // the rest of the family is constructed from rectangles too. One row per surface
                    // pixel of height: the width interpolates from a point to the full span, so the mark
                    // reaches all four edges of its declared square, which is the contract every kind
                    // here owes (see the remarks above).
                    var up = kind == Layout.IconKind.CaretUp;
                    var rows = Math.Max(1, (int)MathF.Round(side));
                    var step = side / rows;
                    var ox2 = rect.X + (rect.Width - side) / 2f;
                    var oy2 = rect.Y + (rect.Height - side) / 2f;
                    for (var r = 0; r < rows; r++)
                    {
                        // 0 at the apex, 1 at the base. +1 on the numerator so the apex row is a mark
                        // rather than a zero-width nothing, which would cost the mark its own tip.
                        var frac = (r + 1f) / rows;
                        // Snapped to whole pixels, and never thinner than one: a half-pixel apex is a
                        // half-covered column either side of centre, which at chip size is a tip that
                        // reads as a blunt end or vanishes into the background entirely.
                        var w = MathF.Max(1f, MathF.Round(side * frac));
                        var y = up ? oy2 + r * step : oy2 + side - (r + 1) * step;
                        FillRect(MathF.Round(ox2 + (side - w) / 2f), y, w, step, ink);
                    }

                    break;
                }

                case Layout.IconKind.Auto:
                    // Four corner brackets from rectangles, then the A from three strokes. Constructed
                    // rather than spelled for the same reason as the others -- but note the A itself would
                    // have been safe as text, since the .notdef risk is about symbol faces and this is ASCII.
                    var arm = unit * 1.6f;
                    // Weighted to match the theme marks, which are filled shapes and so read heavier at the
                    // same nominal size. A hairline bracket beside a solid crescent makes the six look like
                    // two families sharing a header rather than one set.
                    var pen = MathF.Max(1.2f, unit * 0.58f);
                    // Flush to the edges: Size is the mark's BOUNDING BOX, so the brackets reach it.
                    const float inset = 0f;
                    var l = rect.X + inset;
                    var t = rect.Y + inset;
                    var r2 = rect.X + rect.Width - inset;
                    var b = rect.Y + rect.Height - inset;
                    foreach (var (cx, cy, sx, sy) in new[]
                    {
                        (l, t, 1f, 1f), (r2, t, -1f, 1f), (l, b, 1f, -1f), (r2, b, -1f, -1f),
                    })
                    {
                        // Each corner is one horizontal arm and one vertical arm, mirrored by (sx, sy).
                        FillRect(sx > 0 ? cx : cx - arm, sy > 0 ? cy : cy - pen, arm, pen, ink);
                        FillRect(sx > 0 ? cx : cx - pen, sy > 0 ? cy : cy - arm, pen, arm, ink);
                    }

                    var aw = unit * 1.9f;
                    var ah = unit * 2.4f;
                    var acx = rect.X + rect.Width / 2f;
                    var acy = rect.Y + rect.Height / 2f;
                    var stroke = (int)MathF.Round(MathF.Max(1f, unit * 0.58f));
                    DrawLine(acx - aw / 2f, acy + ah / 2f, acx, acy - ah / 2f, ink, stroke);
                    DrawLine(acx + aw / 2f, acy + ah / 2f, acx, acy - ah / 2f, ink, stroke);
                    DrawLine(acx - aw * 0.3f, acy + ah * 0.16f, acx + aw * 0.3f, acy + ah * 0.16f, ink, stroke);
                    break;

                case Layout.IconKind.List:
                    var barW = unit * 5.2f;
                    // Three bars and two gaps span the full side, keeping the old 0.6-to-1 bar:gap ratio:
                    // 3(0.6g) + 2g = 3.8g = side.
                    var listGap = side / 3.8f;
                    var barH = MathF.Max(1.5f, listGap * 0.6f);
                    var bars = barH * 3f + listGap * 2f;
                    var bx = rect.X + (rect.Width - barW) / 2f;
                    var by = rect.Y + (rect.Height - bars) / 2f;
                    for (var i = 0; i < 3; i++)
                    {
                        FillRect(bx, by + i * (barH + listGap), barW, barH, ink);
                    }

                    break;

                case Layout.IconKind.ThemeLight:
                    // A disc with eight rays. The GAP between disc and rays is what makes this a sun rather
                    // than a fuzzy dot, so it is a proportion with a floor rather than a proportion alone: at
                    // 13 px a flat fraction left under 2 px of gap and the rays closed on the disc.
                    var sunR = side * 0.17f;
                    DiscSpans(rect, sunR, ink);
                    var rayPen = (int)MathF.Max(1f, side * 0.075f);
                    var rayInner = sunR + MathF.Max(1.5f, side * 0.085f);
                    // A stroke is centred on its endpoint, so stopping half a pen short puts its outer edge
                    // exactly on the bounding box.
                    var rayOuter = (side - rayPen) / 2f;
                    var scx = rect.X + rect.Width / 2f;
                    var scy = rect.Y + rect.Height / 2f;
                    for (var i = 0; i < 8; i++)
                    {
                        var (sin, cos) = MathF.SinCos(i * MathF.PI / 4f);
                        DrawLine(scx + cos * rayInner, scy + sin * rayInner,
                            scx + cos * rayOuter, scy + sin * rayOuter, ink, rayPen);
                    }

                    break;

                case Layout.IconKind.ThemeDark:
                    // A crescent: the outer disc MINUS an offset one. Built from the spans the subtraction
                    // leaves rather than by over-painting the offset disc in the button's background, which is
                    // how a renderer with no path subtraction usually fakes it. That trick needs to know the
                    // ground, so it breaks over a gradient, an image, or a transparent node -- and this
                    // painter is handed ink and a rect, nothing else. Scanline spans need no ground at all.
                    CrescentSpans(rect, side * 0.5f, side * 0.44f, ink);
                    break;

                case Layout.IconKind.ThemeSystem:
                    // Half filled, half outlined: the conventional "follow the system" / contrast mark. The
                    // outlined half is what makes it read as a divided disc rather than as a half-moon, which
                    // is the distinction that matters when ThemeDark sits next to it.
                    var sysR = side * 0.5f;
                    DiscSpans(rect, sysR, ink, leftHalfOnly: true);
                    RingSpans(rect, sysR, MathF.Max(1f, side * 0.075f), ink, rightHalfOnly: true);
                    break;
            }
        }

        /// <summary>
        /// Fills a disc as horizontal spans, one per device row, so a curve costs no path support and no
        /// per-pixel plotting. <paramref name="leftHalfOnly"/> keeps the spans left of centre.
        /// </summary>
        private void DiscSpans(RectF32 rect, float r, RGBAColor32 ink, bool leftHalfOnly = false)
        {
            var cx = rect.X + rect.Width / 2f;
            var cy = rect.Y + rect.Height / 2f;
            var rows = (int)MathF.Ceiling(r * 2f);
            for (var i = 0; i < rows; i++)
            {
                var y = cy - r + i;
                var dy = y + 0.5f - cy;
                var half = r * r - dy * dy;
                if (half <= 0f)
                {
                    continue;
                }

                half = MathF.Sqrt(half);
                var x0 = cx - half;
                var x1 = leftHalfOnly ? cx : cx + half;
                if (x1 > x0)
                {
                    FillRect(x0, y, x1 - x0, 1f, ink);
                }
            }
        }

        /// <summary>Outlines a disc as spans: the same scan, keeping only <paramref name="pen"/> at each end.</summary>
        private void RingSpans(RectF32 rect, float r, float pen, RGBAColor32 ink, bool rightHalfOnly = false)
        {
            var cx = rect.X + rect.Width / 2f;
            var cy = rect.Y + rect.Height / 2f;
            var rows = (int)MathF.Ceiling(r * 2f);
            for (var i = 0; i < rows; i++)
            {
                var y = cy - r + i;
                var dy = y + 0.5f - cy;
                var outer = r * r - dy * dy;
                if (outer <= 0f)
                {
                    continue;
                }

                outer = MathF.Sqrt(outer);
                var inner = (r - pen) * (r - pen) - dy * dy;
                if (inner <= 0f)
                {
                    // Past the ring's shoulders the row is solid, which is what closes the top and bottom.
                    var x0 = rightHalfOnly ? cx : cx - outer;
                    if (cx + outer > x0)
                    {
                        FillRect(x0, y, cx + outer - x0, 1f, ink);
                    }

                    continue;
                }

                inner = MathF.Sqrt(inner);
                if (!rightHalfOnly)
                {
                    FillRect(cx - outer, y, outer - inner, 1f, ink);
                }

                FillRect(cx + inner, y, outer - inner, 1f, ink);
            }
        }

        /// <summary>
        /// Fills the crescent left by subtracting a disc of <paramref name="biteR"/>, offset up and to the
        /// right, from one of <paramref name="r"/> -- as the spans of the outer disc that the inner one does
        /// not cover.
        /// </summary>
        private void CrescentSpans(RectF32 rect, float r, float biteR, RGBAColor32 ink)
        {
            var cx = rect.X + rect.Width / 2f;
            var cy = rect.Y + rect.Height / 2f;
            // Offset toward the upper right, so the crescent opens that way and its horns point down-left --
            // the orientation nearly every "dark mode" mark uses.
            var bx = cx + r * 0.52f;
            var by = cy - r * 0.34f;

            var rows = (int)MathF.Ceiling(r * 2f);
            for (var i = 0; i < rows; i++)
            {
                var y = cy - r + i;
                var dy = y + 0.5f - cy;
                var outer = r * r - dy * dy;
                if (outer <= 0f)
                {
                    continue;
                }

                outer = MathF.Sqrt(outer);
                float x0 = cx - outer, x1 = cx + outer;

                var bdy = y + 0.5f - by;
                var bite = biteR * biteR - bdy * bdy;
                if (bite > 0f)
                {
                    bite = MathF.Sqrt(bite);
                    float b0 = bx - bite, b1 = bx + bite;

                    // The bite covers this row's right end (it is offset right), so the visible part is
                    // whatever lies left of it. A row swallowed whole contributes nothing.
                    if (b0 <= x0 && b1 >= x1)
                    {
                        continue;
                    }

                    if (b0 > x0 && b0 < x1)
                    {
                        x1 = b0;
                    }
                    else if (b1 > x0 && b1 < x1)
                    {
                        x0 = b1;
                    }
                }

                if (x1 > x0)
                {
                    FillRect(x0, y, x1 - x0, 1f, ink);
                }
            }
        }

        /// <summary>
        /// Draws one horizontal track slider and registers its drag hit-band. <paramref name="frac"/> is the
        /// normalised fill/handle position in [0, 1]. <paramref name="barCenterY"/> is the vertical centre of
        /// the thin track bar; the draggable handle is a <paramref name="handleH"/>-tall marker at
        /// <paramref name="handleY"/>. <paramref name="hitBand"/> is the full press/drag region (its X/Width
        /// drive the cursor-X -> value mapping in <see cref="TrackFrac"/>; the caller also stores it in the
        /// slider's track-rect field for that drag). <paramref name="chrome"/> supplies the unfilled-track +
        /// handle colours; <paramref name="fillColor"/> is the per-slider accent; <paramref name="dpiScale"/>
        /// scales the bar/handle thickness.
        /// </summary>
        protected void DrawTrackSlider(float trackX, float trackW, float barCenterY, float handleY,
            float handleH, float frac, RGBAColor32 fillColor, RectF32 hitBand, HitResult hit,
            TrackSliderChrome chrome, float? dpiScale = null)
        {
            var scale = dpiScale ?? DpiScale;
            var barH = MathF.Max(4f, 6f * scale);
            var handleW = MathF.Max(4f, 6f * scale);

            var barY = barCenterY - barH / 2f;
            FillRect(trackX, barY, trackW, barH, chrome.TrackBackground);
            FillRect(trackX, barY, trackW * frac, barH, fillColor);

            // Handle marker; guard the clamp's upper bound for a sliver-thin track (trackW < handleW would
            // make Math.Clamp's max < min and throw -- the minimize-to-sliver crash).
            var handleMax = MathF.Max(trackX, trackX + trackW - handleW);
            var handleX = Math.Clamp(trackX + trackW * frac - handleW / 2f, trackX, handleMax);
            FillRect(handleX, handleY, handleW, handleH, chrome.Handle);

            RegisterClickable(hitBand.X, hitBand.Y, hitBand.Width, hitBand.Height, hit);
        }

        /// <summary>
        /// Convenience overload: the thin track bar is centred vertically within the handle band
        /// [<paramref name="handleY"/>, handleY + <paramref name="handleH"/>] -- the common case where the
        /// bar runs through the middle of the handle (so the caller passes the handle band once, not the
        /// handle band AND a separate bar centre). Use the <c>barCenterY</c> overload only when the bar and
        /// handle occupy different vertical bands, e.g. a scrub bar centred on a taller strip while the
        /// handle spans a shorter content row.
        /// </summary>
        protected void DrawTrackSlider(float trackX, float trackW, float handleY, float handleH, float frac,
            RGBAColor32 fillColor, RectF32 hitBand, HitResult hit, TrackSliderChrome chrome, float? dpiScale = null) =>
            DrawTrackSlider(trackX, trackW, handleY + handleH / 2f, handleY, handleH, frac,
                fillColor, hitBand, hit, chrome, dpiScale);

        /// <summary>
        /// Maps a cursor X onto a fraction in [0, 1] across <paramref name="track"/> (the captured hit-band).
        /// The single drag-math primitive behind every track slider's Update* handler.
        /// </summary>
        protected static float TrackFrac(RectF32 track, float px)
            => track.Width <= 0f ? 0f : Math.Clamp((px - track.X) / track.Width, 0f, 1f);

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

        /// <summary>
        /// The largest size at or below <paramref name="preferred"/> at which <paramref name="text"/> measures
        /// no wider than <paramref name="maxWidth"/> — <see cref="TextTrim.Shrink"/> for the draw helpers, which
        /// take a rect and a size directly rather than a <see cref="Layout.Node"/> tree.
        ///
        /// <para>Needed because <see cref="DrawText"/> does not bound a run to the rect it is handed: it starts
        /// at the edge and keeps going, so an over-long label draws over whatever shares its strip. Inside a
        /// layout tree the painter now does this for you from the run's own
        /// <see cref="Layout.Content.Text.Trim"/>; this is the same fit for the hand-placed paths — a status
        /// bar, a two-ended strip either side of a camera cutout, a column header beside a button.</para>
        ///
        /// <para>Measured with the widget's <see cref="FontPath"/> and <see cref="FontFallback"/>, so the width
        /// is the one <see cref="DrawText"/> will actually produce. Sizes are in drawing units, like every
        /// other <c>fontSize</c> on this type.</para>
        /// </summary>
        /// <param name="text">The run that has to fit.</param>
        /// <param name="preferred">The size it wants; never exceeded.</param>
        /// <param name="maxWidth">Space available. Zero or negative is "unconstrained" and returns
        /// <paramref name="preferred"/> — nothing is known about the space, so nothing is given up.</param>
        /// <param name="minFontSize">Floor, below which overflowing visibly beats shrinking further.</param>
        protected float FitFontSize(ReadOnlySpan<char> text, float preferred, float maxWidth,
            float minFontSize = TextFit.DefaultMinFontSize)
            => FitFontSize(text, FontPath, preferred, maxWidth, minFontSize);

        /// <summary>
        /// <see cref="FitFontSize(ReadOnlySpan{char}, float, float, float)"/> measured with an explicit font —
        /// for a widget that draws a run in a face other than its own <see cref="FontPath"/> (a symbol or
        /// emoji face, a second UI font). The widget's <see cref="FontFallback"/> still applies, matching
        /// <see cref="DrawText"/>.
        /// </summary>
        protected float FitFontSize(ReadOnlySpan<char> text, string fontPath, float preferred, float maxWidth,
            float minFontSize = TextFit.DefaultMinFontSize)
            => TextFit.ShrinkToWidth(Renderer, text, fontPath, FontFallback, preferred, maxWidth, minFontSize);

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

        /// <summary>The cursor stated by the topmost region under the point, or null if none had a
        /// view — see <see cref="CursorKind"/> for why this is asked of the regions rather than
        /// computed from geometry by the host.</summary>
        public CursorKind? HitTestCursor(float x, float y) => _tracker.HitTestCursor(x, y);

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

            var x = dropdown.AnchorX;
            var y = dropdown.AnchorY;
            var w = dropdown.AnchorWidth;

            // Clamp the menu to the space between its anchor and the bottom of the surface (or an explicit
            // maxHeight, whichever is smaller) so a long list scrolls within view instead of running off the
            // bottom edge -- the correctness fix that makes the scroll engage with no consumer change. A menu
            // that already fits is unchanged: dropdownH stays totalItems * rowH, so MaxOffset is 0, no
            // scrollbar draws, and every row renders exactly as before.
            var available = MathF.Max(rowH, viewportHeight - y);
            var clamp = maxHeight > 0f ? MathF.Min(maxHeight, available) : available;
            var dropdownH = MathF.Min(totalItems * rowH, clamp);

            // Full-screen backdrop — closes dropdown on click-outside
            RegisterClickable(0, 0, viewportWidth, viewportHeight, new HitResult.ButtonHit("DropdownBackdrop"),
                _ => dropdown.Close());

            // Border
            FillRect(x - 1f, y - 1f, w + 2f, dropdownH + 2f, borderColor);
            // Background
            FillRect(x, y, w, dropdownH, bgColor);

            // The menu body is a scroll viewport of `totalItems` atoms, each `rowH` tall. A menu that fits
            // (maxHeight unset, or few enough rows) resolves to MaxOffset 0 -- no scrollbar, full-width
            // rows, offset 0 -- so the common case is byte-identical to the pre-scroll behaviour (the old
            // "+0.5px fit epsilon" that kept an exact-fit last row now lives in ListScrollController's
            // VisibleAtoms). A menu clamped by maxHeight scrolls its window instead of silently dropping the
            // rows past the fold: keyboard Up/Down scrolls via DropdownMenuState.HandleKeyDown->EnsureVisible,
            // a wheel forwarded to HandleScrollInput scrolls too, and the scrollbar draws as the indicator.
            var scroll = dropdown.Scroll;
            scroll.SetExtent(new RectF32(x, y, w, dropdownH), rowH, totalItems, DpiScale);

            // Slightly dimmed, blue-shifted text for the "Custom..." entry (the last atom when present).
            var customColor = new RGBAColor32(
                (byte)((textColor.Red * 3 + 2) / 4),
                (byte)((textColor.Green * 3 + 2) / 4),
                (byte)Math.Min(255, textColor.Blue + 40),
                textColor.Alpha);

            foreach (var (index, rect) in scroll.VisibleRows())
            {
                var isCustom = dropdown.HasCustomEntry && index == dropdown.Items.Length;

                if (index == dropdown.HighlightIndex)
                {
                    FillRect(rect.X, rect.Y, rect.Width, rowH, highlightColor);
                }

                var label = isCustom ? dropdown.CustomEntryLabel : dropdown.Items[index];
                DrawText(label.AsSpan(), fontPath,
                    rect.X + padding, rect.Y, rect.Width - padding * 2f, rowH,
                    fontSize, isCustom ? customColor : textColor, TextAlign.Near, TextAlign.Center);

                var capturedIndex = index;
                if (isCustom)
                {
                    RegisterClickable(rect.X, rect.Y, rect.Width, rowH, new HitResult.ListItemHit("Dropdown", capturedIndex),
                        _ =>
                        {
                            dropdown.OnCustom?.Invoke();
                            dropdown.Close();
                        });
                }
                else
                {
                    var capturedItem = dropdown.Items[index];
                    RegisterClickable(rect.X, rect.Y, rect.Width, rowH, new HitResult.ListItemHit("Dropdown", capturedIndex),
                        _ =>
                        {
                            dropdown.OnSelect?.Invoke(capturedIndex, capturedItem);
                            dropdown.Close();
                        });
                }
            }

            scroll.DrawScrollBar(FillRect);
        }

        // --- Declarative layout (Layout.Node tree -> arrange -> paint + auto-bind clicks) ---

        /// <summary>
        /// Arranges a declarative <see cref="Layout.Node"/> tree into <paramref name="bounds"/> using this
        /// widget's renderer as the text-width oracle. Returns the flat pre-order arranged tree (also handy
        /// for inspection / custom hit-testing). <paramref name="fontPath"/> defaults to the widget's
        /// <see cref="FontPath"/> and <paramref name="dpiScale"/> to its <see cref="DpiScale"/>; pass an
        /// explicit value only to override (e.g. <c>dpiScale: 1f</c> for a tree whose sizes are already
        /// device pixels).
        /// </summary>
        protected ImmutableArray<Layout.ArrangedNode<float>> ArrangeLayout(Layout.Node root, RectF32 bounds, string? fontPath = null, float? dpiScale = null)
            => ArrangeLayout(root, bounds, DefaultContext(fontPath, dpiScale));

        /// <summary>
        /// The measure context the scalar overloads use: the widget's font, DPI scale and
        /// <see cref="FontFallback"/>. Threading the resolver through here is what lets a consumer set it
        /// once on the widget and have measure and paint both honour it.
        /// </summary>
        private PixelMeasureContext<TSurface> DefaultContext(string? fontPath, float? dpiScale)
            => new(Renderer, fontPath ?? FontPath, dpiScale ?? DpiScale) { Fallback = FontFallback };

        /// <summary>
        /// <see cref="ArrangeLayout(Layout.Node, RectF32, string?, float?)"/> with an explicit measure
        /// context — for a tree authored in another unit convention (e.g.
        /// <see cref="PixelMeasureContext{TSurface}.CellAuthored"/> for a cell-authored tree shared with a
        /// terminal). Pass the SAME context to <see cref="PaintLayout(ImmutableArray{Layout.ArrangedNode{float}}, PixelMeasureContext{TSurface}, Action{Layout.Content.Fill, RectF32}?)"/>,
        /// which is what makes measure and paint incapable of disagreeing on font or scale.
        /// </summary>
        protected ImmutableArray<Layout.ArrangedNode<float>> ArrangeLayout(Layout.Node root, RectF32 bounds, PixelMeasureContext<TSurface> ctx)
            => Layout.Engine.Arrange(root, new Rect<float>(bounds.X, bounds.Y, bounds.Width, bounds.Height), ctx);

        /// <summary>
        /// Paints an already-arranged tree: each node's <see cref="Layout.Node.Background"/> fills first
        /// (parent-before-children emission = correct z-order), then leaf content draws, and any
        /// <see cref="Layout.Node.Hit"/> is bound to the node's arranged rect via
        /// <see cref="RegisterClickable"/> -- so draw-position and hit-region cannot drift.
        /// <paramref name="drawFill"/> handles <see cref="Layout.Content.Fill"/> escape-hatch leaves
        /// (charts, sky map, custom widgets).
        /// </summary>
        protected void PaintLayout(ImmutableArray<Layout.ArrangedNode<float>> arranged, string? fontPath = null, float? dpiScale = null,
            Action<Layout.Content.Fill, RectF32>? drawFill = null)
            => PaintLayout(arranged, DefaultContext(fontPath, dpiScale), drawFill);

        /// <summary>
        /// <see cref="PaintLayout(ImmutableArray{Layout.ArrangedNode{float}}, string?, float?, Action{Layout.Content.Fill, RectF32}?)"/>
        /// driven by the SAME measure context the tree was arranged with — the context is the one authority
        /// on font (<see cref="PixelMeasureContext{TSurface}.FontPath"/>), text scale
        /// (<see cref="PixelMeasureContext{TSurface}.FontScale"/>) and chrome scale, so a paint cannot use a
        /// scale the measure did not. The scalar overload delegates here with an isotropic context, which is
        /// byte-identical to what it always did.
        /// </summary>
        protected void PaintLayout(ImmutableArray<Layout.ArrangedNode<float>> arranged, PixelMeasureContext<TSurface> ctx,
            Action<Layout.Content.Fill, RectF32>? drawFill = null)
        {
            var fp = ctx.FontPath;

            // The enclosing hyperlink, so a LinkHit stated on a row wrapper reaches the text leaves under it
            // rather than only working when it happens to sit on the text itself. Keyed by depth: entering a
            // node pops every entry at or below its own depth (those belong to a sibling subtree), so the top
            // is always the nearest enclosing link. Console.Lib's CellLayout resolves it identically -- the
            // two painters have to agree, or the same tree means different things per surface.
            var links = new Stack<(int Depth, string Url)>();

            foreach (var arrangedNode in arranged)
            {
                var (node, bounds) = arrangedNode;

                while (links.Count > 0 && links.Peek().Depth >= arrangedNode.Depth)
                {
                    links.Pop();
                }

                if (node.Hit is HitResult.LinkHit linkHit)
                {
                    links.Push((arrangedNode.Depth, linkHit.Url));
                }

                // CornerRadius is in design units like every other chrome measure, so it scales with DPI —
                // through the context's axis-free mapping, the same one that resolved it at measure time.
                var radius = ctx.ToSurface(node.CornerRadius);

                if (node.Background is { } bg)
                {
                    FillRect(bounds.X, bounds.Y, bounds.Width, bounds.Height, bg, radius);
                }

                // Auto-bind the click region to the arranged rect. Any node can be a hit target -- a whole
                // slot row or panel, not just a leaf -- and inner nodes register later so they win the hit.
                if (node.Hit is { } hit)
                {
                    RegisterClickable(bounds.X, bounds.Y, bounds.Width, bounds.Height, hit, node.OnClick, node.Cursor);
                }
                else if (node.Cursor is { } cursor)
                {
                    // A cursor with no hit still needs a region, or the statement has nowhere to live.
                    RegisterCursor(bounds.X, bounds.Y, bounds.Width, bounds.Height, cursor);
                }

                if (node is Layout.Node.Leaf leaf)
                {
                    switch (leaf.Content)
                    {
                        case Layout.Content.Text text:
                            // The context's resolver, not the widget's: paint must split on exactly what
                            // measure split on, or the arranged rect won't fit what lands in it.
                            //
                            // Fit the run to the rect the engine resolved for it, per the run's OWN policy
                            // (Layout.Content.Text.Trim): cut it at either end, scale it down, or -- with
                            // TextTrim.None -- let it overhang, which is what every run did before this
                            // painter learned to fit. Not cosmetic: DrawText starts at the rect edge and
                            // keeps going, so an over-wide run used to draw straight over its neighbour, on
                            // whichever surface sizes happened not to fit. The engine owns the rect; only
                            // the run knows which half of itself carries the meaning, which is why the
                            // policy travels with the run rather than living here.
                            var (value, fontSize) = TextFit.ForWidth(
                                Renderer, text.Value, fp, ctx.Fallback,
                                text.FontSize * ctx.FontScale, bounds.Width, text.Trim);
                            //
                            // Text under a LinkHit goes out as a selectable run carrying an Href, which is
                            // how a DOM host gets a real <a href> (new-tab, open, copy-link handled by the
                            // browser) instead of a bare clickable rect it has to reimplement. The click
                            // binding above is unchanged and still applies on every host -- the Href is the
                            // navigation affordance on top of it, the pixel-surface counterpart to the OSC 8
                            // wrap Console.Lib's CellLayout paints for the same node.
                            //
                            // Only LINKED text takes this path. Ordinary layout text stays on DrawText, so
                            // nothing else starts landing in the host's selection layer.
                            if (links.Count > 0)
                            {
                                DrawSelectableText(value, fp, ctx.Fallback,
                                    bounds.X, bounds.Y, bounds.Width, bounds.Height,
                                    fontSize, text.Color, text.HAlign, text.VAlign,
                                    links.Peek().Url);
                            }
                            else
                            {
                                DrawText(value.AsSpan(), fp, ctx.Fallback,
                                    bounds.X, bounds.Y, bounds.Width, bounds.Height,
                                    fontSize, text.Color, text.HAlign, text.VAlign);
                            }
                            break;
                        case Layout.Content.Box box when box.Color.Alpha > 0:
                            FillRect(bounds.X, bounds.Y, bounds.Width, bounds.Height, box.Color, radius);
                            break;
                        case Layout.Content.Icon icon:
                            // Draw at the size the icon DECLARES, centred, rather than filling whatever rect
                            // it was arranged into. Size was previously consulted only at measure time, which
                            // made it meaningless the moment a node carried explicit sizing -- and every real
                            // one does, since an icon lives in a button. The visible symptom was a mark
                            // beside a text run: a 13-unit icon in a 20-unit cell painted at 20, so it stood
                            // 38% taller than the word's cap height and read as misaligned even though both
                            // were centred on the same row. The rect still CLAMPS it, so a collapsed cell
                            // shrinks the mark rather than overflowing.
                            var iconSide = MathF.Min(
                                MathF.Min(bounds.Width, bounds.Height), ctx.ToSurface(icon.Size));
                            DrawLayoutIcon(icon.Kind,
                                new RectF32(
                                    bounds.X + (bounds.Width - iconSide) / 2f,
                                    bounds.Y + (bounds.Height - iconSide) / 2f,
                                    iconSide, iconSide),
                                icon.Color);
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

        /// <summary>
        /// Convenience: <see cref="ArrangeLayout"/> + <see cref="PaintLayout"/> in one call.
        /// <paramref name="fontPath"/> defaults to the widget's <see cref="FontPath"/> and
        /// <paramref name="dpiScale"/> to its <see cref="DpiScale"/>.
        /// </summary>
        protected ImmutableArray<Layout.ArrangedNode<float>> RenderLayout(Layout.Node root, RectF32 bounds, string? fontPath = null,
            float? dpiScale = null, Action<Layout.Content.Fill, RectF32>? drawFill = null)
            => RenderLayout(root, bounds, DefaultContext(fontPath, dpiScale), drawFill);

        /// <summary>
        /// <see cref="RenderLayout(Layout.Node, RectF32, string?, float?, Action{Layout.Content.Fill, RectF32}?)"/>
        /// with an explicit measure context, threaded through BOTH halves — one object answers measure and
        /// paint, so the two cannot drift.
        /// </summary>
        protected ImmutableArray<Layout.ArrangedNode<float>> RenderLayout(Layout.Node root, RectF32 bounds,
            PixelMeasureContext<TSurface> ctx, Action<Layout.Content.Fill, RectF32>? drawFill = null)
        {
            var arranged = ArrangeLayout(root, bounds, ctx);
            PaintLayout(arranged, ctx, drawFill);
            return arranged;
        }

        // --- Drawing helpers ---

        /// <summary>
        /// Restricts drawing to this rect until <see cref="PopClip"/> — the x/y/w/h form of
        /// <see cref="Renderer{TSurface}.PushClip"/>, which takes a <see cref="RectInt"/> and so takes
        /// its corners in the opposite order to every other rect a widget states. Single-level, like the
        /// renderer's own pair: a second call replaces the first rather than nesting.
        /// </summary>
        protected void PushClip(float x, float y, float w, float h)
            => Renderer.PushClip(new RectInt(new PointInt((int)(x + w), (int)(y + h)), new PointInt((int)x, (int)y)));

        /// <summary>Opens the clip back up to the whole surface.</summary>
        protected void PopClip() => Renderer.PopClip();

        protected void FillRect(float x, float y, float w, float h, RGBAColor32 color)
            => FillRect(x, y, w, h, color, cornerRadius: 0f);

        /// <summary>
        /// Fills a rect, optionally with rounded corners. A <paramref name="cornerRadius"/> of 0 routes to
        /// <see cref="Renderer{TSurface}.FillRectangle"/> so the untouched path stays byte-identical; a
        /// positive one goes through <see cref="Renderer{TSurface}.FillRoundedRectangle"/>, which a GPU
        /// backend may override with a single SDF quad.
        /// <para>
        /// The radius is expected in <b>surface</b> pixels here (already multiplied by the DPI scale),
        /// unlike <see cref="Layout.Node.CornerRadius"/>, which is in design units.
        /// </para>
        /// </summary>
        protected void FillRect(float x, float y, float w, float h, RGBAColor32 color, float cornerRadius)
        {
            if (w <= 0 || h <= 0) return;
            var rect = new RectInt(new PointInt((int)(x + w), (int)(y + h)), new PointInt((int)x, (int)y));
            if (cornerRadius > 0f)
            {
                Renderer.FillRoundedRectangle(rect, color, cornerRadius);
            }
            else
            {
                Renderer.FillRectangle(rect, color);
            }
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
            => DrawText(text, fontPath, FontFallback, x, y, w, h, fontSize, color, horizAlign, vertAlign);

        /// <summary>
        /// <see cref="DrawText(ReadOnlySpan{char}, string, float, float, float, float, float, RGBAColor32, TextAlign, TextAlign)"/>
        /// with an explicit fallback resolver, so a layout paint can draw with exactly the resolver its
        /// measure used rather than with whatever the widget currently has set.
        /// </summary>
        private void DrawText(ReadOnlySpan<char> text, string fontPath, FontFallbackResolver? fallback,
            float x, float y, float w, float h,
            float fontSize, RGBAColor32 color, TextAlign horizAlign, TextAlign vertAlign)
        {
            if (string.IsNullOrEmpty(fontPath)) return;

            // A run draws with exactly one font, so any string whose glyphs don't all live in one face has
            // to be split. The general form is coverage-driven: ask the resolver which font covers each
            // codepoint. PrimaryCoversAll is allocation-free and true for essentially all chrome, so the
            // split machinery only engages for the strings that actually need it.
            if (fallback is not null && !fallback.PrimaryCoversAll(text))
            {
                DrawCoverageRuns(text, fallback, x, y, w, h, fontSize, color, horizAlign, vertAlign);
                return;
            }

            // Mixed text + emoji needs two fonts, because a run is drawn with exactly one. Without this an
            // emoji inside ordinary text renders as blank space (a text font has no pictograph glyphs), which
            // is why callers used to have to pass the emoji font AS the font and therefore could never put a
            // glyph and a label in the same string. Superseded by the resolver above when one is set; kept
            // for widgets that declare only an emoji font.
            if (EmojiFontPath is { Length: > 0 } emojiFont
                && !string.Equals(emojiFont, fontPath, StringComparison.Ordinal)
                && ContainsEmoji(text))
            {
                DrawMixedEmojiText(text, fontPath, emojiFont, x, y, w, h, fontSize, color, horizAlign, vertAlign);
                return;
            }

            Renderer.DrawText(text, fontPath, fontSize, color,
                new RectInt(new PointInt((int)(x + w), (int)(y + h)), new PointInt((int)x, (int)y)),
                horizAlign, vertAlign);
        }

        // Reusable scratch for the coverage split — a widget paints on the render thread, one string at a
        // time, so a single list per widget keeps the fallback path allocation-free.
        private List<FontFallbackResolver.FontRun>? _coverageRuns;
        private float[]? _coverageWidths;

        private void DrawCoverageRuns(ReadOnlySpan<char> text, FontFallbackResolver fallback,
            float x, float y, float w, float h, float fontSize, RGBAColor32 color,
            TextAlign horizAlign, TextAlign vertAlign)
        {
            var runs = _coverageRuns ??= [];
            fallback.CoverageRuns(text, runs);
            if (runs.Count == 0) return;

            // Split and measure ONCE: alignment needs the total before anything can be placed, so the widths
            // are kept rather than recomputed on a second pass over the same runs.
            if (_coverageWidths is null || _coverageWidths.Length < runs.Count)
                _coverageWidths = new float[Math.Max(runs.Count, 8)];
            var widths = _coverageWidths;

            var total = 0f;
            for (var i = 0; i < runs.Count; i++)
            {
                var (start, length, runFont) = runs[i];
                widths[i] = Renderer.MeasureText(text.Slice(start, length), runFont, fontSize).Width;
                total += widths[i];
            }

            var cursor = horizAlign switch
            {
                TextAlign.Center => x + (w - total) / 2f,
                TextAlign.Far => x + w - total,
                _ => x,
            };

            for (var i = 0; i < runs.Count; i++)
            {
                var (start, length, runFont) = runs[i];
                Renderer.DrawText(text.Slice(start, length), runFont, fontSize, color,
                    new RectInt(
                        new PointInt((int)(cursor + widths[i]), (int)(y + h)),
                        new PointInt((int)cursor, (int)y)),
                    TextAlign.Near, vertAlign);
                cursor += widths[i];
            }
        }

        /// <summary>
        /// Whether <paramref name="text"/> holds a codepoint that needs the emoji font.
        /// <para>
        /// <b>Supplementary planes only</b> (U+1F000 and above), deliberately. The BMP symbol blocks are full
        /// of glyphs the text font already draws well and the app already relies on -- arrows, box drawing,
        /// stars, check marks -- and routing those to an emoji font would change existing chrome everywhere.
        /// Every pictograph anyone actually wants a colour glyph for lives in the supplementary planes.
        /// </para>
        /// </summary>
        private static bool ContainsEmoji(ReadOnlySpan<char> text)
        {
            // A supplementary codepoint is encoded as a surrogate pair, so a high surrogate is the cheap test.
            foreach (var c in text)
            {
                if (char.IsHighSurrogate(c)) return true;
            }
            return false;
        }

        /// <summary>
        /// Draws <paramref name="text"/> as alternating text/emoji runs, each with its own font, laid out
        /// left to right and aligned as a whole.
        /// <para>
        /// Alignment is resolved from the summed width of every run, then each run is drawn
        /// <see cref="TextAlign.Near"/> inside its own slice -- so a centred or right-aligned mixed string
        /// lands where a single-font one would, rather than each run centring itself.
        /// </para>
        /// </summary>
        private void DrawMixedEmojiText(ReadOnlySpan<char> text, string fontPath, string emojiFont,
            float x, float y, float w, float h, float fontSize, RGBAColor32 color,
            TextAlign horizAlign, TextAlign vertAlign)
        {
            // Split and measure ONCE: alignment needs the total before anything can be placed, so the widths
            // are kept rather than recomputed on a second pass over the same runs.
            var runs = EnumerateRuns(text);
            var widths = new float[runs.Count];
            var total = 0f;
            for (var i = 0; i < runs.Count; i++)
            {
                var (run, isEmoji) = runs[i];
                widths[i] = Renderer.MeasureText(
                    text.Slice(run.Start, run.Length), isEmoji ? emojiFont : fontPath, fontSize).Width;
                total += widths[i];
            }

            var cursor = horizAlign switch
            {
                TextAlign.Center => x + (w - total) / 2f,
                TextAlign.Far => x + w - total,
                _ => x,
            };

            for (var i = 0; i < runs.Count; i++)
            {
                var (run, isEmoji) = runs[i];
                Renderer.DrawText(text.Slice(run.Start, run.Length), isEmoji ? emojiFont : fontPath, fontSize, color,
                    new RectInt(
                        new PointInt((int)(cursor + widths[i]), (int)(y + h)),
                        new PointInt((int)cursor, (int)y)),
                    TextAlign.Near, vertAlign);
                cursor += widths[i];
            }
        }

        /// <summary>
        /// Splits <paramref name="text"/> into maximal runs of "needs the emoji font" / "does not".
        /// <para>
        /// A variation selector, a zero-width joiner and a skin-tone modifier all attach to the glyph before
        /// them, so they stay in the emoji run they belong to -- splitting there would break a ZWJ sequence
        /// into pieces that render as separate glyphs.
        /// </para>
        /// </summary>
        private static List<(TextRun Run, bool IsEmoji)> EnumerateRuns(ReadOnlySpan<char> text)
        {
            var runs = new List<(TextRun, bool)>();
            var i = 0;
            while (i < text.Length)
            {
                var emoji = IsEmojiAt(text, i, out var len);
                var start = i;
                i += len;
                while (i < text.Length && IsEmojiAt(text, i, out var nextLen) == emoji)
                {
                    i += nextLen;
                }
                runs.Add((new TextRun(start, i - start), emoji));
            }
            return runs;
        }

        private static bool IsEmojiAt(ReadOnlySpan<char> text, int index, out int length)
        {
            var c = text[index];
            if (char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                length = 2;
                return true;
            }

            length = 1;
            // Attaches to whatever came before, so it must not start a run of the other kind.
            return c is '️' or '‍';
        }

        /// <summary>A slice of a text span: an index and a length, so no substring is allocated.</summary>
        private readonly record struct TextRun(int Start, int Length);

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
        /// <para>
        /// Pass <paramref name="href"/> to mark the run as a hyperlink (see
        /// <see cref="SelectableTextRegion.Href"/>): a DOM host renders a real <c>&lt;a href&gt;</c>; the
        /// raster path is unchanged (no navigation model), so links are a web-only progressive enhancement.
        /// </para>
        /// </summary>
        protected void DrawSelectableText(string text, string fontPath, float x, float y, float w, float h,
            float fontSize, RGBAColor32 color, TextAlign horizAlign = TextAlign.Near, TextAlign vertAlign = TextAlign.Center,
            string? href = null)
            => DrawSelectableText(text, fontPath, FontFallback, x, y, w, h, fontSize, color, horizAlign, vertAlign, href);

        /// <summary>
        /// <see cref="DrawSelectableText(string, string, float, float, float, float, float, RGBAColor32, TextAlign, TextAlign, string?)"/>
        /// with an explicit fallback resolver, mirroring the <see cref="DrawText(ReadOnlySpan{char}, string, float, float, float, float, float, RGBAColor32, TextAlign, TextAlign)"/>
        /// pair — so a layout paint draws with exactly the resolver its MEASURE used rather than with
        /// whatever the widget currently has set. Without this overload, routing a linked run through
        /// selectable text would silently drop back to the widget's resolver and split the string
        /// differently from the arrange that sized its rect.
        /// </summary>
        private void DrawSelectableText(string text, string fontPath, FontFallbackResolver? fallback,
            float x, float y, float w, float h,
            float fontSize, RGBAColor32 color, TextAlign horizAlign, TextAlign vertAlign, string? href)
        {
            if (string.IsNullOrEmpty(fontPath) || string.IsNullOrEmpty(text)) return;
            if (!Renderer.HostRendersSelectableText)
            {
                DrawText(text.AsSpan(), fontPath, fallback, x, y, w, h, fontSize, color, horizAlign, vertAlign);
            }
            _selectableText.Add(new SelectableTextRegion(
                x, y, w, h, text, fontPath, fontSize, color, horizAlign, vertAlign, href));
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
