using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;

namespace DIR.Lib.Prototyping;

/// <summary>One <see cref="Wireframe"/> finding, tagged with where in a prototype it was found.</summary>
/// <param name="Screen">The id of the screen.</param>
/// <param name="State">The state variant drawn: <c>default</c>, or the <c>name=value</c> a <c>when</c> names.</param>
/// <param name="Width">The surface width the screen was drawn at.</param>
/// <param name="Height">The surface height the screen was drawn at.</param>
/// <param name="Finding">What <see cref="Wireframe.Check"/> reported.</param>
public readonly record struct PrototypeFinding(string Screen, string State, uint Width, uint Height, WireframeFinding Finding)
{
    /// <summary>
    /// The name of the document target the screen was drawn at; empty from the sized <see cref="Prototype.Check"/>
    /// overload, which draws at a size rather than a target. Init-only so the constructor and <c>Deconstruct</c> keep their shape.
    /// </summary>
    public string Target { get; init; } = "";

    /// <inheritdoc />
    public override string ToString() => string.IsNullOrEmpty(Target)
        ? $"{Screen} [{State}] {Width}x{Height}: {Finding.Message}"
        : $"{Screen} [{State}] @{Target} {Width}x{Height}: {Finding.Message}";
}

public static partial class Prototype
{
    /// <summary>The look used when a caller passes no theme: a neutral dark one.</summary>
    public static UiTheme DefaultTheme { get; } = new()
    {
        Palette = new UiPalette
        {
            ContentBg = new(18, 18, 20, 255),
            PanelBg = new(28, 29, 32, 255),
            HeaderBg = new(40, 42, 46, 255),
            Separator = new(64, 66, 72, 255),
            BodyText = new(220, 222, 226, 255),
            DimText = new(140, 144, 152, 255),
            Accent = new(90, 160, 255, 255),
            Selection = new(50, 80, 130, 255),
            HeaderText = new(235, 237, 240, 255),
            SelectionText = new(255, 255, 255, 255),
            Info = new(90, 160, 255, 255),
            Warn = new(240, 190, 60, 255),
            Error = new(230, 80, 80, 255),
        },
        Metrics = new UiMetrics(14f, 6f, 28f, 24f, 28f),
    };

    /// <summary>
    /// Draws every screen, in its default state and once for each <c>name=value</c> a <c>when</c> on it names
    /// (each variant starts from the default state, so one variant shows one condition), at every size into a
    /// <see cref="WireframeRenderer"/>, and returns what <see cref="Wireframe.Check"/> finds, by screen, state and size.
    /// </summary>
    /// <param name="document">The loaded document.</param>
    /// <param name="sizes">Surface sizes in pixels; a portrait one belongs here as well as a landscape one.</param>
    /// <param name="theme">The theme; <see cref="DefaultTheme"/> when null.</param>
    /// <param name="options">Which rules run; all of them when null.</param>
    /// <param name="fontPath">
    /// The font file text is measured and drawn with. Null or empty resolves the platform's default face through
    /// <see cref="FontResolver.ResolveSystemFont"/>; see <see cref="ResolveFont"/>.
    /// </param>
    /// <exception cref="FileNotFoundException">No font was given and the platform has none, or the given file is missing.</exception>
    public static ImmutableArray<PrototypeFinding> Check(PrototypeDocument document,
        IEnumerable<(uint Width, uint Height)> sizes, UiTheme? theme = null, WireframeOptions? options = null,
        string? fontPath = null)
    {
        fontPath = ResolveFont(fontPath);
        var result = ImmutableArray.CreateBuilder<PrototypeFinding>();
        foreach (var (w, h) in sizes)
        {
            foreach (var screen in document.Screens)
            {
                foreach (var (label, name, value) in Variants(screen))
                {
                    var renderer = new WireframeRenderer(w, h);
                    var player = new PrototypePlayer<RgbaImage>(renderer, document, theme ?? DefaultTheme, fontPath);
                    player.Goto(screen.Id);
                    if (name is not null) player.State.Set(name, value);
                    renderer.Begin();
                    player.Render(new RectF32(0, 0, w, h));
                    foreach (var f in Wireframe.Check(renderer.Frame, options))
                        result.Add(new PrototypeFinding(screen.Id, label, w, h, f));
                }
            }
        }
        return result.ToImmutable();
    }

    /// <summary>
    /// As the sized overload, but at each of the document's <see cref="PrototypeDocument.Targets"/>: the surface is
    /// the target's width and height times its scale, drawn by a player at that target (so the read-only <c>target</c>
    /// state follows its width), and every finding names the target in <see cref="PrototypeFinding.Target"/>.
    /// </summary>
    public static ImmutableArray<PrototypeFinding> Check(PrototypeDocument document, UiTheme? theme = null,
        WireframeOptions? options = null, string? fontPath = null)
    {
        fontPath = ResolveFont(fontPath);
        var result = ImmutableArray.CreateBuilder<PrototypeFinding>();
        foreach (var target in document.Targets)
        {
            var (w, h) = SurfaceOf(target);
            foreach (var screen in document.Screens)
            {
                foreach (var (label, name, value) in Variants(screen))
                {
                    var renderer = new WireframeRenderer(w, h);
                    var player = new PrototypePlayer<RgbaImage>(renderer, document, theme ?? DefaultTheme, fontPath)
                    {
                        Target = target,
                    };
                    player.Goto(screen.Id);
                    if (name is not null) player.State.Set(name, value);
                    renderer.Begin();
                    player.Render(new RectF32(0, 0, w, h));
                    foreach (var f in Wireframe.Check(renderer.Frame, options))
                        result.Add(new PrototypeFinding(screen.Id, label, w, h, f) { Target = target.Name });
                }
            }
        }
        return result.ToImmutable();
    }

