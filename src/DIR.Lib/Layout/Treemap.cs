using System;
using System.Collections.Generic;

namespace DIR.Lib.Layout;

/// <summary>
/// The squarified treemap layout (Bruls, Huizing and van Wijk, 2000): weights split into strips so that
/// every cell is as close to square as the weights allow, which is what makes a small cell readable at all.
/// </summary>
/// <remarks>
/// <para>
/// <b>It lays out STRIPS, not rectangles.</b> <see cref="Builder.Treemap"/> turns the strips into nested
/// <see cref="Sizing.Star"/>-weighted stacks, so the engine computes every rect at arrange time, at the
/// real size and scale, and each cell is a node: drawn and hit through the same rect, hoverable, visible
/// to <c>describe_layout</c>. The squareness is decided here against an aspect ratio, and only the
/// squareness: the proportions are the star weights', exact at any size. A treemap built against last
/// frame's aspect is therefore never WRONG on a resize, only a little less square for one frame.
/// </para>
/// </remarks>
public static class Treemap
{
    /// <summary>One strip: <see cref="Count"/> cells starting at <see cref="Start"/> in weight order, laid
    /// across the remaining rect's shorter side.</summary>
    /// <param name="Start">The first cell, as an index into the weight-descending order.</param>
    /// <param name="Count">How many cells the strip holds.</param>
    /// <param name="Vertical">True when the strip is a column at the left of the remaining rect (its cells
    /// stacked top to bottom); false for a row along the top (cells left to right).</param>
    public readonly record struct Strip(int Start, int Count, bool Vertical);

    /// <summary>
    /// Splits <paramref name="weights"/>, which must be in DESCENDING order and positive, into strips for a
    /// rect <paramref name="aspect"/> times as wide as it is tall.
    /// </summary>
    public static List<Strip> Squarify(ReadOnlySpan<double> weights, float aspect)
    {
        var strips = new List<Strip>();
        if (weights.IsEmpty) return strips;

        double total = 0;
        foreach (var w in weights) total += w;
        if (total <= 0) return strips;

        // Work in a rect of area 1·aspect: the weights are scaled to areas once, here.
        double width = aspect > 0 && float.IsFinite(aspect) ? aspect : 1.0;
        double height = 1.0;
        var scale = width * height / total;

        var start = 0;
        while (start < weights.Length)
        {
            var vertical = width >= height;          // lay the strip along the SHORTER side
            var side = Math.Min(width, height);

            var count = 1;
            var sum = weights[start] * scale;
            var worst = Worst(weights.Slice(start, 1), sum, side, scale);
            while (start + count < weights.Length)
            {
                var nextSum = sum + weights[start + count] * scale;
                var nextWorst = Worst(weights.Slice(start, count + 1), nextSum, side, scale);
                if (nextWorst > worst) break;        // adding it makes the strip's cells less square
                count++;
                sum = nextSum;
                worst = nextWorst;
            }

            strips.Add(new Strip(start, count, vertical));
            var thickness = sum / side;
            if (vertical) width = Math.Max(0, width - thickness);
            else height = Math.Max(0, height - thickness);
            start += count;
        }

        return strips;
    }

    // The worst aspect ratio among the cells of a strip of total area `sum` laid along `side`.
    private static double Worst(ReadOnlySpan<double> weights, double sum, double side, double scale)
    {
        double max = 0, min = double.MaxValue;
        foreach (var w in weights)
        {
            var a = w * scale;
            if (a > max) max = a;
            if (a < min) min = a;
        }

        var s2 = side * side;
        var sum2 = sum * sum;
        return Math.Max(s2 * max / sum2, sum2 / (s2 * min));
    }
}
