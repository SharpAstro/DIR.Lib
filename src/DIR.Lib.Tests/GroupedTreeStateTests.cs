using System;
using System.Collections.Generic;
using System.Linq;
using DIR.Lib;
using DIR.Lib.Layout;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// The grouped tree model: <see cref="GroupedTreeState"/> and the nodes it flattens. Pure data, no surface, so
/// every rule is checked against plain lists. The tree used throughout is org / repo / row, deliberately not in
/// alphabetical order (the model never sorts), and always built from fresh objects (the model never holds one).
/// </summary>
public class GroupedTreeStateTests
{
    private static GroupedTreeNode<string, int> Group(string key, params GroupedTreeNode<string, int>[] children)
        => GroupedTreeNode<string, int>.Group(key, "title " + key, children);

    private static GroupedTreeNode<string, int> Leaf(string key, int id)
        => GroupedTreeNode<string, int>.Leaf(key, id);

    /// <summary>
    /// The rows with every group open:
    /// <code>
    ///  0 org:zeta            6 org:acme
    ///  1  repo:zeta/web      7  repo:acme/core
    ///  2   row:zeta/web#9    8   row:acme/core#1
    ///  3   row:zeta/web#2    9   row:acme/core#7
    ///  4  repo:zeta/api     10   row:acme/core#3
    ///  5   row:zeta/api#5   11  repo:acme/empty
    /// </code>
    /// </summary>
    private static GroupedTreeNode<string, int>[] OrgTree() =>
    [
        Group("org:zeta",
            Group("repo:zeta/web", Leaf("row:zeta/web#9", 9), Leaf("row:zeta/web#2", 2)),
            Group("repo:zeta/api", Leaf("row:zeta/api#5", 5))),
        Group("org:acme",
            Group("repo:acme/core", Leaf("row:acme/core#1", 1), Leaf("row:acme/core#7", 7), Leaf("row:acme/core#3", 3)),
            Group("repo:acme/empty")),
    ];

    /// <summary>A copy of the tree, fresh objects, without the items whose key is in <paramref name="keys"/>
    /// (a dropped item takes its subtree with it).</summary>
    private static GroupedTreeNode<string, int>[] Without(IEnumerable<GroupedTreeNode<string, int>> nodes, params string[] keys)
        => [.. nodes
            .Where(n => !keys.Contains(n.Key))
            .Select(n => n is GroupedTreeGroup<string, int> g ? Group(g.Key, Without(g.Children, keys)) : Leaf(n.Key, ((GroupedTreeLeaf<string, int>)n).Payload))];

    /// <summary>A copy of the tree in which no key is the one it was: a data set replaced wholesale.</summary>
    private static GroupedTreeNode<string, int>[] Renamed(IEnumerable<GroupedTreeNode<string, int>> nodes)
        => [.. nodes.Select(n => n is GroupedTreeGroup<string, int> g
            ? Group("x:" + g.Key, Renamed(g.Children))
            : Leaf("x:" + n.Key, ((GroupedTreeLeaf<string, int>)n).Payload))];

    private static string[] Keys(IEnumerable<GroupedTreeRow<string, int>> rows) => [.. rows.Select(r => r.Key)];

    private static (bool Handled, int MoveTo, bool Activate) Press(
        GroupedTreeState state, InputKey key, int index, List<GroupedTreeRow<string, int>> rows)
    {
        var handled = state.HandleKey(key, index, rows, out var moveTo, out var activate);
        return (handled, moveTo, activate);
    }

    // ----- the nodes -----

    [Fact]
    public void Factories_build_a_group_and_a_leaf()
    {
        var leaf = Leaf("k", 5);
        var group = GroupedTreeNode<string, int>.Group("g", "title", [leaf]);

        leaf.ShouldBeOfType<GroupedTreeLeaf<string, int>>().Payload.ShouldBe(5);
        group.ShouldBeOfType<GroupedTreeGroup<string, int>>().Children.Count.ShouldBe(1);
        group.Key.ShouldBe("g");
        leaf.Key.ShouldBe("k");
        Should.Throw<ArgumentNullException>(() => GroupedTreeNode<string, int>.Leaf(null!, 1));
    }

