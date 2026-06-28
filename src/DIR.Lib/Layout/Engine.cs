using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;

namespace DIR.Lib.Layout;

/// <summary>
/// The single surface-specific input to layout: turn a piece of text or a design-unit scalar into a
/// concrete extent in the surface's coordinate units <typeparamref name="T"/>. Pixels supply glyph
/// metrics (<see cref="Renderer{TSurface}.MeasureText"/>) and a <c>x dpiScale</c> mapping; a terminal
/// supplies char-count and a cell mapping. Arrange and the tree are otherwise fully shared.
/// </summary>
public interface IMeasureContext<T> where T : INumber<T>
{
    /// <summary>Intrinsic size of a text run (glyph metrics for pixels, char count x 1 for cells).</summary>
    Size<T> MeasureText(ReadOnlySpan<char> text, float fontSize);

    /// <summary>Map a design-unit scalar (the pixel-base values on <see cref="Sizing"/>/<see cref="Node.Padding"/>)
    /// into surface units: pixels x DPI, or rounded character cells.</summary>
    T ToSurface(float designUnits);
}

/// <summary>
/// Two-pass measure/arrange engine over a declarative <see cref="Node"/> tree. Generic over the
/// coordinate numeric <typeparamref name="T"/> (<c>float</c> pixels / <c>int</c> cells) reusing
/// <see cref="Rect{T}"/> + <see cref="DockLayout{T}"/>. <see cref="Arrange{T}"/> returns a flat pre-order
/// list of <see cref="ArrangedNode{T}"/> that a per-surface painter walks to draw + auto-bind clicks.
/// </summary>
public static class Engine
{
    /// <summary>
    /// Measures a node's intrinsic (content-driven) size against <paramref name="available"/>. Exposed for
    /// tests and Auto-sizing callers; <see cref="Arrange{T}"/> uses it internally for <c>Auto</c> children.
    /// </summary>
    public static Size<T> Measure<T>(Node node, Size<T> available, IMeasureContext<T> ctx)
        where T : INumber<T>
    {
        var intrinsic = node switch
        {
            Node.Leaf leaf => MeasureContent(leaf.Content, ctx),
            Node.Stack stack => MeasureStack(stack, available, ctx),
            Node.Grid grid => MeasureGrid(grid, available, ctx),
            Node.Overlay overlay => Union(
                Measure(overlay.Base, available, ctx),
                Measure(overlay.Top, available, ctx)),
            // A Dock or Split fills its bounds; both expect explicit bounds rather than shrink-to-content.
            Node.Dock => available,
            Node.Split => available,
            _ => Size<T>.Zero,
        };

        // Inner padding grows the intrinsic box (except a Dock / Split, which are already "fill").
        if (node is not Node.Dock and not Node.Split && node.Padding != 0f)
        {
            var pad2 = ctx.ToSurface(node.Padding) + ctx.ToSurface(node.Padding);
            intrinsic = new Size<T>(intrinsic.Width + pad2, intrinsic.Height + pad2);
        }

        // Explicit Fixed sizing on the node overrides the intrinsic extent on that axis.
        var w = node.Width.IsFixed ? ctx.ToSurface(node.Width.Value) : intrinsic.Width;
        var h = node.Height.IsFixed ? ctx.ToSurface(node.Height.Value) : intrinsic.Height;
        return new Size<T>(w, h);
    }

    /// <summary>
    /// Arranges <paramref name="root"/> into <paramref name="bounds"/>, returning every node placed at an
    /// absolute rect, in pre-order (parent before children; Overlay base-subtree before top-subtree) so a
    /// painter drawing in list order gets correct z-stacking.
    /// </summary>
    public static ImmutableArray<ArrangedNode<T>> Arrange<T>(Node root, Rect<T> bounds, IMeasureContext<T> ctx)
        where T : INumber<T>
    {
        var builder = ImmutableArray.CreateBuilder<ArrangedNode<T>>();
        ArrangeNode(root, bounds, ctx, builder, 0);
        return builder.ToImmutable();
    }

    private static void ArrangeNode<T>(Node node, Rect<T> rect, IMeasureContext<T> ctx,
        ImmutableArray<ArrangedNode<T>>.Builder output, int depth) where T : INumber<T>
    {
        output.Add(new ArrangedNode<T>(node, rect) { Depth = depth });

        var inner = Inset(rect, ctx.ToSurface(node.Padding));
        var childDepth = depth + 1;
        switch (node)
        {
            case Node.Leaf:
                break;
            case Node.Stack stack:
                ArrangeStack(stack, inner, ctx, output, childDepth);
                break;
            case Node.Dock dock:
                ArrangeDock(dock, inner, ctx, output, childDepth);
                break;
            case Node.Grid grid:
                ArrangeGrid(grid, inner, ctx, output, childDepth);
                break;
            case Node.Overlay overlay:
                ArrangeNode(overlay.Base, inner, ctx, output, childDepth); // base first
                ArrangeNode(overlay.Top, inner, ctx, output, childDepth);  // top on top
                break;
            case Node.Split split:
                ArrangeSplit(split, inner, ctx, output, childDepth);
                break;
        }
    }