    /// <summary>
    /// Draws every screen in its entry state at each of the document's targets, at the target's width and height times
    /// its scale, and returns one image per target and screen, targets in document order, screens in screen order.
    /// </summary>
    public static ImmutableArray<(string Target, string Screen, RgbaImage Image)> Render(PrototypeDocument document,
        UiTheme? theme = null, string? fontPath = null)
    {
        fontPath = ResolveFont(fontPath);
        var images = ImmutableArray.CreateBuilder<(string, string, RgbaImage)>();
        foreach (var target in document.Targets)
        {
            var (w, h) = SurfaceOf(target);
            foreach (var screen in document.Screens)
            {
                var renderer = new RgbaImageRenderer(w, h);
                renderer.Surface.Clear(new RGBAColor32(0, 0, 0, 255));
                var player = new PrototypePlayer<RgbaImage>(renderer, document, theme ?? DefaultTheme, fontPath)
                {
                    Target = target,
                };
                player.Goto(screen.Id);
                player.Render(new RectF32(0, 0, w, h));
                images.Add((target.Name, screen.Id, renderer.Surface));
            }
        }
        return images.ToImmutable();
    }

    private static (uint Width, uint Height) SurfaceOf(PrototypeTarget t)
        => ((uint)MathF.Round(t.Width * t.Scale), (uint)MathF.Round(t.Height * t.Scale));

    /// <summary>
    /// Draws every screen in its entry (default) state at <paramref name="size"/> and returns one image per screen,
    /// in the document's screen order.
    /// </summary>
    /// <param name="fontPath">As for <see cref="Check"/>: null or empty resolves the platform's default face.</param>
    /// <exception cref="FileNotFoundException">No font was given and the platform has none, or the given file is missing.</exception>
    public static ImmutableArray<(string Screen, RgbaImage Image)> Render(PrototypeDocument document,
        (uint Width, uint Height) size, UiTheme? theme = null, string? fontPath = null)
    {
        fontPath = ResolveFont(fontPath);
        var images = ImmutableArray.CreateBuilder<(string, RgbaImage)>();
        foreach (var screen in document.Screens)
        {
            var renderer = new RgbaImageRenderer(size.Width, size.Height);
            renderer.Surface.Clear(new RGBAColor32(0, 0, 0, 255));
            var player = new PrototypePlayer<RgbaImage>(renderer, document, theme ?? DefaultTheme, fontPath);
            player.Goto(screen.Id);
            player.Render(new RectF32(0, 0, size.Width, size.Height));
            images.Add((screen.Id, renderer.Surface));
        }
        return images.ToImmutable();
    }

    /// <summary>
    /// The font <see cref="Check"/> and <see cref="Render"/> draw with: <paramref name="fontPath"/> when given, else
    /// the platform default from <see cref="FontResolver.ResolveSystemFont"/>.
    /// </summary>
    /// <remarks>
    /// It fails rather than returning an empty path, because with no font every text measures as zero and draws
    /// nothing, so a check would report a screen of overlapping text as clean and a render would have no text.
    /// </remarks>
    /// <exception cref="FileNotFoundException">No font was given and the platform has none, or the given file is missing.</exception>
    public static string ResolveFont(string? fontPath)
    {
        if (string.IsNullOrEmpty(fontPath))
        {
            var system = FontResolver.ResolveSystemFont();
            if (system.Length == 0)
                throw new FileNotFoundException(
                    "No font was given and this platform has no default face; pass fontPath. With no font no text is measured or drawn.");
            return system;
        }
        if (!File.Exists(fontPath))
            throw new FileNotFoundException($"The prototype font '{fontPath}' does not exist.", fontPath);
        return fontPath;
    }

    // The default, then one variant per distinct name=value a when names that the default state does not already show.
    private static List<(string Label, string? Name, string Value)> Variants(PrototypeScreen screen)
    {
        var list = new List<(string, string?, string)> { ("default", null, "") };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var empty = new PrototypeState();
        void Walk(PrototypeNode n)
        {
            if (n.When is { } when && !empty.WhenHolds(when) && seen.Add(when))
            {
                int eq = when.IndexOf('=');
                // 'target' comes from the surface size, never from a variant.
                if (eq > 0 && when[..eq] != PrototypeTargets.StateName) list.Add((when, when[..eq], when[(eq + 1)..]));
            }
            foreach (var c in n.Children) Walk(c);
        }
        Walk(screen.Root);
        return list;
    }
}
