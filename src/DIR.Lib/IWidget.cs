namespace DIR.Lib;

/// <summary>
/// Base interface for input-handling widgets, shared across pixel (GPU) and cell (terminal) renderers.
/// Implement in platform-specific widget base classes (<c>PixelWidgetBase&lt;T&gt;</c>, <c>Widget</c>).
/// </summary>
public interface IWidget
{
    /// <summary>
    /// Handles a key press while this widget is active. Returns true if consumed.
    /// </summary>
    bool HandleKeyDown(InputKey key, InputModifier modifiers) => false;

    /// <summary>
    /// Handles a mouse wheel event. Returns true if consumed.
    /// </summary>
    bool HandleMouseWheel(float scrollY, float mouseX, float mouseY) => false;
}
