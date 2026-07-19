using System.Collections.Immutable;

namespace DIR.Lib;

/// <summary>
/// GPU pixel-coordinate widget for the vertical "wizard" menu. Wraps <see cref="MenuModel"/>
/// (state/input) and <see cref="MenuLayout.BuildTree"/> (rendering) via
/// <see cref="PixelWidgetBase{TSurface}"/>. Replaces VkMenuWidget in SdlVulkan.Renderer (Phase B).
/// </summary>
/// <typeparam name="TSurface">The renderer surface type (e.g., VkImage, RgbaImage).</typeparam>
public class PixelMenuWidget<TSurface>(Renderer<TSurface> renderer, string fontPath)
    : PixelWidgetBase<TSurface>(renderer)
{
    private readonly MenuModel _model = new();

    /// <summary>Color palette. Override individual fields via object initializer or property init.</summary>
    public MenuColors Colors { get; init; } = new();

    /// <summary>Zero-based index of the currently highlighted item.</summary>
    public int SelectedIndex => _model.SelectedIndex;

    /// <summary>True after the user has confirmed a selection.</summary>
    public bool IsConfirmed => _model.IsConfirmed;

    /// <summary>
    /// Resets the menu with new content and clears the confirmed state.
    /// <paramref name="selected"/> is clamped to the valid item range.
    /// </summary>
    public void Reset(string title, string prompt, ImmutableArray<string> items, int selected = 0)
        => _model.Reset(title, prompt, items, selected);


    /// <summary>
    /// Renders the menu across the full surface using the layout tree from
    /// <see cref="MenuLayout.BuildTree"/>. Must be called between the renderer's BeginFrame
    /// and EndFrame. Font size scales with surface height (1/25th, minimum 16px), then is capped so the
    /// widest line still fits the surface width — otherwise a tall, narrow portrait (phones) overflows
    /// and clips the longest item.
    /// </summary>
    public void Render()
    {
        BeginFrame();
        var fontSize = MathF.Max(16f, Renderer.Height / 25f);

        var widest = WidestLineWidth(fontSize);
        var available = Renderer.Width * 0.94f; // small side margin
        if (widest > available)
            fontSize = MathF.Max(16f, fontSize * (available / widest));

        var bounds = new RectF32(0, 0, Renderer.Width, Renderer.Height);
        RenderLayout(MenuLayout.BuildTree(_model, Colors, fontSize), bounds, fontPath);
    }

    /// <summary>Widest rendered line at <paramref name="fontSize"/>: the title (1.6x, per
    /// <see cref="MenuLayout"/>), the prompt, and each item prefixed with the selection marker (the
    /// widest case). Text width scales linearly with font size, so the caller scales by a ratio.</summary>
    private float WidestLineWidth(float fontSize)
    {
        // Mirrors MenuLayout: title is 1.6x and the selected row carries a "▶  " prefix.
        var widest = Renderer.MeasureText(_model.Title.AsSpan(), fontPath, fontSize * 1.6f).Width;
        widest = MathF.Max(widest, Renderer.MeasureText(_model.Prompt.AsSpan(), fontPath, fontSize).Width);
        foreach (var item in _model.Items)
            widest = MathF.Max(widest, Renderer.MeasureText(("▶  " + item).AsSpan(), fontPath, fontSize).Width);
        return widest;
    }

    /// <inheritdoc/>
    public override bool HandleInput(InputEvent evt)
    {
        return evt switch
        {
            InputEvent.KeyDown(var key, _) => _model.HandleKey(key),
            InputEvent.MouseDown(var x, var y, _, _, _) => HitTestAndDispatch(x, y) is not null,
            _ => false,
        };
    }
}
