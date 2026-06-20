namespace DIR.Lib.Layout;

/// <summary>
/// The paintable + hit-testable payload of a <see cref="Node.Leaf"/>. Surface-neutral: it says
/// <i>what</i> to measure/draw, not <i>how</i> -- a per-surface painter interprets the concrete record.
/// The engine only needs <see cref="Text"/>/<see cref="Box"/>/<see cref="Fill"/> to compute intrinsic
/// (Auto) sizes; <see cref="Node.Hit"/> is the click region the painter auto-binds to the arranged rect.
/// </summary>
public abstract record Content
{
    /// <summary>A text run. Intrinsic size = the measure context's glyph metrics (px) / char count (cells).</summary>
    public sealed record Text(string Value, float FontSize = 14f) : Content
    {
        /// <summary>Glyph colour (default white). Surface-neutral -- Vulkan uses it directly, the TUI maps it to the nearest SGR.</summary>
        public RGBAColor32 Color { get; init; } = new(0xff, 0xff, 0xff, 0xff);

        /// <summary>Horizontal alignment of the text within the leaf's arranged rect.</summary>
        public TextAlign HAlign { get; init; } = TextAlign.Near;

        /// <summary>Vertical alignment of the text within the leaf's arranged rect.</summary>
        public TextAlign VAlign { get; init; } = TextAlign.Center;
    }

    /// <summary>A fixed-size piece (icon, swatch, separator, spacer) -- intrinsic size is <paramref name="Width"/> x <paramref name="Height"/> design units. The painter fills it only when <see cref="Color"/> is non-transparent, so a transparent Box is a pure spacer.</summary>
    public sealed record Box(float Width, float Height) : Content
    {
        /// <summary>Fill colour. Default transparent => the painter draws nothing (spacer).</summary>
        public RGBAColor32 Color { get; init; }
    }

    /// <summary>
    /// An app-drawn escape hatch (chart, sky map, custom widget, text input). Carries only a minimum intrinsic
    /// size in design units; pair with <c>Star</c> sizing to fill available space. The painter draws it via an
    /// app <c>drawFill</c> callback, which receives this instance back -- so when one tree contains several
    /// <see cref="Fill"/> leaves (e.g. a panel with multiple inputs), set <see cref="Key"/> to route each to its
    /// own draw closure (e.g. <c>map[fill.Key]?.Invoke(rect)</c>) without a central switch.
    /// </summary>
    public sealed record Fill(float MinWidth = 0f, float MinHeight = 0f, string? Key = null) : Content;
}