    // ----- flatten -----

    [Fact]
    public void Flatten_emits_every_item_depth_first_in_the_order_given()
    {
        var rows = new GroupedTreeState().Flatten(OrgTree());

        Keys(rows).ShouldBe(
        [
            "org:zeta", "repo:zeta/web", "row:zeta/web#9", "row:zeta/web#2", "repo:zeta/api", "row:zeta/api#5",
            "org:acme", "repo:acme/core", "row:acme/core#1", "row:acme/core#7", "row:acme/core#3", "repo:acme/empty",
        ]);
        rows.Select(r => r.Depth).ShouldBe([0, 1, 2, 2, 1, 2, 0, 1, 2, 2, 2, 1]);
        rows.Select(r => r.ParentIndex).ShouldBe([-1, 0, 1, 1, 0, 4, -1, 6, 7, 7, 7, 6]);
        rows.Select(r => r.Kind).ShouldBe(
        [
            GroupedTreeRowKind.Group, GroupedTreeRowKind.Group, GroupedTreeRowKind.Row, GroupedTreeRowKind.Row,
            GroupedTreeRowKind.Group, GroupedTreeRowKind.Row, GroupedTreeRowKind.Group, GroupedTreeRowKind.Group,
            GroupedTreeRowKind.Row, GroupedTreeRowKind.Row, GroupedTreeRowKind.Row, GroupedTreeRowKind.Group,
        ]);
    }

    [Fact]
    public void Flatten_carries_the_payload_of_each_kind_and_only_that_one()
    {
        var rows = new GroupedTreeState().Flatten(OrgTree());

        rows[0].GroupPayload.ShouldBe("title org:zeta");
        rows[0].RowPayload.ShouldBe(0);
        rows[2].RowPayload.ShouldBe(9);
        rows[2].GroupPayload.ShouldBeNull();
    }

    [Fact]
    public void Flatten_never_reorders_what_it_is_given()
    {
        var tree = OrgTree();
        Array.Reverse(tree);

        var rows = new GroupedTreeState().Flatten(tree);

        rows.Where(r => r.Depth == 0).Select(r => r.Key).ShouldBe(["org:acme", "org:zeta"]);
        Keys(rows).Skip(1).Take(5).ShouldBe(["repo:acme/core", "row:acme/core#1", "row:acme/core#7", "row:acme/core#3", "repo:acme/empty"]);
    }

    [Fact]
    public void Groups_are_open_unless_folded_and_a_leaf_or_an_empty_group_has_no_caret()
    {
        var state = new GroupedTreeState();
        state.IsExpanded("never-heard-of-it").ShouldBeTrue();

        var rows = state.Flatten(OrgTree());

        rows.Where(r => r.Kind == GroupedTreeRowKind.Group && r.Key != "repo:acme/empty")
            .ShouldAllBe(r => r.HasChildren && r.IsExpanded);
        rows.Where(r => r.Kind == GroupedTreeRowKind.Row).ShouldAllBe(r => !r.HasChildren && !r.IsExpanded);
        rows[11].Key.ShouldBe("repo:acme/empty");
        rows[11].HasChildren.ShouldBeFalse();
        rows[11].IsExpanded.ShouldBeFalse();
    }

    [Fact]
    public void A_folded_group_stays_as_a_row_and_its_children_are_skipped()
    {
        var state = new GroupedTreeState();
        state.Collapse("org:zeta").ShouldBeTrue();

        var rows = state.Flatten(OrgTree());

        Keys(rows).ShouldBe(["org:zeta", "org:acme", "repo:acme/core", "row:acme/core#1", "row:acme/core#7", "row:acme/core#3", "repo:acme/empty"]);
        rows[0].HasChildren.ShouldBeTrue();
        rows[0].IsExpanded.ShouldBeFalse();
        rows[1].ParentIndex.ShouldBe(-1);
        rows[2].ParentIndex.ShouldBe(1);
    }