    private static void ArrangeSplit<T>(Node.Split split, Rect<T> inner, IMeasureContext<T> ctx,
        ImmutableArray<ArrangedNode<T>>.Builder output, int depth) where T : INumber<T>
    {
        var horizontal = split.Axis == Axis.Horizontal;
        var mainAvail = horizontal ? inner.Width : inner.Height;
        var divider = ctx.ToSurface(split.DividerThickness);

        // FirstExtent is consumer-owned; clamp so the divider + both panes always fit the bounds.
        var maxFirst = Max(T.Zero, mainAvail - divider);
        var first = Min(Max(ctx.ToSurface(split.FirstExtent), T.Zero), maxFirst);
        var second = Max(T.Zero, mainAvail - divider - first);

        Rect<T> firstRect, dividerRect, secondRect;
        if (horizontal)
        {
            firstRect = new Rect<T>(inner.X, inner.Y, first, inner.Height);
            dividerRect = new Rect<T>(inner.X + first, inner.Y, divider, inner.Height);
            secondRect = new Rect<T>(inner.X + first + divider, inner.Y, second, inner.Height);
        }
        else
        {
            firstRect = new Rect<T>(inner.X, inner.Y, inner.Width, first);
            dividerRect = new Rect<T>(inner.X, inner.Y + first, inner.Width, divider);
            secondRect = new Rect<T>(inner.X, inner.Y + first + divider, inner.Width, second);
        }

        ArrangeNode(split.First, firstRect, ctx, output, depth);

        // Synthesize the divider as its own draw==hit node: the painter fills DividerColor and binds
        // DividerHit to the SAME arranged rect, so the grab region cannot drift from the drawn bar.
        // Emitted only when there is something to draw or hit (otherwise the divider is a pure gap).
        if (split.DividerColor is not null || split.DividerHit is not null)
        {
            var dividerNode = new Node.Leaf(new Content.Box(0f, 0f))
            {
                Background = split.DividerColor,
                Hit = split.DividerHit,
            };
            output.Add(new ArrangedNode<T>(dividerNode, dividerRect) { Depth = depth });
        }

        ArrangeNode(split.Second, secondRect, ctx, output, depth);
    }

    private static void ArrangeStack<T>(Node.Stack stack, Rect<T> inner, IMeasureContext<T> ctx,
        ImmutableArray<ArrangedNode<T>>.Builder output, int depth) where T : INumber<T>
    {
        var children = stack.Children;
        var n = children.Length;
        if (n == 0)
        {
            return;
        }

        var axis = stack.Axis;
        var gap = ctx.ToSurface(stack.Gap);
        var availSize = new Size<T>(inner.Width, inner.Height);
        var mainAvail = MainOf(axis, availSize);
        var crossAvail = CrossOf(axis, availSize);

        // Pass 1: resolve Fixed + Auto main-axis sizes; defer Star to the leftover split.
        var mains = new T[n];
        var starWeights = new List<float>();
        var starIndices = new List<int>();
        var usedMain = T.Zero;
        for (var i = 0; i < n; i++)
        {
            var sizing = MainSizing(axis, children[i]);
            if (sizing.IsStar)
            {
                starIndices.Add(i);
                starWeights.Add(sizing.Value);
                continue;
            }

            var size = sizing.IsFixed
                ? ctx.ToSurface(sizing.Value)
                : MainOf(axis, Measure(children[i], availSize, ctx));
            mains[i] = size;
            usedMain += size;
        }

        // Pass 2: split the leftover (after Fixed/Auto mains + gaps) among Star children by weight.
        if (starIndices.Count > 0)
        {
            var gaps = gap * T.CreateChecked(Math.Max(0, n - 1));
            var leftover = Max(T.Zero, mainAvail - usedMain - gaps);
            var shares = DistributeByWeight(leftover, starWeights);
            for (var s = 0; s < starIndices.Count; s++)
            {
                mains[starIndices[s]] = shares[s];
            }
        }

        // Pass 3: resolve cross-axis size + position each child sequentially along the main axis.
        var cursor = axis == Axis.Vertical ? inner.Y : inner.X;
        for (var i = 0; i < n; i++)
        {
            var crossSizing = CrossSizing(axis, children[i]);
            var cross = crossSizing.Kind switch
            {
                SizeKind.Fixed => ctx.ToSurface(crossSizing.Value),
                SizeKind.Star => crossAvail,                                   // stretch to fill cross axis
                _ => Min(crossAvail, CrossOf(axis, Measure(children[i], availSize, ctx))),
            };

            var childRect = axis == Axis.Vertical
                ? new Rect<T>(inner.X, cursor, cross, mains[i])
                : new Rect<T>(cursor, inner.Y, mains[i], cross);
            ArrangeNode(children[i], childRect, ctx, output, depth);

            cursor += mains[i] + gap;
        }
    }

