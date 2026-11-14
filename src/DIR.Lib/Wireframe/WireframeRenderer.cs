using System.Collections.Immutable;

namespace DIR.Lib;

/// <summary>One line of text as a frame drew it.</summary>
/// <param name="Order">Its place in the frame's paint order, shared with the fills and the regions.</param>
/// <param name="Box">The rectangle the caller handed <c>DrawText</c>.</param>
/// <param name="Ink">Where the glyphs land: the measured line, aligned in <paramref name="Box"/>. It leaves the
/// box when the text is wider than the box.</param>
/// <param name="Visible">The part of <paramref name="Ink"/> inside the clip in force, which is what a person sees.</param>
public readonly record struct WireframeText(int Order, string Text, RectF32 Box, RectF32 Ink, RectF32 Visible, float FontSize);

/// <summary>An opaque rectangle a frame filled, already cut to the clip in force.</summary>
public readonly record struct WireframeFill(int Order, RectF32 Rect, RGBAColor32 Color);

/// <summary>A clickable region as a widget registered it.</summary>
/// <param name="Visible">The part of <paramref name="Rect"/> inside the clip in force when it was registered.</param>
public readonly record struct WireframeRegion(int Order, RectF32 Rect, RectF32 Visible, HitResult Result);

/// <summary>What one frame put where: its text, its opaque fills and its clickable regions, in one paint order.</summary>
public sealed record WireframeFrame(
    RectF32 Bounds,
    ImmutableArray<WireframeText> Texts,
    ImmutableArray<WireframeFill> Fills,
    ImmutableArray<WireframeRegion> Regions);

/// <summary>
/// A CPU renderer that records where a frame puts its text, its opaque fills and its clickable regions, for
/// <see cref="Wireframe.Check"/> to judge: text over text, text over a control, a control over a control,
/// anything off the surface, text a clip cut. A widget is drawn on it at one size after another in a unit test,
/// so a row that only overlaps at half a window's width fails a test instead of waiting to be seen.
/// </summary>
/// <remarks>
/// <para>It records at the DRAW CALL, not from the arranged tree, so content a widget draws by hand into a slot
/// is seen as well as what the layout engine placed. Text is measured with the same rasterizer
/// <see cref="RgbaImageRenderer"/> draws with; a test that wants no font file derives from this and answers
/// <see cref="RgbaImageRenderer.MeasureText"/> itself.</para>
/// <para>Nothing is rasterized unless <c>paint</c> is set, which a failing test turns on to save the picture.</para>
/// </remarks>
public class WireframeRenderer(uint width, uint height, bool paint = false) : RgbaImageRenderer(width, height)
{
    /// <summary>A fill less opaque than this is a wash over what is under it, not a cover.</summary>
    private const byte OpaqueFrom = 0xC0;

    /// <summary>A fill narrower or shorter than this is a rule or a span of a constructed shape, not a surface.</summary>
    private const int LeastFillSide = 4;

    private readonly List<WireframeText> _texts = [];
    private readonly List<WireframeFill> _fills = [];
    private readonly List<WireframeRegion> _regions = [];
    private RectF32? _clip;
    private int _order;

    // Above zero while a primitive the base class builds out of FillRectangle spans is being drawn: the spans are
    // not surfaces of their own.
    private int _constructing;

    /// <summary>Forgets what was recorded. Call before each frame.</summary>
    public void Begin()
    {
        _texts.Clear();
        _fills.Clear();
        _regions.Clear();
        _order = 0;
        if (paint)
        {
            Surface.Clear(new RGBAColor32(0, 0, 0, 0xff));
        }
    }

    /// <summary>What was drawn since <see cref="Begin"/>.</summary>
    public WireframeFrame Frame => new WireframeFrame(
        new RectF32(0f, 0f, Width, Height), [.. _texts], [.. _fills], [.. _regions]);

    protected override void ApplyClip(in RectInt rect)
    {
        _clip = ToRect(rect);
        base.ApplyClip(rect);
    }

    protected override void ClearClip()
    {
        _clip = null;
        base.ClearClip();
    }

    public override void NoteRegion(in ClickableRegion region)
    {
        var rect = new RectF32(region.X, region.Y, region.Width, region.Height);
        _regions.Add(new WireframeRegion(_order++, rect, Clipped(rect), region.Result));
    }

