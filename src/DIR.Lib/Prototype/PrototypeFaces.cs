using System;
using System.IO;

namespace DIR.Lib.Prototyping;

/// <summary>
/// The font files a prototype draws with: the body face, and the bold, monospace and bold monospace faces that a
/// text node's <c>weight: bold</c> and <c>face: mono</c> reach. A face that is null falls back as the painter does
/// (<see cref="WindowUiSettings.BoldFontPath"/> and <see cref="WindowUiSettings.MonoFontPath"/>): bold draws in
/// <see cref="Regular"/>, mono in <see cref="Regular"/>, bold mono in <see cref="Mono"/>.
/// </summary>
public sealed record PrototypeFaces
{
    /// <summary>The body face; required.</summary>
    public required string Regular { get; init; }

    /// <summary>The bold face of <see cref="Regular"/>; null draws bold text in the regular face.</summary>
    public string? Bold { get; init; }

    /// <summary>The monospace face; null draws mono text in the regular face.</summary>
    public string? Mono { get; init; }

    /// <summary>The bold monospace face; null draws bold mono text in <see cref="Mono"/>.</summary>
    public string? MonoBold { get; init; }

    /// <summary>
    /// The platform's faces: a UI face (Arial, else the platform default of <see cref="FontResolver.ResolveSystemFont"/>)
    /// with its bold, and the platform monospace face. A face the platform lacks is left null and falls back.
    /// </summary>
    /// <exception cref="FileNotFoundException">The platform has no default face at all.</exception>
    public static PrototypeFaces Platform()
    {
        var mono = FontResolver.ResolveSystemFont();
        var regular = FontResolver.ResolveInstalledFace("Arial", FontStyle.Regular);
        if (string.IsNullOrEmpty(regular)) regular = mono;
        if (string.IsNullOrEmpty(regular))
            throw new FileNotFoundException(
                "No font was given and this platform has no default face; pass fontPath. With no font no text is measured or drawn.");
        var bold = FontResolver.ResolveInstalledFace("Arial", FontStyle.Bold);
        return new PrototypeFaces
        {
            Regular = regular,
            Bold = string.IsNullOrEmpty(bold) || bold == regular ? null : bold,
            Mono = string.IsNullOrEmpty(mono) ? null : mono,
        };
    }

    /// <summary>Copies the faces onto a window's settings, where the painter reads them.</summary>
    public void ApplyTo(WindowUiSettings ui)
    {
        ui.BoldFontPath = Bold;
        ui.MonoFontPath = Mono;
        ui.MonoBoldFontPath = MonoBold;
    }
}
