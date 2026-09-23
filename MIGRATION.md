# Migration notes

One section per breaking release, newest first, each with a port recipe. Additive releases are not
listed here -- [CHANGELOG.md](CHANGELOG.md) says what changed in every version, breaking or not.

Upgrading across more than one major? Work UP the file: the sections are independent, and a 9.x
consumer taking 10.0 needs only the 10.0 section.

## 11.0 the caret blinks on the clock

Affects anyone setting `PixelWidgetBase.FrameCount`, or calling `TextInputRenderer.Render` with a
`frameCount` argument.

### `FrameCount++` per frame -> `CaretPhase` from a clock, and redraw on the flip

```csharp
// Before -- every frame, and a focused field forced every frame
widget.FrameCount++;
bool NeedsRedraw() => ... || focusedField is { IsActive: true };

// After -- once per frame, from a monotonic clock
var phase = CaretBlink.PhaseAt(TimeProvider.System);   // or PhaseAt(timestamp, ticksPerSecond)
widget.CaretPhase = phase;
paintedCaretPhase = phase;
bool NeedsRedraw() => ... || (focusedField is { IsActive: true }
    && CaretBlink.PhaseAt(TimeProvider.System) != paintedCaretPhase);
```

A host that never set `FrameCount` needs nothing: `CaretPhase` defaults to 0, an ON phase, which is
the steady caret it already drew. A host that only wants the caret hidden in a test sets
`CaretPhase = 1`.

### `TextInputRenderer.Render(..., frameCount, ...)` -> `caretVisible`

```csharp
// Before
TextInputRenderer.Render(renderer, state, x, y, w, h, font, size, frameCount);

// After
TextInputRenderer.Render(renderer, state, x, y, w, h, font, size,
    caretVisible: CaretBlink.IsVisible(CaretBlink.PhaseAt(TimeProvider.System)));
```

The parameter is a `bool` now, so a `long` passed positionally fails to compile rather than being
read as a visibility.

### `MouseMove` carries `Modifiers` (additive, nothing to port)

A host that produces moves should fill it, from the same keyboard state it reads for a press:

```csharp
new InputEvent.MouseMove(x, y, MouseButton.None, currentModifiers);
```

Existing `MouseMove(x, y)` / `MouseMove(x, y, button)` calls and `MouseMove(var x, var y)` /
`MouseMove(var x, var y, var button)` patterns keep compiling and binding; they mean "no modifiers".

## 10.0 the cuts: one dispatcher, one focus owner, a popover stack

Affects anyone calling `IPixelWidget.HitTestAndDispatch`, implementing `IKeyboardClaimant` or reading
`WindowUiSettings.KeyboardClaimant`, calling `PixelWidgetBase.RenderDropdownMenu`, constructing
`HitResult.SliderHit` or a one-argument `HitResult.TextInputHit`, calling `TextInputState.Activate` /
`Deactivate`, or assigning `LayoutInspection.Enabled`.

Each cut is a thing 9.x kept for consumers that have since moved off it. Nothing here needs a new
feature: every replacement shipped in 9.2 or later.

### `HitTestAndDispatch` on a widget -> route the press

```csharp
// Before
if (evt is InputEvent.MouseDown down && widget.HitTestAndDispatch(down.X, down.Y) is not null)
{
    return true;
}

// After -- one router per surface, built once, given the frame's widgets
var router = new InputRouter(widget.Ui, tracker, RequestRedraw) { Widgets = () => [widget] };
// ...
return router.Handle(evt);
```

`IPixelWidget.HitTest` is unchanged and still answers "what is under this point" without running
anything. `ClickableRegionTracker.HitTestAndDispatch` is unchanged too -- it is the tracker's own method,
and a cell surface still calls it.

**Two things behave differently once a press is routed, and both are the point.** The router consumes the
press for ANY region under the pointer whether or not a handler ran, so a region registered with a hit and
no handler is DEAD rather than transparent -- registering a hit without a handler means "nothing happens
here", never "someone else will do it". And a press somewhere other than a text field blurs whichever
field had the keyboard, which a widget-level dispatch never did.

### `IKeyboardClaimant` -> the painted-popover stack

```csharp
// Before -- a type per overlay whose whole body was "Escape closes me"
sealed class MyPanelKeys(MyState state) : IKeyboardClaimant
{
    public bool HandleKeyDown(InputKey key)
    {
        if (!state.IsOpen || key != InputKey.Escape) return false;
        state.IsOpen = false;
        return true;
    }
}
ui.KeyboardClaimant = new MyPanelKeys(state);          // and the host consulted it by hand

// After -- the overlay is a Popover node, and painting it IS the claim
var tree = Layout.Builder.Popover(anchor, content, state);   // state is a PopoverState
// nothing else: InputRouter asks ui.PaintedPopovers, topmost first
```