    [Fact]
    public void A_group_opened_again_shows_its_subtree_as_it_was_left()
    {
        var state = new GroupedTreeState();
        state.Collapse("repo:zeta/web");
        state.Collapse("org:zeta");
        state.Expand("org:zeta").ShouldBeTrue();

        var keys = Keys(state.Flatten(OrgTree()));

        keys.Take(4).ShouldBe(["org:zeta", "repo:zeta/web", "repo:zeta/api", "row:zeta/api#5"]);
    }

    [Fact]
    public void A_fold_survives_a_tree_rebuilt_from_fresh_objects()
    {
        var state = new GroupedTreeState();
        state.Collapse("repo:acme/core");

        var before = OrgTree();
        var after = OrgTree();
        ReferenceEquals(before[0], after[0]).ShouldBeFalse();

        var first = state.Flatten(before);
        var second = state.Flatten(after);

        Keys(second).ShouldBe(Keys(first));
        second.Select(r => r.IsExpanded).ShouldBe(first.Select(r => r.IsExpanded));
        Keys(second).ShouldNotContain("row:acme/core#1");
        second[7].IsExpanded.ShouldBeFalse();
    }

    [Fact]
    public void Expand_Collapse_Toggle_and_ExpandAll_report_a_change_and_raise_Changed_only_for_one()
    {
        var state = new GroupedTreeState();
        var changes = 0;
        state.Changed = () => changes++;

        state.Collapse("a").ShouldBeTrue();
        state.Collapse("a").ShouldBeFalse();
        changes.ShouldBe(1);
        state.IsExpanded("a").ShouldBeFalse();

        state.Expand("a").ShouldBeTrue();
        state.Expand("a").ShouldBeFalse();
        changes.ShouldBe(2);

        state.Toggle("a");
        state.IsExpanded("a").ShouldBeFalse();
        state.Toggle("a");
        state.IsExpanded("a").ShouldBeTrue();
        changes.ShouldBe(4);

        state.ExpandAll().ShouldBeFalse();
        changes.ShouldBe(4);
        state.Collapse("a");
        state.Collapse("b");
        state.ExpandAll().ShouldBeTrue();
        state.IsExpanded("a").ShouldBeTrue();
        state.IsExpanded("b").ShouldBeTrue();
        changes.ShouldBe(7);
    }

    // ----- save and restore -----

    [Fact]
    public void Save_and_Restore_round_trip_the_folds_sorted()
    {
        var state = new GroupedTreeState();
        state.Collapse("repo:zeta/web");
        state.Collapse("org:acme");

        var saved = state.Save();
        saved.Folded.ShouldBe(["org:acme", "repo:zeta/web"]);

        // The record is a copy: editing it later does not fold or unfold anything.
        saved.Folded[0] = "tampered";
        state.IsExpanded("org:acme").ShouldBeFalse();
        state.Save().Folded.ShouldBe(["org:acme", "repo:zeta/web"]);

        var restored = GroupedTreeState.From(state.Save());
        var expected = state.Flatten(OrgTree());
        var actual = restored.Flatten(OrgTree());

        Keys(actual).ShouldBe(Keys(expected));
        actual.Select(r => r.IsExpanded).ShouldBe(expected.Select(r => r.IsExpanded));
        restored.Save().Folded.ShouldBe(state.Save().Folded);
    }

    [Fact]
    public void Restore_tolerates_keys_that_are_no_longer_present_and_keeps_them()
    {
        var state = GroupedTreeState.From(new GroupedTreeState.Saved(["gone/1", "org:zeta", "gone/2"]));

        var rows = state.Flatten(OrgTree());

        rows[0].Key.ShouldBe("org:zeta");
        rows[0].IsExpanded.ShouldBeFalse();
        rows.First(r => r.Key == "org:acme").IsExpanded.ShouldBeTrue();
        // A group filtered out today is folded again the day it comes back.
        state.Save().Folded.ShouldBe(["gone/1", "gone/2", "org:zeta"]);
    }

