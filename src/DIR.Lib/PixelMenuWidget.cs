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
    /// <remarks>
    /// Keys only. A press on a row reaches <see cref="MenuLayout.BuildTree"/>'s own <c>.Clickable</c>
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

    /// <summary>
    /// Everything a menu takes, for a host that has no <see cref="InputRouter"/> of its own: a press goes through a
    /// router built for that press over this one widget (so it reaches the row's click, <see cref="MenuModel.ConfirmAt"/>,
    /// exactly as a host router would deliver it), and everything else goes to <see cref="HandleInput"/>. True when
    /// consumed; then <see cref="IsConfirmed"/> says whether a row was confirmed.
    /// </summary>
    /// <remarks>
    /// A host that already routes its surface should list this widget on its router instead (see the remarks on
    /// <see cref="HandleInput"/>). This is for hosts that show a menu on its own: chess passed every press to
    /// <see cref="HandleInput"/> after 10.0 made it keys-only, so none of its five menus took a click or a tap until it
    /// grew a helper of its own, which this replaces. A router per press keeps no hover or capture state (a row only
    /// answers a press) and cannot hold a stale <c>Ui</c>; it walks the regions of the frame this widget last painted,
    /// the one the user pressed on.
    /// </remarks>
    public bool HandleStandalone(InputEvent evt) => evt switch
    {
        InputEvent.MouseDown => new InputRouter(Ui, new BackgroundTaskTracker(), static () => { })
        {
            Widgets = () => [this],
        }.Handle(evt),
        _ => HandleInput(evt),
    };
}
