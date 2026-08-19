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
/// Entry points, one per role the platform can answer for:
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
/// <item><see cref="ResolveSystemScriptFonts"/> — the per-script fallback chain
///   (CJK, Devanagari, Arabic), for codepoints the primary face lacks.</item>
/// <item><see cref="ResolveEmojiFont"/> — the platform's colour-emoji face.</item>
/// </list>
///
/// <see cref="ResolveSystemFont"/> returns "" (not null) when no candidate is
/// found, mirroring the pre-existing API in TianWen.UI.Abstractions and
/// matching the natural use site (string concatenation, length check).
/// </summary>
public static class FontResolver
{
    private static readonly string[] WindowsMonoCandidates =
        [@"C:\Windows\Fonts\consola.ttf", @"C:\Windows\Fonts\cour.ttf"];

    private static readonly string[] MacOSMonoCandidates =
        ["/System/Library/Fonts/Menlo.ttc", "/System/Library/Fonts/Monaco.dfont"];

    private static readonly string[] LinuxMonoCandidates =
        ["/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf",
         "/usr/share/fonts/TTF/DejaVuSansMono.ttf"];

    // Colour-emoji faces by platform. Windows ships Segoe UI Emoji and macOS Apple Color Emoji; Linux
    // distros disagree about the path, so the common Noto locations are all probed.
    private static readonly string[] WindowsEmojiCandidates =
        [@"C:\Windows\Fonts\seguiemj.ttf"];

    private static readonly string[] MacOSEmojiCandidates =
        ["/System/Library/Fonts/Apple Color Emoji.ttc"];