    [Fact]
    public void Restore_replaces_the_earlier_folds_and_raises_Changed()
    {
        var state = new GroupedTreeState();
        state.Collapse("a");
        var changes = 0;
        state.Changed = () => changes++;

        state.Restore(new GroupedTreeState.Saved(["b"]));

        state.IsExpanded("a").ShouldBeTrue();
        state.IsExpanded("b").ShouldBeFalse();
        changes.ShouldBe(1);
        Should.Throw<ArgumentNullException>(() => state.Restore(null!));
    }

    [Fact]
    public void Restore_tolerates_a_record_a_deserializer_left_gaps_in()
    {
        var state = new GroupedTreeState();

        state.Restore(new GroupedTreeState.Saved([null!, "a"]));
        state.Save().Folded.ShouldBe(["a"]);

        state.Restore(new GroupedTreeState.Saved(null!));
        state.Save().Folded.ShouldBeEmpty();
    }

    [Fact]
    public void Restore_does_not_touch_the_selection()
    {
        var state = new GroupedTreeState { SelectedKey = "row:acme/core#7" };

        state.Restore(new GroupedTreeState.Saved(["org:acme"]));

        state.SelectedKey.ShouldBe("row:acme/core#7");
    }

    // ----- selection -----

    [Fact]
    public void Selection_follows_its_key_across_a_reorder()
    {
        var state = new GroupedTreeState { SelectedKey = "row:acme/core#7" };
        state.Reselect(state.Flatten(OrgTree())).ShouldBe(9);

        var reordered = OrgTree();
        Array.Reverse(reordered);
        var rows = state.Flatten(reordered);

        var index = state.Reselect(rows);
        index.ShouldBe(3);
        rows[index].Key.ShouldBe("row:acme/core#7");
        state.SelectedKey.ShouldBe("row:acme/core#7");
    }

    [Fact]
    public void Selection_follows_its_key_across_a_tree_rebuilt_from_fresh_objects()
    {
        var state = new GroupedTreeState { SelectedKey = "row:zeta/api#5" };
        state.Reselect(state.Flatten(OrgTree())).ShouldBe(5);

        state.Reselect(state.Flatten(OrgTree())).ShouldBe(5);
    }

    [Fact]
    public void No_selection_gives_minus_one()
    {
        var state = new GroupedTreeState();

        state.Reselect(state.Flatten(OrgTree())).ShouldBe(-1);
        state.SelectedKey.ShouldBeNull();
    }

    [Fact]
    public void A_row_hidden_by_a_fold_selects_the_group_that_hides_it()
    {
        var state = new GroupedTreeState { SelectedKey = "row:acme/core#7" };
        state.Reselect(state.Flatten(OrgTree())).ShouldBe(9);

        state.Collapse("repo:acme/core");
        state.Reselect(state.Flatten(OrgTree())).ShouldBe(7);
        state.SelectedKey.ShouldBe("repo:acme/core");
    }

    [Fact]
    public void A_row_hidden_under_a_folded_grandparent_selects_the_outermost_group_still_shown()
    {
        var state = new GroupedTreeState { SelectedKey = "row:acme/core#7" };
        state.Reselect(state.Flatten(OrgTree())).ShouldBe(9);

        state.Collapse("org:acme");
        state.Collapse("repo:acme/core");
        state.Reselect(state.Flatten(OrgTree())).ShouldBe(6);
        state.SelectedKey.ShouldBe("org:acme");
    }

    [Fact]
    public void A_dropped_row_selects_the_next_sibling()
    {
        var state = new GroupedTreeState { SelectedKey = "row:acme/core#7" };
        state.Reselect(state.Flatten(OrgTree()));

        var rows = state.Flatten(Without(OrgTree(), "row:acme/core#7"));

        var index = state.Reselect(rows);
        rows[index].Key.ShouldBe("row:acme/core#3");
        index.ShouldBe(9);
        state.SelectedKey.ShouldBe("row:acme/core#3");
    }

