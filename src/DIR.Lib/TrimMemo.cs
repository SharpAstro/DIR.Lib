namespace DIR.Lib;

/// <summary>
/// The last few answers of <see cref="TextFit.TrimToWidth{TSurface}"/>, per thread, so a run cut the same way frame
/// after frame (a tab title, a card's detail line) is neither searched nor allocated again.
/// </summary>
/// <remarks>
/// <para>Keyed by the text's REFERENCE, not its content: a painted label is the same string instance from one frame
/// to the next while it does not change, and comparing by reference costs nothing where hashing a long run every
/// frame would cost what the memo saves. A new string with the same content is a miss, and is then remembered.</para>
/// <para>The answer depends on the faces as well as the size and the width; those are the font path and the
/// resolver, both compared by reference too (a resolver's faces never change once it is built). A renderer that
/// measures differently from another for the same face (a different scale) is not part of the key, which is safe
/// only because every surface on one thread measures a face alike, as the render thread's do.</para>
/// <para>Sixteen entries replaced round-robin: enough for a strip of tabs and a panel's trimmed lines, and a miss
/// costs only what every frame cost before.</para>
/// </remarks>
internal static class TrimMemo
{
    private const int Capacity = 16;

    private struct Entry
    {
        public string Text;
        public string FontPath;
        public FontFallbackResolver? Fallback;
        public float FontSize;
        public float MaxWidth;
        public TextTrim Trim;
        public string Result;
    }

    [ThreadStatic] private static Entry[]? t_entries;
    [ThreadStatic] private static int t_next;

    public static bool TryGet(string text, string fontPath, FontFallbackResolver? fallback, float fontSize,
        float maxWidth, TextTrim trim, out string result)
    {
        if (t_entries is { } entries)
        {
            for (var i = 0; i < entries.Length; i++)
            {
                ref var e = ref entries[i];
                if (ReferenceEquals(e.Text, text) && ReferenceEquals(e.FontPath, fontPath)
                    && ReferenceEquals(e.Fallback, fallback) && e.FontSize == fontSize && e.MaxWidth == maxWidth
                    && e.Trim == trim)
                {
                    result = e.Result;
                    return true;
                }
            }
        }

        result = "";
        return false;
    }

    public static void Store(string text, string fontPath, FontFallbackResolver? fallback, float fontSize,
        float maxWidth, TextTrim trim, string result)
    {
        var entries = t_entries ??= new Entry[Capacity];
        entries[t_next] = new Entry
        {
            Text = text, FontPath = fontPath, Fallback = fallback, FontSize = fontSize, MaxWidth = maxWidth,
            Trim = trim, Result = result,
        };
        t_next = (t_next + 1) % Capacity;
    }
}
