using DIR.Lib;

namespace DIR.Lib.Tests;

/// <summary>
/// The headless fixture the 9.2 declaration tests share: a renderer that answers <c>MeasureText</c>
/// arithmetically so no font file is loaded, and a widget that paints a tree and exposes the seams a
/// consumer would reach for.
/// </summary>
/// <remarks>
/// One fixture rather than one per file, because each of these tests asks the same two questions of a
/// painted tree -- what regions did it register, and what pixels did it leave -- and a per-file copy of
/// the widget is exactly the duplication these features exist to remove one layer up.
/// </remarks>
internal sealed class DeclarationStubRenderer(uint w, uint h) : RgbaImageRenderer(w, h)
{
    /// <summary>Half the font size per character, so a run's width is arithmetic a test can state.</summary>
    public override (float Width, float Height) MeasureText(ReadOnlySpan<char> text, string fontFamily, float fontSize)
        => (text.Length * fontSize * 0.5f, fontSize);

    /// <summary>What the last <c>DrawText</c> was told to paint with, since a dimmed run is a COLOUR
    /// change and the glyphs themselves are not drawn here.</summary>
    public RGBAColor32? LastTextColor { get; private set; }

    public override void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize,
        RGBAColor32 fontColor, in RectInt layout, TextAlign horizAlign = TextAlign.Near,
        TextAlign vertAlign = TextAlign.Center)
        => LastTextColor = fontColor;
}

internal sealed class DeclarationWidget(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
{
    public void Render(Layout.Node root, RectF32 bounds)
    {
        BeginFrame();
        RenderLayout(root, bounds, fontPath: "stub.ttf", scale: DesignScale.One);
    }

    /// <summary>The measure seam, which is protected on the base because only a widget has a font.</summary>
    public Layout.Size<float> Measure(Layout.Node root, Layout.Size<float> available)
        => MeasureLayout(root, available, fontPath: "stub.ttf", scale: DesignScale.One);

    /// <summary>The one context measure, arrange and paint can share.</summary>
    public PixelMeasureContext<RgbaImage> Context()
        => MeasureContext(fontPath: "stub.ttf", scale: DesignScale.One);

    /// <summary>Hands this widget's window settings to another, as a host does for the panes it owns, so a
    /// test can exercise the two-widget cases per-WINDOW state exists for.</summary>
    public void ShareWith(DeclarationWidget other) => ShareUiContext(other);
}
