namespace DIR.Lib;

/// <summary>
/// Which PART of a text run a painter sacrifices when the run does not fit its arranged rect.
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

    /// <summary>
    /// Drop the MIDDLE, ellipsis in the middle: <c>"C:\Users\se…\Program.cs"</c>. For a run whose two
    /// ENDS both carry meaning and whose middle does not -- the case neither <see cref="Start"/> nor
    /// <see cref="End"/> covers.
    /// <para>
    /// A path is the canonical one, and it is why <see cref="Start"/> is not already enough: the root
    /// says WHICH volume or install this is (a Store package lives under <c>WindowsApps</c>, a dev
    /// build under <c>bin\Debug</c>) and the leaf says which file, while the dozen directories
    /// between them are the part a reader skips. Start-trimming keeps the leaf and throws away the
    /// half that identifies the install; end-trimming does the reverse. A diagnostic panel listing
    /// where an app is installed and where it searched for its models needs both ends of every line.
    /// </para>
    /// <para>
    /// A CELL surface honours this exactly as a pixel surface does -- it is a character-count cut like
    /// the other two, not a scale -- so unlike <see cref="Shrink"/> it needs no degradation.
    /// </para>
    /// </summary>
    Middle,

    /// <summary>
    /// Keep every character and scale the run DOWN until it fits — <c>"a long label"</c> a little smaller
    /// rather than <c>"a long lab…"</c>. For a run where every character carries meaning and a smaller
    /// WHOLE beats a larger fragment: a chess move (<c>Nc6xb4</c> cut to <c>Nc6x…</c> has lost the
    /// destination square, which is the part being read), a measurement, a coordinate, a short title
    /// sharing a strip with a control.
    /// <para>
    /// Only a surface that can scale text can honour this. A CELL surface cannot — a character grid has one
    /// size — so it treats Shrink as <see cref="End"/>, the closest thing available to it. That degradation
    /// is deliberate: a tree authored for both surfaces still arranges and paints on both.
    /// </para>
    /// </summary>
    Shrink,

    /// <summary>
    /// Do not fit at all: draw the run whole, at its stated size, and let it overflow its rect.
    /// <para>
    /// The escape hatch, and the pixel painter's behaviour for every run before it learned to fit — so a
    /// label that was deliberately overhanging its box, or one whose neighbours are known to be empty, says
    /// so with this rather than being silently ellipsized. A cell surface cannot overflow (writing past the
    /// rect would corrupt the neighbouring cells), so it hard-clips instead: the same "keep the head, add
    /// nothing" cut, without the ellipsis that would claim something was removed.
    /// </para>
    /// </summary>
    None,
}
