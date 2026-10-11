namespace DIR.Lib;

/// <summary>
/// What a file dialog answered: the paths chosen, empty when the user cancelled, or an error when the
/// platform could not show one.
/// </summary>
/// <remarks>
/// A token cancellation is not one of these: it completes the task as cancelled, so a result with no paths
/// always means the user said no. See <see cref="IFileDialogs"/>.
/// </remarks>
public sealed record FileDialogResult
{
    /// <summary>The paths chosen, absolute. Empty when the user cancelled or <see cref="Error"/> is set.</summary>
    public IReadOnlyList<string> Paths { get; init; } = [];

    /// <summary>
    /// Which filter the user had selected, or -1 when the platform does not say, which is often: GTK's
    /// portal, zenity and macOS report none. Never decide a file's format from it; use the extension, which
    /// a save path is given when it lacks one (<see cref="FileDialogFilter.WithDefaultExtension"/>).
    /// </summary>
    public int FilterIndex { get; init; } = -1;

    /// <summary>Why no dialog could be shown, or null. The platform's own message, for a log line.</summary>
    public string? Error { get; init; }

    /// <summary>The user closed the dialog without choosing.</summary>
    public bool Cancelled => Error is null && Paths.Count == 0;

    /// <summary>The first path chosen, or null: the one answer an open-one or a save dialog gives.</summary>
    public string? Path => Paths.Count > 0 ? Paths[0] : null;
}
