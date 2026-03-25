namespace DIR.Lib;

/// <summary>
/// Base interface for input-handling widgets, shared across pixel (GPU) and cell (terminal) renderers.
/// Implement in platform-specific widget base classes (<c>PixelWidgetBase&lt;T&gt;</c>, <c>Widget</c>).
/// </summary>
public interface IWidget
{
    /// <summary>
    /// Handles an input event. Returns true if consumed.
    /// Pattern match on <see cref="InputEvent"/> subtypes to handle specific events.
    /// </summary>
    bool HandleInput(InputEvent evt) => false;
}
