using System.Collections.Immutable;
using System.Text;

namespace DIR.Lib;

/// <summary>
/// Turns a glyph into horizontal runs of constant coverage, so it can be drawn as rectangles instead
/// of as text.
/// </summary>
/// <remarks>
/// <para><b>Why a mark would be baked rather than drawn from the font.</b> Three things it buys, each of
/// which the runtime text path actually costs. It works where the face is not installed: an app that
/// bundles no emoji font resolves none on a typical Linux host, and a missing glyph draws NOTHING rather
/// than a placeholder, so a button's only mark silently disappears. It is MONOCHROME, so it takes the ink
/// colour and dims with the label beside it, which a COLRv1 colour glyph carrying its own palette
/// structurally cannot. And it is identical everywhere, where the text path varies with whichever face
/// the host happened to resolve.</para>
/// <para><b>Both a build-time and a runtime API, deliberately.</b> A tool bakes a fixed set into generated
/// constants, which is right for an app's own icons. But the same logic has to be callable at RUNTIME for
/// the case a bake cannot serve: an app whose theme turns the whole UI one colour (a night / dark-adaptation
/// mode) wants its normally-full-colour emoji rendered as tintable coverage, and which emoji those are is
/// not known until the app draws them. So the logic lives here and the tool is a thin wrapper over it.
/// Runtime callers should cache per (codepoint, size) -- rasterising is not free, and a mask is immutable
/// and safe to share.</para>
/// <para><b>Runs, not a bitmap.</b> A run is one horizontal span, so drawing is a loop of rectangle fills
/// and needs no new primitive on a renderer -- the same reason <see cref="IconKind"/>'s pixel painter is
/// built from rectangles. It also stays tiny: a 20px mark is around a hundred runs.</para>
/// <para><b>Reproducible.</b> <see cref="ManagedFontRasterizer"/> is pure managed (no FreeType, no
/// DirectWrite), so given the same font file the same inputs produce byte-identical output on any host.
/// That is what lets a build pipeline VERIFY a generated file rather than trust it.</para>
/// </remarks>
public static class IconBaker
{
    /// <summary>The default number of coverage buckets.</summary>
    /// <remarks>
    /// One bucket is a hard 1-bit threshold, which reads chunky against antialiased text beside it. Four
    /// keeps an edge smooth enough at icon size and costs only a few more runs, since the interior of a
    /// mark is one bucket however many there are.
    /// </remarks>
    public const int DefaultAlphaLevels = 4;

    /// <summary>How many times <see cref="Bake"/> may shrink its request to fit the mask square.</summary>
    /// <remarks>
    /// A backstop on the fit loop, not a tuning dial: each pass multiplies the request by the overflow
    /// ratio, so it decreases monotonically and converges in one or two passes for a real face. The cap
    /// only bounds a pathological one.
    /// </remarks>
    private const int MaxFitAttempts = 8;

    /// <summary>One horizontal span of constant coverage, in mask pixels.</summary>
    public readonly record struct CoverageRun(byte X, byte Y, byte Width, byte Alpha);

    /// <summary>A glyph's coverage at one size, as runs over a <see cref="Size"/>-square grid.</summary>
    public readonly record struct CoverageMask(int Size, ImmutableArray<CoverageRun> Runs)
    {
        /// <summary>Whether anything was covered. False when the face does not carry the codepoint.</summary>
        public bool IsEmpty => Runs.IsDefaultOrEmpty;
    }

