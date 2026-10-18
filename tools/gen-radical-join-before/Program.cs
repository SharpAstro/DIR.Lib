// Measures the square root of a base commit (generate.ps1 says which, and why) at every point
// RadicalJoinTests sweeps and writes the result as src/DIR.Lib.Tests/RadicalJoinTests.Before.cs. Built
// against a copy of that commit's sources whose SqrtBox generate.ps1 has patched to record its bar in a
// `_probeBar` field; see GenRadicalJoinBefore.csproj.
//
// Arguments: <fonts dir> <output .cs path> <base commit>.
//
// The point set here and the sweep in RadicalJoinTests must agree; the test's BeforeDataCoversTheSweep
// fails when a sweep point has no row here. Each point records the radical's rendering path, its Height
// and Depth (round-trip floats), and the clear pixel rows between its bar and the radicand, measured as
// RadicalJoinTests.Rendered.ClearRowsUnderBar measures them: below the bar's last row, over the bar's
// columns from the glyph's right edge to the bar's end, down to the first row with any ink.

using System.Globalization;
using System.Reflection;
using System.Text;
using DIR.Lib;
using DIR.Lib.Markdown;
using DIR.Lib.MathLayout;
using LALR.CC.LexicalGrammar;
using SharpAstro.Fonts;

if (args.Length != 3)
{
    Console.Error.WriteLine("usage: GenRadicalJoinBefore <fonts dir> <output .cs path> <base commit>");
    return 2;
}
var baseCommit = args[2];

var inv = CultureInfo.InvariantCulture;
var fonts = new Dictionary<string, string>
{
    ["STIX2Math"] = Path.Combine(args[0], "STIX2Math.otf"),
    ["DejaVuSans"] = Path.Combine(args[0], "DejaVuSans.ttf"),
};

// The sweep: every integer size from 8 to 40 px and, densely, the ranges Console.Lib's shrink loop walks
// continuously (down to two thirds of its 10 px half-block, 12 px sextant and 32 px Sixel sizes), plus four
// large sizes. Sizes are computed in
// double and rounded, exactly as RadicalJoinTests computes them, so the floats compare equal.
string[] sweepScenes = ["sqrt-2", "sqrt-x", "sqrt-paren-frac", "sqrt-nested-frac", "sqrt-frac-a-b"];
float[] sweepSizes =
[
    .. Enumerable.Range(8, 33).Select(i => (float)i)
        .Concat(Enumerable.Range(0, 109).Select(i => (float)Math.Round(6.6 + i * 0.05, 2)))
        .Concat(Enumerable.Range(0, 108).Select(i => (float)Math.Round(21.3 + i * 0.1, 1)))
        .Concat([48f, 64f, 72f, 96f])
        .Distinct(),
];

// The exact-pixel join cases, and the points the review rounds named outside the sweep.
string[] joinScenes = ["sqrt-x2-plus-y2", "sqrt-2", "sqrt-frac-a-b", "quadratic-formula", "index-box-api", "sqrt-paren-frac", "sqrt-nested-frac"];
(string Font, string Scene, float Size)[] named =
[
    ("DejaVuSans", "sqrt-x", 20f), ("DejaVuSans", "sqrt-x", 22.7f), ("DejaVuSans", "sqrt-x", 26f), ("DejaVuSans", "sqrt-x", 28f),
    ("STIX2Math", "sqrt-frac-a-b", 10f), ("STIX2Math", "sqrt-frac-a-b", 11f), ("STIX2Math", "sqrt-frac-a-b", 13f),
    ("STIX2Math", "sqrt-2", 15f), ("STIX2Math", "sqrt-2", 17f), ("STIX2Math", "sqrt-x", 13f), ("STIX2Math", "sqrt-x", 19f),
    ("DejaVuSans", "sqrt-2", 14f), ("DejaVuSans", "sqrt-x", 16f), ("DejaVuSans", "sqrt-x", 17f),
    ("STIX2Math", "index-box-api", 10.85f), ("STIX2Math", "index-box-api", 11.5f), ("STIX2Math", "index-box-api", 12.05f),
];

