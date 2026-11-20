using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace DIR.Lib.Layout;

/// <summary>What a design-unit extent the engine is about to map stands for. See <see cref="ExtentRequest"/>.</summary>
public enum ExtentKind
{
    /// <summary>A <see cref="SizeKind.Fixed"/> width, height, column, strip or wrap-child extent.</summary>
    Fixed,
    /// <summary>One side of a node's inner padding (<see cref="Node.Padding"/> across, <see cref="Node.PadDown"/> down).</summary>
    Padding,
    /// <summary>One side of a node's <see cref="Node.BorderStyle"/> (its <see cref="BorderStyle.Width"/>), reserved outside the padding.
    /// The default mapping is the surface extent of the width but never less than one surface unit, so a border does not round away;
    /// a cell context answers one cell per declared side.</summary>
    Border,
}

/// <summary>
/// What <see cref="IMeasureContext{T}.MapExtent"/> is asked: one authored design-unit extent, and what the node it
/// belongs to holds, so a surface that cannot represent the number as such (a terminal that rounds to whole cells)
/// can decide with the node in view. The engine builds one per Fixed extent and per padding it resolves, in both
/// measure and arrange, so the two always agree.
/// <para>
/// A record struct with init-only properties, not positional parameters, so a field added later is additive.
/// </para>
/// </summary>
public readonly record struct ExtentRequest
{
    /// <summary>The authored extent in design units.</summary>
    public float Value { get; init; }

    /// <summary>The axis the extent runs along.</summary>
    public Axis Axis { get; init; }

    /// <summary>Whether <see cref="Value"/> is a fixed size or a padding.</summary>
    public ExtentKind Kind { get; init; }

    /// <summary>The node (or a descendant) holds content that paints: a <see cref="Content.Text"/>, <see cref="Content.TextEditor"/>,
    /// <see cref="Content.TextInput"/>, <see cref="Content.Icon"/> or <see cref="Content.Slider"/> leaf.</summary>
    public bool HasContent { get; init; }

    /// <summary>The lines of that content as authored (explicit line breaks, not wrapping; a text input, icon or slider is
    /// one line): vertical stacks add their children's, other containers take the most; 0 when <see cref="HasContent"/>
    /// is false.</summary>
    public int ContentLines { get; init; }

    /// <summary>The node or a descendant carries a <see cref="Node.Background"/> (or is a <see cref="Content.Fill"/>, or a <see cref="Content.Box"/> with a visible colour),
    /// so it paints something even with no content.</summary>
    public bool HasFill { get; init; }
}

/// <summary>Per-node answers behind <see cref="ExtentRequest"/>, computed once per node reference, except for a
/// subtree that holds a text editor, which is computed on every call (see <see cref="ExtentFacts.Of"/>).</summary>
internal sealed class ExtentFacts
{
    private static readonly ConditionalWeakTable<Node, ExtentFacts> Cache = new();

    public bool HasContent { get; private init; }
    public int Lines { get; private init; }
    public bool HasFill { get; private init; }

    /// <summary>True when the facts read mutable caller-owned state: only a text editor's <c>State.Text</c> (its line count)
    /// does. A text input's placeholder is never read here, so a subtree holding one is still cached.</summary>
    private bool ReadsMutable { get; init; }

    /// <summary>Facts of <paramref name="node"/>. A subtree holding a text editor is computed per call, so a keystroke
    /// is seen; any other is cached per node reference.</summary>
    public static ExtentFacts Of(Node node)
    {
        if (Cache.TryGetValue(node, out var known))
        {
            return known;
        }

        var facts = Compute(node);
        return facts.ReadsMutable ? facts : Cache.GetValue(node, _ => facts);
    }

    private static ExtentFacts Compute(Node node)
    {
        var fill = node.Background is not null || node.BorderStyle is { Color.Alpha: > 0, Sides: not BorderSides.None };
        var has = false;
        var lines = 0;
        var mutable = false;
        switch (node)
        {
            case Node.Leaf { Content: Content.Text t }:
                has = true;
                lines = CountLines(t.Value);
                break;
            case Node.Leaf { Content: Content.TextEditor e }:
                has = true;
                lines = CountLines(e.State.Text);
                mutable = true;
                break;
            case Node.Leaf { Content: Content.TextInput or Content.Icon or Content.Slider }:
                // A single-line field, a pictogram and a slider all paint: one line of content each.
                has = true;
                lines = 1;
                break;
            case Node.Leaf { Content: Content.Box { Color.Alpha: > 0 } }:
                fill = true;
                break;
            case Node.Leaf { Content: Content.Fill }:
                fill = true;
                break;
            default:
                var vertical = node is Node.Stack { Axis: Axis.Vertical };
                foreach (var child in ChildrenOf(node))
                {
                    var f = Of(child);
                    fill |= f.HasFill;
                    mutable |= f.ReadsMutable;
                    if (!f.HasContent)
                    {
                        continue;
                    }

                    has = true;
                    lines = vertical ? lines + f.Lines : Math.Max(lines, f.Lines);
                }

                break;
        }

        return new ExtentFacts { HasContent = has, Lines = lines, HasFill = fill, ReadsMutable = mutable };
    }

    private static int CountLines(string s)
    {
        var n = 1;
        foreach (var c in s)
        {
            if (c == '\n')
            {
                n++;
            }
        }

        return n;
    }

    private static ImmutableArray<Node> ChildrenOf(Node node) => node switch
    {
        Node.Stack s => s.Children,
        Node.Wrap w => w.FirstLineTrailing is { } trailing ? w.Children.Add(trailing) : w.Children,
        Node.Grid g => g.Cells,
        Node.Overlay o => [o.Base, o.Top],
        Node.Split sp => [sp.First, sp.Second],
        Node.Dock d => DockChildren(d),
        Node.Anchored a => [a.Child],
        _ => [],
    };

    private static ImmutableArray<Node> DockChildren(Node.Dock d)
    {
        var b = ImmutableArray.CreateBuilder<Node>(d.Docked.Length + 1);
        foreach (var dc in d.Docked)
        {
            b.Add(dc.Child);
        }

        b.Add(d.Fill);
        return b.ToImmutable();
    }
}
