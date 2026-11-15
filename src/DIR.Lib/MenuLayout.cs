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
    private const string SelectedPrefix = "▶  ";

    /// <summary>Three spaces: unselected item indent matching the selected prefix width.</summary>
    private const string UnselectedPrefix = "   ";

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
    {
        var titleSize = fontSize * 1.6f;
        var itemLineH = fontSize * 2f;
        var gapH = fontSize * 0.5f;

        var children = ImmutableArray.CreateBuilder<Layout.Node>(model.Items.Length + 5);

        // Top Star spacer - fills available space above the content block.
        // Carries Up: the menu's keys are declared in the tree, so InputRouter reaches them with no host fallback.
        children.Add(new Layout.Node.Leaf(new Layout.Content.Box(0, 0))
        {
            Height = Layout.Sizing.Star(),
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
            Height = Layout.Sizing.Star(),
            Width = Layout.Sizing.Star(),
        }.WithShortcut(InputKey.Down).Activatable(_ => model.HandleKey(InputKey.Down)));

        return new Layout.Node.Stack(children.MoveToImmutable(), Layout.Axis.Vertical);
    }
}
