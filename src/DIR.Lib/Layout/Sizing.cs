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
