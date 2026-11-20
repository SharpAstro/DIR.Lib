using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;

namespace DIR.Lib;

/// <summary>
/// The SVG-backed <see cref="Layout.IconKind"/> members: one outline each, embedded in this assembly, read once
/// and kept as a <see cref="VectorIcon"/>.
/// </summary>
/// <remarks>
/// All twelve are drawn on one 16-unit grid with one stroke weight (1.5) and round caps, in black; the colour a
/// person sees is the ink the caller gives, applied to the coverage mask, never the outline's own.
/// </remarks>
public static class IconSet
{
    private static readonly ConcurrentDictionary<Layout.IconKind, VectorIcon> Loaded = new();

    /// <summary>Whether <paramref name="kind"/> is drawn from an embedded SVG rather than from rectangles.</summary>
    public static bool IsVectorBacked(Layout.IconKind kind) => ResourceName(kind) is not null;

    /// <summary>
    /// The drawing of an SVG-backed kind, read on first use and kept (thread-safe); null for a kind drawn from
    /// rectangles. Its <see cref="VectorIcon.Issues"/> is empty for every shipped outline.
    /// </summary>
    public static VectorIcon? Get(Layout.IconKind kind)
    {
        if (Loaded.TryGetValue(kind, out var kept))
        {
            return kept;
        }

        var name = ResourceName(kind);
        if (name is null)
        {
            return null;
        }

        using var stream = typeof(IconSet).Assembly.GetManifestResourceStream("DIR.Lib.Icons." + name + ".svg")
            ?? throw new InvalidOperationException("Embedded icon '" + name + "' is missing from DIR.Lib.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Loaded.GetOrAdd(kind, VectorIcon.FromSvg(buffer.ToArray()));
    }

    /// <summary>
    /// The filled form a toggle shows when pressed: Pin becomes PinFilled. A kind with no filled drawing keeps its
    /// outline (and a filled kind maps to itself), so the one mapping serves every toggle.
    /// </summary>
    public static Layout.IconKind FilledForm(Layout.IconKind kind) => kind switch
    {
        Layout.IconKind.Pin => Layout.IconKind.PinFilled,
        _ => kind,
    };

    private static string? ResourceName(Layout.IconKind kind) => kind switch
    {
        Layout.IconKind.Info => "info",
        Layout.IconKind.Warning => "warning",
        Layout.IconKind.Error => "error",
        Layout.IconKind.Success => "success",
        Layout.IconKind.Close => "close",
        Layout.IconKind.Pin => "pin",
        Layout.IconKind.PinFilled => "pin-filled",
        Layout.IconKind.OpenInPage => "open-in-page",
        Layout.IconKind.CompareSideBySide => "compare-side-by-side",
        Layout.IconKind.CompareOverlay => "compare-overlay",
        Layout.IconKind.CompareSlider => "compare-slider",
        Layout.IconKind.CompareChanges => "compare-changes",
        _ => null,
    };
}

/// <summary>
/// The one-character cell glyph of the icon kinds added with the SVG icon set (Info, Warning, Error, Success, Close,
/// Pin, PinFilled, OpenInPage and the Compare kinds): what a cell host, such as Console.Lib's <c>CellLayout.IconGlyph</c>,
/// should draw for them. The older kinds are listed too, but Console.Lib's own mapping for those (Grid, Minus,
/// ThemeLight, ThemeDark) differs and stays authoritative.
/// </summary>
public static class IconGlyphs
{
    /// <summary>The single character standing for <paramref name="kind"/> on a cell surface.</summary>
    public static char For(Layout.IconKind kind) => kind switch
    {
        Layout.IconKind.Grid => '#',
        Layout.IconKind.CaretUp => '▲',
        Layout.IconKind.CaretDown => '▼',
        Layout.IconKind.CaretLeft => '◀',
        Layout.IconKind.CaretRight => '▶',
        Layout.IconKind.Plus => '+',
        Layout.IconKind.Minus => '-',
        Layout.IconKind.List => '≡',
        Layout.IconKind.Auto => 'A',
        Layout.IconKind.ThemeSystem => '◐',
        Layout.IconKind.ThemeLight => '☼',
        Layout.IconKind.ThemeDark => '☾',
        Layout.IconKind.Search => '?',
        Layout.IconKind.Pan => '✥',
        Layout.IconKind.IBeam => 'I',
        Layout.IconKind.Check => '✓',
        Layout.IconKind.Bell => '␇',
        Layout.IconKind.Info => 'i',
        Layout.IconKind.Warning => '!',
        Layout.IconKind.Error => '✗',
        Layout.IconKind.Success => '✓',
        Layout.IconKind.Close => '×',
        Layout.IconKind.Pin => '↑',
        Layout.IconKind.PinFilled => '⬆',
        Layout.IconKind.OpenInPage => '↗',
        Layout.IconKind.CompareSideBySide => '‖',
        Layout.IconKind.CompareOverlay => '❐',
        Layout.IconKind.CompareSlider => '↔',
        Layout.IconKind.CompareChanges => '⇄',
        _ => '?',
    };
}