    private static void ArrangeDock<T>(Node.Dock dock, Rect<T> inner, IMeasureContext<T> ctx,
        ImmutableArray<ArrangedNode<T>>.Builder output, int depth) where T : INumber<T>
    {
        var layout = new DockLayout<T>(inner);
        foreach (var dc in dock.Docked)
        {
            var remaining = layout.Fill();
            var alongAxis = dc.Side is DockSide.Top or DockSide.Bottom; // vertical strip => measure height

            T size;
            if (dc.Size.IsFixed)
            {
                size = ctx.ToSurface(dc.Size.Value);
            }
            else
            {
                // Auto (or Star, which a dock strip treats as Auto): measure along the dock axis.
                var measured = Measure(dc.Child, new Size<T>(remaining.Width, remaining.Height), ctx);
                size = alongAxis ? measured.Height : measured.Width;
            }

            var r = layout.Dock(ToDockStyle(dc.Side), size);
            ArrangeNode(dc.Child, r, ctx, output, depth);
        }

        ArrangeNode(dock.Fill, layout.Fill(), ctx, output, depth);
    }

    private static void ArrangeGrid<T>(Node.Grid grid, Rect<T> inner, IMeasureContext<T> ctx,
        ImmutableArray<ArrangedNode<T>>.Builder output, int depth) where T : INumber<T>
    {
        var columns = grid.Columns;
        var cells = grid.Cells;
        if (columns <= 0 || cells.Length == 0)
        {
            return;
        }

        var rows = (cells.Length + columns - 1) / columns;
        var colGap = ctx.ToSurface(grid.ColumnGap);
        var rowGap = ctx.ToSurface(grid.RowGap);
        var totalColGap = colGap * T.CreateChecked(Math.Max(0, columns - 1));
        var totalRowGap = rowGap * T.CreateChecked(Math.Max(0, rows - 1));

        // Even column/row split, with remainder distributed so cell rects exactly tile the inner rect.
        var colWidths = DistributeByWeight(Max(T.Zero, inner.Width - totalColGap), EqualWeights(columns));
        var rowHeights = DistributeByWeight(Max(T.Zero, inner.Height - totalRowGap), EqualWeights(rows));

        for (var idx = 0; idx < cells.Length; idx++)
        {
            var col = idx % columns;
            var row = idx / columns;

            var x = inner.X;
            for (var k = 0; k < col; k++)
            {
                x += colWidths[k] + colGap;
            }

            var y = inner.Y;
            for (var k = 0; k < row; k++)
            {
                y += rowHeights[k] + rowGap;
            }

            ArrangeNode(cells[idx], new Rect<T>(x, y, colWidths[col], rowHeights[row]), ctx, output, depth);
        }
    }

    // --- Measure helpers ---

    private static Size<T> MeasureContent<T>(Content content, IMeasureContext<T> ctx) where T : INumber<T>
        => content switch
        {
            Content.Text text => ctx.MeasureText(text.Value.AsSpan(), text.FontSize),
            Content.Box box => new Size<T>(ctx.ToSurface(box.Width), ctx.ToSurface(box.Height)),
            Content.Fill fill => new Size<T>(ctx.ToSurface(fill.MinWidth), ctx.ToSurface(fill.MinHeight)),
            _ => Size<T>.Zero,
        };

    private static Size<T> MeasureStack<T>(Node.Stack stack, Size<T> available, IMeasureContext<T> ctx)
        where T : INumber<T>
    {
        var axis = stack.Axis;
        var n = stack.Children.Length;
        var main = T.Zero;
        var cross = T.Zero;
        for (var i = 0; i < n; i++)
        {
            var child = stack.Children[i];
            var size = Measure(child, available, ctx);
            // Star main contributes nothing to the natural size — it only claims leftover at arrange time.
            if (!MainSizing(axis, child).IsStar)
            {
                main += MainOf(axis, size);
            }

            cross = Max(cross, CrossOf(axis, size));
        }

        main += ctx.ToSurface(stack.Gap) * T.CreateChecked(Math.Max(0, n - 1));
        return Compose(axis, main, cross);
    }

