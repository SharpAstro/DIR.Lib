using System;
using System.Collections.Immutable;

namespace DIR.Lib;

/// <summary>
/// A widget's arranged tree, kept from one frame to the next for as long as what it was built from stands.
/// </summary>
/// <remarks>
/// <para>
/// A widget's tree is built, measured and arranged in the frame that paints it, and most frames paint a widget whose
/// inputs have not changed: a status bar, a palette or a hint bar is the same tree for every frame of a pan and every
/// move of a hover. Building them again was most of what a frame allocated in the apps that traced it (TianWen's sky
/// atlas, about 300 KB a frame; the Drawboard PDF viewer, about 4.5 MB a second while panning), and each collection
/// that buys is a pause inside a paint. The painting itself is not kept: a kept tree is painted every frame
/// (<see cref="PixelWidgetBase{TSurface}"/>'s <c>PaintLayout</c>), which is what registers its regions and resolves
/// its hover. <see cref="TabBar"/> keeps its strip the same way.
/// </para>
/// <para>
/// <b>The key is everything the build reads.</b> A value the build reads that is not in the key is a widget that shows
/// the old value until something else changes. The bounds, the scale, the font and the theme generation are asked for
/// by name so that none of them can be left out; the rest is the widget's own <typeparamref name="TKey"/>. A handler in
/// a kept tree runs frames after it was made, so it reads the state when it runs and captures no value computed at
/// build time that is not in the key.
/// </para>
/// <para>
/// The theme generation is the caller's: a number its colour theme bumps whenever any colour changes, so a theme switch
/// rebuilds every kept tree without each key listing the colours.
/// </para>
/// </remarks>
/// <typeparam name="TKey">What the widget's build reads, apart from what <see cref="TryGet"/> asks for by name.</typeparam>
public struct KeptLayout<TKey> where TKey : struct, IEquatable<TKey>
{
    private TKey _key;
    private RectF32 _bounds;
    private DesignScale _scale;
    private string? _fontPath;
    private int _themeGeneration;
    private ImmutableArray<Layout.ArrangedNode<float>> _arranged;
    private bool _kept;

    /// <summary>
    /// How many times the tree was built. The observable for "a frame that changes nothing builds nothing": the kept
    /// frame is the identical picture, so only a count tells it from one that built the tree again.
    /// </summary>
    public int Builds { get; private set; }

    /// <summary>The tree kept for these inputs, when there is one.</summary>
    public readonly bool TryGet(in TKey key, RectF32 bounds, DesignScale scale, string? fontPath, int themeGeneration,
        out ImmutableArray<Layout.ArrangedNode<float>> arranged)
    {
        arranged = _arranged;
        return _kept
            && _themeGeneration == themeGeneration
            && _bounds == bounds
            && _scale == scale
            && string.Equals(_fontPath, fontPath, StringComparison.Ordinal)
            && _key.Equals(key);
    }

    /// <summary>Keeps <paramref name="arranged"/> as the tree for these inputs, and hands it back.</summary>
    public ImmutableArray<Layout.ArrangedNode<float>> Keep(in TKey key, RectF32 bounds, DesignScale scale, string? fontPath,
        int themeGeneration, ImmutableArray<Layout.ArrangedNode<float>> arranged)
    {
        _key = key;
        _bounds = bounds;
        _scale = scale;
        _fontPath = fontPath;
        _themeGeneration = themeGeneration;
        _arranged = arranged;
        _kept = true;
        Builds++;
        return arranged;
    }

    /// <summary>Drops the kept tree: the next frame builds.</summary>
    public void Forget() => _kept = false;
}