`WindowUiSettings.PaintedPopovers` (`IReadOnlyList<PopoverState>`) replaces the `KeyboardClaimant` slot.
It is filled by painting and cleared per paint cycle, so a popover that closed is simply not on it. Keys
its content wants go on `PopoverState.ContentKeys`, now a `Func<InputKey, bool>?`:

```csharp
popover.ContentKeys = myMenu.HandleKeyDown;   // was: ContentKeys = myMenu;  // : IKeyboardClaimant
```

A host that asked the claimant by hand deletes that call: the router asks first, ahead of shortcuts and
the focused field.

### `RenderDropdownMenu` -> `Layout.Builder.Dropdown`

```csharp
// Before -- last call in the render pass, ten arguments
RenderDropdownMenu(menu, fontPath, fontSize, bg, highlight, text, border,
    viewportWidth, viewportHeight, maxHeight);

// After -- a node in the tree, anywhere; the overlay layer puts it on top
var anchor = new RectF32(menu.AnchorX, menu.AnchorY, menu.AnchorWidth, 0f);
var node = Layout.Builder.Dropdown(anchor, menu,
    maxHeight: MathF.Max(fontSize, viewport.Size.Y - menu.AnchorY));
```

`DropdownMenuState` is unchanged, `AnchorX` / `AnchorY` / `AnchorWidth` and `Open(...)` included -- the
declared menu is declared from exactly those. **State `maxHeight` yourself**: `RenderDropdownMenu`
clamped internally to the space between the anchor and the bottom edge, and a `Builder` call has no
viewport to derive it from. It is not cosmetic; it is what engages the scroll on a long menu.

### `HitResult.SliderHit(int)` -> `SliderStateHit`

```csharp
// Before -- an index into a parallel array of track rects the paint kept by hand
RegisterClickable(x, y, w, h, new HitResult.SliderHit(i));
// After -- a Content.Slider leaf; the engine paints it, registers it and owns its drag
Layout.Builder.Slider(state)      // hit is HitResult.SliderStateHit(state)
```

A host that only FORMATS a hit (an inspector, a log line) drops the `SliderHit` arm; `SliderStateHit`
carries the `SliderState` itself.

### `TextInputState.Activate` / `Deactivate` -> `TextInputFocus`

They are `internal`. Which act you meant decides the replacement, and the two were conflated:

```csharp
// "Give this field the keyboard"
focus.Focus(input);                   // was: input.Activate();

// "Open an editor on this value" -- seeds AND selects, so a following SelectAll is a second mechanism
focus.Focus(input, $"{seconds}");     // was: input.Activate($"{seconds}"); input.SelectAll();

// "Seed the text, but the keyboard belongs elsewhere" -- Activate could not say this, and six sites
// meant it: all three fields of a form painted as focused, and the row said nothing about where
// typing would go.
input.Text = text;
input.CursorPos = text.Length;

// "Take the keyboard away"
focus.Blur();                         // was: input.Deactivate();
focus.BlurIfFocused(input);           // ...without stealing it from someone else's field
```

`TextInputFocus` is the window's, at `WindowUiSettings.Focus` -- reached through
`PixelWidgetBase.Ui.Focus`, and shared by `ShareUiContext` so sibling widgets cannot end up with two.

**`TextInputState.IsActive` is read-only from outside too**, for the same reason: it is a cache of the
owner's record of focus, so `new TextInputState { IsActive = true }` (a common test fixture) becomes a
`Focus` call on an owner. Reading it is unchanged, which is what a painter does with it.

### `HitResult.TextInputHit` takes both arguments

```csharp
new HitResult.TextInputHit(state)                 // gone
new HitResult.TextInputHit(state, default)        // a surface with no text geometry to state
new HitResult.TextInputHit(state, new TextInputGeometry(x, fontFamily, fontSize, leadingRoom))
```

`PixelWidgetBase.RenderTextInput` already states the real geometry, so a consumer painting fields
through the engine has nothing to change. A surface registering the hit itself now has to say where it
laid the text down -- `default` is still available and still means "caret placement is not wired here",
but it has to be written rather than defaulted into.

### `LayoutInspection`

Delete the assignment. Layout capture has been unconditional since 8.8 and nothing has read the flag
since; it survived 9.x only because removing a public type needs a major.

## 9.0 the pre-layout scale travels as `DesignScale`, not a bare float

Affects anyone calling `ListScrollController.SetExtent`, `TapOrDragGesture.Arm`,
`FloatingPalette`'s offset methods, or the `dpiScale:` argument of `RenderLayout` / `ArrangeLayout` /
`PaintLayout` / `DrawTrackSlider`.

### What changed

