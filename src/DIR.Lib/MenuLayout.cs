using System.Collections.Immutable;

namespace DIR.Lib;

/// <summary>
/// Builds a surface-neutral <see cref="Layout.Node"/> tree for a vertical "wizard" menu.
/// Pure static: no renderer, no mutable state. The same tree is consumed by the GPU
/// (<see cref="PixelMenuWidget{TSurface}"/>) and by the TUI cell layout (Console.Lib, Phase B).
/// </summary>
public static class MenuLayout
{
    /// <summary>
    /// Prefix rendered before the selected item row. Unicode right-pointing triangle + two spaces.
    /// </summary>
    internal const string SelectedPrefix = "▶  ";

    /// <summary>Three spaces: unselected item indent matching the selected prefix width.</summary>
    internal const string UnselectedPrefix = "   ";

    // D1..D9 select-and-confirm row i, as MenuModel.HandleKey does; rows past the ninth have no digit.
    private static Layout.Node WithDigit(Layout.Node row, int i)
        => i < 9 ? row.WithShortcut(new KeyChord((InputKey)((int)InputKey.D1 + i))) : row;

    /// <summary>
    /// Builds a vertically-centered <see cref="Layout.Node"/> tree from the given
    /// <paramref name="model"/> and <paramref name="colors"/>.
    /// <para>
    /// Tree structure: outer vertical Stack with two Star spacers (before/after) that center
    /// the content block: title leaf, prompt leaf, a small gap box, and one leaf per item.
    /// Each item leaf carries a <see cref="HitResult.ListItemHit"/> ("MenuItem", i) and
    /// an OnClick that calls <see cref="MenuModel.ConfirmAt"/>. The keys are declared too, as
    /// <see cref="Layout.Node.Shortcut"/>s that call <see cref="MenuModel.HandleKey"/> (Up and Down on the
    /// spacers, Enter on the gap, D1..D9 on the rows), so <see cref="InputRouter"/> reaches the model with no
    /// host fallback and <see cref="MenuModel"/> stays the one owner of the selection.
    /// </para>
    /// </summary>
    /// <param name="model">The menu model supplying title, prompt, items, and selected index.</param>
    /// <param name="colors">The color palette for all menu elements.</param>
    /// <param name="fontSize">Base font size in design units. Title uses 1.6x this value.</param>
    public static Layout.Node BuildTree(MenuModel model, MenuColors colors, float fontSize)
        => BuildColumn(model, colors, fontSize, Layout.Sizing.Star());

    /// <summary>
    /// The rows both <see cref="BuildTree"/> and <see cref="BuildPanel"/> carry: title, prompt, gap and one leaf per
    /// item between two edge leaves of height <paramref name="edge"/> (Star to centre a block in a surface, Fixed for
    /// a card's padding). The edge leaves carry Up and Down and the gap carries Enter, so the keys are declared once.
    /// </summary>
    private static Layout.Node BuildColumn(MenuModel model, MenuColors colors, float fontSize, Layout.Sizing edge)
    {
        var titleSize = fontSize * 1.6f;
        var itemLineH = fontSize * 2f;
        var gapH = fontSize * 0.5f;

        var children = ImmutableArray.CreateBuilder<Layout.Node>(model.Items.Length + 5);

        // Top Star spacer - fills available space above the content block.
        // Carries Up: the menu's keys are declared in the tree, so InputRouter reaches them with no host fallback.
        children.Add(new Layout.Node.Leaf(new Layout.Content.Box(0, 0))
        {
            Height = edge,
            Width = Layout.Sizing.Star(),
        }.WithShortcut(InputKey.Up).Activatable(_ => model.HandleKey(InputKey.Up)));

        // Title leaf - centered horizontally, larger font.
        children.Add(new Layout.Node.Leaf(new Layout.Content.Text(model.Title, titleSize)
        {
            Color = colors.TitleColor,
            HAlign = TextAlign.Center,
            VAlign = TextAlign.Center,
        })
        {
            Height = Layout.Sizing.Fixed(titleSize * 2f),
            Width = Layout.Sizing.Star(),
        });

        // Prompt leaf - centered, base font size.
        children.Add(new Layout.Node.Leaf(new Layout.Content.Text(model.Prompt, fontSize)
        {
            Color = colors.PromptColor,
            HAlign = TextAlign.Center,
            VAlign = TextAlign.Center,
        })
        {
            Height = Layout.Sizing.Fixed(itemLineH),
            Width = Layout.Sizing.Star(),
        });

        // Small gap between prompt and items.
        // Carries Enter.
        children.Add(new Layout.Node.Leaf(new Layout.Content.Box(0, gapH))
        {
            Height = Layout.Sizing.Fixed(gapH),
            Width = Layout.Sizing.Star(),
        }.WithShortcut(InputKey.Enter).Activatable(_ => model.HandleKey(InputKey.Enter)));

        // One leaf per item.
        for (var i = 0; i < model.Items.Length; i++)
        {
            var isSelected = i == model.SelectedIndex;
            var prefix = isSelected ? SelectedPrefix : UnselectedPrefix;
            var label = prefix + model.Items[i];
            var fgColor = isSelected ? colors.SelectedForeground : colors.ItemColor;
            var bgColor = isSelected ? (RGBAColor32?)colors.SelectedBackground : null;

            // Capture i for the closure.
            var capturedIndex = i;
            children.Add(WithDigit(new Layout.Node.Leaf(new Layout.Content.Text(label, fontSize)
            {
                Color = fgColor,
                HAlign = TextAlign.Center,
                VAlign = TextAlign.Center,
            })
            {
                Height = Layout.Sizing.Fixed(itemLineH),
                Width = Layout.Sizing.Star(),
                Background = bgColor,
                Hit = new HitResult.ListItemHit("MenuItem", capturedIndex),
                OnClick = _ => model.ConfirmAt(capturedIndex),
            }, i));
        }

        // Bottom Star spacer - mirrors the top spacer to vertically center the block.
        // Carries Down.
        children.Add(new Layout.Node.Leaf(new Layout.Content.Box(0, 0))
        {
            Height = edge,
            Width = Layout.Sizing.Star(),
        }.WithShortcut(InputKey.Down).Activatable(_ => model.HandleKey(InputKey.Down)));

        return new Layout.Node.Stack(children.MoveToImmutable(), Layout.Axis.Vertical);
    }

