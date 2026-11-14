using System.Collections.Immutable;
using System.Globalization;

namespace DIR.Lib;

/// <summary>The things <see cref="Wireframe.Check"/> looks for.</summary>
[Flags]
public enum WireframeRules
{
    None = 0,

    /// <summary>Two lines of text whose ink shares pixels, with nothing painted between them to cover the first.</summary>
    TextOverText = 1,

    /// <summary>Text that lies partly on a clickable region and partly off it.</summary>
    TextOverRegion = 2,

    /// <summary>Two clickable regions that share pixels, neither inside the other.</summary>
    RegionOverRegion = 4,

    /// <summary>Text or a region that reaches past the surface.</summary>
    OutsideBounds = 8,

    /// <summary>Text a clip cut across its width, which does not end in an ellipsis.</summary>
    CutText = 16,

    All = TextOverText | TextOverRegion | RegionOverRegion | OutsideBounds | CutText,
}

/// <summary>What <see cref="Wireframe.Check"/> judges and what it leaves alone.</summary>
public sealed record WireframeOptions
{
    public WireframeRules Rules { get; init; } = WireframeRules.All;

    /// <summary>How far, in pixels, two things may share an edge before it counts: rounding, and a hairline.</summary>
    public float Tolerance { get; init; } = 1.5f;

    /// <summary>Text no rule applies to: labels on a map or a chart, which lie where the data puts them.</summary>
    public Func<WireframeText, bool>? IgnoreText { get; init; }

    /// <summary>Regions no rule applies to.</summary>
    public Func<WireframeRegion, bool>? IgnoreRegion { get; init; }
}

/// <summary>One thing a frame got wrong, in words, and where.</summary>
public readonly record struct WireframeFinding(WireframeRules Rule, string Message, RectF32 Where)
{
    public override string ToString() => Message;
}

/// <summary>
/// Judges a frame a <see cref="WireframeRenderer"/> recorded. The rules are about what a person would see as
/// broken at a glance, and each is decided in paint order: what an opaque surface was painted over (the page
/// under an open popover) is not on show and collides with nothing.
/// </summary>
public static class Wireframe
{
    public static ImmutableArray<WireframeFinding> Check(WireframeFrame frame, WireframeOptions? options = null)
    {
        options ??= new WireframeOptions();
        var tol = options.Tolerance;
        var findings = ImmutableArray.CreateBuilder<WireframeFinding>();

        var texts = frame.Texts.Where(t => !IsEmpty(t.Visible) && options.IgnoreText?.Invoke(t) != true).ToArray();
        var regions = frame.Regions.Where(r => !IsEmpty(r.Visible) && options.IgnoreRegion?.Invoke(r) != true).ToArray();
        var fills = frame.Fills;

        if (options.Rules.HasFlag(WireframeRules.TextOverText))
        {
            for (var i = 0; i < texts.Length; i++)
            {
                for (var j = i + 1; j < texts.Length; j++)
                {
                    var (a, b) = (texts[i], texts[j]);
                    var shared = Intersect(a.Visible, b.Visible);
                    if (!TextShares(shared, MathF.Min(a.FontSize, b.FontSize), tol)
                        // A shadow or an outline: the same words again, a pixel or two off.
                        || (a.Text == b.Text && MathF.Abs(a.Ink.X - b.Ink.X) <= 2f && MathF.Abs(a.Ink.Y - b.Ink.Y) <= 2f)
                        || Covered(fills, shared, a.Order, b.Order, tol, null, null))
                    {
                        continue;
                    }
                    findings.Add(new WireframeFinding(WireframeRules.TextOverText,
                        $"text {Describe(b)} is drawn over text {Describe(a)}", shared));
                }
            }
        }

        if (options.Rules.HasFlag(WireframeRules.TextOverRegion))
        {
            foreach (var t in texts)
            {
                foreach (var r in regions)
                {
                    var shared = Intersect(t.Visible, r.Visible);
                    if (!TextShares(shared, t.FontSize, tol)
                        // Inside the control, or the control inside the text (a link in a line): not a collision.
                        || !SticksOut(t, r.Visible, tol) || Contains(t.Visible, r.Visible, tol))
                    {
                        continue;
                    }
                    // Hidden where they meet by a surface that is neither's own: a panel painted after the earlier
                    // of the two. The control's own background does not count, since text it was painted across is
                    // text cut by a control.
                    var (from, to) = t.Order < r.Order ? (t.Order, int.MaxValue) : (r.Order, t.Order);
                    if (Covered(fills, shared, from, to, tol, r.Visible, null))
                    {
                        continue;
                    }
                    findings.Add(new WireframeFinding(WireframeRules.TextOverRegion,
                        $"text {Describe(t)} lies partly over the control {Describe(r)}", shared));
                }
            }
        }

        if (options.Rules.HasFlag(WireframeRules.RegionOverRegion))
        {
            for (var i = 0; i < regions.Length; i++)
            {
                for (var j = i + 1; j < regions.Length; j++)
                {
                    var (a, b) = (regions[i], regions[j]);
                    var shared = Intersect(a.Visible, b.Visible);
                    if (shared.Width <= 2f * tol || shared.Height <= 2f * tol
                        || Contains(a.Visible, b.Visible, tol) || Contains(b.Visible, a.Visible, tol)
                        || Covered(fills, shared, a.Order, b.Order, tol, a.Visible, b.Visible))
                    {
                        continue;
                    }
                    findings.Add(new WireframeFinding(WireframeRules.RegionOverRegion,
                        $"the control {Describe(b)} overlaps the control {Describe(a)}", shared));
                }
            }
        }

        if (options.Rules.HasFlag(WireframeRules.OutsideBounds))
        {
            foreach (var t in texts)
            {
                if (!Contains(frame.Bounds, t.Visible, tol))
                {
                    findings.Add(new WireframeFinding(WireframeRules.OutsideBounds,
                        $"text {Describe(t)} reaches past the surface {Describe(frame.Bounds)}", t.Visible));
                }
            }
            foreach (var r in regions)
            {
                if (!Contains(frame.Bounds, r.Visible, tol))
                {
                    findings.Add(new WireframeFinding(WireframeRules.OutsideBounds,
                        $"the control {Describe(r)} reaches past the surface {Describe(frame.Bounds)}", r.Visible));
                }
            }
        }

        if (options.Rules.HasFlag(WireframeRules.CutText))
        {
            foreach (var t in texts)
            {
                if (t.Ink.Width - t.Visible.Width > tol
                    && !t.Text.EndsWith('…') && !t.Text.EndsWith("...", StringComparison.Ordinal))
                {
                    findings.Add(new WireframeFinding(WireframeRules.CutText,
                        $"text {Describe(t)} is cut to {t.Visible.Width.ToString("0.#", CultureInfo.InvariantCulture)} px wide with no ellipsis",
                        t.Visible));
                }
            }
        }

        return findings.ToImmutable();
    }