    [Fact]
    public void A_dropped_last_row_selects_the_previous_sibling()
    {
        var state = new GroupedTreeState { SelectedKey = "row:acme/core#3" };
        state.Reselect(state.Flatten(OrgTree()));

        var rows = state.Flatten(Without(OrgTree(), "row:acme/core#3"));

        rows[state.Reselect(rows)].Key.ShouldBe("row:acme/core#7");
    }

    [Fact]
    public void A_dropped_only_child_selects_its_parent()
    {
        var state = new GroupedTreeState { SelectedKey = "row:zeta/api#5" };
        state.Reselect(state.Flatten(OrgTree()));

        var rows = state.Flatten(Without(OrgTree(), "row:zeta/api#5"));

        var index = state.Reselect(rows);
        index.ShouldBe(4);
        rows[index].Key.ShouldBe("repo:zeta/api");
    }

    [Fact]
    public void A_dropped_top_level_group_selects_the_neighbouring_one()
    {
        var first = new GroupedTreeState { SelectedKey = "org:zeta" };
        first.Reselect(first.Flatten(OrgTree()));
        var rows = first.Flatten(Without(OrgTree(), "org:zeta"));
        rows[first.Reselect(rows)].Key.ShouldBe("org:acme");

        var last = new GroupedTreeState { SelectedKey = "org:acme" };
        last.Reselect(last.Flatten(OrgTree()));
        rows = last.Flatten(Without(OrgTree(), "org:acme"));
        rows[last.Reselect(rows)].Key.ShouldBe("org:zeta");
    }

    [Fact]
    public void A_row_dropped_with_all_its_siblings_selects_the_group_that_held_it()
    {
        var state = new GroupedTreeState { SelectedKey = "row:zeta/web#9" };
        state.Reselect(state.Flatten(OrgTree()));

        var rows = state.Flatten(Without(OrgTree(), "repo:zeta/web"));

        var index = state.Reselect(rows);
        index.ShouldBe(0);
        rows[index].Key.ShouldBe("org:zeta");
    }

    [Fact]
    public void A_row_whose_whole_ancestry_is_dropped_selects_the_nearest_survivor_in_the_old_order()
    {
        var state = new GroupedTreeState { SelectedKey = "row:zeta/api#5" };
        state.Reselect(state.Flatten(OrgTree()));

        var rows = state.Flatten(Without(OrgTree(), "org:zeta"));

        var index = state.Reselect(rows);
        index.ShouldBe(0);
        rows[index].Key.ShouldBe("org:acme");
    }

    [Fact]
    public void When_nothing_of_the_old_list_survives_the_selection_stays_at_its_old_position()
    {
        var state = new GroupedTreeState { SelectedKey = "row:acme/core#7" };
        state.Reselect(state.Flatten(OrgTree())).ShouldBe(9);

        var longer = state.Flatten(Renamed(OrgTree()));
        state.Reselect(longer).ShouldBe(9);
        state.SelectedKey.ShouldBe("x:row:acme/core#7");

        var shorter = state.Flatten<string, int>([Group("only", Leaf("a", 1), Leaf("b", 2))]);
        state.Reselect(shorter).ShouldBe(2);
        state.SelectedKey.ShouldBe("b");
    }

    [Fact]
    public void An_empty_list_gives_minus_one_and_keeps_the_selection_for_when_the_data_returns()
    {
        var state = new GroupedTreeState { SelectedKey = "row:acme/core#7" };
        state.Reselect(state.Flatten(OrgTree())).ShouldBe(9);

        GroupedTreeNode<string, int>[] none = [];
        state.Reselect(state.Flatten(none)).ShouldBe(-1);
        state.SelectedKey.ShouldBe("row:acme/core#7");

        state.Reselect(state.Flatten(OrgTree())).ShouldBe(9);
    }

    [Fact]
    public void A_key_never_seen_as_a_row_is_kept_until_it_appears()
    {
        var state = new GroupedTreeState { SelectedKey = "row:later" };

        state.Reselect(state.Flatten(OrgTree())).ShouldBe(-1);
        state.SelectedKey.ShouldBe("row:later");

        state.Reselect(state.Flatten<string, int>([.. OrgTree(), Leaf("row:later", 0)])).ShouldBe(12);
    }

