namespace DIR.Lib;

/// <summary>
/// One glyph encoded for Slug text drawing: a single versioned run of 32-bit integers (<see cref="Words"/>)
/// plus the glyph's bounds in em units. A GPU backend uploads <see cref="Words"/> as it is (an integer
/// buffer or texture; a float-texel upload is exact too: every coordinate is at most
/// <see cref="SlugGlyphEncoder.MaxCoordinate"/> (2^22) in magnitude, and the other words, counts and word offsets,
/// stay below 2^24 as long as the blob has fewer than 2^24 words, which a glyph does by orders of magnitude)
/// and a fragment shader reads it as laid out below. A glyph with no outline (a space) is <see cref="IsEmpty"/>: its blob
/// is only the header, and nothing is drawn.
/// </summary>
/// <remarks>
/// <para><b>Units.</b> Every coordinate in the blob is a signed integer in <i>blob units</i>, eight to a
/// font unit (<see cref="SlugGlyphEncoder.UnitsPerFontUnit"/>). Glyph points sit on a quarter font unit
/// grid, so they are even; the extra bit makes the control point of a line, the midpoint of its ends,
/// exact. A blob unit is <c>1 / (8 * UnitsPerEm)</c> of an em, and the y axis points up.</para>
///
/// <para><b>Layout.</b> Everything is a 32-bit word; <c>w[n]</c> is word <c>n</c> of <see cref="Words"/>.
/// Offsets are word offsets from the start of the blob.</para>
/// <code>
/// header, 16 words
///   w[0]   format version (<see cref="FormatVersion"/>); a reader that does not know it must not draw the blob
///   w[1]   units per em of the face
///   w[2]   curve count (quadratic Beziers; a line is a degenerate quadratic)
///   w[3]   hBandCount  horizontal bands, 1..16 (a horizontal band is a strip of rows, crossed by a ray along x)
///   w[4]   vBandCount  vertical bands, 1..16 (a strip of columns, crossed by a ray along y)
///   w[5]   bandOffset  first band header
///   w[6]   indexOffset first curve reference
///   w[7]   curveOffset first curve
///   w[8]   minX   bounds of every curve point, in blob units
///   w[9]   minY
///   w[10]  maxX
///   w[11]  maxY
///   w[12..15] zero, reserved
///
/// band headers, 4 words each, at bandOffset: the hBandCount horizontal bands bottom to top, then the
/// vBandCount vertical bands left to right
///   +0  count      curves in the band
///   +1  descStart  word offset of the band's curve list ordered by DESCENDING max along the ray
///   +2  ascStart   word offset of the band's curve list ordered by ASCENDING min along the ray
///   +3  split      a sample at or past it (along the ray) uses the descending list, before it the ascending one
///
/// curve references, from indexOffset: per band, count words then count words (the two lists above);
/// each is the word offset of a curve
///
/// curves, 6 words each from curveOffset: x1 y1 x2 y2 x3 y3, the quadratic's start, control and end point.
/// A contour is its curves in drawing order, each starting where the last ended, closed.
/// </code>
///
/// <para><b>Bands.</b> The h bands split [minY, maxY] into hBandCount equal strips: band <c>b</c> is
/// <c>[minY + b*h/n, minY + (b+1)*h/n]</c>, closed, with <c>h = maxY - minY</c>. A sample at height y
/// belongs to band <c>clamp(floor((y - minY) * n / h), 0, n - 1)</c>. The v bands are the same along x.
/// A curve is listed in every band whose closed strip its bounds touch, so one that ends exactly on a
/// band edge is in both neighbours. The bounds are those of the QUANTISED points, which are what the
/// shader draws (computing them from the original outline misses coverage where rounding moves a
/// curve across a band edge: HarfBuzz #6132). A curve with no extent across the axis (a horizontal line
/// in the horizontal bands) is not listed there: a ray along it crosses nothing.</para>
///
/// <para><b>Lists.</b> In a horizontal band the ray runs along x, and each list is sorted along x:
/// the ascending list by the smallest x of a curve's three points, the descending list by the largest.
/// A sample left of the split walks the ascending list and stops at the first curve whose smallest x is
/// past the sample's footprint (every later one is too); a sample at or right of the split walks the
/// descending list and stops at the first curve whose largest x is before the footprint. Ties are
/// broken by curve order, so a blob is deterministic. Vertical bands are the same with x and y swapped.
/// The split is chosen from the band's own curves to keep the shorter walk shortest; it is an
/// optimisation only, either list gives the same answer.</para>
///
/// <para><b>Versioning.</b> <see cref="FormatVersion"/> is bumped whenever any of this changes, so a
/// disk cache keeps the blob with its version and drops one <see cref="IsReadable"/> refuses.</para>
/// </remarks>
public readonly struct SlugGlyphBlob
{
    /// <summary>The layout version written into every blob (word 0).</summary>
    public const int FormatVersion = 1;

    /// <summary>Words in the header.</summary>
    public const int HeaderWords = 16;

    /// <summary>Words in one band header.</summary>
    public const int BandHeaderWords = 4;

    /// <summary>Words in one curve.</summary>
    public const int CurveWords = 6;

    /// <summary>The most bands on either axis.</summary>
    public const int MaxBandsPerAxis = 16;

    internal SlugGlyphBlob(int[] words) => Words = words;

    /// <summary>The blob. Empty (default) only for a <c>default</c> struct.</summary>
    public int[] Words { get; }

    /// <summary>The layout version in the blob, 0 for a default struct.</summary>
    public int Version => Words is { Length: >= HeaderWords } ? Words[0] : 0;

    /// <summary>Units per em of the face the glyph came from.</summary>
    public int UnitsPerEm => Header(1);

    /// <summary>Quadratic curves in the blob.</summary>
    public int CurveCount => Header(2);

    /// <summary>Horizontal bands (strips of rows), 1 to <see cref="MaxBandsPerAxis"/>; 0 for an empty glyph.</summary>
    public int HBandCount => Header(3);

    /// <summary>Vertical bands (strips of columns), 1 to <see cref="MaxBandsPerAxis"/>; 0 for an empty glyph.</summary>
    public int VBandCount => Header(4);

    /// <summary>True for a glyph with no outline: draw nothing.</summary>
    public bool IsEmpty => CurveCount == 0;

    /// <summary>Left edge of the glyph's points in em units (y up), from the quantised points.</summary>
    public float MinXEm => ToEm(Header(8));

    /// <summary>Bottom edge of the glyph's points in em units (y up), from the quantised points.</summary>
    public float MinYEm => ToEm(Header(9));

    /// <summary>Right edge of the glyph's points in em units (y up), from the quantised points.</summary>
    public float MaxXEm => ToEm(Header(10));

    /// <summary>Top edge of the glyph's points in em units (y up), from the quantised points.</summary>
    public float MaxYEm => ToEm(Header(11));

    /// <summary>
    /// Whether <paramref name="words"/> is a blob this version of the layout can draw: it carries
    /// <see cref="FormatVersion"/>, and its header, band headers, curve lists and curves all lie inside it, so a
    /// truncated blob is refused. A cache that finds another version, or a short entry, drops it and re-encodes.
    /// The check is structural (offsets and counts); it does not re-verify the curve data itself.
    /// </summary>
    public static bool IsReadable(ReadOnlySpan<int> words)
    {
        if (words.Length < HeaderWords || words[0] != FormatVersion) return false;
        int curves = words[2], bandOffset = words[5], indexOffset = words[6], curveOffset = words[7];
        if (curves < 0 || curveOffset < HeaderWords || curveOffset > words.Length) return false;
        // The curves are the tail of the blob, so a short blob is one whose curves do not fit.
        if ((long)curveOffset + (long)curves * CurveWords != words.Length) return false;
        if (curves == 0) return words[3] == 0 && words[4] == 0;

        int hBands = words[3], vBands = words[4];
        if (hBands is < 1 or > MaxBandsPerAxis || vBands is < 1 or > MaxBandsPerAxis) return false;
        int bands = hBands + vBands;
        if (bandOffset != HeaderWords || indexOffset != bandOffset + bands * BandHeaderWords || indexOffset > curveOffset) return false;
        // Each band's two lists lie between the first curve reference and the first curve.
        for (int b = 0; b < bands; b++)
        {
            int h = bandOffset + b * BandHeaderWords;
            int count = words[h];
            long desc = words[h + 1], asc = words[h + 2];
            if (count < 0 || count > curves) return false;
            if (desc < indexOffset || desc + count > curveOffset || asc < indexOffset || asc + count > curveOffset) return false;
        }
        return true;
    }

    /// <summary>Wraps a blob read back from a cache, or false if <see cref="IsReadable"/> refuses it.</summary>
    public static bool TryFromWords(int[] words, out SlugGlyphBlob blob)
    {
        ArgumentNullException.ThrowIfNull(words);
        bool ok = IsReadable(words);
        blob = ok ? new SlugGlyphBlob(words) : default;
        return ok;
    }

    private int Header(int i) => Words is { Length: >= HeaderWords } ? Words[i] : 0;

    private float ToEm(int blobUnits)
    {
        int em = UnitsPerEm;
        return em == 0 ? 0f : blobUnits / (float)(SlugGlyphEncoder.UnitsPerFontUnit * em);
    }
}
