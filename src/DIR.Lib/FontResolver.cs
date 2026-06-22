namespace DIR.Lib;

/// <summary>
/// Font weight / slant, expressed as the four conventional faces of a family.
/// The integer values double as the index into a family's ordered
/// <c>[regular, bold, italic, bold-italic]</c> face list, so
/// <c>(int)FontStyle.BoldItalic == 3</c>.
/// </summary>
[Flags]
public enum FontStyle
{
    Regular = 0,
    Bold = 1,
    Italic = 2,
    BoldItalic = Bold | Italic,
}

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

    // ---- By-name resolution (family + style → installed face file) ----------
    //
    // Producers spell the standard fonts many ways — "Arial", "ArialMT",
    // "Arial,Bold", "Arial-BoldMT", "TimesNewRomanPS-BoldItalicMT" — so we parse
    // the family + weight/slant (TryParseFamilyStyle) and probe the conventional
    // file names below in the installed-font index built from
    // EnumerateInstalledFonts(). Each face list is indexed by
    // (int)FontStyle = (bold?1:0)+(italic?2:0): [regular, bold, italic, bold-italic];
    // the second name in each pair is the Linux metric-compatible Liberation
    // equivalent. Helvetica maps onto Arial. Symbol/ZapfDingbats have a single
    // face (no styled variants).

    private static readonly string[][] ArialFaces =
    [
        ["arial.ttf",   "liberationsans-regular.ttf"],
        ["arialbd.ttf", "liberationsans-bold.ttf"],
        ["ariali.ttf",  "liberationsans-italic.ttf"],
        ["arialbi.ttf", "liberationsans-bolditalic.ttf"],
    ];

    private static readonly string[][] TimesFaces =
    [
        ["times.ttf",   "liberationserif-regular.ttf"],
        ["timesbd.ttf", "liberationserif-bold.ttf"],
        ["timesi.ttf",  "liberationserif-italic.ttf"],
        ["timesbi.ttf", "liberationserif-bolditalic.ttf"],
    ];

    private static readonly string[][] CourierFaces =
    [
        ["cour.ttf",   "liberationmono-regular.ttf"],
        ["courbd.ttf", "liberationmono-bold.ttf"],
        ["couri.ttf",  "liberationmono-italic.ttf"],
        ["courbi.ttf", "liberationmono-bolditalic.ttf"],
    ];

    private static readonly string[][] SymbolFaces = [["symbol.ttf"]];
    private static readonly string[][] DingbatsFaces = [["wingding.ttf"]];

    // Normalised family key → styled face list. Declared after the face arrays so
    // the static initialisers (which run in textual order) see non-null values.
    private static readonly Dictionary<string, string[][]> SystemFontFamilies = new(StringComparer.Ordinal)
    {
        ["arial"] = ArialFaces,
        ["helvetica"] = ArialFaces,
        ["timesnewroman"] = TimesFaces,
        ["times"] = TimesFaces,
        ["couriernew"] = CourierFaces,
        ["courier"] = CourierFaces,
        ["symbol"] = SymbolFaces,
        ["zapfdingbats"] = DingbatsFaces,
    };

    // Machine-global index of installed fonts: file name → absolute path,
    // discovered once via EnumerateInstalledFonts() (system + per-user font
    // directories, cross-platform). First occurrence wins, matching the
    // system-before-user ordering of FontDirectories. Lazy<T> is thread-safe by default.
    private static readonly Lazy<IReadOnlyDictionary<string, string>> InstalledFontsByName = new(() =>
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in EnumerateInstalledFonts())
            map.TryAdd(Path.GetFileName(path), path);
        return map;
    });

    // First of the candidate file names that the OS actually has installed, or null.
    private static string? FindInstalledFile(string[] fileNames)
    {
        foreach (var name in fileNames)
            if (InstalledFontsByName.Value.TryGetValue(name, out var path))
                return path;
        return null;
    }

    /// <summary>
    /// Parses a font name into a normalised family key and a <see cref="FontStyle"/>,
    /// tolerating the conventions producers actually emit: "Arial", "ArialMT",
    /// "Arial,Bold", "Arial-BoldMT", "TimesNewRomanPS-BoldItalicMT", and the
    /// "ABCDEF+" subset prefix embedders prepend. Weight/slant come from the
    /// presence of the bold / italic / oblique words; the family key is what
    /// remains after stripping those, the PostScript "MT"/"PS" tags, and
    /// separators. Returns false if nothing meaningful remains (so the caller can
    /// leave resolution to its own fallbacks).
    /// </summary>
    public static bool TryParseFamilyStyle(string name, out string family, out FontStyle style)
    {
        // Drop the subset prefix producers prepend to embedded fonts ("ABCDEF+Arial" → "Arial").
        var plus = name.IndexOf('+');
        if (plus >= 0 && plus < name.Length - 1)
            name = name[(plus + 1)..];

        var lower = name.ToLowerInvariant();
        var bold = lower.Contains("bold");
        var italic = lower.Contains("italic") || lower.Contains("oblique");
        style = (bold ? FontStyle.Bold : FontStyle.Regular) | (italic ? FontStyle.Italic : FontStyle.Regular);

        // Strip style words, the PostScript "ps"/"mt" tags, and separators to isolate the family.
        ReadOnlySpan<string> noise = ["bold", "italic", "oblique", "regular", "ps", "mt"];
        var key = lower;
        foreach (var token in noise)
            key = key.Replace(token, "");
        // "+" is in the separator set so a degenerate name with no family after the
        // subset tag ("+", "ABCDEF+") collapses to empty rather than a junk family.
        family = key.Replace(",", "").Replace("-", "").Replace("_", "").Replace(" ", "").Replace("+", "");
        return family.Length > 0;
    }

    /// <summary>
    /// Resolves a known font family + <see cref="FontStyle"/> to an installed face
    /// file. The family is matched against the standard aliases (Arial/Helvetica,
    /// Times[ New Roman], Courier[ New], Symbol, ZapfDingbats) and probed in the
    /// installed-font index — the requested styled face first, then the family's
    /// regular weight if that exact face isn't installed (better a same-family
    /// substitute than an unrelated fallback). Returns null if the family is
    /// unknown or none of its faces are installed.
    /// </summary>
    public static string? ResolveInstalledFace(string family, FontStyle style)
    {
        var key = family.ToLowerInvariant()
            .Replace(",", "").Replace("-", "").Replace("_", "").Replace(" ", "");
        if (!SystemFontFamilies.TryGetValue(key, out var faces))
            return null;
        var idx = (int)style < faces.Length ? (int)style : 0; // Symbol/Dingbats have one face only
        return FindInstalledFile(faces[idx]) ?? FindInstalledFile(faces[0]);
    }

    /// <summary>
    /// Resolves a font name (in any of the forms <see cref="TryParseFamilyStyle"/>
    /// accepts) to an installed font file. Tries the standard-family table first,
    /// then a direct "&lt;family&gt;.ttf" probe against the installed-font index so it
    /// also covers fonts beyond the standard families (Tahoma → tahoma.ttf,
    /// ISOCPEUR → isocpeur.ttf). Returns null when nothing installed matches.
    /// </summary>
    public static string? ResolveInstalledFont(string name)
    {
        if (!TryParseFamilyStyle(name, out var family, out var style))
            return null;
        return ResolveInstalledFace(family, style) ?? FindInstalledFile([family + ".ttf"]);
    }
}
