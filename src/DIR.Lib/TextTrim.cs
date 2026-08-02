namespace DIR.Lib;

/// <summary>
/// Which END of a text run a painter sacrifices when the run does not fit its arranged rect.
/// <para>
/// The choice belongs to the AUTHOR of the run, not to the painter, because only the author knows which
/// half carries the meaning. A label reads left-to-right and keeps its head. A path does not: trimmed at
/// the end, <c>C:\Users\seb\source\repos\so…</c> identifies nothing at all, while
/// <c>…\repos\ftw\Program.cs</c> is the part that was being read. A painter that only knew one rule forced
/// every caller with a path to pre-truncate against a width it had to derive itself — which is exactly the
/// arithmetic the layout engine exists to own.
/// </para>
/// </summary>
public enum TextTrim
{
    /// <summary>
    /// Drop the tail, ellipsis last: <c>"a long lab…"</c>. The default, and right for anything that reads
    /// front-to-back.
    /// </summary>
    End,

    /// <summary>
    /// Drop the head, ellipsis first: <c>"…\ftw\Program.cs"</c>. For paths, URLs, fully-qualified names —
    /// any run whose distinguishing part is at the end.
    /// </summary>
    Start,
}
