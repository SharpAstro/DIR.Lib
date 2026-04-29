namespace DIR.Lib;

/// <summary>
/// Resolves a system monospace font path for use with
/// <see cref="ManagedFontRasterizer"/> / <see cref="RgbaImageRenderer.DrawText"/>.
/// Used by both pixel renderers (GPU/SDL) and TUI Sixel renderers — anywhere
/// the caller needs an absolute TTF/OTF path on disk and is happy with the
/// platform's default monospaced face.
///
/// Returns "" (not null) when no candidate is found, mirroring the
/// pre-existing API in TianWen.UI.Abstractions and matching the natural use
/// site (string concatenation, length check). Empty-string is a safe sentinel:
/// <see cref="ManagedFontRasterizer.RasterizeGlyph"/> will fail loudly on it,
/// so the caller always notices.
/// </summary>
public static class FontResolver
{
    private static readonly string[] WindowsCandidates =
        [@"C:\Windows\Fonts\consola.ttf", @"C:\Windows\Fonts\cour.ttf"];

    private static readonly string[] MacOSCandidates =
        ["/System/Library/Fonts/Menlo.ttc", "/System/Library/Fonts/Monaco.dfont"];

    private static readonly string[] LinuxCandidates =
        ["/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf",
         "/usr/share/fonts/TTF/DejaVuSansMono.ttf"];

    /// <summary>
    /// Returns the first matching candidate path that exists on disk for the
    /// current OS, or "" if none of the candidates are present.
    /// </summary>
    public static string ResolveSystemFont()
    {
        var candidates = OperatingSystem.IsWindows() ? WindowsCandidates
                       : OperatingSystem.IsMacOS()   ? MacOSCandidates
                                                     : LinuxCandidates;
        foreach (var path in candidates)
            if (File.Exists(path)) return path;
        return "";
    }
}
