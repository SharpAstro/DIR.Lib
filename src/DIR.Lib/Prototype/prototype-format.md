# Prototype document format

A prototype is one JSON document of screens. `Prototype.Load(json)` (namespace `DIR.Lib.Prototyping`) reads it and
returns the document, or every problem found, each with its JSON path (`$.screens[2].root.children[0].kind`).

This page is the vocabulary. The table behind the loader is `PrototypeVocabulary`, and `PrototypeFormatDocTests`
fails when this page and that table differ: every kind and every property below is accepted by the loader, and
every one the loader accepts is below. Each node maps one to one onto a `Builder` call of the same name.

## Document

| Property | Type | Notes |
|---|---|---|
| `width` | number | design width, greater than zero, required |
| `height` | number | design height, greater than zero, required |
| `entry` | string | id of the screen the prototype starts on, required |
| `targets` | array | the surfaces the prototype is for, optional (see below) |
| `screens` | array | the screens, at least one, required |

A screen has `id` (required, unique), `title` (optional, defaults to the id) and `root` (required, a node).
Every screen must be reachable from `entry` by a `goto` action.

A target is an object with `name` (required, unique), `width` and `height` (required, design units, greater than
zero) and `scale` (optional, greater than zero, default 1). A document with no `targets` has one, named `design`,
at the document's `width` and `height` and scale 1.

`target` is a reserved, read-only state: the player sets it before building each frame, to `compact` when the design
width it draws at is under 600 design units and to `regular` otherwise. A `when` of `target=compact` or
`target=regular` shows a node on one kind of target only; any other value is a problem. `set:target=...`,
`toggle:target` and `bind: "target"` are problems too.

`Prototype.Check(document, theme?, options?, fontPath?)` and `Prototype.Render(document, theme?, fontPath?)`, given
no sizes, run at every target: the surface is `width * scale` by `height * scale` pixels, drawn by a player at that
target, so `target` follows the target's design width. A finding names its target (`PrototypeFinding.Target`, shown
as `@name` in its text) and a render returns `(Target, Screen, Image)`. The sized overloads draw at a size and leave
the target name empty. `Check` never uses `target` as a state variant: it comes from the size.

## Faces

A bold run (`weight: bold`, the heading and label styles) and a mono run (`face: mono`) are drawn in a face of their own
only when the host gives one. `PrototypeFaces` names them (`Regular`, `Bold`, `Mono`, `MonoBold`); `Prototype.Check`,
`Prototype.Render` and `PrototypePlayer` take it in a new overload. What a face falls back to:

- **A face left null** draws in the face the painter falls back to: bold in `Regular`, mono in `Regular`, bold mono in `Mono`.
- **A `fontPath` given** (the older overloads, and the player constructor taking a path) names the one face every run
  draws in, so the result does not depend on the machine: bold and mono are the same face as the body.