// Radicals with ink above them in their parent (a fraction's rule) or none but the canvas's edge (a
// superscript), over the sweep's sizes again: the clear rows between the bar's top and what is above it.
string[] contextScenes =
[
    "frac-1-sqrt-x", "frac-1-sqrt-frac-a-b", "x-sup-sqrt-y",
    // Radicands whose ink may start further above their reported height: an accent, a big operator, a
    // stretchy delimiter (review round 1, Q1).
    "frac-1-sqrt-hat-x", "frac-1-sqrt-sum-x", "frac-1-sqrt-paren-x",
];

var points = new List<(string Font, string Scene, float Size)>();
void Add((string, string, float) p)
{
    if (!points.Contains(p)) points.Add(p);
}
foreach (var font in fonts.Keys)
{
    foreach (var scene in sweepScenes)
        foreach (var size in sweepSizes)
            Add((font, scene, size));
    foreach (var scene in joinScenes)
        foreach (var size in new[] { 24f, 32f, 96f })
            Add((font, scene, size));
}
foreach (var p in named) Add(p);
foreach (var font in fonts.Keys)
    foreach (var scene in contextScenes)
        foreach (var size in sweepSizes)
            Add((font, scene, size));
// The radicals with an index, over the sweep's sizes: the clear pixels between the index and the glyph
// (review round 1, Q2). index-box-api is \sqrt[3]{x}; the other two are a letter index over a composite
// radicand and a two-digit index (radical-index).
string[] indexScenes = ["index-box-api", "sqrt-n-x2-plus-1", "sqrt-10-x"];
foreach (var font in fonts.Keys)
    foreach (var scene in indexScenes)
        foreach (var size in sweepSizes)
            Add((font, scene, size));

var bf = BindingFlags.NonPublic | BindingFlags.Instance;
var probeBar = typeof(SqrtBox).GetField("_probeBar", bf)
    ?? throw new InvalidOperationException("SqrtBox has no _probeBar field: run generate.ps1, which patches it in");
var radicalFont = typeof(SqrtBox).GetField("_radicalFont", bf)!;
var radicalScaled = typeof(SqrtBox).GetField("_radicalScaled", bf)!;
var joinedRadical = typeof(SqrtBox).GetField("_joinedRadical", bf)!;
var flagTopRow = typeof(SqrtBox).GetField("_flagTopRow", bf)!;

var rows = new StringBuilder();
foreach (var (font, scene, size) in points)
{
    var style = new BoxStyle(fonts[font], size, new RGBAColor32(0, 0, 0, 255));
    var box = Build(scene, style);
    var image = BoxRasterizer.RenderToRgba(box, style);
    var sqrt = FindSqrt(box);
    var path = radicalFont.GetValue(sqrt) is not null ? 1
        : ((GlyphBitmap)radicalScaled.GetValue(sqrt)!).Rgba is not null ? 2 : 3;
    var (barTop, barBottom, barStart, barEnd) = ((int, int, int, int))probeBar.GetValue(sqrt)!;
    barEnd = Math.Max(barStart, barEnd);
    var y = barBottom;
    while (y < image.Height && !RowHasInk(image, y, barStart, barEnd)) y++;
    // The clear rows above the bar's top row, to the next ink or the canvas's edge.
    var above = barTop - 1;
    while (above >= 0 && !RowHasInk(image, above, barStart, barEnd)) above--;
    var indexClear = "null";
    if (indexScenes.Contains(scene))
    {
        // The glyph's bitmap rectangle as Draw placed it: its right edge is where the bar starts, its top the
        // bar's top less the flag's top row.
        var glyph = (GlyphBitmap)joinedRadical.GetValue(sqrt)!;
        var glyphY = barTop - (int)flagTopRow.GetValue(sqrt)!;
        var blank = BoxRasterizer.RenderToRgba(IndexScene(scene, style, blankIndex: true), style);
        indexClear = IndexClearance(image, blank, barStart - glyph.Width, glyphY, glyph.Width, glyph.Height).ToString(inv);
    }
    rows.Append(CultureInfo.InvariantCulture,
        $"        new(\"{font}\", \"{scene}\", {F(size)}, {path}, {F(sqrt.Height)}, {F(sqrt.Depth)}, {y - barBottom}, {barTop - 1 - above}, {CutOff(box, image, style)}, {indexClear}),\n");
}

