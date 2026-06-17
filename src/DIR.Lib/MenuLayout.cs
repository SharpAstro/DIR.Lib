using System.Collections.Immutable;

namespace DIR.Lib;

/// <summary>
/// Builds a surface-neutral <see cref="LayoutNode"/> tree for a vertical "wizard" menu.
/// Pure static: no renderer, no mutable state. The same tree is consumed by the GPU
/// (<see cref="PixelMenuWidget{TSurface}"/>) and by the TUI cell layout (Console.Lib, Phase B).
/// </summary>
public static class MenuLayout
{
    /// <summary>
    /// Prefix rendered before the selected item row. Unicode right-pointing triangle + two spaces.
    /// </summary>
    private const string SelectedPrefix = "▶  ";

    /// <summary>Three spaces: unselected item indent matching the selected prefix width.</summary>
    private const string UnselectedPrefix = "   ";

    /// <summary>
    /// Builds a vertically-centered <see cref="LayoutNode"/> tree from the given
    /// <paramref name="model"/> and <paramref name="colors"/>.
    /// <para>
    /// Tree structure: outer vertical Stack with two Star spacers (before/after) that center
    /// the content block: title leaf, prompt leaf, a small gap box, and one leaf per item.
    /// Each item leaf carries a <see cref="HitResult.ListItemHit"/> ("MenuItem", i) and
    /// an OnClick that calls <see cref="MenuModel.ConfirmAt"/>.
    /// </para>
    /// </summary>
    /// <param name="model">The menu model supplying title, prompt, items, and selected index.</param>
    /// <param name="colors">The color palette for all menu elements.</param>
    /// <param name="fontSize">Base font size in design units. Title uses 1.6x this value.</param>
    public static LayoutNode BuildTree(MenuModel model, MenuColors colors, float fontSize)
    {
        var titleSize = fontSize * 1.6f;
        var itemLineH = fontSize * 2f;
        var gapH = fontSize * 0.5f;

        var children = ImmutableArray.CreateBuilder<LayoutNode>(model.Items.Length + 5);

        // Top Star spacer - fills available space above the content block.
        children.Add(new LayoutNode.Leaf(new LayoutContent.Box(0, 0))
        {
            Height = Sizing.Star(),
            Width = Sizing.Star(),
        });

        // Title leaf - centered horizontally, larger font.
        children.Add(new LayoutNode.Leaf(new LayoutContent.Text(model.Title, titleSize)
        {
            Color = colors.TitleColor,
            HAlign = TextAlign.Center,
            VAlign = TextAlign.Center,
        })
        {
            Height = Sizing.Fixed(titleSize * 2f),
            Width = Sizing.Star(),
        });

        // Prompt leaf - centered, base font size.
        children.Add(new LayoutNode.Leaf(new LayoutContent.Text(model.Prompt, fontSize)
        {
            Color = colors.PromptColor,
            HAlign = TextAlign.Center,
            VAlign = TextAlign.Center,
        })
        {
            Height = Sizing.Fixed(itemLineH),
            Width = Sizing.Star(),
        });

        // Small gap between prompt and items.
        children.Add(new LayoutNode.Leaf(new LayoutContent.Box(0, gapH))
        {
            Height = Sizing.Fixed(gapH),
            Width = Sizing.Star(),
        });

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
            children.Add(new LayoutNode.Leaf(new LayoutContent.Text(label, fontSize)
            {
                Color = fgColor,
                HAlign = TextAlign.Center,
                VAlign = TextAlign.Center,
            })
            {
                Height = Sizing.Fixed(itemLineH),
                Width = Sizing.Star(),
                Background = bgColor,
                Hit = new HitResult.ListItemHit("MenuItem", capturedIndex),
                OnClick = _ => model.ConfirmAt(capturedIndex),
            });
        }

        // Bottom Star spacer - mirrors the top spacer to vertically center the block.
        children.Add(new LayoutNode.Leaf(new LayoutContent.Box(0, 0))
        {
            Height = Sizing.Star(),
            Width = Sizing.Star(),
        });

        return new LayoutNode.Stack(children.MoveToImmutable(), LayoutAxis.Vertical);
    }
}
