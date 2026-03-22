namespace DIR.Lib
{
    /// <summary>Activate a text input field and start platform text input (e.g. SDL StartTextInput).</summary>
    public readonly record struct ActivateTextInputSignal(TextInputState Input);

    /// <summary>Deactivate the currently active text input and stop platform text input.</summary>
    public readonly record struct DeactivateTextInputSignal;

    /// <summary>Request application exit.</summary>
    public readonly record struct RequestExitSignal;

    /// <summary>Request a redraw on the next frame.</summary>
    public readonly record struct RequestRedrawSignal;
}
