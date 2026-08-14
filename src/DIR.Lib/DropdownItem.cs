namespace DIR.Lib
{
    /// <summary>
    /// One entry in a <see cref="DropdownMenuState{T}"/>: what it says, what it MEANS, and whether it can
    /// be chosen right now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The value is carried, not inferred from position.</b> A menu of bare strings forces the consumer
    /// to map the chosen INDEX back to meaning -- <c>idx switch { 1 =&gt; PolarAlign, 2 =&gt; Planetary, … }</c>
    /// -- so the label order and that switch have to agree, and nothing checks. Reordering the labels then
    /// silently selects the wrong thing. Here the entry the user clicked IS the value.
    /// </para>
    /// <para>
    /// <b><see cref="IsEnabled"/> exists so a menu never has to answer a click by doing nothing.</b> Without
    /// it the only way to express "not available right now" is to accept the selection and then decline to
    /// act, which is indistinguishable from a broken control -- and the explanation usually ends up somewhere
    /// the user only reaches by making the selection that just failed. A disabled entry draws greyed, is
    /// skipped by keyboard navigation, and ignores clicks, with <see cref="Tooltip"/> carrying the reason.
    /// </para>
    /// </remarks>
    /// <param name="Label">The text drawn for this entry.</param>
    /// <param name="Value">What choosing it means, handed straight back to the select callback.</param>
    public sealed record DropdownItem<T>(string Label, T Value)
    {
        /// <summary>Whether the entry can be chosen. Disabled entries are still DRAWN -- a menu that hides
        /// what is unavailable teaches nothing about how to make it available.</summary>
        public bool IsEnabled { get; init; } = true;

        /// <summary>
        /// Extra explanation for the entry, and the REASON when <see cref="IsEnabled"/> is false. Rendered
        /// beside a disabled label so the answer to "why can't I pick this" is on screen at the moment of
        /// the decision.
        /// </summary>
        public string? Tooltip { get; init; }

        /// <summary>
        /// Runs instead of the menu's select callback when this entry is chosen, for a row that DOES
        /// something rather than selecting a value -- a "Custom…" row that opens an editor, say.
        /// </summary>
        /// <remarks>
        /// This replaced a hard-coded custom-entry mechanism: three parameters on <c>Open</c>
        /// (<c>hasCustomEntry</c>, <c>onCustom</c>, <c>customEntryLabel</c>), three properties to hold them,
        /// and an <c>index == Items.Length</c> special case repeated through keyboard navigation, Enter,
        /// and the painter. All of that existed to describe ONE extra row. As an item it is simply the last
        /// entry in the list, and every count, bound and loop goes back to being over <c>Items</c> alone.
        /// <para>
        /// An action entry also draws in the accent colour, which is what the custom row always did.
        /// </para>
        /// </remarks>
        public System.Action? OnChoose { get; init; }

        /// <summary>An entry that cannot be chosen, and says why.</summary>
        public static DropdownItem<T> Disabled(string label, T value, string reason)
            => new(label, value) { IsEnabled = false, Tooltip = reason };

        /// <summary>
        /// An entry that performs an action rather than selecting a value. <see cref="Value"/> is unused and
        /// left at default -- an action row has no value to mean, and callers read <see cref="OnChoose"/>.
        /// </summary>
        public static DropdownItem<T> Action(string label, System.Action onChoose)
            => new(label, default!) { OnChoose = onChoose };

        /// <summary>An entry enabled or not by <paramref name="enabled"/>, carrying <paramref name="reason"/>
        /// when it is not -- the shape a caller with a precondition check actually has.</summary>
        public static DropdownItem<T> When(bool enabled, string label, T value, string? reason = null)
            => new(label, value) { IsEnabled = enabled, Tooltip = enabled ? null : reason };
    }

    /// <summary>Factories for the common case where an entry's value is its own label.</summary>
    public static class DropdownItem
    {
        /// <summary>A plain text entry whose value is the label itself.</summary>
        public static DropdownItem<string> Text(string label) => new(label, label);
    }
}