A new `readonly record struct DesignScale(float X, float Y)` — surface units per design unit, per
axis — replaces the `float dpiScale` those APIs took. `PixelMeasureContext` hands out its own mapping
as `.Scale`, and `PixelWidgetBase` exposes `Scale`.

**`WindowUiSettings.DpiScale` and `PixelWidgetBase.DpiScale` are unchanged.** A host still sets one
number, and `PixelWidgetBase.DpiScale` is still virtual so a composite can propagate it. Only what
components pass to *each other* changed.

`TapOrDragGesture.Arm`'s `slopPx` parameter is renamed `slopDesignUnits`, because it is scaled and so
was never pixels. `PixelMeasureContext`'s own `dpiScale:` constructor parameter is unchanged — it is
the isotropic convenience, and it is the set-point, not the currency.

### Why

Each of those components kept a private copy of a number the measure context already owned. Two homes
for one value is a drift waiting for a display change, and nothing would have reported it — the chrome
would simply have been sized against one scale while the text it sits behind was measured against
another.

The pair, rather than a float, is the second half. A single scalar asserts that a design unit is
square, which is true on a pixel surface and false on a terminal (a cell is roughly 8 units across and
16 down). With one number a caller is right on one axis by construction and on the other only by luck.

### Port recipe

Before:

```csharp
scroll.SetExtent(viewport, rowH, count, DpiScale);
gesture.Arm(x, y, mods, DpiScale, slopPx: 6f);
palette.DragTo(pointerY, DpiScale);
RenderLayout(root, bounds, dpiScale: 1f);
```

After:

```csharp
scroll.SetExtent(viewport, rowH, count, Scale);
gesture.Arm(x, y, mods, Scale, slopDesignUnits: 6f);
palette.DragTo(pointerY, Scale);
RenderLayout(root, bounds, scale: DesignScale.One);
```

Inside a widget, `Scale` is the inherited property. Anywhere else, take it from the measure context
you are already using (`ctx.Scale`) rather than building one from a scale of your own — building one
is what re-creates the second source this removes. `new DesignScale(1.5f)` is the isotropic
constructor when you genuinely have only a number; `DesignScale.One` is the unscaled case, and it is
what an omitted optional argument means.

## 8.0 `TabBar` becomes a widget

Affects anyone who constructs a `TabBar`, sets its `Scale`, or calls `Render`.

### What changed

`TabBar` is now `TabBar<TSurface> : PixelWidgetBase<TSurface>`. It takes its `Renderer<TSurface>` at
construction, like every other widget; the font path and `FontFallbackResolver` it used to take as
constructor arguments are now the widget's own `FontPath` / `FontFallback`; `Scale` is gone, replaced
by the inherited `DpiScale`; and `Render` no longer takes a renderer.

`TabClick`, `Colors`, `Height`, `Font`, `Pad`, `Border`, `ShowNewTabButton`, `NewTabActive`,
`NewTabHovered`, `Pointer`, `HandleMouseDown`, `HitNewTabButton` and `SlotAt` are all unchanged in
name and meaning.

### Why

Three per-window values — the font, the fallback chain, the display scale — were being pushed into
the bar through three channels of its own, all of which the window already owns and already shares
with every other widget through `WindowUiSettings`. A host with a `ShareUiContext` call now simply
includes the bar in it, and a value added to the context later reaches it with no further change.

The larger reason is that the bar was hit-testing against a private copy of its layout: a `_rects`
list filled during the draw, read by `HandleMouseDown` and `SlotAt`. That is the shape where draw and
hit drift apart. It now **registers** each tab, each ✕ and the + as it paints them, and those three
methods report from the registered rects. Two properties come free with that. The ✕ is registered
after the tab it sits in, so it wins the hit as an inner control should. And through 7.32's frame
stamp the whole strip goes quiet on a frame the host did not draw it in — which a tab bar does meet,
since a host carrying a torn-out tab as its own small window paints it as a chip and draws no strip,
leaving the bar holding the layout of a strip that is gone. Whether a press reaches it there was the
host's guards to get right; now it is not.

### Port recipe

Before:

```csharp
_tabBar = new TabBar(fontPath, fallback);
...
_tabBar.Scale = UiScale;
_tabBar.Render(renderer, contentLeft, stripRight, titles, activeIndex);
```

After:

```csharp
_tabBar = new TabBar<TSurface>(renderer);
ShareUiContext(_tabBar, /* the window's other widgets */);   // font, fallback, DPI, frame id
...
_tabBar.Render(contentLeft, stripRight, titles, activeIndex);
```

A host that does not share a context sets the three values on the bar directly
(`FontPath`, `FontFallback`, `DpiScale`) and everything else is as it was.

**The host must bump `Ui.FrameId` once per frame** for the strip to go quiet when it is not drawn. A
host that does not count frames keeps 7.x behaviour exactly: the id stays 0, so does every stamp, and
every hit test matches as before.

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