- **No `fontPath` and no faces** (`Check` and `Render` with a null path) use `PrototypeFaces.Platform()`: Arial as the body
  face with its bold where installed (else the platform's default face), and the platform's monospace face from
  `FontResolver.ResolveSystemFont`. A platform without a default face throws, as before.

## Nodes

A node is an object with `kind` (required), the kind's properties, the common properties, and:

- `children`: an array of nodes, for the kinds that take them.
- `action`: what a click does. `goto:<screen>`, `set:<name>=<value>` or `toggle:<name>`.
- `when`: `<name>=<value>`; the node is shown only while that state equals the value (equality only).
- `selectedWhen`: `<name>=<value>`, validated like `when`; while it holds the node is drawn selected (the `Selection` fill and an `Accent` edge).
- `doubleAction`: an action in the form of `action`, run on a double-click. A node with a `doubleAction` and no `action` is still clickable, so the double-click reaches it. **Enter** reaches only the rows of a `tree` and a `table`: each is a declared list (Up and Down move a cursor that shows a focus ring, Enter runs the row's `doubleAction`, else its `action`). Enter does nothing on any other node, because no other kind is a list the keyboard can be in. A `tableRow` with neither an `action` nor a `doubleAction` is not clickable and the cursor steps over it.
- `bind`: a state name, for `textInput`, `checkbox`, `switch`, `slider`, `buttonGroup` and `dropdown`.

State names are letters, digits and underscores, not starting with a digit. A `key` is unique within its screen.

## Common properties

Accepted by every kind.

| Property | Type | Notes |
|---|---|---|
| `padding` | number | `Pad` |
| `background` | colour | `Bg` |
| `border` | colour | `Border` colour |
| `borderWidth` | number | default 1 |
| `borderRadius` | number | default 0 |
| `width` | size | `W`: a number, `"auto"`, `"star"` or `"star:<weight>"` |
| `height` | size | `H`, same forms |
| `key` | string | names the node |
| `tooltip` | string | `WithTooltip` |
| `side` | enum | `left`, `right`, `top`, `bottom`: pins a child of a `dock` |
| `size` | number | extent of a docked child along its dock axis |
| `hoverBackground` | colour | `BgHover`: the fill while the pointer is over the node |
| `revealOnHover` | bool | the node shows only while its row is hovered or selected |
| `selectedWhen` | string | `<name>=<value>`: drawn selected while the state equals the value |
| `doubleAction` | string | an action run on double-click, and on Enter for a tree row or a table row |

## Numbers

A number that measures something drawn may not be below zero, and one that is is a load problem at its path:
`padding`, `borderWidth`, `borderRadius`, `size`, `gap`, `firstExtent`, `fontSize`, `iconSize`, `minWidth`,
`minHeight`, a `treeRow`'s `depth` (a whole number), and a `width` or `height` given as a number. Zero is accepted. A slider's `min` and `max` and a
progress `value` are values, not extents, and may be any number.

## Colours

A colour is a `UiPalette` role name or `#rgb`, `#rrggbb` or `#rrggbbaa`.

Roles: `ContentBg`, `PanelBg`, `HeaderBg`, `Separator`, `SeparatorStrong`, `BodyText`, `DimText`, `HeaderText`, `Accent`, `AccentAlt`, `Selection`, `SelectionText`, `Focus`, `Info`, `Warn`, `Error`, `Success`

## Kinds

### `vstack`

Children top to bottom (`Builder.VStack`).

| Property | Type | Notes |
|---|---|---|
| `gap` | number | between children |

### `hstack`

Children left to right (`Builder.HStack`), centred on the row's vertical middle (since 11.104; before it every child sat at the top, so a row of mixed heights laid out differently).

| Property | Type | Notes |
|---|---|---|
| `gap` | number | between children |

### `grid`

| Property | Type | Notes |
|---|---|---|
| `columns` | integer | required |
| `gap` | number | between cells |

### `wrap`

Children flow onto further lines (`WrapH`, or `WrapV` with `vertical`).

| Property | Type | Notes |
|---|---|---|
| `gap` | number | between children |
| `vertical` | bool | flow top to bottom |

### `dock`

One or more children pinned to edges (each sets `side` and `size`), and exactly one child without `side` that takes the rest.

### `split`

Exactly two children and a divider.

| Property | Type | Notes |
|---|---|---|
| `axis` | enum | `horizontal` (default) or `vertical` |
| `firstExtent` | number | the first pane's extent |

### `overlay`

Exactly two children: the layer, then the one drawn on top.

### `modal`

Exactly two children: the layer, then the modal card. `action` runs when the scrim is pressed.

| Property | Type | Notes |
|---|---|---|
| `open` | bool | whether the card is shown (default true) |

### `text`

| Property | Type | Notes |
|---|---|---|
| `text` | string | required |
| `fontSize` | number | default 14 |
| `color` | colour | |
| `align` | enum | `near`, `center`, `far` |
| `weight` | enum | `regular` or `bold`; drawn in the bold face (see Faces) |
| `face` | enum | `ui` (default) or `mono`; drawn in the monospace face (see Faces) |
| `style` | enum | `body` (default), `heading` (bold, larger) or `label` (small, upper-case, `DimText`); sizes come from the theme's metrics |

### `button`

The themed button; its `action` runs on a press.

| Property | Type | Notes |
|---|---|---|
| `text` | string | required |
| `style` | enum | `accent`, `standard` (default), `subtle` or `link` (a hyperlink) |
| `disabled` | string | the reason, shown as the tooltip; the button is flat and does nothing |
| `icon` | string | an `IconKind` name |
| `opens` | bool | adds a right-pointing caret |

### `switch`

Takes `bind`: the state holds true or false.

| Property | Type | Notes |
|---|---|---|
| `label` | string | |
| `labelBefore` | bool | the label before the track |
| `disabled` | string | the reason, shown as the tooltip |

### `tree`

A column of `treeRow` children, and nothing else. Takes `bind`: the state holds the `key` of the selected row.

### `treeRow`

Only valid directly inside a `tree`. A row of the tree: indent, an optional mark, the text, a note and a trailing
icon. A press on the row sets the tree's `bind` to the row's `key` and runs its `action`; the row is selected, with
the theme's selection fill and an accent edge, while the bind equals its `key` (a row with no `key` is never
selected). The row lights on hover.

| Property | Type | Notes |
|---|---|---|
| `text` | string | required |
| `depth` | integer | indent levels, not below zero; default 0 |
| `mark` | string | an `IconKind` name drawn before the text (a state mark) |
| `note` | string | dim text after the text |
| `trailingIcon` | string | an `IconKind` name drawn at the end, shown only while the row is hovered or selected |
| `trailingAction` | string | an action run when the trailing icon is pressed; needs `trailingIcon` |

### `tabs`

A tab strip with the selected tab underlined in the `Accent`. Takes `bind`: the state holds the chosen option;
unset, the first tab is shown. A press on a tab sets the bind. A tab's width is its label measured by the player's
renderer; built with no surface it is estimated at 0.6 of the font size per character.

| Property | Type | Notes |
|---|---|---|
| `options` | string array | required: the tab labels |

### `table`

A header row and `tableRow` children, and nothing else. The header uses the palette's `HeaderBg` and `HeaderText`;
every row is sized by the same columns and gap, so a title sits over its values.

**Known defect:** a table narrower than its fixed columns (at 240 wide, `columnWidths` of 140 and 80 leave a `star` column no room) draws its cells overlapping. Keep a table at or above the sum of its fixed columns plus a usable star column; the narrowest captured width is 320. Not yet fixed in the engine.

| Property | Type | Notes |
|---|---|---|
| `columns` | string array | required: the column titles |
| `columnWidths` | size array | one per column: a number, `"auto"`, `"star"` or `"star:<weight>"`; default all `"star"` |

### `tableRow`

Only valid directly inside a `table`. Its children are its cells, in column order; extra cells are dropped and
missing ones left empty. A row lights on hover, and takes `selectedWhen`, `action` and `doubleAction`.

### `markdown`

| Property | Type | Notes |
|---|---|---|
| `source` | string | markdown source, required |

### `icon`

| Property | Type | Notes |
|---|---|---|
| `icon` | string | an `IconKind` name, required |
| `iconSize` | number | omit to match the text beside it |
| `color` | colour | |

### `box`

A fixed-size box; size it with the common `width` and `height`.

| Property | Type | Notes |
|---|---|---|
| `color` | colour | transparent when omitted |

### `fill`

The escape hatch an app paints itself.

| Property | Type | Notes |
|---|---|---|
| `minWidth` | number | |
| `minHeight` | number | |

### `spacer`

An empty node that takes the space its sizing gives it.

### `textInput`

Takes `bind`.

| Property | Type | Notes |
|---|---|---|
| `placeholder` | string | |
| `fontSize` | number | |

### `checkbox`

Takes `bind`.

| Property | Type | Notes |
|---|---|---|
| `label` | string | required |

### `buttonGroup`

Takes `bind`: the state holds the chosen option.

| Property | Type | Notes |
|---|---|---|
| `options` | string array | required |

### `dropdown`

Takes `bind`: the state holds the chosen option.

| Property | Type | Notes |
|---|---|---|
| `options` | string array | required |
| `label` | string | |

### `slider`

Takes `bind`.

| Property | Type | Notes |
|---|---|---|
| `min` | number | |
| `max` | number | |
| `color` | colour | fill colour |

### `progress`

| Property | Type | Notes |
|---|---|---|
| `value` | number | fraction, 0 to 1, required |
| `label` | string | |
| `color` | colour | fill |
| `track` | colour | |

## Example

```json
{
  "width": 360,
  "height": 640,
  "entry": "home",
  "screens": [
    {
      "id": "home",
      "title": "Home",
      "root": {
        "kind": "vstack", "gap": 8, "padding": 12, "background": "PanelBg",
        "children": [
          { "kind": "text", "text": "Welcome", "fontSize": 20, "color": "HeaderText" },
          { "kind": "text", "text": "Go to settings", "color": "Accent", "action": "goto:settings" }
        ]
      }
    },
    {
      "id": "settings",
      "root": {
        "kind": "vstack", "gap": 8,
        "children": [
          { "kind": "checkbox", "label": "Dark mode", "bind": "dark" },
          { "kind": "text", "text": "Dark is on", "when": "dark=true" },
          { "kind": "text", "text": "Back", "action": "goto:home" }
        ]
      }
    }
  ]
}
```

## Known limits

What the loader and builder do on purpose, or have not tightened yet:

- **Loading.** The loader walks a `JsonDocument` by hand rather than deserializing through the source-generated
  context, because a node's properties depend on its `kind` and every problem needs its JSON path. No reflection is used, so the AOT rule holds.
- **Modal card.** A modal card is centred by the document design size, even when the modal is nested in a smaller
  container. Declare modals at the screen root.
- **Dropdown state.** Dropdowns that share a `bind` share one open state, as inputs sharing a `bind` share one value.
  Give each dropdown its own `bind`.
