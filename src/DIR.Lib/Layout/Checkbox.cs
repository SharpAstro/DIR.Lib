namespace DIR.Lib.Layout;

/// <summary>
/// How a <see cref="Builder.Checkbox"/> looks, stated once so a checked row and an unchecked one are one
/// decision. The tick is drawn (<see cref="IconKind.Check"/>), never spelled: a row that wrote "[x] " into
/// its label was a mark in a text run, which draws whatever the face has for it and cannot be told apart
/// from the label by anything reading the tree.
/// </summary>
/// <param name="BoxFill">The well the tick sits in, checked or not.</param>
/// <param name="CheckColor">The tick.</param>
/// <param name="LabelColor">The label of an unchecked row.</param>
/// <param name="HoverFill">The row's background under the pointer, when a press would toggle it.</param>
public readonly record struct CheckboxStyle(
    RGBAColor32 BoxFill,
    RGBAColor32 CheckColor,
    RGBAColor32 LabelColor,
    RGBAColor32 HoverFill)
{
    /// <summary>The label of a checked row, or null for <see cref="LabelColor"/>.</summary>
    public RGBAColor32? CheckedLabelColor { get; init; }

    /// <summary>The row's background when unchecked, or null for none.</summary>
    public RGBAColor32? RowFill { get; init; }

    /// <summary>The row's background when checked, or null for <see cref="RowFill"/>. A row that tints
    /// itself when on says so twice, which is right for a switch read from across the room.</summary>
    public RGBAColor32? CheckedRowFill { get; init; }

    /// <summary>Side of the box in design units, or 0 for the font size.</summary>
    public float BoxSize { get; init; }

    /// <summary>Space between the box and the label, design units.</summary>
    public float Gap { get; init; } = 6f;

    /// <summary>Corner radius of the box, design units.</summary>
    public float CornerRadius { get; init; } = 2f;
}