    private static Size<T> MeasureGrid<T>(Node.Grid grid, Size<T> available, IMeasureContext<T> ctx)
        where T : INumber<T>
    {
        var columns = grid.Columns;
        if (columns <= 0 || grid.Cells.Length == 0)
        {
            return Size<T>.Zero;
        }

        var rows = (grid.Cells.Length + columns - 1) / columns;
        var maxW = T.Zero;
        var maxH = T.Zero;
        foreach (var cell in grid.Cells)
        {
            var size = Measure(cell, available, ctx);
            maxW = Max(maxW, size.Width);
            maxH = Max(maxH, size.Height);
        }

        var w = maxW * T.CreateChecked(columns) + ctx.ToSurface(grid.ColumnGap) * T.CreateChecked(Math.Max(0, columns - 1));
        var h = maxH * T.CreateChecked(rows) + ctx.ToSurface(grid.RowGap) * T.CreateChecked(Math.Max(0, rows - 1));
        return new Size<T>(w, h);
    }

    // --- Star / even split, exact for both fractional (float) and integral (int) coordinates ---

    /// <summary>
    /// Splits <paramref name="total"/> among <paramref name="weights"/>. Uses cumulative-target rounding so
    /// the parts sum <i>exactly</i> to <paramref name="total"/>: for fractional <typeparamref name="T"/> the
    /// split is exact; for integral <typeparamref name="T"/> the remainder lands deterministically on the
    /// later items (no cells lost or gained).
    /// </summary>
    private static T[] DistributeByWeight<T>(T total, IReadOnlyList<float> weights) where T : INumber<T>
    {
        var count = weights.Count;
        var result = new T[count];
        if (count == 0)
        {
            return result;
        }

        double weightSum = 0;
        for (var i = 0; i < count; i++)
        {
            weightSum += weights[i];
        }

        if (weightSum <= 0)
        {
            return result; // all zero
        }

        var fractional = IsFractional<T>();
        var totalD = double.CreateChecked(total);
        var cumulativeWeight = 0d;
        var assigned = T.Zero;
        for (var i = 0; i < count; i++)
        {
            cumulativeWeight += weights[i];
            var targetD = totalD * cumulativeWeight / weightSum;
            var target = T.CreateChecked(fractional ? targetD : Math.Floor(targetD));
            result[i] = target - assigned;
            assigned = target;
        }

        return result;
    }

    private static float[] EqualWeights(int n)
    {
        var w = new float[n];
        for (var i = 0; i < n; i++)
        {
            w[i] = 1f;
        }

        return w;
    }

    /// <summary>True when <typeparamref name="T"/> can represent sub-unit values (float/double) vs. integral (int).</summary>
    private static bool IsFractional<T>() where T : INumber<T> => T.One / (T.One + T.One) > T.Zero;

    // --- Axis + geometry helpers ---

    private static Sizing MainSizing(Axis axis, Node node)
        => axis == Axis.Vertical ? node.Height : node.Width;

    private static Sizing CrossSizing(Axis axis, Node node)
        => axis == Axis.Vertical ? node.Width : node.Height;

    private static T MainOf<T>(Axis axis, Size<T> size) where T : INumber<T>
        => axis == Axis.Vertical ? size.Height : size.Width;

    private static T CrossOf<T>(Axis axis, Size<T> size) where T : INumber<T>
        => axis == Axis.Vertical ? size.Width : size.Height;

    private static Size<T> Compose<T>(Axis axis, T main, T cross) where T : INumber<T>
        => axis == Axis.Vertical ? new Size<T>(cross, main) : new Size<T>(main, cross);

    private static Size<T> Union<T>(Size<T> a, Size<T> b) where T : INumber<T>
        => new(Max(a.Width, b.Width), Max(a.Height, b.Height));

    private static Rect<T> Inset<T>(Rect<T> r, T padding) where T : INumber<T>
        => padding == T.Zero
            ? r
            : new Rect<T>(r.X + padding, r.Y + padding,
                Max(T.Zero, r.Width - padding - padding), Max(T.Zero, r.Height - padding - padding));

    private static T Max<T>(T a, T b) where T : INumber<T> => a > b ? a : b;

    private static T Min<T>(T a, T b) where T : INumber<T> => a < b ? a : b;

    private static DockStyle ToDockStyle(DockSide side) => side switch
    {
        DockSide.Top => DockStyle.Top,
        DockSide.Bottom => DockStyle.Bottom,
        DockSide.Left => DockStyle.Left,
        _ => DockStyle.Right,
    };
}
