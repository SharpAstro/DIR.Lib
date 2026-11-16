namespace DIR.Lib.Prototyping;

/// <summary>
/// The names and rules around a prototype's targets: the read-only state <see cref="StateName"/> the player sets
/// from the design width it draws at, and the two values a <c>when</c> can name.
/// </summary>
public static class PrototypeTargets
{
    /// <summary>The reserved, read-only state name (<c>target</c>). A document may not <c>set:</c>, <c>toggle:</c> or <c>bind</c> it.</summary>
    public const string StateName = "target";

    /// <summary>The value of <see cref="StateName"/> under <see cref="CompactBelow"/> design units wide.</summary>
    public const string Compact = "compact";

    /// <summary>The value of <see cref="StateName"/> at <see cref="CompactBelow"/> design units wide or more.</summary>
    public const string Regular = "regular";

    /// <summary>The design width, in design units, below which the target is <see cref="Compact"/>.</summary>
    public const float CompactBelow = 600f;

    /// <summary>The name of the one target a document with no <c>targets</c> gets (its own width and height at scale 1).</summary>
    public const string DefaultName = "design";

    /// <summary>The value of <see cref="StateName"/> for a design width.</summary>
    public static string ForWidth(float designWidth) => designWidth < CompactBelow ? Compact : Regular;

    /// <summary>True when <paramref name="value"/> is one a <c>when</c> on <see cref="StateName"/> may name.</summary>
    public static bool IsValue(string value) => value is Compact or Regular;
}
