namespace DIR.Lib;

/// <summary>
/// What a file dialog is asked to show: its title, the file types it offers, where it starts, and for a
/// save, the name it suggests. Every member is optional, and a platform that cannot honour one (a title,
/// say) ignores it rather than failing.
/// </summary>
/// <remarks>
/// Init-only properties rather than a positional record, so a member added later is not a binary break for
/// a caller already compiled against this one (see CLAUDE.md, "an optional parameter added to a record's
/// primary constructor is a BINARY break").
/// </remarks>
public sealed record FileDialogRequest
{
    /// <summary>The dialog's title, or null for the platform's own ("Open", "Save As").</summary>
    public string? Title { get; init; }

    /// <summary>
    /// The file types offered, in order. The caller puts a combined "All supported" entry first if it wants
    /// one; nothing is synthesised. Empty offers every file.
    /// </summary>
    public IReadOnlyList<FileDialogFilter> Filters { get; init; } = [];

    /// <summary>The folder the dialog opens in, or null for the platform's choice (usually the last one it
    /// showed). Remembering a folder per purpose is the caller's to do.</summary>
    public string? InitialDirectory { get; init; }

    /// <summary>A save dialog's suggested file name, extension included. Ignored by open and folder.</summary>
    public string? SuggestedFileName { get; init; }

    /// <summary>Whether an open dialog lets the user choose several files. Ignored by save. Not every
    /// platform supports it; one that does not lets the user choose one.</summary>
    public bool AllowMany { get; init; }
}
