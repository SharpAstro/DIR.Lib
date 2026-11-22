using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Numerics;
using SharpAstro.Fonts.Scenes;
using SharpAstro.Svg;
using SharpAstro.VectorGraphics;

namespace DIR.Lib;

/// <summary>
/// A vector drawing held ready to be drawn at any size: read once, rasterised once per size, and never again.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> An SVG is text. Reading one costs microseconds for an icon and tens of
/// milliseconds for a large drawing, and rasterising it costs more; neither belongs in a frame. This type is the
/// place both are done ONCE: <see cref="FromSvg(byte[], SvgReadOptions?)"/> reads the document into a scene and
/// throws the XML away, and <see cref="Mask"/> and <see cref="Image"/> keep what they produce. Make one per drawing,
/// keep it (a static field, a dictionary by name), and draw from it every frame.</para>
/// <para><b>Two ways to draw it.</b> <see cref="Mask"/> is the drawing's coverage as the same runs a baked glyph
/// is (<see cref="IconBaker.CoverageMask"/>), so it is drawn by <c>DrawCoverageMask</c> in whatever ink the
/// caller chooses: a single-colour icon that dims and tints with the text beside it. <see cref="Image"/> is the
/// drawing in its own colours, for one that has them.</para>
/// <para><b>For an app's own fixed icons, bake instead.</b> The <c>bake-icons</c> tool takes <c>--svg</c> and
/// writes the masks as generated constants, so nothing is read or rasterised at run time at all. This type is
/// for what is not known at build time: a user's file, a theme's assets, a size ladder too long to bake.</para>
/// <para><b>Embedded pictures.</b> An SVG can carry a picture (an <c>image</c> with a <c>data:</c> URI). A PNG is
/// drawn as it is. For every other format hand over a decoder, which is one argument when the app has the
/// <c>SharpAstro.Codecs</c> facade: <c>VectorIcon.FromSvg(svg, ImageCodecs.TryDecode)</c>. DIR.Lib does not reference
/// the facade itself, so that an app which draws no such pictures does not carry every codec. A picture the document
/// only names by an address is never drawn: nothing is fetched.</para>
/// <para><b>Text.</b> An SVG's text names fonts and holds none, so it is drawn with fonts found here: the ones
/// installed on this machine (<see cref="SceneFonts.Installed"/>) unless the icon is given others. For a drawing that
/// must read the same everywhere, hand over the faces to use:
/// <c>VectorIcon.FromSvg(svg, new SceneRenderOptions { ShapeText = SceneFonts.From(path) })</c>. Text is drawn in its
/// own paint and, in <see cref="Mask"/>, counts as coverage like any shape.</para>
/// <para>Safe to use from any thread. Two threads asking for the same size at once may both rasterise it; one result is kept.</para>
/// </remarks>
public sealed class VectorIcon
{
    /// <summary>How many sizes of each kind are kept. A UI asks for a handful (its DPI scales); past this the oldest is dropped.</summary>
    private const int MaxKept = 24;

    private readonly ConcurrentDictionary<(int Size, int Levels), IconBaker.CoverageMask> _masks = new();
    private readonly ConcurrentDictionary<(int Width, int Height), RgbaImage> _images = new();
    private readonly ConcurrentQueue<(int, int)> _maskOrder = new(), _imageOrder = new();

    /// <summary>What the drawing is rasterised with: the decoder for its pictures and the shaper for its text.</summary>
    private readonly SceneRenderOptions _render;

    /// <summary>Wraps a scene that is already at hand.</summary>
    public VectorIcon(VectorScene scene) : this(scene, [], new SceneRenderOptions())
    {
    }

    /// <summary>Wraps a scene that is already at hand, its embedded pictures decoded by <paramref name="decodeImage"/>.</summary>
    /// <param name="decodeImage">Decodes an image file from its bytes: <c>ImageCodecs.TryDecode</c> of <c>SharpAstro.Codecs</c>, say.</param>
    public VectorIcon(VectorScene scene, ImageFileDecoder decodeImage)
        : this(scene, [], new SceneRenderOptions { DecodeImage = SceneRasterizer.DecodeWith(decodeImage) })
    {
    }

    /// <summary>Wraps a scene that is already at hand, drawn with what <paramref name="render"/> gives: a decoder for its pictures, a shaper (and so the fonts) for its text.</summary>
    /// <param name="render">What is not given is the default: an embedded PNG is decoded, text is shaped with <see cref="SceneFonts.Installed"/>.</param>
    public VectorIcon(VectorScene scene, SceneRenderOptions render) : this(scene, [], render)
    {
    }

