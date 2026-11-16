using System.Globalization;
using System.Text;
using DIR.Lib;
using SharpAstro.Svg;

// A thin wrapper over DIR.Lib's IconBaker: it owns the baking, this owns the file format. Everything
// about WHICH glyphs is an argument, so nothing app-specific lives here, which is why the tool ships from
// the library's own repo rather than from a consumer -- a second app with icons to bake takes it off the
// feed (`dnx DIR.Lib.IconBaker`) instead of vendoring a copy of the generator.
//
//   bake-icons --out <file.g.cs> --namespace <ns>
//              [--font <path> --glyph Name=U+1F300 [--glyph ...]]
//              [--svg Name=path.svg [--svg ...]] [--scenes <dir>]
//              [--class BakedIcons] [--sizes 13,16,20] [--levels 4] [--access internal]
//
// A mark comes from a font glyph or from an SVG file, and the two can be mixed in one output: both bake
// to the same runs. An SVG is read and rasterised HERE, at build time; the generated file holds no SVG, so
// the app that uses it parses and rasterises nothing.
//
// --scenes <dir> also writes each SVG as a scene file (<dir>/Name.savs), the drawing with the reading done. That
// is for a drawing an app wants in COLOUR or at sizes it cannot list: VectorIcon.Load takes the file and reads no
// SVG. The baked masks are for the one-colour marks of a fixed size ladder.
//
// Emits DATA ONLY, typed as DIR.Lib.IconBaker.CoverageMask. An earlier version re-emitted the run/mask
// types and the size picker into every generated file, which is a copy of a library type per consumer.
//
// Hand-rolled argument parsing so the tool's only dependency is the rasteriser and it can be packed
// without dragging a CLI framework along.

var fontPath = "";
var outputPath = "";
var ns = "";
var className = "BakedIcons";
var scenesDir = "";
var access = "internal";
// The size ladder is DPI SCALES, not round numbers: a mark is BaseToolbarMarkSize (13 design units)
// times the scale, so these are 13 at 1.0, 1.25, 1.5, 1.75, 2.0, 2.5 and 3.0. Chosen that way because
// the caller scales the nearest bake, and a ladder of pretty numbers leaves gaps exactly where common
// hosts sit -- 13/16/20/26/39 forced 1.75x to resample by 14% and 2.5x by 25%, while this keeps every
// common scale within about 2%. Each extra size is ~200 runs, so closing the gaps is nearly free.
int[] sizes = [13, 16, 20, 23, 26, 33, 39];
var levels = IconBaker.DefaultAlphaLevels;
var icons = new List<(string Name, int Codepoint, string? SvgPath)>();

for (var i = 0; i < args.Length; i++)
{
    var next = i + 1 < args.Length ? args[i + 1] : null;
    switch (args[i])
    {
        case "--font" when next is not null: fontPath = next; i++; break;
        case "--out" when next is not null: outputPath = next; i++; break;
        case "--namespace" when next is not null: ns = next; i++; break;
        case "--class" when next is not null: className = next; i++; break;
        case "--access" when next is not null: access = next; i++; break;
        case "--levels" when next is not null:
            levels = int.Parse(next, CultureInfo.InvariantCulture);
            i++;
            break;
        case "--sizes" when next is not null:
            sizes = [.. next.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.Parse(s, CultureInfo.InvariantCulture))];
            i++;
            break;
        case "--glyph" when next is not null:
            {
                var parts = next.Split('=', 2);
                if (parts.Length != 2)
                {
                    Console.Error.WriteLine($"bake-icons: --glyph wants Name=U+XXXX, got '{next}'");
                    return 2;
                }
                // U+XXXX, 0xXXXX and bare hex all mean the same. A bare DECIMAL codepoint is deliberately
                // not accepted: it would be ambiguous with hex and silently bake the wrong glyph.
                var raw = parts[1].Trim();
                if (raw.StartsWith("U+", StringComparison.OrdinalIgnoreCase)
                    || raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    raw = raw[2..];
                }
                if (!int.TryParse(raw, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var cp))
                {
                    Console.Error.WriteLine($"bake-icons: '{parts[1]}' is not a hex codepoint");
                    return 2;
                }
                icons.Add((parts[0].Trim(), cp, null));
                i++;
                break;
            }
        case "--scenes" when next is not null: scenesDir = next; i++; break;
        case "--svg" when next is not null:
            {
                var parts = next.Split('=', 2);
                if (parts.Length != 2 || parts[0].Trim().Length == 0 || parts[1].Trim().Length == 0)
                {
                    Console.Error.WriteLine($"bake-icons: --svg wants Name=path.svg, got '{next}'");
                    return 2;
                }
                icons.Add((parts[0].Trim(), 0, parts[1].Trim()));
                i++;
                break;
            }
        default:
            Console.Error.WriteLine($"bake-icons: unknown argument '{args[i]}'");
            return 2;
    }
}

var glyphs = icons.Any(icon => icon.SvgPath is null);
if (outputPath.Length == 0 || ns.Length == 0 || icons.Count == 0 || (glyphs && fontPath.Length == 0))
{
    Console.Error.WriteLine(
        "usage: bake-icons --out <file.g.cs> --namespace <ns> [--font <path> --glyph Name=U+XXXX ...] [--svg Name=path.svg ...]");
    return 2;
}

if (glyphs && !File.Exists(fontPath))
{
    Console.Error.WriteLine($"bake-icons: source font not found: {fontPath}");
    return 2;
}

foreach (var (name, _, svgPath) in icons)
{
    if (svgPath is not null && !File.Exists(svgPath))
    {
        Console.Error.WriteLine($"bake-icons: {name}: SVG not found: {svgPath}");
        return 2;
    }
}

