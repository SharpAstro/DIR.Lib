using System;

namespace DIR.Lib
{
    /// <summary>
    /// The <see cref="Layout.IconKind"/> drawings, split out of the widget base.
    /// <para>
    /// They live in their own part because they are the one block here that grows with a LIST rather
    /// than with the widget: every kind added to the family costs a case, and the set had reached a
    /// third of the file without any of it being about being a widget. Nothing else moved, and nothing
    /// changed -- the scanline span helpers come along because they exist only to draw these marks.
    /// </para>
    /// </summary>
    public abstract partial class PixelWidgetBase<TSurface>
    {
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
        /// <see cref="Layout.IconKind.Minus"/> is the sole exception and could not be otherwise: a horizontal
        /// bar has no height to give. It fills its full WIDTH and borrows <see cref="Layout.IconKind.Plus"/>'s
        /// bar thickness and centre line, so the pair still lines up where it matters -- beside each other in
        /// a stepper.
        /// </para>
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

                case Layout.IconKind.Plus:
                case Layout.IconKind.Minus:
                {
                    // Whole pixels, because a half-covered bar at chip size reads as a LIGHTER mark than its
                    // neighbour rather than a thinner one -- and a stepper sets the two side by side, where
                    // that is the one difference the eye is guaranteed to catch.
                    var barT = MathF.Max(1f, MathF.Round(side * 0.14f));
                    var px = rect.X + (rect.Width - side) / 2f;
                    var py = rect.Y + (rect.Height - side) / 2f;

                    // The arm across is drawn for both, identically, which is what makes the pair align: one
                    // thickness, one centre line, neither re-derived by the other kind.
                    FillRect(px, MathF.Round(py + (side - barT) / 2f), side, barT, ink);
                    if (kind == Layout.IconKind.Plus)
                    {
                        FillRect(MathF.Round(px + (side - barT) / 2f), py, barT, side, ink);
                    }

                    break;
                }

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

                case Layout.IconKind.Pan:
                {
                    // Four arrows from a common centre: two crossed shafts, then a barbed head on each
                    // end. Proportions relative to the mark's own side rather than absolute, so it is one
                    // mark at a 13-unit chip and at a 34-unit tool button -- the arms reach the box on
                    // both axes, which is the contract every kind owes.
                    var panArm = side / 2f;
                    var head = side * (2f / 9f);
                    var shaft = panArm - head;
                    var panPen = (int)MathF.Round(MathF.Max(1f, side * (1.6f / 18f)));
                    var pcx = rect.X + rect.Width / 2f;
                    var pcy = rect.Y + rect.Height / 2f;

                    DrawLine(pcx - shaft, pcy, pcx + shaft, pcy, ink, panPen);
                    DrawLine(pcx, pcy - shaft, pcx, pcy + shaft, ink, panPen);

                    // Filled heads, because a chevron of two strokes loses its point first at chip size --
                    // the same reason the carets are filled.
                    Span<float> heads =
                    [
                        pcx + panArm, pcy, pcx + shaft, pcy - head, pcx + shaft, pcy + head,
                        pcx - panArm, pcy, pcx - shaft, pcy - head, pcx - shaft, pcy + head,
                        pcx, pcy + panArm, pcx - head, pcy + shaft, pcx + head, pcy + shaft,
                        pcx, pcy - panArm, pcx - head, pcy - shaft, pcx + head, pcy - shaft,
                    ];
                    Renderer.DrawTriangles(heads, ink);
                    break;
                }

                case Layout.IconKind.IBeam:
                {
                    // A stem with a serif at each end. Like Minus this cannot ink its full square -- an
                    // I-beam is tall and narrow by definition -- so the height reaches the box and the
                    // width is the serifs'. They are the whole mark: a bare stem at chip size is a
                    // separator, which is the one neighbour it must not read as.
                    var half = side / 2f;
                    var serif = side * (2f / 9f);
                    var stem = (int)MathF.Round(MathF.Max(1f, side * (1.6f / 18f)));
                    var icx = rect.X + rect.Width / 2f;
                    var icy = rect.Y + rect.Height / 2f;

                    DrawLine(icx, icy - half, icx, icy + half, ink, stem);
                    DrawLine(icx - serif, icy - half, icx + serif, icy - half, ink, stem);
                    DrawLine(icx - serif, icy + half, icx + serif, icy + half, ink, stem);
                    break;
                }

                case Layout.IconKind.Search:
                {
                    // A ring up-left, a handle running out of it to the bottom-right corner. Both extremes
                    // are on the bounding box -- the ring's top-left arc and the handle's tip -- so the mark
                    // fills its declared square along the diagonal, which is the contract every kind here
                    // owes even though this one is the only diagonal in the family.
                    var lensPen = MathF.Max(1f, side * 0.105f);
                    // Radius measured to the ring's OUTER edge, then pulled in by half a pen, because
                    // RingSpans centres the stroke on the radius it is given: asking for the full 0.34
                    // would put the outer half of the pen past the box.
                    var lensR = side * 0.34f - lensPen / 2f;
                    // The lens sits up-left of centre by exactly the room the handle needs, so the handle
                    // can reach the corner without the two fighting over the middle.
                    var off = side * 0.5f - (lensR + lensPen / 2f);
                    var lens = new RectF32(rect.X + (rect.Width - side) / 2f - off,
                        rect.Y + (rect.Height - side) / 2f - off, side, side);
                    RingSpans(lens, lensR, lensPen, ink);

                    // From the ring's outer edge on the 45-degree diagonal out to the box's own corner. The
                    // far end is offset by `far` on EACH axis rather than along the diagonal: a point at
                    // radius `far` from the centre reaches only 0.71 of it per axis, which stops the handle
                    // well short and leaves the mark sitting in the middle of a square of nothing.
                    //
                    // Started ON the ring rather than clear of it, because a gap there reads as two marks
                    // at chip size where an overlap just looks like a joint.
                    var diag = MathF.Sqrt(0.5f);
                    var lcx = lens.X + lens.Width / 2f;
                    var lcy = lens.Y + lens.Height / 2f;
                    var hx0 = lcx + diag * lensR;
                    var hy0 = lcy + diag * lensR;
                    // Half a pen short of the edge, since a stroke is centred on its endpoint.
                    var far = side / 2f - lensPen / 2f;
                    var cx3 = rect.X + rect.Width / 2f;
                    var cy3 = rect.Y + rect.Height / 2f;
                    DrawLine(hx0, hy0, cx3 + far, cy3 + far, ink, (int)MathF.Round(lensPen));
                    break;
                }
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
    }
}