    private static readonly string[] LinuxEmojiCandidates =
        ["/usr/share/fonts/truetype/noto/NotoColorEmoji.ttf",
         "/usr/share/fonts/noto/NotoColorEmoji.ttf",
         "/usr/share/fonts/truetype/noto/NotoColorEmoji-Regular.ttf"];

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
        var candidates = OperatingSystem.IsWindows() ? WindowsMonoCandidates
                       : OperatingSystem.IsMacOS()   ? MacOSMonoCandidates
                                                     : LinuxMonoCandidates;
        foreach (var path in candidates)
            if (File.Exists(path)) return path;
        return "";
    }

    /// <summary>
    /// The first colour-emoji face this platform ships that exists on disk, or "" when it ships none.
    /// </summary>
    /// <remarks>
    /// <para>A THIRD platform role beside the monospace default and the script chain, and it belongs here
    /// for the same reason those do: "where does this platform keep its emoji font" is a property of the
    /// platform, not of any one app. Held privately by a consumer, it gets copied -- TianWen carried these
    /// tables in its own UI layer and grew a second copy of them in a second renderer.</para>
    /// <para>Every caller needs a non-emoji fallback regardless. An unavailable glyph does not draw a
    /// placeholder, it draws NOTHING, so a control whose only mark is an emoji silently loses it rather
    /// than degrading. Pair this with <see cref="FontFallbackResolver.CanRender(Rune)"/> to ask whether a
    /// specific mark is actually drawable before committing to it.</para>
    /// </remarks>
    /// <param name="extra">
    /// Paths consulted BEFORE the platform faces, highest priority first -- e.g. an app-bundled emoji
    /// font, whose coverage is the only kind a caller can actually depend on. Mirrors
    /// <see cref="ResolveSystemScriptFonts"/>, so a caller states its own asset without this class
    /// knowing about that caller.
    /// </param>
    public static string ResolveEmojiFont(IEnumerable<string>? extra = null)
    {
        if (extra is not null)
        {
            foreach (var path in extra)
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    return path;
                }
            }
        }

        var candidates = OperatingSystem.IsWindows() ? WindowsEmojiCandidates
                       : OperatingSystem.IsMacOS()   ? MacOSEmojiCandidates
                                                     : LinuxEmojiCandidates;
        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }

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
        family = NormalizeFamilyKey(lower);
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
    /// accepts) to an installed face, as a <see cref="FontFaceId"/> — a plain file path for
    /// an ordinary font, <c>path#index</c> for a face inside a collection.
    ///
    /// <para>Three strategies in order of cost: the standard-family table, a direct
    /// "&lt;family&gt;.ttf" probe against the file-name index (Tahoma → tahoma.ttf), and
    /// finally <see cref="InstalledFaces"/> — every installed face keyed by the family name it
    /// declares in its own 'name' table. The last is what finds a face whose file name isn't
    /// its family ("Segoe UI Symbol" lives in seguisym.ttf) or that has no file name of its own
    /// at all (anything past the first face of a .ttc). It builds an index on first use, so the
    /// two cheap probes run first.</para>
    ///
    /// <para>Returns null when nothing installed matches.</para>
    /// </summary>
    public static string? ResolveInstalledFont(string name)
    {
        if (!TryParseFamilyStyle(name, out var family, out var style))
            return null;
        return ResolveInstalledFace(family, style)
            ?? FindInstalledFile([family + ".ttf"])
            ?? ResolveDeclaredFamily(family, style);
    }

    /// <summary>
    /// The installed faces covering the scripts a Latin UI font does not: CJK first, then the Indic /
    /// complex-script UI face, in the order a fallback chain should consult them. Only faces that actually
    /// resolve on this machine are returned, so the list is usually one or two entries.
    /// </summary>
    /// <remarks>
    /// This is knowledge about OPERATING SYSTEMS, not about any one app, which is why it lives beside
    /// <see cref="ResolveSystemFont"/> rather than in a host: every consumer that draws user-supplied text
    /// needs exactly this list, and each one working it out again would get a different, quietly incomplete
    /// answer. Feed it to <see cref="FontFallbackResolver.FromRoles"/> as the script role.
    /// <para>
    /// Deliberately the OS's faces rather than bundled ones. A bundled Noto CJK face is ~17 MB each and a
    /// full set is ~68 MB per published binary; anyone who can TYPE a script already has a face for it
    /// installed. Bundle one only when a render has to be byte-identical across machines (a document
    /// viewer), and pass it via <c>extra</c> so it is preferred over the platform's.
    /// </para>
    /// </remarks>
    /// <param name="extra">Paths consulted BEFORE the platform faces — e.g. bundled script faces.</param>
    public static IReadOnlyList<string> ResolveSystemScriptFonts(IEnumerable<string>? extra = null)
    {
        // The faces this platform ships, preferred because they match the rest of the UI's weight and are
        // the ones the user's own system settings are tuned around.
        string[] platform = OperatingSystem.IsWindows()
            ? ["Microsoft YaHei", "SimSun", "Malgun Gothic", "Yu Gothic", "MS Gothic", "Nirmala UI"]
            : OperatingSystem.IsMacOS()
                ? ["PingFang SC", "Hiragino Sans", "Apple SD Gothic Neo", "Arial Unicode MS"]
                : [];

        // Then the portable open families, APPENDED on every platform rather than being the non-Windows,
        // non-macOS branch. They are the default on Linux but are also widely installed on Windows and
        // macOS, and as an else-branch a Windows box that happened to lack Microsoft YaHei but carried
        // Noto Sans CJK would have resolved nothing at all -- a blank field on a machine that could plainly
        // render the text. Unresolved names cost nothing, so breadth here is free.
        string[] portable =
        [
            "Noto Sans CJK SC", "Noto Sans CJK JP", "Noto Sans CJK KR", "Noto Sans CJK TC",
            "Source Han Sans", "WenQuanYi Zen Hei", "Noto Sans Devanagari", "Noto Sans Arabic",
        ];

        List<string> resolved = [];

        if (extra is not null)
        {
            foreach (var path in extra)
            {
                if (!string.IsNullOrEmpty(path) && !resolved.Contains(path))
                {
                    resolved.Add(path);
                }
            }
        }

        foreach (var name in platform.Concat(portable))
        {
            if (ResolveInstalledFont(name) is { Length: > 0 } path && !resolved.Contains(path))
            {
                resolved.Add(path);
            }
        }

        return resolved;
    }

    // ---- Declared-name resolution (every installed face, by its own 'name' table) -----------

    /// <summary>
    /// One installed face. <see cref="FaceIndex"/> is its position inside a collection (0 for a
    /// single-face file); <see cref="Id"/> is the string to hand to the rasterizer.
    /// </summary>
    /// <param name="Path">Absolute path of the file holding the face.</param>
    /// <param name="FaceIndex">Index within a .ttc/.otc; 0 for a plain font file.</param>
    /// <param name="Family">The family the face declares.</param>
    /// <param name="Subfamily">The style the face declares, verbatim ("Book", "SemiBold Italic").</param>
    /// <param name="Style">The subfamily reduced to the four style-linked faces.</param>
    /// <param name="Weight">OS/2 usWeightClass (400 = Regular, 700 = Bold); 0 if the face declares none.</param>
    public readonly record struct InstalledFace(
        string Path, int FaceIndex, string Family, string? Subfamily, FontStyle Style, ushort Weight)
    {
        /// <summary>The <see cref="FontFaceId"/> naming this face.</summary>
        public string Id => FontFaceId.Create(Path, FaceIndex);
    }

    // Normalized family key -> faces sharing it. Built once, lazily: the scan reads only each
    // file's 'name'/'OS/2' tables (SharpAstro.Fonts.FontFaceReader seeks rather than loading),
    // which costs tens of milliseconds warm across a few hundred installed files — but seconds
    // when the OS file cache is cold, so it must not be triggered from a render thread.
    // Lazy<T> is thread-safe by default.
    private static readonly Lazy<IReadOnlyDictionary<string, InstalledFace[]>> DeclaredFamilies =
        new(BuildDeclaredFamilyIndex);

    /// <summary>
    /// Every installed face, grouped by normalized family key, each group indexed by
    /// <c>(int)</c><see cref="FontStyle"/>. Built on first access (see
    /// <see cref="ResolveInstalledFont"/> for the cost); warm it off the render thread if a
    /// stall would be visible.
    /// </summary>
    public static IReadOnlyDictionary<string, InstalledFace[]> InstalledFaces => DeclaredFamilies.Value;

    /// <summary>
    /// All faces of one family, indexed by <c>(int)</c><see cref="FontStyle"/>; null if the
    /// family isn't installed. <paramref name="family"/> is matched leniently — the same
    /// normalization <see cref="TryParseFamilyStyle"/> applies is applied to both sides, so
    /// spacing, case and separators don't matter.
    /// </summary>
    public static InstalledFace[]? FindDeclaredFamily(string family)
        => DeclaredFamilies.Value.TryGetValue(NormalizeFamilyKey(family), out var faces) ? faces : null;

    private static string? ResolveDeclaredFamily(string family, FontStyle style)
    {
        var faces = FindDeclaredFamily(family);
        if (faces is null) return null;
        // Prefer the exact styled face, else the family's regular — a same-family substitute
        // beats an unrelated fallback, matching ResolveInstalledFace.
        var exact = faces[(int)style];
        if (exact.Path is not null) return exact.Id;
        var regular = faces[(int)FontStyle.Regular];
        return regular.Path is not null ? regular.Id : null;
    }

    private static Dictionary<string, InstalledFace[]> BuildDeclaredFamilyIndex()
    {
        // Materialize first so the fold below is deterministic: the parallel scan writes into
        // slots, and first-wins is then resolved in FontDirectories order (system before user)
        // rather than in whatever order threads happened to finish.
        var files = EnumerateInstalledFonts().ToArray();
        var perFile = new SharpAstro.Fonts.FontFaceInfo[files.Length][];
        Parallel.For(0, files.Length, i => perFile[i] = SharpAstro.Fonts.FontFaceReader.ReadFaces(files[i]));

        var index = new Dictionary<string, InstalledFace[]>(StringComparer.Ordinal);
        foreach (var faces in perFile)
        {
            foreach (var face in faces)
            {
                // A face with no declared family (a PDF subset carrying only a PostScript name)
                // can't be looked up by family; its PostScript name is indexed instead.
                Add(index, face.Family, face);
                Add(index, face.LegacyFamily, face);
                Add(index, face.PostScriptName, face);
            }
        }
        return index;
    }

    private static void Add(Dictionary<string, InstalledFace[]> index, string? family, SharpAstro.Fonts.FontFaceInfo face)
    {
        if (string.IsNullOrEmpty(family)) return;
        var key = NormalizeFamilyKey(family);
        if (key.Length == 0) return;

        if (!index.TryGetValue(key, out var slots))
            index[key] = slots = new InstalledFace[4];

        var style = (face.IsBold ? FontStyle.Bold : FontStyle.Regular)
                  | (face.IsItalic ? FontStyle.Italic : FontStyle.Regular);
        // First wins: FontDirectories yields system fonts before per-user ones, and a face's
        // typographic family is offered before its legacy one.
        if (slots[(int)style].Path is not null) return;
        slots[(int)style] = new InstalledFace(
            face.Path, face.FaceIndex, family, face.Subfamily, style, face.WeightClass);
    }

    /// <summary>
    /// Reduce a family name to the key both sides of a lookup are compared on: lowercase, with
    /// style words, the PostScript "PS"/"MT" tags and all separators removed. Applied to the
    /// caller's string and to the face's declared family alike, so whatever it mangles, it
    /// mangles identically on both sides.
    /// </summary>
    private static string NormalizeFamilyKey(string name)
    {
        ReadOnlySpan<string> noise = ["bold", "italic", "oblique", "regular", "ps", "mt"];
        var key = name.ToLowerInvariant();
        foreach (var token in noise)
            key = key.Replace(token, "");
        // "+" is in the separator set so a degenerate name with no family after the
        // subset tag ("+", "ABCDEF+") collapses to empty rather than a junk family.
        return key.Replace(",", "").Replace("-", "").Replace("_", "").Replace(" ", "").Replace("+", "");
    }
}
