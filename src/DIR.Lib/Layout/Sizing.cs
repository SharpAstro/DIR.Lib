using System.Numerics;

namespace DIR.Lib.Layout;

/// <summary>How a node (or a docked strip) is sized along an axis.</summary>
public enum SizeKind
{
    /// <summary>An explicit extent in <i>design units</i> (mapped to surface units by the measure context).</summary>
    Fixed,

    /// <summary>Shrink-to-content: the node's intrinsic measured size.</summary>
    Auto,

    /// <summary>Proportional: split the leftover space after Fixed/Auto siblings by <see cref="Sizing.Value"/> weight.</summary>
    Star,
}

/// <summary>
/// The flex story for one axis: <c>Fixed(n)</c> | <c>Auto</c> | <c>Star(weight)</c>. Surface-neutral --
/// <c>Fixed</c>/min values are <i>design units</i> that <see cref="IMeasureContext{T}.ToSurface"/> maps to
/// pixels (x DPI) or character cells. The default is <see cref="Auto"/>.
/// <para>
/// <paramref name="Min"/>/<paramref name="Max"/> (design units) clamp the extent the engine resolves for an
/// <see cref="SizeKind.Auto"/> or <see cref="SizeKind.Star"/> axis: a min-clamped Star can no longer starve
/// to zero when Fixed siblings eat the container (it holds its floor and overflows <i>visibly</i> instead --
/// the fix for the "fixed side panel wider than a phone screen leaves the content a negative width and
/// nothing paints" bug class), and a max-clamped Star stops growing at its cap with the surplus
/// redistributed to its Star siblings. <c>0</c> means <i>unclamped</i> on both bounds (not "clamp to
/// zero"): the sentinel is deliberately <c>0</c> rather than <c>float.PositiveInfinity</c> so that
/// <c>default(Sizing)</c> -- which skips field initializers -- stays harmless.
/// <see cref="SizeKind.Fixed"/> is explicit and ignores the clamps.
/// </para>
/// </summary>
public readonly record struct Sizing(SizeKind Kind, float Value, float Min = 0f, float Max = 0f)
{
    /// <summary>An explicit extent in design units.</summary>
    public static Sizing Fixed(float designUnits) => new(SizeKind.Fixed, designUnits);

    /// <summary>Shrink-to-content.</summary>
    public static readonly Sizing Auto = new(SizeKind.Auto, 0f);

    /// <summary>Proportional split of leftover space (default weight 1), optionally clamped to
    /// [<paramref name="min"/>, <paramref name="max"/>] design units (0 = unclamped bound).</summary>
    public static Sizing Star(float weight = 1f, float min = 0f, float max = 0f)
        => new(SizeKind.Star, weight, min, max);

    /// <summary>
    /// A Star whose share is capped at its own measured content: it sits at its natural extent while the
    /// row has room for it, and shrinks -- by <paramref name="weight"/> against its content-capped
    /// siblings, down to <paramref name="min"/> design units -- only when the row is short, its text
    /// fitting through the run's own <see cref="TextTrim"/>. See <see cref="UpToContent"/> for how a Stack
    /// resolves it.
    /// </summary>
    public static Sizing StarUpToContent(float weight = 1f, float min = 0f)
        => new(SizeKind.Star, weight, min) { UpToContent = true };

    /// <summary>
    /// Set by <see cref="StarUpToContent"/>: a Star capped at the node's measured content (its
    /// <c>WidthSample</c>, if it has one). Along a Stack's axis the content-capped Stars are served
    /// BEFORE the plain ones: they split the leftover by weight with their content as their max and
    /// <see cref="Min"/> as their floor, and the plain Stars share what they leave (a plain Star's own
    /// <see cref="Min"/> is reserved first). So a plain Star is the one that gives way, and with no plain
    /// Star the rest of the row stays unused, exactly as it does for Stars that hit a <see cref="Max"/>.
    /// A content-capped Star counts its content in its Stack's natural size, and across a Stack it takes
    /// its content rather than stretching. Elsewhere (a Dock strip, a Grid column, a Wrap) it acts as a
    /// plain Star. An init-only property rather than a constructor parameter, so the struct's
    /// constructor and <c>Deconstruct</c> keep their published shape.
    /// </summary>
    public bool UpToContent { get; init; }

    public bool IsFixed => Kind == SizeKind.Fixed;
    public bool IsAuto => Kind == SizeKind.Auto;
    public bool IsStar => Kind == SizeKind.Star;

    /// <summary>True when either clamp bound is set on a kind that honours clamps (Auto/Star).</summary>
    public bool HasClamp => !IsFixed && (Min > 0f || Max > 0f);
}

/// <summary>A width/height pair in surface coordinate units.</summary>
public readonly record struct Size<T>(T Width, T Height) where T : INumber<T>
{
    public static Size<T> Zero => new(T.Zero, T.Zero);
}