    private VectorIcon(VectorScene scene, ImmutableArray<SvgIssue> issues, SceneRenderOptions render)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(render);
        Scene = scene;
        Issues = issues;
        // Text is drawn with this machine's fonts unless the caller says with which.
        _render = render.ShapeText is null ? render with { ShapeText = SceneFonts.Installed } : render;
    }

    /// <summary>Reads an SVG document (plain or gzipped) once. The bytes are not kept.</summary>
    /// <exception cref="InvalidDataException">Not an SVG document this reader accepts.</exception>
    public static VectorIcon FromSvg(byte[] svg, SvgReadOptions? options = null)
    {
        SvgDocument document = SvgReader.Read(svg, options);
        return new VectorIcon(document.Scene, document.Issues, new SceneRenderOptions());
    }

    /// <summary>
    /// Reads an SVG document (plain or gzipped) once; it is drawn with what <paramref name="render"/> gives: a decoder
    /// for its pictures, a shaper (and so the fonts) for its text.
    /// </summary>
    /// <param name="svg">The document.</param>
    /// <param name="render">What is not given is the default: an embedded PNG is decoded, text is shaped with <see cref="SceneFonts.Installed"/>.</param>
    /// <param name="options">The reader's options.</param>
    /// <exception cref="InvalidDataException">Not an SVG document this reader accepts.</exception>
    public static VectorIcon FromSvg(byte[] svg, SceneRenderOptions render, SvgReadOptions? options = null)
    {
        SvgDocument document = SvgReader.Read(svg, options);
        return new VectorIcon(document.Scene, document.Issues, render);
    }

    /// <summary>Reads an SVG document from its text, once. See <see cref="FromSvg(byte[], SceneRenderOptions, SvgReadOptions?)"/>.</summary>
    /// <exception cref="InvalidDataException">Not an SVG document this reader accepts.</exception>
    public static VectorIcon FromSvg(string svg, SceneRenderOptions render, SvgReadOptions? options = null)
    {
        SvgDocument document = SvgReader.Parse(svg, options);
        return new VectorIcon(document.Scene, document.Issues, render);
    }

    /// <summary>
    /// Reads an SVG document (plain or gzipped) once; its embedded pictures are decoded by <paramref name="decodeImage"/>
    /// when the drawing is rasterised.
    /// </summary>
    /// <param name="svg">The document.</param>
    /// <param name="decodeImage">Decodes an image file from its bytes: <c>ImageCodecs.TryDecode</c> of <c>SharpAstro.Codecs</c>, say.</param>
    /// <param name="options">The reader's options.</param>
    /// <exception cref="InvalidDataException">Not an SVG document this reader accepts.</exception>
    public static VectorIcon FromSvg(byte[] svg, ImageFileDecoder decodeImage, SvgReadOptions? options = null)
    {
        SvgDocument document = SvgReader.Read(svg, options);
        return new VectorIcon(document.Scene, document.Issues, new SceneRenderOptions { DecodeImage = SceneRasterizer.DecodeWith(decodeImage) });
    }

    /// <summary>Reads an SVG document from its text, once.</summary>
    /// <exception cref="InvalidDataException">Not an SVG document this reader accepts.</exception>
    public static VectorIcon FromSvg(string svg, SvgReadOptions? options = null)
    {
        SvgDocument document = SvgReader.Parse(svg, options);
        return new VectorIcon(document.Scene, document.Issues, new SceneRenderOptions());
    }

    /// <summary>Reads an SVG document from its text, once. See <see cref="FromSvg(byte[], ImageFileDecoder, SvgReadOptions?)"/>.</summary>
    /// <exception cref="InvalidDataException">Not an SVG document this reader accepts.</exception>
    public static VectorIcon FromSvg(string svg, ImageFileDecoder decodeImage, SvgReadOptions? options = null)
    {
        SvgDocument document = SvgReader.Parse(svg, options);
        return new VectorIcon(document.Scene, document.Issues, new SceneRenderOptions { DecodeImage = SceneRasterizer.DecodeWith(decodeImage) });
    }

    /// <summary>
    /// Loads a drawing from either form, told apart by its first bytes: a scene file (<see cref="VectorSceneFile"/>,
    /// what <see cref="ToSceneFile"/> and <c>bake-icons --scenes</c> write), or an SVG document.
    /// </summary>
    /// <remarks>
    /// A scene file is the drawing with the reading already done, and loads 15 to 50 times faster than the SVG is
    /// read. Ship those for drawings an app loads at start-up, and keep SVG for what a user hands it.
    /// </remarks>
    /// <exception cref="InvalidDataException">Neither a scene file nor an SVG document this reader accepts.</exception>
    public static VectorIcon Load(byte[] data, SvgReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        return VectorSceneFile.IsSceneFile(data) ? new VectorIcon(VectorSceneFile.Read(data)) : FromSvg(data, options);
    }

    /// <summary>
    /// Loads a drawing from either form, as <see cref="Load(byte[], SvgReadOptions?)"/> does; its embedded pictures
    /// are decoded by <paramref name="decodeImage"/>. A scene file carries its pictures as the files they were, so
    /// it needs the decoder as much as the SVG does.
    /// </summary>
    /// <exception cref="InvalidDataException">Neither a scene file nor an SVG document this reader accepts.</exception>
    public static VectorIcon Load(byte[] data, ImageFileDecoder decodeImage, SvgReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        return VectorSceneFile.IsSceneFile(data) ? new VectorIcon(VectorSceneFile.Read(data), decodeImage) : FromSvg(data, decodeImage, options);
    }

    /// <summary>
    /// Loads a drawing from either form, as <see cref="Load(byte[], SvgReadOptions?)"/> does, to be drawn with what
    /// <paramref name="render"/> gives. A scene file carries its text as the SVG did, fonts unnamed, so it needs the
    /// fonts as much as the SVG does.
    /// </summary>
    /// <exception cref="InvalidDataException">Neither a scene file nor an SVG document this reader accepts.</exception>
    public static VectorIcon Load(byte[] data, SceneRenderOptions render, SvgReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        return VectorSceneFile.IsSceneFile(data) ? new VectorIcon(VectorSceneFile.Read(data), render) : FromSvg(data, render, options);
    }

    /// <summary>The drawing as a scene file: bytes that <see cref="Load(byte[], SvgReadOptions)"/> turns back into it without reading any SVG.</summary>
    public byte[] ToSceneFile() => VectorSceneFile.Write(Scene);

    /// <summary>The drawing.</summary>
    public VectorScene Scene { get; }

    /// <summary>The drawing's own width, in its units (CSS pixels for an SVG).</summary>
    public float Width => Scene.Width;

    /// <summary>The drawing's own height.</summary>
    public float Height => Scene.Height;

    /// <summary>
    /// What the SVG asked for that is not in the drawing (features not read yet, errors in the document). Empty for a
    /// scene that did not come from <see cref="FromSvg(byte[], SvgReadOptions?)"/>. Worth logging once when an icon is loaded.
    /// </summary>
    public ImmutableArray<SvgIssue> Issues { get; }

    /// <summary>
    /// The drawing's coverage in a <paramref name="size"/>-pixel square, as runs: fitted inside the square with its
    /// proportions kept, and centred. Rasterised the first time a size is asked for and kept.
    /// </summary>
    /// <param name="size">Edge of the square, 1 to <see cref="IconBaker.MaxSceneSize"/> pixels.</param>
    /// <param name="alphaLevels">Coverage buckets, 1..8.</param>
    public IconBaker.CoverageMask Mask(int size, int alphaLevels = IconBaker.DefaultAlphaLevels)
    {
        if (_masks.TryGetValue((size, alphaLevels), out IconBaker.CoverageMask kept)) return kept;
        IconBaker.CoverageMask mask = IconBaker.Bake(Scene, size, alphaLevels, _render);
        if (_masks.TryAdd((size, alphaLevels), mask))
        {
            _maskOrder.Enqueue((size, alphaLevels));
            while (_masks.Count > MaxKept && _maskOrder.TryDequeue(out (int, int) oldest)) _masks.TryRemove(oldest, out _);
        }
        return mask;
    }

    /// <summary>
    /// The drawing in its own colours, stretched to <paramref name="width"/> x <paramref name="height"/> pixels (pass
    /// sizes in the drawing's proportions to keep them). Rasterised the first time a size is asked for and kept;
    /// the image is shared, so do not draw into it.
    /// </summary>
    /// <returns>RGBA, not premultiplied, transparent where the drawing is empty.</returns>
    public RgbaImage Image(int width, int height)
    {
        if (_images.TryGetValue((width, height), out RgbaImage? kept)) return kept;
        // Stretched to fill, which is what the rasteriser's own three-argument form does.
        Matrix3x2 fill = Width > 0 && Height > 0 ? Matrix3x2.CreateScale(width / Width, height / Height) : Matrix3x2.Identity;
        var image = new RgbaImage(width, height, SceneRasterizer.Rasterize(Scene, width, height, fill, _render).Pixels);
        if (_images.TryAdd((width, height), image))
        {
            _imageOrder.Enqueue((width, height));
            while (_images.Count > MaxKept && _imageOrder.TryDequeue(out (int, int) oldest)) _images.TryRemove(oldest, out _);
            return image;
        }
        return _images.TryGetValue((width, height), out kept) ? kept : image;
    }

    /// <summary>
    /// The drawing in its own colours, fitted inside a box with its proportions kept: the largest size that fits
    /// <paramref name="maxWidth"/> x <paramref name="maxHeight"/>.
    /// </summary>
    public RgbaImage ImageFitting(int maxWidth, int maxHeight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxHeight, 1);
        if (!(Width > 0) || !(Height > 0)) return Image(maxWidth, maxHeight);
        float scale = Math.Min(maxWidth / Width, maxHeight / Height);
        return Image(Math.Max(1, (int)MathF.Round(Width * scale)), Math.Max(1, (int)MathF.Round(Height * scale)));
    }
}
