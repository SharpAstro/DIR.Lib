namespace DIR.Lib;

/// <summary>What a key or mouse button asks of a window's navigation history.</summary>
public enum NavigationCommand
{
    /// <summary>Not navigation: the host goes on with the event.</summary>
    None,

    /// <summary>Go back: Alt+Left, or the mouse's back button going down.</summary>
    Back,

    /// <summary>Go forward: Alt+Right, or the mouse's forward button going down.</summary>
    Forward,

    /// <summary>
    /// The release of the mouse's back or forward button. The press already acted (a button acts as it goes down), so its
    /// release is taken and does nothing, rather than reaching whatever is under the pointer as a stray click.
    /// </summary>
    Swallow,
}

/// <summary>
/// The one rule that turns input into Back and Forward, so a host's window and its tooltip (<c>Back (Alt+Left)</c>) say the
/// same thing. Alt+Left and the back button go back, Alt+Right and the forward button go forward; Alt means Alt ALONE, so
/// Ctrl+Alt+Left or Shift+Alt+Left belong to whoever binds them.
/// </summary>
/// <remarks>
/// A host calls this ahead of what is painted and of a focused field, runs the history on <see cref="NavigationCommand.Back"/>
/// and <see cref="NavigationCommand.Forward"/>, and returns true (the event is consumed) for everything but
/// <see cref="NavigationCommand.None"/>. <see cref="InputRouter"/> already keeps these buttons off every click region.
/// </remarks>
public static class NavigationInput
{
    public static NavigationCommand Classify(InputEvent evt) => evt switch
    {
        InputEvent.KeyDown { Key: InputKey.Left, Modifiers: InputModifier.Alt } => NavigationCommand.Back,
        InputEvent.KeyDown { Key: InputKey.Right, Modifiers: InputModifier.Alt } => NavigationCommand.Forward,
        InputEvent.MouseDown { Button: MouseButton.Back } => NavigationCommand.Back,
        InputEvent.MouseDown { Button: MouseButton.Forward } => NavigationCommand.Forward,
        InputEvent.MouseUp { Button: MouseButton.Back or MouseButton.Forward } => NavigationCommand.Swallow,
        _ => NavigationCommand.None,
    };
}