    /// <summary>
    /// Bakes one glyph at one size, or an empty mask when the face does not cover it.
    /// </summary>
    /// <remarks>
    /// An empty result is a normal answer, not an error: coverage is a property of the face, and a caller
    /// that has a fallback (a drawn mark, another face) needs to be told rather than thrown at. Check
    /// <see cref="CoverageMask.IsEmpty"/>.
    /// </remarks>
    /// <param name="rasterizer">Shared rasteriser; caches faces internally, so reuse one.</param>
    /// <param name="fontPath">The face to take the glyph from.</param>
    /// <param name="codepoint">The glyph to bake.</param>
    /// <param name="size">Edge of the square mask, in pixels.</param>
    /// <param name="alphaLevels">Coverage buckets, 1..8. See <see cref="DefaultAlphaLevels"/>.</param>
    public static CoverageMask Bake(ManagedFontRasterizer rasterizer, string fontPath, Rune codepoint,
        int size, int alphaLevels = DefaultAlphaLevels)
    {
        ArgumentNullException.ThrowIfNull(rasterizer);
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(alphaLevels, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(alphaLevels, 8);

        var bmp = rasterizer.RasterizeGlyph(fontPath, size, codepoint);
        if (bmp.Width <= 0 || bmp.Height <= 0 || bmp.Rgba.Length == 0)
        {
            return new CoverageMask(size, ImmutableArray<CoverageRun>.Empty);
        }

        // Shrink the request until the ink FITS, rather than centring an oversized bitmap and dropping
        // whatever falls outside. The size is an em size and a glyph's ink may exceed its em box -- every
        // emoji in Noto's COLRv1 face does, by about 15% -- so the centred-only form silently shaved one
        // to two pixels off each edge of EVERY mark. That is unreadable as a fault on a mark whose
        // extremes are thin (a sparkle loses a tip nobody can name) and obvious on one bounded by a
        // curve: a circle acquires a flat top and bottom, which is how a baked globe gave it away.
        var request = (float)size;
        for (var attempt = 0; attempt < MaxFitAttempts && (bmp.Width > size || bmp.Height > size); attempt++)
        {
            // Scale by the worse-overflowing axis so both land inside, uniformly so the aspect holds.
            // Re-measure rather than trust the arithmetic: ink is rounded to whole pixels, so a request
            // that should just fit can still come back a pixel over.
            request *= (float)size / Math.Max(bmp.Width, bmp.Height);
            var fitted = rasterizer.RasterizeGlyph(fontPath, request, codepoint);
            if (fitted.Width <= 0 || fitted.Height <= 0 || fitted.Rgba.Length == 0)
            {
                // Keep the last raster that had ink; the clamps below still bound what gets emitted.
                break;
            }
            bmp = fitted;
        }

        // Centre the glyph's INK in the square. The rasteriser reports an ink box plus bearings, and
        // bearing-relative placement puts each glyph where its own font metrics say -- correct for text,
        // wrong for an icon, where a row of unrelated marks has to look aligned to each other.
        var offX = (size - bmp.Width) / 2;
        var offY = (size - bmp.Height) / 2;
        var runs = ImmutableArray.CreateBuilder<CoverageRun>();

        for (var row = 0; row < bmp.Height; row++)
        {
            // Backstop only: the fit loop leaves both offsets non-negative, so this drops nothing for a
            // face that converges. It stays because a font defeating the cap must still yield an in-bounds
            // mask -- CoverageRun packs X/Y/Width into bytes over a size-square grid.
            var y = offY + row;
            if (y is < 0 || y >= size)
            {
                continue;
            }

            var runStart = -1;
            var runLevel = 0;

            // One past the width, so a run touching the right edge is closed by the same branch as any
            // other rather than needing a copy of the emit after the loop.
            for (var col = 0; col <= bmp.Width; col++)
            {
                var level = 0;
                var x = offX + col;
                if (col < bmp.Width && x >= 0 && x < size)
                {
                    // Alpha is the silhouette whether the face is outline or colour: a COLRv1 emoji
                    // rasterises its paint tree to RGBA, and coverage is exactly what is wanted from it.
                    level = Quantise(bmp.Rgba[((row * bmp.Width) + col) * 4 + 3], alphaLevels);
                }

                if (level != runLevel)
                {
                    if (runLevel > 0 && runStart >= 0)
                    {
                        runs.Add(new CoverageRun(
                            (byte)runStart, (byte)y, (byte)(offX + col - runStart), BucketAlpha(runLevel, alphaLevels)));
                    }
                    runStart = offX + col;
                    runLevel = level;
                }
            }
        }

        return new CoverageMask(size, runs.ToImmutable());
    }

    /// <summary>Bakes one glyph at several sizes.</summary>
    /// <remarks>
    /// Per size rather than one master scaled at draw time, because a run is a row of PIXELS: scaling the
    /// rows either overlaps them or opens gaps between them, and both show at icon size. Pick with
    /// <see cref="NearestSize"/>.
    /// </remarks>
    public static ImmutableArray<CoverageMask> Bake(ManagedFontRasterizer rasterizer, string fontPath,
        Rune codepoint, ReadOnlySpan<int> sizes, int alphaLevels = DefaultAlphaLevels)
    {
        var masks = ImmutableArray.CreateBuilder<CoverageMask>(sizes.Length);
        foreach (var size in sizes)
        {
            masks.Add(Bake(rasterizer, fontPath, codepoint, size, alphaLevels));
        }
        return masks.ToImmutable();
    }

    /// <summary>
    /// Coverage to a bucket in <c>1..alphaLevels</c>, with 0 meaning uncovered.
    /// </summary>
    /// <remarks>
    /// ANY non-zero coverage lands in at least bucket 1, which the obvious
    /// <c>alpha * alphaLevels / 256</c> does not do, and its two failures are why this is a named method.
    /// It maps to <c>0..alphaLevels-1</c>, so the faintest bucket of every antialiased edge is discarded
    /// as "uncovered" -- the mark comes out slightly thin, invisibly. And at <c>alphaLevels = 1</c> EVERY
    /// pixel lands in bucket 0, so the whole mark disappears, which is how it was found: a test asserting
    /// that one level is a hard threshold got an empty mask.
    /// </remarks>
    private static int Quantise(byte alpha, int alphaLevels)
        => alpha == 0 ? 0 : Math.Min(alphaLevels, (alpha * alphaLevels / 256) + 1);

    /// <summary>
    /// A bucket mapped back to an 8-bit alpha.
    /// </summary>
    /// <remarks>
    /// The TOP bucket is 255, and that is not cosmetic: an interior pixel is FULLY covered, and a
    /// bucket-centre mapping leaves it at 223 for four levels, which makes the whole mark read visibly
    /// greyer than the text and the drawn marks beside it. Measured rather than judged by eye -- peak
    /// brightness of a baked mark against a drawn one had to match.
    /// </remarks>
    private static byte BucketAlpha(int level, int alphaLevels)
        => (byte)Math.Min(255, level * 255 / alphaLevels);

    /// <summary>The mask whose size is closest to <paramref name="pixels"/>.</summary>
    /// <remarks>
    /// Nearest rather than exact: a DPI scale is continuous, so an exact match would need a bake per
    /// possible scale. The caller scales the runs, so picking the closest is what keeps that scale near 1.
    /// </remarks>
    public static CoverageMask NearestSize(ImmutableArray<CoverageMask> masks, float pixels)
    {
        var best = masks[0];
        var bestDelta = MathF.Abs(best.Size - pixels);
        foreach (var mask in masks)
        {
            var delta = MathF.Abs(mask.Size - pixels);
            if (delta < bestDelta)
            {
                best = mask;
                bestDelta = delta;
            }
        }
        return best;
    }
}