var output = $$"""
// <auto-generated>
// Generated by tools/gen-radical-join-before (run generate.ps1 there). Do not edit by hand.
// </auto-generated>

namespace DIR.Lib.Tests;

public sealed partial class RadicalJoinTests
{
    /// <summary>
    /// The radical as commit {{baseCommit}} drew it, at every point the sweep and the named
    /// cases visit: which path drew it, its <see cref="MathLayout.Box.Height"/> and
    /// <see cref="MathLayout.Box.Depth"/>, the clear pixel rows between its bar and the radicand,
    /// measured as <see cref="Rendered.ClearRowsUnderBar"/> does, those between the bar and the ink above
    /// it, measured as <see cref="Rendered.ClearRowsAboveBar"/> does, and the alpha the canvas
    /// <see cref="MathLayout.BoxRasterizer.RenderToRgba"/> drew the scene on cut off, measured as
    /// <see cref="InkCutOff"/> does, and for the index scene the clear pixels between the index's ink and the
    /// glyph's, measured as <see cref="IndexClearance"/> does. Floats are printed round-trip, so they are exact.
    /// {{points.Count}} points.
    /// </summary>
    private static readonly BeforePoint[] BeforePoints =
    [
{{rows.ToString().TrimEnd('\n')}}
    ];

    private readonly record struct BeforePoint(
        string Font, string Scene, float Size, int Path, float Height, float Depth, int ClearRows, int ClearAbove,
        long CutOff, int? IndexClear);
}

""";
File.WriteAllText(args[1], output.ReplaceLineEndings("\n"));
Console.WriteLine($"{points.Count} points -> {args[1]}");
return 0;

string F(float v) => v.ToString("R", inv) + "f";

// The alpha the canvas RenderToRgba made cut off: the scene drawn again at the same coordinates on a canvas
// 8 px larger on every side, through an integer content translation so every position is computed as
// before, less the ink on the canvas. RadicalJoinTests.InkCutOff measures the branch the same way.
static long CutOff(Box box, RgbaImage canvas, BoxStyle style)
{
    const int pad = 8;
    var margin = (canvas.Width - (int)MathF.Ceiling(box.Width)) / 2;
    using var renderer = new RgbaImageRenderer((uint)(canvas.Width + 2 * pad), (uint)(canvas.Height + 2 * pad));
    renderer.ContentTransform = new ContentTransform(Rotation90.None, 1f, pad, pad);
    box.Draw(renderer, margin, margin + box.Height, style);
    return Ink(renderer.Surface) - Ink(canvas);
}

// An index scene, or the same with its index blank: a box with the index's metrics that draws nothing, so
// the layout is the same and the two renders differ by the index's ink alone. The parsed scenes are built
// as the grammar builds \sqrt[n]{x}: the index parsed at script-script style.
static Box IndexScene(string scene, BoxStyle style, bool blankIndex)
{
    var ss = style.Smaller().Smaller();
    var (radicand, index) = scene switch
    {
        "index-box-api" => ((Box)new MathGlyphBox("x", style, MathStyle.Italic), (Box)new GlyphBox("3", ss)),
        "sqrt-n-x2-plus-1" => (Parse("x^2+1", style), Parse("n", ss)),
        "sqrt-10-x" => (Parse("x", style), Parse("10", ss)),
        _ => throw new ArgumentException($"'{scene}' is not an index scene"),
    };
    return new SqrtBox(radicand, blankIndex ? new BlankBox(index) : index, style);
}

// The fewest clear pixels between the index's ink (where the render with it has more alpha than the one
// without) and the glyph's (ink inside the glyph's bitmap rectangle, in the render without the index), by
// chessboard distance: 0 where they touch, even at a corner, -1 where they overlap. RadicalJoinTests'
// IndexClearance measures the branch the same way.
static int IndexClearance(RgbaImage withIndex, RgbaImage withoutIndex, int gx, int gy, int gw, int gh)
{
    int w = withIndex.Width, h = withIndex.Height;
    byte A(RgbaImage im, int i) => im.Pixels[i * 4 + 3];
    var dist = new int[w * h];
    Array.Fill(dist, int.MaxValue);
    var queue = new Queue<int>();
    for (var y = Math.Max(0, gy); y < Math.Min(h, gy + gh); y++)
        for (var x = Math.Max(0, gx); x < Math.Min(w, gx + gw); x++)
            if (A(withoutIndex, y * w + x) > 0) { dist[y * w + x] = 0; queue.Enqueue(y * w + x); }
    while (queue.TryDequeue(out var i))
    {
        int cx = i % w, cy = i / w;
        for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                int nx = cx + dx, ny = cy + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h || dist[ny * w + nx] != int.MaxValue) continue;
                dist[ny * w + nx] = dist[i] + 1;
                queue.Enqueue(ny * w + nx);
            }
    }
    var best = int.MaxValue;
    for (var i = 0; i < w * h; i++)
        if (A(withIndex, i) > A(withoutIndex, i)) best = Math.Min(best, dist[i]);
    return best == int.MaxValue ? int.MaxValue : best - 1;
}

