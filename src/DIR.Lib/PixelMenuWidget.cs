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

    /// <summary>Host action for the panel's Back control (top-left); null means no control. See <see cref="MenuModel.Back"/>.</summary>
    public Action? Back { get => _model.Back; set => _model.Back = value; }

    /// <summary>Host action for the panel's close control (top-right); null means no control. See <see cref="MenuModel.Close"/>.</summary>
    public Action? Close { get => _model.Close; set => _model.Close = value; }

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

    /// <summary>Look and sizing of <see cref="RenderPanel"/>.</summary>
    public MenuPanelStyle PanelStyle { get; init; } = new();

    /// <summary>
    /// Renders the menu as a modal panel (<see cref="MenuLayout.BuildPanel"/>): a scrim over the whole surface and a
    /// content-sized card centred on it. Same model, rows and regions as <see cref="Render"/>, which is unchanged.
    /// The surface size is passed to the builder in design units, and the card's text is measured through the
    /// widget's own measure context.
    /// </summary>
    public void RenderPanel()
    {
        BeginFrame();
        var ctx = MeasureContext(fontPath);
        var unit = ctx.ToSurface(1f);
        var tree = MenuLayout.BuildPanel(_model, Colors, PanelStyle, ctx, Renderer.Width / unit, Renderer.Height / unit);
        RenderLayout(tree, new RectF32(0, 0, Renderer.Width, Renderer.Height), ctx);
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
    /// <remarks>
    /// A host needs no <c>Unhandled</c>: list the widget on <see cref="InputRouter"/> and Up, Down, Enter and
    /// the digits reach <see cref="MenuModel"/> through the shortcuts <see cref="MenuLayout.BuildTree"/>
    /// declares (also inside a modal Top). The router consumes the key there, so a host that still wires
    /// <c>Unhandled = menu.HandleInput</c> steps once per key; this method stays for a host that calls it
    /// directly with no router. Keys only. A press on a row reaches <see cref="MenuLayout.BuildTree"/>'s own <c>.Clickable</c>
    /// through <see cref="InputRouter"/>, which walks the regions this widget registered while painting --
    /// so a host lists the widget on the router and writes no press arm. Until 10.0 this branch hit-tested
    /// and dispatched for itself, which is a second dispatcher beside the router's for the same rects.
    /// </remarks>
    public override bool HandleInput(InputEvent evt)
    {
        return evt switch
        {
            InputEvent.KeyDown(var key, _) => _model.HandleKey(key),
            _ => false,
        };
    }
}
