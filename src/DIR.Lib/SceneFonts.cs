using System.Collections.Concurrent;
using System.Text;
using SharpAstro.Fonts;
using SharpAstro.Fonts.Scenes;
using SharpAstro.VectorGraphics;

namespace DIR.Lib;

/// <summary>
/// The fonts for the text of a vector drawing (<see cref="VectorIcon"/>, <see cref="IconBaker"/>): what turns "Georgia,
/// serif, bold, 14" into a face.
/// </summary>
/// <remarks>
/// <para>A drawing's text names fonts the way a web page does: a list of families, a weight, a slant. It holds no
/// font. <see cref="Installed"/> answers from the fonts on this machine, through <see cref="FontResolver"/>, which is
/// what a drawing the user hands over needs. <see cref="From"/> answers from faces the app names, which is what an
/// app's own drawings need when they are to look the same on every machine.</para>
/// <para><b>Shaping is plain</b>: one glyph a character and pair kerning (<see cref="SceneText.ShaperFor(SceneFontResolver)"/>).
/// That is right for Latin, Greek and Cyrillic labels. A drawing whose text needs ligatures, joining or reordering
/// is given a <see cref="SceneTextShaper"/> of the caller's own in <see cref="SceneRenderOptions.ShapeText"/>.</para>
/// </remarks>
public static class SceneFonts
{
    /// <summary>Faces already loaded, by their <see cref="FontFaceId"/>. A face that could not be loaded is kept as null, so it is not tried again.</summary>
    private static readonly ConcurrentDictionary<string, OpenTypeFont?> Faces = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] Sans = ["Arial", "Helvetica", "Segoe UI", "DejaVu Sans", "Liberation Sans", "Noto Sans"];
    private static readonly string[] Serif = ["Times New Roman", "Times", "Georgia", "DejaVu Serif", "Liberation Serif", "Noto Serif"];
    private static readonly string[] Mono = ["Consolas", "Courier New", "Menlo", "DejaVu Sans Mono", "Liberation Mono"];

    /// <summary>
    /// Text shaped with the fonts installed on this machine: each family a span names is looked for in turn (the generic
    /// ones, <c>serif</c>, <c>sans-serif</c> and <c>monospace</c>, as the usual faces of each kind), then a sans-serif
    /// face, then the platform's faces for other scripts and its emoji face for a character none of those has.
    /// </summary>
    /// <remarks>
    /// The first time a family that is not one of the standard ones is asked for, every installed font's name is read
    /// (<see cref="FontResolver.InstalledFaces"/>): tens of milliseconds warm, seconds on a cold disk. A
    /// <see cref="VectorIcon"/> rasterises when it is first drawn, so load such a drawing's sizes ahead of the first
    /// frame if that would show.
    /// </remarks>
    public static SceneTextShaper Installed { get; } = SceneText.ShaperFor(ResolveInstalled);

    /// <summary>
    /// Text shaped with these faces and no others, whatever families a drawing names: for each character the first
    /// face that has it, the first face of all when none does. Give font files an app ships (a path, or a
    /// <see cref="FontFaceId"/> for a face inside a collection) and its drawings read the same on every machine.
    /// </summary>
    public static SceneTextShaper From(params string[] faces)
    {
        ArgumentNullException.ThrowIfNull(faces);
        string[] ids = [.. faces];
        return SceneText.ShaperFor((_, character) =>
        {
            OpenTypeFont? first = null;
            foreach (string id in ids)
            {
                if (Load(id) is not { } face) continue;
                if (Covers(face, character)) return face;
                first ??= face;
            }
            return first;
        });
    }

    /// <summary>The face on this machine for a font a drawing asks for and a character it writes; see <see cref="Installed"/>. Null when the machine has no font at all.</summary>
    public static OpenTypeFont? ResolveInstalled(VectorFont font, Rune character)
    {
        ArgumentNullException.ThrowIfNull(font);
        FontStyle style = (font.Weight >= 600 ? FontStyle.Bold : FontStyle.Regular) | (font.Slant != FontSlant.Normal ? FontStyle.Italic : FontStyle.Regular);

        OpenTypeFont? named = null;
        foreach (string family in font.Families.IsDefault ? [] : font.Families)
        {
            if (Named(family, style) is not { } face) continue;
            if (Covers(face, character)) return face;
            named ??= face;
        }
        // No family named, none of them installed, or none of them has the character.
        foreach (string family in Sans)
        {
            if (Standard(family, style) is not { } face) continue;
            if (Covers(face, character)) return face;
            named ??= face;
        }
        if (Load(FontResolver.ResolveSystemFont()) is { } system)
        {
            if (Covers(system, character)) return system;
            named ??= system;
        }
        foreach (string path in FontResolver.ResolveSystemScriptFonts())
        {
            if (Load(path) is { } script && Covers(script, character)) return script;
        }
        if (Load(FontResolver.ResolveEmojiFont()) is { } emoji && Covers(emoji, character)) return emoji;
        return named;
    }

    /// <summary>A family by the name a drawing gives it: a generic one as the usual faces of its kind, any other as an installed family of that name.</summary>
    private static OpenTypeFont? Named(string family, FontStyle style)
    {
        string[]? generic = family.ToLowerInvariant() switch
        {
            "serif" or "ui-serif" => Serif,
            "sans-serif" or "system-ui" or "ui-sans-serif" or "cursive" or "fantasy" => Sans,
            "monospace" or "ui-monospace" => Mono,
            _ => null,
        };
        if (generic is not null)
        {
            foreach (string one in generic)
            {
                if (Standard(one, style) is { } face) return face;
            }
            return null;
        }
        return Standard(family, style) ?? Declared(family, style);
    }

    /// <summary>One of the families the resolver knows by file name: a table lookup and a file probe, no scan.</summary>
    private static OpenTypeFont? Standard(string family, FontStyle style) =>
        FontResolver.ResolveInstalledFace(family, style) is { } path ? Load(path) : null;

    /// <summary>A family by the name its own faces declare: the styled face, or the family's regular one.</summary>
    private static OpenTypeFont? Declared(string family, FontStyle style)
    {
        if (FontResolver.FindDeclaredFamily(family) is not { } faces) return null;
        FontResolver.InstalledFace exact = (int)style < faces.Length ? faces[(int)style] : default, regular = faces.Length > 0 ? faces[0] : default;
        return exact.Path is not null ? Load(exact.Id) : regular.Path is not null ? Load(regular.Id) : null;
    }

    private static bool Covers(OpenTypeFont face, Rune character) => face.GetGlyphId((uint)character.Value) != 0;

    private static OpenTypeFont? Load(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return Faces.GetOrAdd(id, static faceId =>
        {
            try
            {
                return FontFaceId.TryParse(faceId, out string path, out int index) && !File.Exists(faceId)
                    ? OpenTypeFont.LoadFromFile(path, index)
                    : OpenTypeFont.LoadFromFile(faceId);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or ArgumentException)
            {
                // A font file that cannot be read is a font that is not there.
                return null;
            }
        });
    }
}