static long Ink(RgbaImage image)
{
    long sum = 0;
    for (var i = 3; i < image.Pixels.Length; i += 4) sum += image.Pixels[i];
    return sum;
}

static bool RowHasInk(RgbaImage image, int y, int x0, int x1)
{
    for (var x = Math.Max(0, x0); x < Math.Min(image.Width, x1); x++)
        if (image.Pixels[(y * image.Width + x) * 4 + 3] > 0) return true;
    return false;
}

static Box Build(string scene, BoxStyle style) => scene switch
{
    "sqrt-x2-plus-y2" => Parse(@"\sqrt{x^2+y^2}", style),
    "sqrt-2" => Parse(@"\sqrt{2}", style),
    "sqrt-x" => Parse(@"\sqrt{x}", style),
    "sqrt-frac-a-b" => Parse(@"\sqrt{\frac{a}{b}}", style),
    "quadratic-formula" => Parse(@"\frac{-b \pm \sqrt{b^2-4ac}}{2a}", style),
    "index-box-api" or "sqrt-n-x2-plus-1" or "sqrt-10-x" => IndexScene(scene, style, blankIndex: false),
    "sqrt-paren-frac" => Parse(@"\sqrt{\left(\frac{a}{b}\right)}", style),
    "sqrt-nested-frac" => Parse(@"\sqrt{\frac{\frac{a}{b}}{\frac{c}{d}}}", style),
    "frac-1-sqrt-x" => Parse(@"\frac{1}{\sqrt{x}}", style),
    "frac-1-sqrt-frac-a-b" => Parse(@"\frac{1}{\sqrt{\frac{a}{b}}}", style),
    "x-sup-sqrt-y" => Parse(@"x^{\sqrt{y}}", style),
    "frac-1-sqrt-hat-x" => Parse(@"\frac{1}{\sqrt{\hat{x}}}", style),
    "frac-1-sqrt-sum-x" => Parse(@"\frac{1}{\sqrt{\sum x}}", style),
    "frac-1-sqrt-paren-x" => Parse(@"\frac{1}{\sqrt{\left(x\right)}}", style),
    _ => throw new ArgumentException($"unknown scene '{scene}'"),
};

static Box Parse(string latex, BoxStyle style)
{
    var src = MarkdownMacros.ResolveBackslashEscapes(MarkdownMacros.NormalizeLatexAliases(latex));
    var parser = Latex.BuildParser(new BoxBuildingVisitor(style));
    using var lexer = BytesLexer.FromString(src, MarkdownMacros.MathLexerTable);
    using var tokens = new SyncLATokenIterator(lexer);
    var item = parser.ParseInput(tokens, debugger: null);
    if (item.IsError) throw new InvalidOperationException($"'{latex}' did not parse");
    return ((Func<BoxStyle, Box>)item.Content!)(style);
}

static SqrtBox FindSqrt(Box root)
{
    var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
    var pending = new Stack<object>();
    pending.Push(root);
    while (pending.TryPop(out var node))
    {
        if (!seen.Add(node)) continue;
        if (node is SqrtBox sqrt) return sqrt;
        for (var type = node.GetType(); type is not null && type != typeof(object); type = type.BaseType)
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                switch (field.GetValue(node))
                {
                    case Box child: pending.Push(child); break;
                    case System.Collections.IEnumerable items and not string:
                        foreach (var item in items) if (item is Box b) pending.Push(b);
                        break;
                }
    }
    throw new InvalidOperationException("scene has no SqrtBox");
}

/// <summary>A box with another's metrics that draws nothing.</summary>
sealed class BlankBox(Box metrics) : Box
{
    public override float Width => metrics.Width;
    public override float Height => metrics.Height;
    public override float Depth => metrics.Depth;
    public override void Draw(RgbaImageRenderer renderer, float penX, float baselineY, BoxStyle style) { }
}