    [Fact]
    public void A_selection_restored_under_a_group_folded_all_along_waits_for_the_group_to_open()
    {
        var state = GroupedTreeState.From(new GroupedTreeState.Saved(["org:acme"]));
        state.SelectedKey = "row:acme/core#7";

        state.Reselect(state.Flatten(OrgTree())).ShouldBe(-1);
        state.SelectedKey.ShouldBe("row:acme/core#7");

        state.Expand("org:acme");
        state.Reselect(state.Flatten(OrgTree())).ShouldBe(9);
    }

    [Fact]
    public void Each_refresh_starts_the_next_fallback_from_where_the_selection_now_is()
    {
        var state = new GroupedTreeState { SelectedKey = "row:acme/core#7" };
        state.Reselect(state.Flatten(OrgTree()));

        var rows = state.Flatten(Without(OrgTree(), "row:acme/core#7"));
        rows[state.Reselect(rows)].Key.ShouldBe("row:acme/core#3");

        rows = state.Flatten(Without(OrgTree(), "row:acme/core#7", "row:acme/core#3"));
        rows[state.Reselect(rows)].Key.ShouldBe("row:acme/core#1");
    }

    // ----- keyboard -----

    [Fact]
    public void Right_unfolds_a_folded_group_and_the_caller_flattens_again()
    {
        var state = new GroupedTreeState();
        state.Collapse("repo:acme/core");
        var changes = 0;
        state.Changed = () => changes++;
        var rows = state.Flatten(OrgTree());
        rows.Count.ShouldBe(9);

        Press(state, InputKey.Right, 7, rows).ShouldBe((true, -1, false));

        changes.ShouldBe(1);
        state.IsExpanded("repo:acme/core").ShouldBeTrue();
        state.Flatten(OrgTree()).Count.ShouldBe(12);
    }

    [Fact]
    public void Right_on_an_open_group_steps_into_its_first_child()
    {
        var state = new GroupedTreeState();
        var changes = 0;
        state.Changed = () => changes++;
        var rows = state.Flatten(OrgTree());

        Press(state, InputKey.Right, 0, rows).ShouldBe((true, 1, false));
        Press(state, InputKey.Right, 7, rows).ShouldBe((true, 8, false));

        changes.ShouldBe(0);
    }

    [Fact]
    public void Right_on_a_leaf_or_an_empty_group_does_nothing()
    {
        var state = new GroupedTreeState();
        var rows = state.Flatten(OrgTree());

        Press(state, InputKey.Right, 2, rows).ShouldBe((false, -1, false));
        Press(state, InputKey.Right, 11, rows).ShouldBe((false, -1, false));
        state.Save().Folded.ShouldBeEmpty();
    }

    [Fact]
    public void Left_folds_an_open_group_and_the_caller_flattens_again()
    {
        var state = new GroupedTreeState();
        var changes = 0;
        state.Changed = () => changes++;
        var rows = state.Flatten(OrgTree());

        Press(state, InputKey.Left, 7, rows).ShouldBe((true, -1, false));

        changes.ShouldBe(1);
        state.IsExpanded("repo:acme/core").ShouldBeFalse();
        state.Flatten(OrgTree()).Count.ShouldBe(9);
    }

    [Fact]
    public void Left_on_a_folded_group_steps_to_its_parent()
    {
        var state = new GroupedTreeState();
        state.Collapse("repo:acme/core");
        var rows = state.Flatten(OrgTree());

        Press(state, InputKey.Left, 7, rows).ShouldBe((true, 6, false));
        state.IsExpanded("repo:acme/core").ShouldBeFalse();
    }

    [Fact]
    public void Left_on_a_leaf_or_an_empty_group_steps_to_its_parent()
    {
        var state = new GroupedTreeState();
        var rows = state.Flatten(OrgTree());

        Press(state, InputKey.Left, 9, rows).ShouldBe((true, 7, false));
        Press(state, InputKey.Left, 2, rows).ShouldBe((true, 1, false));
        Press(state, InputKey.Left, 11, rows).ShouldBe((true, 6, false));
        state.Save().Folded.ShouldBeEmpty();
    }

