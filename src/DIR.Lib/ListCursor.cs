using System;

namespace DIR.Lib;

/// <summary>
/// Where the keyboard is inside a list that a layout tree has already declared — the position the
/// arrow keys move and Enter acts on.
/// </summary>
/// <remarks>
/// <para>A list is declared by its rows, not by a model beside them: a row that says
/// <c>.Clickable(new HitResult.ListItemHit("views", i), …)</c> is row <c>i</c> of the list
/// <c>views</c>, and that one statement is enough to click it, to navigate to it, and — with
/// <see cref="Layout.Node.FocusBackground"/> — to light it up. Nothing else has to be kept in step.
/// </para>
/// <para>
/// <b>Reachability comes for free, and that is the point.</b> A row a reader cannot act on is simply
/// not clickable, so it registers no region and the cursor steps over it. A widget that tracked its
/// own highlight index instead has to answer "can this row be acted on" a second time, beside the
/// list, and a wrong answer there is invisible: the card looks drawn and stops responding.
/// </para>
/// <para>
/// The cursor holds no rows. It is a list id and an index, resolved against the regions the last
/// paint registered (<c>PixelWidgetBase.MoveListCursor</c>), which is the same list a click is tested
/// against — so what the arrows reach and what the mouse reaches cannot differ.
/// </para>
/// </remarks>
public sealed class ListCursor
{
    /// <summary>The list the cursor is in, or null when it is in none.</summary>
    public string? ListId { get; private set; }

    /// <summary>
    /// The row within <see cref="ListId"/>, or -1 for a list the reader has not yet moved in.
    /// </summary>
    /// <remarks>
    /// -1 is a real state and not an error: it paints nothing, so a card that has just opened carries
    /// no highlight the reader did not ask for. The first arrow lands on the first row, and Enter
    /// before any arrow acts on the first row too — so a list is usable from the keyboard without
    /// arriving pre-lit.
    /// </remarks>
    public int Index { get; private set; } = -1;

    /// <summary>Whether the cursor is in a list at all.</summary>
    public bool IsOpen => ListId is not null;

    /// <summary>
    /// Puts the cursor in <paramref name="listId"/>, at <paramref name="index"/> when the caller has
    /// a row to start on — normally whatever the list is currently showing, so the arrows step off
    /// something the reader can see rather than from the top of a list of thirty.
    /// </summary>
    public void Open(string listId, int index = -1)
    {
        ListId = listId;
        Index = index;
    }

    /// <summary>Takes the cursor out of any list.</summary>
    public void Close()
    {
        ListId = null;
        Index = -1;
    }

    /// <summary>Moves the cursor to <paramref name="index"/> of the list it is already in.</summary>
    public void MoveTo(int index) => Index = index;

    /// <summary>Whether <paramref name="hit"/> is the row the cursor is on.</summary>
    public bool IsOn(HitResult? hit)
        => Index >= 0 && ListId is { } list
            && hit is HitResult.ListItemHit item
            && item.Index == Index
            && string.Equals(item.ListId, list, StringComparison.Ordinal);

    /// <summary>Whether <paramref name="hit"/> belongs to the list the cursor is in.</summary>
    public bool Owns(HitResult? hit)
        => ListId is { } list
            && hit is HitResult.ListItemHit item
            && string.Equals(item.ListId, list, StringComparison.Ordinal);
}