    public override void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize, RGBAColor32 fontColor,
        in RectInt layout, TextAlign horizAlignment = TextAlign.Center, TextAlign vertAlignment = TextAlign.Near)
    {
        if (!text.IsEmpty && fontColor.Alpha > 0)
        {
            Record(text, fontFamily, fontSize, layout, horizAlignment, vertAlignment);
        }
        if (paint)
        {
            base.DrawText(text, fontFamily, fontSize, fontColor, layout, horizAlignment, vertAlignment);
        }
    }

    // The same placement RgbaImageRenderer.DrawText makes: lines 1.3 em apart, the block aligned in the box, each
    // line aligned across it. The ink of a line is taken as one em high in the middle of its line box.
    private void Record(ReadOnlySpan<char> text, string fontFamily, float fontSize, in RectInt layout,
        TextAlign horizAlignment, TextAlign vertAlignment)
    {
        var box = ToRect(layout);
        var lineHeight = fontSize * 1.3f;
        var lines = text.Count('\n') + 1;
        var top = vertAlignment switch
        {
            TextAlign.Center => box.Y + (box.Height - lines * lineHeight) / 2f,
            TextAlign.Far => box.Y + box.Height - lines * lineHeight,
            _ => box.Y,
        };

        var index = 0;
        foreach (var range in text.Split('\n'))
        {
            var line = text[range];
            var lineTop = top + index++ * lineHeight;
            if (line.IsWhiteSpace())
            {
                continue;
            }

            var lineWidth = MeasureText(line, fontFamily, fontSize).Width;
            var x = horizAlignment switch
            {
                TextAlign.Center => box.X + (box.Width - lineWidth) / 2f,
                TextAlign.Far => box.X + box.Width - lineWidth,
                _ => box.X,
            };
            var ink = new RectF32(x, lineTop + (lineHeight - fontSize) / 2f, lineWidth, fontSize);
            _texts.Add(new WireframeText(_order++, line.ToString(), box, ink, Clipped(ink), fontSize));
        }
    }

    public override void FillRectangle(in RectInt rect, RGBAColor32 fillColor)
    {
        RecordFill(rect, fillColor);
        if (paint)
        {
            base.FillRectangle(rect, fillColor);
        }
    }

    public override void FillRectangles(ReadOnlySpan<(RectInt Rect, RGBAColor32 Color)> rectangles)
    {
        foreach (var (rect, color) in rectangles)
        {
            RecordFill(rect, color);
        }
        if (paint)
        {
            base.FillRectangles(rectangles);
        }
    }

    public override void FillRoundedRectangle(in RectInt rect, RGBAColor32 fillColor, float cornerRadius)
    {
        // Recorded whole: the corners it leaves open are smaller than anything a rule asks about.
        RecordFill(rect, fillColor);
        if (!paint)
        {
            return;
        }
        _constructing++;
        try
        {
            base.FillRoundedRectangle(rect, fillColor, cornerRadius);
        }
        finally
        {
            _constructing--;
        }
    }

    public override void DrawRectangle(in RectInt rect, RGBAColor32 strokeColor, int strokeWidth)
    {
        if (paint)
        {
            base.DrawRectangle(rect, strokeColor, strokeWidth);
        }
    }

    public override void DrawLine(float x0, float y0, float x1, float y1, RGBAColor32 color, int thickness = 1)
    {
        if (paint)
        {
            base.DrawLine(x0, y0, x1, y1, color, thickness);
        }
    }

    public override void DrawEllipse(in RectInt rect, RGBAColor32 strokeColor, float strokeWidth = 1f)
    {
        if (paint)
        {
            base.DrawEllipse(rect, strokeColor, strokeWidth);
        }
    }

    public override void FillEllipse(in RectInt rect, RGBAColor32 fillColor)
    {
        if (paint)
        {
            base.FillEllipse(rect, fillColor);
        }
    }

    public override void DrawTriangles(ReadOnlySpan<float> vertices, RGBAColor32 color)
    {
        if (!paint)
        {
            return;
        }
        _constructing++;
        try
        {
            base.DrawTriangles(vertices, color);
        }
        finally
        {
            _constructing--;
        }
    }

    private void RecordFill(in RectInt rect, RGBAColor32 color)
    {
        if (_constructing > 0 || color.Alpha < OpaqueFrom)
        {
            return;
        }
        var visible = Clipped(ToRect(rect));
        if (visible.Width >= LeastFillSide && visible.Height >= LeastFillSide)
        {
            _fills.Add(new WireframeFill(_order++, visible, color));
        }
    }

    private RectF32 Clipped(RectF32 rect) => _clip is { } clip ? Wireframe.Intersect(rect, clip) : rect;

    private static RectF32 ToRect(in RectInt rect)
    {
        var r = rect.Normalized();
        return new RectF32(r.UpperLeft.X, r.UpperLeft.Y, r.Width, r.Height);
    }
}