    // Row heights of the shared column in base font sizes: title 2 x 1.6, prompt 2, gap 0.5, items 2 each.
    private const float TitleLines = 3.2f, PromptLines = 2f, GapLines = 0.5f, ItemLines = 2f;

    /// <summary>
    /// Sizes the panel card for <paramref name="model"/> on a surface of <paramref name="surfaceWidth"/> x
    /// <paramref name="surfaceHeight"/> design units, measuring text through <paramref name="ctx"/> so every surface
    /// resolves it its own way. The card is as wide as its widest line wants, clamped to
    /// <see cref="MenuPanelStyle.MinWidth"/>..<see cref="MenuPanelStyle.MaxWidth"/> (full width less the side margins on
    /// a portrait surface); the font starts at <see cref="MenuPanelStyle.FontToCard"/> of the card's widest allowed
    /// extent and is capped so the widest line (title at 1.6x, the selected-row prefix) fits inside it.
    /// </summary>
    public static MenuPanelPlan PlanPanel(MenuModel model, MenuPanelStyle style, Layout.IMeasureContext<float> ctx,
        float surfaceWidth, float surfaceHeight)
    {
        var unit = ctx.ToSurface(1f);
        if (unit <= 0f) unit = 1f;
        float Width(string text, float size) => ctx.MeasureText(text.AsSpan(), size).Width / unit;

        var portrait = surfaceHeight > surfaceWidth * style.PortraitAspect;
        var room = MathF.Max(1f, surfaceWidth - 2f * style.SideMargin);
        var maxW = portrait ? room : MathF.Min(style.MaxWidth, room);
        var minW = portrait ? room : MathF.Min(style.MinWidth, maxW);
        var border = style.Border is { Width: > 0f } b ? b.Width : 0f;

        // Widest line per unit of font size; text width is linear in size, so one measurement at 1 is enough.
        var w1 = Width(model.Title, 1.6f);
        w1 = MathF.Max(w1, Width(model.Prompt, 1f));
        foreach (var item in model.Items)
            w1 = MathF.Max(w1, Width(SelectedPrefix + item, 1f));

        // Horizontal padding is PadX font sizes per side, so the widest line plus padding is linear in size too.
        var perFont = w1 + 2f * style.PadX;
        var font = MathF.Min(maxW * style.FontToCard, (maxW - 2f * border) / perFont);
        var wanted = perFont * font + 2f * border;
        var width = portrait ? room : MathF.Max(minW, MathF.Min(maxW, wanted));
        // A min width above the wanted one only adds room; the cap against the final width keeps the fit exact.
        font = MathF.Min(font, (width - 2f * border) / perFont);

        // A renderer's advances are not linear in size (hinted, rounded per glyph), so the size-1 estimate can be
        // short at the size the rows are drawn. Re-measure every row at the font it will use and shrink until the
        // widest fits inside the card less border and padding; a few passes converge, so the loop is bounded.
        for (var pass = 0; pass < 8; pass++)
        {
            var widest = Width(model.Title, 1.6f * font);
            widest = MathF.Max(widest, Width(model.Prompt, font));
            foreach (var item in model.Items)
            {
                widest = MathF.Max(widest, Width(SelectedPrefix + item, font));
                widest = MathF.Max(widest, Width(UnselectedPrefix + item, font));
            }
            var room2 = width - 2f * border - 2f * style.PadX * font;
            // widest <= 0f: nothing measurable (empty model or a zero-width measure), so scaling cannot help.
            if (widest <= room2 || widest <= 0f) break;
            // 0.995: aim a hair under the room so rounding in the measure cannot leave the widest row 1px over.
            font *= room2 / widest * 0.995f;
        }

        var lines = TitleLines + PromptLines + GapLines + ItemLines * model.Items.Length + 2f * style.PadY;
        var height = MathF.Min(lines * font + 2f * border, MathF.Max(1f, surfaceHeight - 2f * style.SideMargin));
        return new MenuPanelPlan(width, height, font, portrait);
    }

