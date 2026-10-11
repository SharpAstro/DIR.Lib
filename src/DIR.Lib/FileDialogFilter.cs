namespace DIR.Lib;

/// <summary>
/// One file type a dialog offers: the name the user sees and the extensions it matches.
/// </summary>
/// <param name="Name">What the dialog shows, e.g. "FITS images".</param>
/// <param name="Extensions">The extensions it matches, without dots, separated by semicolons:
/// <c>"fits;fit;fts;fz"</c>. <c>"*"</c> matches every file. A multi-part name is matched on its LAST
/// extension, so a <c>.fits.fz</c> file is offered by <c>"fz"</c> and a <c>"fits.fz"</c> entry never matches.</param>
public readonly record struct FileDialogFilter(string Name, string Extensions)
{
    /// <summary>The extensions, split, trimmed, and without any leading dot a caller wrote anyway.</summary>
    public IEnumerable<string> ExtensionList =>
        Extensions.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.TrimStart('.'))
            .Where(e => e.Length > 0);

    /// <summary>
    /// A save path with an extension: the one the user typed, or else the first extension of the filter
    /// they chose, or of the first filter when the platform does not say which (Windows'
    /// <c>lpstrDefExt</c>, applied everywhere).
    /// </summary>
    /// <remarks>
    /// Done once, here, for every platform: GTK's portal, zenity and macOS append nothing, and a caller
    /// that decides the format from the extension (as it should, since <see cref="FileDialogResult.FilterIndex"/>
    /// is often unknown) would otherwise get a file it cannot classify. A filter matching every file
    /// (<c>"*"</c>) has no extension to give, and the path is left as typed.
    /// </remarks>
    /// <param name="path">The path the dialog returned.</param>
    /// <param name="filters">The filters it was shown with.</param>
    /// <param name="filterIndex">The filter the user chose, or -1 when unknown.</param>
    public static string WithDefaultExtension(string path, IReadOnlyList<FileDialogFilter> filters, int filterIndex = -1)
    {
        if (path.Length == 0 || Path.HasExtension(path) || filters.Count == 0)
        {
            return path;
        }

        var filter = filterIndex >= 0 && filterIndex < filters.Count ? filters[filterIndex] : filters[0];
        var extension = filter.ExtensionList.FirstOrDefault();
        return extension is null or "*" ? path : $"{path}.{extension}";
    }
}
