namespace DIR.Lib;

/// <summary>
/// The string that names one font face throughout DIR.Lib — the value every API calls
/// <c>fontPath</c>, and the key every cache (glyph atlas, SDF atlas, shaper, identity memo) is
/// keyed by.
///
/// <para>For an ordinary .ttf/.otf that string is simply the file path. A collection
/// (.ttc/.otc) holds several faces in one file, so a path alone cannot name a face past the
/// first: the id gains a <c>#index</c> suffix. Because the suffix is part of the id, distinct
/// faces get distinct cache entries with no further plumbing.</para>
///
/// <para><see cref="Create"/> never appends <c>#0</c>, so every id minted for a single-face
/// file is byte-identical to the plain path it always was.</para>
/// </summary>
public static class FontFaceId
{
    /// <summary>Separates the path from the face index. Face indices are small integers.</summary>
    public const char Separator = '#';

    // Face counts in real collections are single digits (Windows' largest ships 3); a cap keeps
    // a path that merely ends in '#' followed by digits from being mistaken for one.
    private const int MaxIndexDigits = 3;

    /// <summary>
    /// The id naming face <paramref name="faceIndex"/> of <paramref name="path"/>. Face 0 —
    /// every non-collection font — yields the path unchanged.
    /// </summary>
    public static string Create(string path, int faceIndex)
        => faceIndex <= 0 ? path : string.Concat(path, Separator, faceIndex.ToString());

    /// <summary>
    /// Split an id into its path and face index. Returns false for an id carrying no face
    /// suffix, in which case <paramref name="path"/> is the id itself and
    /// <paramref name="faceIndex"/> is 0 — so callers can use the outputs unconditionally and
    /// ignore the result.
    /// </summary>
    public static bool TryParse(string id, out string path, out int faceIndex)
    {
        path = id;
        faceIndex = 0;

        var hash = id.LastIndexOf(Separator);
        // Needs a non-empty path before the separator and a plausible index after it.
        var digits = id.Length - hash - 1;
        if (hash <= 0 || digits is 0 or > MaxIndexDigits) return false;

        var index = 0;
        for (var i = hash + 1; i < id.Length; i++)
        {
            var c = id[i];
            if (c < '0' || c > '9') return false;
            index = index * 10 + (c - '0');
        }

        // "path#0" is redundant but legal; normalize it to the bare path so it can't split the
        // cache in two for one face.
        path = id[..hash];
        faceIndex = index;
        return index > 0;
    }
}
