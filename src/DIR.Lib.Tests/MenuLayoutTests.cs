using System;
using System.Collections.Immutable;
using System.Linq;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Tests for <see cref="MenuLayout.BuildTree"/>: verifies tree structure, item count,
/// selected-item visual state, and per-item hit/click wiring -- all headless (no renderer).
/// </summary>
public class MenuLayoutTests
{
    private static readonly MenuColors DefaultColors = new();

    private static MenuModel BuildModel(int itemCount, int selected = 0)
    {
        var m = new MenuModel();
        var items = ImmutableArray.CreateRange(Enumerable.Range(1, itemCount).Select(i => $"Item {i}"));
        m.Reset("Test Menu", "Choose an option", items, selected);
        return m;
    }

    /// <summary>Collects all <see cref="LayoutNode.Leaf"/> nodes from a tree, depth-first.</summary>
    private static ImmutableArray<LayoutNode.Leaf> CollectLeaves(LayoutNode node)
    {
        var builder = ImmutableArray.CreateBuilder<LayoutNode.Leaf>();
        Collect(node, builder);
        return builder.ToImmutable();

        static void Collect(LayoutNode n, ImmutableArray<LayoutNode.Leaf>.Builder b)
        {
            if (n is LayoutNode.Leaf leaf)
            {
                b.Add(leaf);
            }
            else if (n is LayoutNode.Stack stack)
            {
                foreach (var child in stack.Children)
                {
                    Collect(child, b);
                }
            }
        }
    }

    /// <summary>Returns only the item leaves (those carrying a "MenuItem" ListItemHit).</summary>
    private static ImmutableArray<LayoutNode.Leaf> ItemLeaves(LayoutNode root)
        => CollectLeaves(root).Where(l => l.Hit is HitResult.ListItemHit { ListId: "MenuItem" }).ToImmutableArray();

    // --- Item count ---

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(9)]
    public void BuildTree_YieldsCorrectNumberOfItemLeaves(int count)
    {
        var model = BuildModel(count);
        var root = MenuLayout.BuildTree(model, DefaultColors, 16f);
        ItemLeaves(root).Length.ShouldBe(count);
    }

    // --- Selected item visual state ---

    [Fact]
    public void SelectedItemLeaf_HasNonTransparentBackground()
    {
        var model = BuildModel(3, selected: 1);
        var root = MenuLayout.BuildTree(model, DefaultColors, 16f);

        var items = ItemLeaves(root);
        var selected = items[1];
        selected.Background.ShouldNotBeNull();
        selected.Background!.Value.Alpha.ShouldNotBe((byte)0);
    }

    [Fact]
    public void UnselectedItemLeaves_HaveNullBackground()
    {
        var model = BuildModel(3, selected: 1);
        var root = MenuLayout.BuildTree(model, DefaultColors, 16f);

        var items = ItemLeaves(root);
        items[0].Background.ShouldBeNull();
        items[2].Background.ShouldBeNull();
    }

    [Fact]
    public void SelectedItemLeaf_TextPrefixedWithArrow()
    {
        var model = BuildModel(3, selected: 0);
        var root = MenuLayout.BuildTree(model, DefaultColors, 16f);

        var items = ItemLeaves(root);
        var text = (items[0].Content as LayoutContent.Text).ShouldNotBeNull();
        text.Value.ShouldStartWith("▶");
    }

    [Fact]
    public void UnselectedItemLeaf_TextPrefixedWithSpaces()
    {
        var model = BuildModel(3, selected: 0);
        var root = MenuLayout.BuildTree(model, DefaultColors, 16f);

        var items = ItemLeaves(root);
        var text = (items[1].Content as LayoutContent.Text).ShouldNotBeNull();
        text.Value.ShouldStartWith("   ");
    }

    // --- Hit + OnClick wiring ---

    [Fact]
    public void AllItemLeaves_HaveNonNullHit()
    {
        var model = BuildModel(5);
        var root = MenuLayout.BuildTree(model, DefaultColors, 16f);

        foreach (var leaf in ItemLeaves(root))
        {
            leaf.Hit.ShouldNotBeNull();
        }
    }

    [Fact]
    public void AllItemLeaves_HaveNonNullOnClick()
    {
        var model = BuildModel(5);
        var root = MenuLayout.BuildTree(model, DefaultColors, 16f);

        foreach (var leaf in ItemLeaves(root))
        {
            leaf.OnClick.ShouldNotBeNull();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void InvokingItemOnClick_ConfirmsModelAtThatIndex(int clickedIndex)
    {
        var model = BuildModel(3, selected: 0);
        var root = MenuLayout.BuildTree(model, DefaultColors, 16f);

        var items = ItemLeaves(root);
        items[clickedIndex].OnClick!.Invoke(InputModifier.None);

        model.IsConfirmed.ShouldBeTrue();
        model.SelectedIndex.ShouldBe(clickedIndex);
    }

    // --- Hit result IDs ---

    [Fact]
    public void AllItemLeaves_HitResultIsListItemHitWithMenuItemId()
    {
        var model = BuildModel(4);
        var root = MenuLayout.BuildTree(model, DefaultColors, 16f);

        var items = ItemLeaves(root);
        for (var i = 0; i < items.Length; i++)
        {
            var hit = items[i].Hit.ShouldBeOfType<HitResult.ListItemHit>();
            hit.ListId.ShouldBe("MenuItem");
            hit.Index.ShouldBe(i);
        }
    }

    // --- Tree has Star spacers for vertical centering ---

    [Fact]
    public void BuildTree_RootHasTopAndBottomStarSpacers()
    {
        var model = BuildModel(3);
        var root = MenuLayout.BuildTree(model, DefaultColors, 16f);

        var stack = root.ShouldBeOfType<LayoutNode.Stack>();
        var first = stack.Children[0];
        var last = stack.Children[^1];

        first.Height.IsStar.ShouldBeTrue();
        last.Height.IsStar.ShouldBeTrue();
    }
}