    /// <summary>
    /// The menu as a panel: a modal <see cref="Layout.Node.Overlay"/> whose base is a full-surface scrim and whose top
    /// is a card centred on the surface, sized by <see cref="PlanPanel"/> and edged with <see cref="MenuPanelStyle.Border"/>
    /// as a declared <see cref="Layout.Node.BorderStyle"/>. The card holds the very rows <see cref="BuildTree"/> builds
    /// (same Clickable regions, same declared keys), so <see cref="MenuModel"/> stays the one owner of the selection. While
    /// the panel is painted the router sees the card only: a press on the scrim is consumed and reaches nothing beneath.
    /// </summary>
    public static Layout.Node BuildPanel(MenuModel model, MenuColors colors, MenuPanelStyle style,
        Layout.IMeasureContext<float> ctx, float surfaceWidth, float surfaceHeight)
    {
        var plan = PlanPanel(model, style, ctx, surfaceWidth, surfaceHeight);
        var column = BuildColumn(model, colors, plan.FontSize, Layout.Sizing.Fixed(plan.FontSize * style.PadY));
        var card = new Layout.Node.Stack([column], Layout.Axis.Vertical)
        {
            Width = Layout.Sizing.Fixed(plan.CardWidth),
            Height = Layout.Sizing.Fixed(plan.CardHeight),
            Background = style.CardBackground,
            BorderStyle = style.Border,
            Padding = plan.FontSize * style.PadX,
            PaddingY = 0f,
        };
        var scrim = new Layout.Node.Leaf(new Layout.Content.Box(0, 0))
        {
            Width = Layout.Sizing.Star(),
            Height = Layout.Sizing.Star(),
            Background = style.Scrim,
            Hit = new HitResult.ChromeHit(),
        };
        var top = Layout.Builder.Anchored(card, side: null,
            offsetAlong: (surfaceWidth - plan.CardWidth) / 2f,
            offsetAcross: (surfaceHeight - plan.CardHeight) / 2f);
        return Layout.Builder.Modal(scrim, top);
    }
}

/// <summary>What <see cref="MenuLayout.PlanPanel"/> decided, in design units.</summary>
/// <param name="CardWidth">Card width, border included.</param>
/// <param name="CardHeight">Card height, border included.</param>
/// <param name="FontSize">Base font size (the title is 1.6x).</param>
/// <param name="Portrait">Whether the portrait rule applied (full width less side margins).</param>
public readonly record struct MenuPanelPlan(float CardWidth, float CardHeight, float FontSize, bool Portrait);

/// <summary>Declared look and sizing of <see cref="MenuLayout.BuildPanel"/>, in design units. Init-only throughout.</summary>
public sealed record MenuPanelStyle
{
    /// <summary>Narrowest card on a landscape surface.</summary>
    public float MinWidth { get; init; } = 320f;
    /// <summary>Widest card on a landscape surface.</summary>
    public float MaxWidth { get; init; } = 560f;
    /// <summary>Portrait when height exceeds width times this; a portrait card is full width less <see cref="SideMargin"/> each side.</summary>
    public float PortraitAspect { get; init; } = 1f;
    /// <summary>Gap kept between the card and the surface edge.</summary>
    public float SideMargin { get; init; } = 16f;
    /// <summary>Starting font size as a fraction of the card's widest allowed width, before the widest line caps it.</summary>
    public float FontToCard { get; init; } = 1f / 20f;
    /// <summary>Horizontal padding per side, in font sizes.</summary>
    public float PadX { get; init; } = 1f;
    /// <summary>Vertical padding top and bottom, in font sizes.</summary>
    public float PadY { get; init; } = 1f;
    /// <summary>Colour over the whole surface behind the card.</summary>
    public RGBAColor32 Scrim { get; init; } = new(0, 0, 0, 0xa0);
    /// <summary>Card fill.</summary>
    public RGBAColor32 CardBackground { get; init; } = new(0x1c, 0x1f, 0x2a, 0xff);
    /// <summary>The card's edge, declared as a node border; null for none.</summary>
    public Layout.BorderStyle? Border { get; init; } = new() { Color = new RGBAColor32(0x60, 0x70, 0xa0, 0xff), Width = 2f, Radius = 8f };
}
