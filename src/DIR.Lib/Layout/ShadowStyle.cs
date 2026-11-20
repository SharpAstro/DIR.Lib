namespace DIR.Lib.Layout;

/// <summary>
/// A drop shadow a <see cref="Node"/> declares: offset, blur extent and colour, in design units. Painted under the
/// node's (rounded) rect by a pixel surface, through <c>Renderer.DrawShadow</c>; a cell surface draws nothing, and the
/// engine ignores it (a shadow takes no space).
/// <para>
/// A record with init-only properties, not positional parameters, so a field added later is additive.
/// </para>
/// </summary>
public sealed record ShadowStyle
{
    /// <summary>Horizontal offset of the shadow from the node's rect; positive is right.</summary>
    public float OffsetX { get; init; }

    /// <summary>Vertical offset of the shadow from the node's rect; positive is down.</summary>
    public float OffsetY { get; init; }

    /// <summary>How far the shadow fades out beyond the (offset) rect, in design units; 0 is a hard-edged copy.</summary>
    public float Blur { get; init; }

    /// <summary>The shadow colour; its alpha is the strength at the rect's edge. A fully transparent colour paints nothing.</summary>
    public RGBAColor32 Color { get; init; } = new RGBAColor32(0, 0, 0, 140);

    /// <summary>The most a shadow reaches beyond the node's rect on any side, in the same units as its fields:
    /// the blur plus the larger offset. A damage rect grows by this.</summary>
    public float Extent => Blur + MathF.Max(MathF.Abs(OffsetX), MathF.Abs(OffsetY));
}
