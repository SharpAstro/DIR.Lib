namespace DIR.Lib;

/// <summary>
/// Resolves system / user-installed font paths for use with
/// <see cref="ManagedFontRasterizer"/> / <see cref="RgbaImageRenderer.DrawText"/>.
/// Used by both pixel renderers (GPU/SDL) and TUI Sixel renderers — anywhere
/// the caller needs an absolute TTF / OTF / TTC path on disk.
///
/// Two entry points:
/// <list type="bullet">
/// <item><see cref="ResolveSystemFont"/> — returns a single platform-default
///   monospace path (Consolas → Courier on Windows, Menlo → Monaco on macOS,
///   DejaVu Sans Mono on Linux). Returns "" if none exists.</item>
/// <item><see cref="EnumerateInstalledFonts"/> — lists every installed font
///   file across the system + per-user font directories. Windows 11 introduced
///   <c>%LOCALAPPDATA%\Microsoft\Windows\Fonts</c> for fonts the user can
///   install without admin rights, so a Windows-only scan that only walks
///   <c>C:\Windows\Fonts</c> will silently miss whatever the user side-loaded
///   (JetBrains Mono, Fira Code, etc.). macOS / Linux per-user dirs are
///   included too.</item>
/// </list>
///
/// <see cref="ResolveSystemFont"/> returns "" (not null) when no candidate is
/// found, mirroring the pre-existing API in TianWen.UI.Abstractions and
/// matching the natural use site (string concatenation, length check).
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

    private static readonly string[] FontExtensions =
        [".ttf", ".otf", ".ttc", ".otc"];

    /// <summary>
    /// Returns the first matching default monospace candidate path that
    /// exists on disk for the current OS, or "" if none of the candidates
    /// are present. (Doesn't probe the per-user font dirs — the defaults
    /// always live in the system dir on every OS we target.)
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

    /// <summary>
    /// All font directories the OS searches for installed fonts, in the
    /// conventional system-then-user order. On Windows 11 the per-user dir
    /// <c>%LOCALAPPDATA%\Microsoft\Windows\Fonts</c> is included so fonts
    /// installed without admin rights are discoverable. Missing or
    /// non-existent paths are not filtered out here — that's the caller's
    /// job (<see cref="EnumerateInstalledFonts"/> does it).
    /// </summary>
    public static IEnumerable<string> FontDirectories
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                // SpecialFolder.Fonts returns C:\Windows\Fonts on Windows.
                var sys = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                if (!string.IsNullOrEmpty(sys)) yield return sys;

                // Per-user fonts (Windows 11): %LOCALAPPDATA%\Microsoft\Windows\Fonts.
                // No special-folder enum for it — assemble manually.
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(local))
                    yield return Path.Combine(local, "Microsoft", "Windows", "Fonts");
            }
            else if (OperatingSystem.IsMacOS())
            {
                yield return "/System/Library/Fonts";
                yield return "/Library/Fonts";
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(home))
                    yield return Path.Combine(home, "Library", "Fonts");
            }
            else
            {
                // Linux / *BSD — XDG basedir spec plus the legacy ~/.fonts location.
                yield return "/usr/share/fonts";
                yield return "/usr/local/share/fonts";
                var xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                if (!string.IsNullOrEmpty(xdgData))
                {
                    yield return Path.Combine(xdgData, "fonts");
                }
                else
                {
                    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    if (!string.IsNullOrEmpty(home))
                        yield return Path.Combine(home, ".local", "share", "fonts");
                }
                var legacyHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(legacyHome))
                    yield return Path.Combine(legacyHome, ".fonts");
            }
        }
    }

    /// <summary>
    /// Enumerate every installed font file (.ttf / .otf / .ttc / .otc) across
    /// the system + per-user font directories returned by
    /// <see cref="FontDirectories"/>. Each path is yielded at most once even
    /// if the same file appears under multiple roots (case-insensitive on
    /// Windows / macOS, case-sensitive on Linux). Directories the current
    /// process can't enumerate (permission errors) are silently skipped —
    /// the goal is best-effort discovery, not an audit.
    /// </summary>
    public static IEnumerable<string> EnumerateInstalledFonts()
    {
        var pathComparer = OperatingSystem.IsLinux()
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;
        var seen = new HashSet<string>(pathComparer);

        foreach (var dir in FontDirectories)
        {
            if (!Directory.Exists(dir)) continue;
            IEnumerable<string> files;
            try
            {
                // Recurse — Linux frequently buckets fonts by family
                // (/usr/share/fonts/dejavu/, /liberation/, etc.) and macOS
                // /Library/Fonts/Supplemental/ holds bonus faces.
                files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories);
            }
            catch (UnauthorizedAccessException) { continue; }
            catch (DirectoryNotFoundException) { continue; }

            foreach (var f in files)
            {
                if (!IsFontFile(f)) continue;
                if (seen.Add(f)) yield return f;
            }
        }
    }

    private static bool IsFontFile(string path)
    {
        var ext = Path.GetExtension(path);
        foreach (var supported in FontExtensions)
            if (ext.Equals(supported, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
