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
/// </summary>
public readonly record struct Sizing(SizeKind Kind, float Value)
{
    /// <summary>An explicit extent in design units.</summary>
    public static Sizing Fixed(float designUnits) => new(SizeKind.Fixed, designUnits);

    /// <summary>Shrink-to-content.</summary>
    public static readonly Sizing Auto = new(SizeKind.Auto, 0f);

    /// <summary>Proportional split of leftover space (default weight 1).</summary>
    public static Sizing Star(float weight = 1f) => new(SizeKind.Star, weight);

    public bool IsFixed => Kind == SizeKind.Fixed;
    public bool IsAuto => Kind == SizeKind.Auto;
    public bool IsStar => Kind == SizeKind.Star;
}

/// <summary>A width/height pair in surface coordinate units.</summary>
public readonly record struct Size<T>(T Width, T Height) where T : INumber<T>
{
    public static Size<T> Zero => new(T.Zero, T.Zero);
}
