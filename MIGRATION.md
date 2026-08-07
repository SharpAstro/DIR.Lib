# Migration notes

One section per breaking release, newest first. Additive releases are not listed here; the release
notes in `.github/workflows/dotnet.yml` cover every version.

## 7.11 `UiPalette` grows eight roles and becomes a `sealed record`

Affects anyone who constructs a `UiPalette`, a `UiMetrics` pair into `UiTheme`, or stores one in a
field. `TabBar`, `TabBarColors` and `MenuColors` are source-compatible; only the palette handed to
them changed shape.

### What changed

`UiPalette` was a positional `readonly record struct` with eight members. It is now a `sealed record`
with eleven `required` members and five derived ones. `UiTheme` became a `sealed record` with
`required Palette` and `required Metrics`. `UiMetrics` is untouched and is still a
`readonly record struct`.

### Why

Two independent reasons, both of which had already cost real defects.

A record struct always carries an implicit parameterless constructor, and property initializers do
not run for it. So `default(UiPalette)`, a `UiPalette` field never assigned, or a `new UiPalette()`
gave every role `RGBAColor32` zero. For a palette that is transparent black, painted silently, with
no exception and nothing on screen to explain it. As a `sealed record` with `required` members the
omission is a compile error, and a null reference is a clean throw at the point of use.

A positional record also cannot gain a member without breaking every call site, which is exactly
what a palette has to do over time. Eight roles could not express a semantic severity (an error had
to borrow the accent) or a second rule weight. Moving to init properties means the next role is
additive.

### Port recipe

Replace the positional call with an object initializer, and add the four newly required roles.

Before:

```csharp
private static readonly UiPalette Chrome = new(
    ContentBg: new RGBAColor32(0xff, 0xff, 0xff, 0xff),
    PanelBg: new RGBAColor32(0xf2, 0xf2, 0xf4, 0xff),
    HeaderBg: new RGBAColor32(0xff, 0xff, 0xff, 0xff),
    HeaderText: new RGBAColor32(0x1a, 0x1a, 0x1e, 0xff),
    BodyText: new RGBAColor32(0x33, 0x33, 0x38, 0xff),
    DimText: new RGBAColor32(0x6a, 0x6a, 0x72, 0xff),
    Separator: new RGBAColor32(0xc8, 0xc8, 0xd0, 0xff),
    Selection: new RGBAColor32(0x20, 0x60, 0xff, 0xff));
```

After:

```csharp
private static readonly UiPalette Chrome = new()
{
    ContentBg = new RGBAColor32(0xff, 0xff, 0xff, 0xff),
    PanelBg = new RGBAColor32(0xf2, 0xf2, 0xf4, 0xff),
    HeaderBg = new RGBAColor32(0xff, 0xff, 0xff, 0xff),
    BodyText = new RGBAColor32(0x33, 0x33, 0x38, 0xff),
    DimText = new RGBAColor32(0x6a, 0x6a, 0x72, 0xff),
    Separator = new RGBAColor32(0xc8, 0xc8, 0xd0, 0xff),
    Selection = new RGBAColor32(0x20, 0x60, 0xff, 0xff),
    // newly required
    Accent = new RGBAColor32(0x20, 0x60, 0xff, 0xff),
    Info = new RGBAColor32(0x0a, 0x63, 0xa8, 0xff),
    Warn = new RGBAColor32(0x8a, 0x50, 0x00, 0xff),
    Error = new RGBAColor32(0xb0, 0x2a, 0x20, 0xff),
};
```

`UiTheme` gets the same treatment:

```csharp
// before
var theme = new UiTheme(Chrome, Metrics);
// after
var theme = new UiTheme { Palette = Chrome, Metrics = Metrics };
```

### The eleven required roles

`ContentBg`, `PanelBg`, `HeaderBg`, `Separator`, `BodyText`, `DimText`, `Accent`, `Selection`,
`Info`, `Warn`, `Error`.

### The five derived roles, and what they fall back to

Stating one is optional; omitted, it tracks the role it extends, so a palette with a single rule
weight or a single accent need not invent a second.

| Role | Falls back to |
|------|---------------|
| `SeparatorStrong` | `Separator` |
| `HeaderText` | `Accent` |
| `AccentAlt` | `Accent` |
| `Focus` | `Accent` |
| `Success` | `Accent` |

Two notes on these. `HeaderText` **was** required and is now derived, so a palette that omits it
gets the accent rather than a compile error; if your headers were a near-white distinct from your
accent, keep stating it. And the fallback lives in nullable backing fields rather than in the
property value, so the record copy constructor keeps an unstated role unstated: recolour `Accent`
through `with` and `AccentAlt` follows it, instead of freezing at the old accent on the first clone.

`Success` defaults to `Accent` rather than to a green on purpose. A palette that cannot spend the
green channel at all, a dark-adaptation scheme for example, still needs a positive mark, and its
accent is the right one. Where green is available, state it: a consumer drawing a three way
offline / online / running indicator from `DimText` / `Success` / `Info` will otherwise collapse two
of the three onto one colour wherever `Info` and `Accent` happen to be equal.

### Also added

`UiPalette.IsDark`, computed from `ContentBg` rather than stored, so it cannot disagree with the
colours it describes the way a hand set flag eventually does.

`MenuColors.FromPalette`, the menu counterpart of `TabBarColors.FromPalette` from 7.10. Derive it
when the theme moves, not per frame; both allocate.

### Kept on purpose

`UiMetrics` stays a `readonly record struct`. It is five floats with no colour semantics, nothing
derives from it, and an all zero metrics set is visibly broken rather than silently wrong, so none
of the reasoning above applies to it.

`TabBarColors.ActiveAccent` still does not come from the palette in `FromPalette`. That decision is
from 7.10 and is unchanged: the accent means "this is the tab you are on", which reads the same on a
light strip as on a dark one, so running it through a theme changes what it communicates rather than
how it reads.