    /// <summary>The findings as one message for an assertion: one line each, or empty when there are none.</summary>
    public static string Describe(ImmutableArray<WireframeFinding> findings)
        => string.Join("\n", findings.Select(f => f.Message));

    internal static RectF32 Intersect(RectF32 a, RectF32 b)
    {
        var x = MathF.Max(a.X, b.X);
        var y = MathF.Max(a.Y, b.Y);
        var right = MathF.Min(a.Right, b.Right);
        var bottom = MathF.Min(a.Bottom, b.Bottom);
        return right > x && bottom > y ? new RectF32(x, y, right - x, bottom - y) : new RectF32(x, y, 0f, 0f);
    }

    private static bool IsEmpty(RectF32 r) => r.Width <= 0f || r.Height <= 0f;

    // Text shares a place with something when the shared part is wider than the tolerance and taller than a
    // quarter of the type: two rows of a tight table touch by less than that.
    private static bool TextShares(RectF32 shared, float fontSize, float tol)
        => shared.Width > tol && shared.Height > MathF.Max(tol, fontSize * 0.25f);

    private static bool SticksOut(in WireframeText t, RectF32 r, float tol)
    {
        var v = t.Visible;
        var down = MathF.Max(tol, t.FontSize * 0.25f);
        return v.X < r.X - tol || v.Right > r.Right + tol || v.Y < r.Y - down || v.Bottom > r.Bottom + down;
    }

    private static bool Contains(RectF32 outer, RectF32 inner, float tol)
        => inner.X >= outer.X - tol && inner.Y >= outer.Y - tol
            && inner.Right <= outer.Right + tol && inner.Bottom <= outer.Bottom + tol;

    // Whether an opaque fill painted after `after` and before `before` holds the whole shared part. A fill that
    // lies inside `notWithinA` or `notWithinB` is that control's own background and covers nothing of it.
    private static bool Covered(ImmutableArray<WireframeFill> fills, RectF32 shared, int after, int before, float tol,
        RectF32? notWithinA, RectF32? notWithinB)
    {
        foreach (var f in fills)
        {
            if (f.Order > after && f.Order < before && Contains(f.Rect, shared, tol)
                && !(notWithinA is { } a && Contains(a, f.Rect, tol))
                && !(notWithinB is { } b && Contains(b, f.Rect, tol)))
            {
                return true;
            }
        }
        return false;
    }

    private static string Describe(in WireframeText t) => $"\"{t.Text}\" {Describe(t.Visible)}";

    private static string Describe(in WireframeRegion r) => $"{r.Result} {Describe(r.Visible)}";

    private static string Describe(RectF32 r)
        => string.Create(CultureInfo.InvariantCulture, $"[{r.X:0.#},{r.Y:0.#} {r.Width:0.#}x{r.Height:0.#}]");
}