    [Fact]
    public void Left_on_a_folded_top_level_group_does_nothing()
    {
        var state = new GroupedTreeState();
        state.Collapse("org:zeta");
        var rows = state.Flatten(OrgTree());

        Press(state, InputKey.Left, 0, rows).ShouldBe((false, -1, false));
    }

    [Fact]
    public void Enter_toggles_a_group_both_ways()
    {
        var state = new GroupedTreeState();
        var changes = 0;
        state.Changed = () => changes++;
        var rows = state.Flatten(OrgTree());

        Press(state, InputKey.Enter, 0, rows).ShouldBe((true, -1, false));
        state.IsExpanded("org:zeta").ShouldBeFalse();
        rows = state.Flatten(OrgTree());
        rows.Count.ShouldBe(7);

        Press(state, InputKey.Enter, 0, rows).ShouldBe((true, -1, false));
        state.IsExpanded("org:zeta").ShouldBeTrue();
        state.Flatten(OrgTree()).Count.ShouldBe(12);
        changes.ShouldBe(2);
    }

    [Fact]
    public void Enter_on_a_leaf_row_activates_it_and_folds_nothing()
    {
        var state = new GroupedTreeState();
        var changes = 0;
        state.Changed = () => changes++;
        var rows = state.Flatten(OrgTree());

        Press(state, InputKey.Enter, 2, rows).ShouldBe((true, -1, true));

        changes.ShouldBe(0);
        state.Save().Folded.ShouldBeEmpty();
    }

    [Fact]
    public void Enter_on_a_group_with_nothing_in_it_does_nothing()
    {
        var state = new GroupedTreeState();
        var rows = state.Flatten(OrgTree());

        Press(state, InputKey.Enter, 11, rows).ShouldBe((false, -1, false));
        state.Save().Folded.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(InputKey.Up)]
    [InlineData(InputKey.Down)]
    [InlineData(InputKey.Home)]
    [InlineData(InputKey.End)]
    [InlineData(InputKey.PageUp)]
    [InlineData(InputKey.PageDown)]
    [InlineData(InputKey.Escape)]
    [InlineData(InputKey.Space)]
    public void Keys_the_list_owns_are_left_to_it(InputKey key)
    {
        var state = new GroupedTreeState();
        var changes = 0;
        state.Changed = () => changes++;
        var rows = state.Flatten(OrgTree());

        foreach (var index in new[] { 0, 2, 7, 11 })
        {
            Press(state, key, index, rows).ShouldBe((false, -1, false));
        }

        changes.ShouldBe(0);
        state.Save().Folded.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(12)]
    [InlineData(500)]
    public void A_cursor_on_no_row_does_nothing(int index)
    {
        var state = new GroupedTreeState();
        var rows = state.Flatten(OrgTree());

        foreach (var key in new[] { InputKey.Left, InputKey.Right, InputKey.Enter })
        {
            Press(state, key, index, rows).ShouldBe((false, -1, false));
        }

        state.Save().Folded.ShouldBeEmpty();
    }

    [Fact]
    public void The_keyboard_and_the_selection_agree_through_a_fold()
    {
        // Left folds the group the cursor is on; the selection stays on it. Right unfolds it again.
        var state = new GroupedTreeState();
        var rows = state.Flatten(OrgTree());
        state.SelectedKey = rows[7].Key;
        state.Reselect(rows).ShouldBe(7);

        Press(state, InputKey.Left, 7, rows).Handled.ShouldBeTrue();
        rows = state.Flatten(OrgTree());
        state.Reselect(rows).ShouldBe(7);

        Press(state, InputKey.Right, 7, rows).Handled.ShouldBeTrue();
        rows = state.Flatten(OrgTree());
        state.Reselect(rows).ShouldBe(7);
        rows.Count.ShouldBe(12);
    }
}