if (icons.Any(icon => icon.SvgPath is not null) && sizes.Any(size => size > IconBaker.MaxSceneSize))
{
    Console.Error.WriteLine($"bake-icons: an SVG is baked at {IconBaker.MaxSceneSize}px at most.");
    return 2;
}

// LICENSING cannot be checked here, so it is stated where whoever adds a glyph will read it: baking
// redistributes GLYPH SHAPES. Point this at a face whose licence permits that (Noto is OFL). A
// proprietary system face is not a valid source, and a runtime emoji probe legitimately falling back to
// one does not make its outlines redistributable.

using var rasterizer = new ManagedFontRasterizer();
var sb = new StringBuilder();

sb.AppendLine("// <auto-generated>");
sb.AppendLine("//     Generated by bake-icons. Do not edit by hand: re-run the tool and commit.");
if (glyphs)
{
    sb.AppendLine($"//     Source font: {Path.GetFileName(fontPath)}");
}
sb.AppendLine("// </auto-generated>");
sb.AppendLine("#nullable enable");
sb.AppendLine();
sb.AppendLine("using System.Collections.Immutable;");
sb.AppendLine("using DIR.Lib;");
sb.AppendLine();
sb.AppendLine($"namespace {ns}");
sb.AppendLine("{");
sb.AppendLine("    /// <summary>Coverage baked at build time, drawn via PixelWidgetBase.DrawCoverageMask.</summary>");
sb.AppendLine("    /// <remarks>");
sb.AppendLine("    /// Baked because a mark drawn from a font at runtime is absent where the face is not installed,");
sb.AppendLine("    /// cannot be tinted when the face is colour, and varies with whichever face the host resolved.");
sb.AppendLine("    /// See <see cref=\"IconBaker\"/>, which also bakes at RUNTIME for marks not known ahead of time.");
sb.AppendLine("    /// </remarks>");
sb.AppendLine($"    {access} static class {className}");
sb.AppendLine("    {");

var total = 0;

foreach (var (name, codepoint, svgPath) in icons)
{
    System.Collections.Immutable.ImmutableArray<IconBaker.CoverageMask> masks;
    string source;
    if (svgPath is not null)
    {
        // Read once, here. What the reader could not take from the file is said on stderr and is not fatal:
        // the mask is baked from what it did read, and whoever runs this decides whether that is the icon.
        SvgDocument document;
        try
        {
            document = SvgReader.Read(File.ReadAllBytes(svgPath));
        }
        catch (InvalidDataException e)
        {
            Console.Error.WriteLine($"bake-icons: {name}: {Path.GetFileName(svgPath)} is not an SVG this tool reads: {e.Message}");
            return 1;
        }
        foreach (var issue in document.Issues)
        {
            Console.Error.WriteLine($"bake-icons: {name}: {Path.GetFileName(svgPath)}: {issue.Kind}: {issue.What} (x{issue.Count})");
        }
        masks = IconBaker.Bake(document.Scene, sizes, levels);
        source = Path.GetFileName(svgPath);
        if (scenesDir.Length > 0)
        {
            Directory.CreateDirectory(scenesDir);
            var scenePath = Path.Combine(scenesDir, name + ".savs");
            var sceneBytes = SharpAstro.VectorGraphics.VectorSceneFile.Write(document.Scene);
            File.WriteAllBytes(scenePath, sceneBytes);
            Console.WriteLine($"bake-icons: {name} -> {scenePath} ({sceneBytes.Length} bytes)");
        }
        if (masks.Any(m => m.IsEmpty))
        {
            Console.Error.WriteLine($"bake-icons: {name} ({source}) produced NO coverage. Nothing in it is drawn.");
            return 1;
        }
    }
    else
    {
        masks = IconBaker.Bake(rasterizer, fontPath, new System.Text.Rune(codepoint), sizes, levels);
        source = $"U+{codepoint:X4}";
    }

    if (svgPath is null && masks.Any(m => m.IsEmpty))
    {
        var missing = string.Join(", ", masks.Where(m => m.IsEmpty).Select(m => $"{m.Size}px"));
        Console.Error.WriteLine(
            $"bake-icons: {name} (U+{codepoint:X4}) produced NO coverage at {missing} from " +
            $"{Path.GetFileName(fontPath)}. The face probably does not carry it.");
        return 1;
    }

    sb.AppendLine(svgPath is null
        ? $"        /// <summary>U+{codepoint:X4}, baked from {Path.GetFileName(fontPath)}.</summary>"
        : $"        /// <summary>Baked from {source}.</summary>");
    sb.AppendLine($"        {access} static readonly ImmutableArray<IconBaker.CoverageMask> {name} =");
    sb.AppendLine("        [");
    foreach (var mask in masks)
    {
        sb.Append(CultureInfo.InvariantCulture, $"            new IconBaker.CoverageMask({mask.Size}, [");
        for (var i = 0; i < mask.Runs.Length; i++)
        {
            var r = mask.Runs[i];
            sb.Append(CultureInfo.InvariantCulture, $"new({r.X},{r.Y},{r.Width},{r.Alpha})");
            if (i < mask.Runs.Length - 1)
            {
                sb.Append(", ");
            }
        }
        sb.AppendLine("]),");
        total += mask.Runs.Length;
    }
    sb.AppendLine("        ];");
    sb.AppendLine();

    Console.WriteLine($"bake-icons: {name} {source} -> " +
        string.Join(", ", masks.Select(m => $"{m.Size}px:{m.Runs.Length}")));
}

sb.AppendLine("    }");
sb.AppendLine("}");

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
File.WriteAllText(outputPath, sb.ToString().ReplaceLineEndings("\r\n"));

Console.WriteLine($"bake-icons: wrote {outputPath} ({total} runs)");
return 0;
