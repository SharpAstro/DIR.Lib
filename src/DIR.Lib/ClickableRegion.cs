using System;

namespace DIR.Lib;

/// <summary>
/// A clickable region registered during rendering. The hit test walks these
/// in reverse order (last-registered = on top) to find what was clicked.
/// </summary>
public readonly record struct ClickableRegion(float X, float Y, float Width, float Height, HitResult Result, Action<InputModifier>? OnClick = null);

/// <summary>
/// Describes what was hit during a click. Open hierarchy — extend with
/// app-specific subclasses (e.g. SlotHit, SliderHit) in downstream projects.
/// </summary>
public record HitResult
{
    /// <summary>A text input field was clicked — activate it and start text input.</summary>
    public sealed record TextInputHit(TextInputState Input) : HitResult;

    /// <summary>A named action button was clicked.</summary>
    public sealed record ButtonHit(string Action) : HitResult;

    /// <summary>A list item was clicked at the given index.</summary>
    public sealed record ListItemHit(string ListId, int Index) : HitResult;

    /// <summary>A slot was clicked for assignment. Payload is app-specific.</summary>
    public sealed record SlotHit<T>(T Slot) : HitResult;

    /// <summary>A slider was clicked/dragged at the given index.</summary>
    public sealed record SliderHit(int SliderIndex) : HitResult;
}
