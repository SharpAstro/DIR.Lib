# DIR.Lib

**D**evice-**I**ndependent input + **R**endering library for .NET. Provides the shared foundation for GPU (SDL3 + Vulkan), terminal (Console), and browser (WebGL2 / Blazor WebAssembly) applications. Pure-managed, AOT-compatible, no native dependencies.

## Rendering Primitives

- **`PointInt`** — 2D integer point
- **`RectInt`** — 2D integer rectangle (note: constructor argument order is `(LowerRight, UpperLeft)`)
- **`RectF32`** — 2D float rectangle (x, y, width, height) for pixel-based layout
- **`RGBAColor32`** — 32-bit RGBA color with Lerp, WithAlpha, Luminance
- **`TextAlign`** — Near/Center/Far alignment enum
- **`Renderer<TSurface>`** — Abstract renderer: FillRectangle, FillRoundedRectangle, DrawRectangle, FillEllipse / DrawEllipse, DrawLine, DrawLineDashed, DrawPolyline, DrawPolylineDashed, DrawText, MeasureText
- **`RgbaImage`** / **`RgbaImageRenderer : Renderer<RgbaImage>`** — pure software renderer for tests and headless scenarios; output is plain RGBA pixels (callers own the choice of PNG / JPEG / TIFF / sixel encoder)
- **`GlyphBitmap`** / **`SdfGlyphBitmap`** — raw RGBA glyph bitmap with bearing/advance info; SDF variant for scalable text on the GPU side
- **`ManagedFontRasterizer`** — pure-managed glyph rasterizer backed by `SharpAstro.Fonts.OpenTypeFont`; supports COLRv1 color glyphs, grayscale, and PDF subset fonts. AOT-compatible, no GC pinning, no native bindings.
- **`FontResolver`** — resolves platform-default monospace fonts and enumerates installed font files across system + per-user font directories (incl. Windows 11 `%LOCALAPPDATA%\Microsoft\Windows\Fonts`)
- **`FontFallbackResolver`** — splits a string into runs that each draw with a face that covers them; with faces declared by role (`FromRoles`), a codepoint whose Unicode default presentation is emoji is taken from the emoji face even where the primary covers it
- **`EmojiPresentation`** — the UTS #51 `Emoji_Presentation` property (`IsDefaultFor`), over a table generated from the Unicode Character Database by `tools/gen-emoji-presentation`
- **`ListCursor`** -- the keyboard's position in a list the layout tree already declares. A row states `ListItemHit(list, index)` for its click binding and is navigable by that alone: `PixelWidgetBase.ListCursor` + `HandleListKey` move and act, `Layout.Node.FocusBackground` (`.BgFocus`) shows where it is, and a row that is not clickable -- or one declared `.Disabled(reason)` -- is unreachable without anything beside the list saying so. `Open(listId, index, count)` adds the row count a VIRTUALISED list needs, so the arrows can step onto a row the last paint did not draw; `Moved` is where a consumer hangs `EnsureVisible`
- **`TextSelection<TSurface>`**: drag-select, highlight and copy for `.Selectable()` text on raster hosts; a widget opts in with one `WithSelection(page)` call and `InputRouter` routes the drag, Ctrl+C and the right-click (`OpenSelectionMenu`)
- **`FloatingPalette`**: a floating, grip-dragged, collapsible palette of toggle rows, as a `Layout.Node` tree plus the pure state and colour rules around it (`FloatingPaletteState`, `PaletteItem`, `PaletteColors`); `Build` returns the tree and touches no surface; the overload taking a `DesignScale` declares the grip's drag, its double-press collapse and the panel's claim on the pointer, so a host routes none of them by hand

## Input Handling

- **`InputEvent`** — open record hierarchy: `KeyDown`, `TextInput`, `MouseDown`, `MouseUp`, `MouseMove`, `Scroll`, `Pinch`, `PinchEnd`
- **`InputKey`** — platform-agnostic key codes (letters, digits, function keys, navigation, symbols)
- **`InputModifier`** — modifier flags (Shift, Ctrl, Alt)
- **`MouseButton`** -- Left / Middle / Right, plus `None` for a pointer moving with nothing held
- **`KeyChord`** -- a key plus its modifiers, and the precedence rule on it: `BeatsFocusedField` is true for Ctrl, Alt and the function keys, false for a bare letter. Stated once so a router and a test read the same fact, instead of each host special-casing its own global key by name
- **`PointerPress`** / **`PointerMove`** / **`DragCapture`** -- a press with its position, button, modifiers and click count; the capture a press handler returns to own every move and the release until the button comes up
- **`InputRouter`**: the dispatcher, so a host stops writing one. It takes an `InputEvent` and hands it to the regions, fields, shortcuts, captures and scroll controllers the last paint declared, in one fixed order: an open popover reads a key first, then any painted node's declared `Shortcut` if `KeyChord.BeatsFocusedField` lets it through, then the focused field, then the host's own `Unhandled`. A press places the caret or arms a drag, a wheel finds the innermost list under the pointer, and `AfterPaint` blurs a field that left the screen, answers a `focusOnOpen` request and expires a stale tooltip. `CursorAt(x, y)` and `Tooltip` are what a host paints from: the tooltip through `Layout.Builder.Tooltip(router.Tooltip, style)`, last in the frame, with `TooltipNeedsFrame` in the host's redraw check (11.13)
- **`IWidget`** — shared interface with `HandleInput` for both pixel and terminal widgets

Platform bridges (in downstream packages):
- `SdlVulkan.Renderer` provides `SdlInputMapping` (SDL3 Scancode → InputKey)
- `Console.Lib` provides `ConsoleInputMapping` (ConsoleKey → InputKey)
- `WebGl.Renderer` provides `WebGlCanvas`, a Blazor component that maps DOM pointer / wheel / pinch events to backing-space coordinates (`CanvasPointerEventArgs`, `CanvasWheelEventArgs`, `CanvasPinchEventArgs`) and delivers keys through `OnInputKey` as a `CanvasKeyEventArgs` (InputKey, InputModifier and the raw event), with the raw `OnKeyDown` still there beside it; it also provides `WebInputMapping` (browser `KeyboardEvent.Key` → InputKey, modifiers → InputModifier), the analogue of `SdlInputMapping`

## Widget System

- **`IPixelWidget`** — extends IWidget with pixel-coordinate hit testing and click dispatch, plus what a router reads back from the last paint: `CollectPaintedRegions` / `CollectPaintedNodes` (a composite folds its children's in), `Pointer`, `ScrollTargetAt`, `HitTestCursor` and `CaretIndexAt`
- **`PixelWidgetBase<TSurface>`** -- base class for pixel widgets: clickable regions, text input, buttons, drawing helpers. `MeasureLayout` / `MeasureContext` are the measure seam (a box is the MEASUREMENT of its content, through the same context arrange and paint share); `ScrollTargetAt(x, y)` answers which declared list a wheel belongs to; `DimTowards(color, background)` is the one grey a disabled thing is painted in
- **`PixelLayout`** + **`PixelDockStyle`** — dock-based layout engine (Top/Bottom/Left/Right/Fill)
- **`DockLayout<T>`** — generic dock layout using `INumber<T>` (the integer / pixel layouts above are built on this)
- **`ClickableRegion`** + **`ClickableRegionTracker`** — registered during render, walked in reverse for hit testing
- **`HitResult`** — open discriminated union: `TextInputHit`, `ButtonHit`, `ChromeHit`, `LinkHit`, `ListItemHit`, `SlotHit<T>`, `SliderStateHit`
- **`IFileDialogs`** — the platform's open, save and folder dialogs as a widget asks for them: `OpenAsync` / `SaveAsync` / `OpenFolderAsync` over a `FileDialogRequest` (title, `FileDialogFilter`s, start folder, suggested name, many), answered with a `FileDialogResult`. An interface so a test substitutes it; the host implements it over its windowing (SdlVulkan.Renderer's `SdlFileDialogs`). It never blocks the render thread, completes on the window's event thread, completes as cancelled when its token is, and gives a save path an extension when the user typed none (`FileDialogFilter.WithDefaultExtension`)
- **`DropdownMenuState`** — dropdown / popup menu state machine
- **`PopoverState`** + **`PopoverGroup`** — whether a popover is open, the Escape that closes it, and the group within which at most one is open (`new PopoverState { Group = bar }`). A node declared `.Opens(state)` is the popover's trigger: a press or its shortcut toggles it, and while it is open a press on the backdrop that lands on a trigger beneath reaches the trigger, so a bar of cards switches in one press

## Declarative Layout (`DIR.Lib.Layout`)

A surface-agnostic declarative layout engine. Describe a tree of immutable records; the engine measures + arranges it into rects; a per-surface painter (`PixelWidgetBase.PaintLayout`) draws each node and binds its click region **from the same arranged rect** — draw == hit by construction, with no separate hit-rect arithmetic that can drift.

- **`Layout.Node`** — the tree. Variants: `Stack` (vertical/horizontal), `Dock` (edge strips + a fill remainder), `Grid`, `Wrap` (children flow and wrap into new lines when out of extent — the flexbox `wrap` for toolbars/chip rows on narrow surfaces), `Overlay` (base/top, for modals/popups), `Split` (two resizable panes + a draggable divider; since 11.18 `Builder.Split(first, second, onResize, ...)` makes the divider drag itself, reporting the first pane's extent in design units, with a grab band wider than the drawn line), `Leaf` (a `Content`). Chrome lives on the base node: `Width`/`Height` (`Sizing`), `Padding`, `Background`, `Hit`, `OnClick`, `CollapseThreshold`.
- **What a node DECLARES, beyond its chrome** (9.2) -- `OnPress` (a press with its position, returning a `DragCapture` to own the drag that follows), `OnActivate` (what Enter does when it differs from a click), `Tooltip`, `DisabledReason` (`IsDisabled`), `Scroll` (a `ListScrollController` whose viewport this node's arranged rect IS), `Shortcut` (a `KeyChord`), and since 11.1 `OnDoubleClick` (`.DoubleClickable`, run on the second press in place of the click). The painter binds every one of them to the same arranged rect it drew in, so what is declared and what responds cannot drift. Painting a tooltip and firing a shortcut belong to the router; declaring them does not wait for it.
- **Controls built from those declarations** (11.1) -- `Builder.ButtonGroup<T>` (a segmented control whose `ButtonGroupStyle` owns which segment looks chosen; every segment swallows its press, only an actionable one lights, a null `onSelect` makes it a display the press passes through) and `Builder.Checkbox` (a well with a drawn `IconKind.Check`, the whole row toggling), beside the existing `Builder.Dropdown`. They add no mechanism; they state once the decisions a hand-built copy gets wrong.
- **`Layout.Content`** — leaf payload: `Text` (value + colour + alignment), `Box` (fixed icon/swatch/spacer), `Fill` (an app-drawn escape hatch — chart, image, text input; routed by `Key`).
- **`Layout.Sizing`** — `Fixed(designUnits)` | `Auto` (shrink-to-content) | `Star(weight, min, max)` (proportional split of leftover). Values are *design units* mapped to surface units (px × DPI, or character cells) by `Layout.IMeasureContext`. `Min`/`Max` clamp the resolved extent of an Auto/Star axis (0 = unclamped): a min-clamped Star holds its floor and overflows *visibly* instead of starving to zero when Fixed siblings eat the container, and a max-clamped Star's surplus redistributes to its Star siblings.
- **Content-capped Star** (11.22) -- `Sizing.StarUpToContent(weight, min)`, fluent `.WStarUpToContent(weight, min)` / `.HStarUpToContent(…)`: a Star whose share is capped at its own measured content (its `WidthSample`, if it has one). Along a Stack's axis these are served *before* the plain Stars: with room each sits at exactly its content and the surplus goes to the plain Stars; in a short row the plain Stars give way first, then the content-capped ones shrink by weight toward `min` (one whose share exceeds its content hands the excess to the others) and their text fits through its own `TextTrim`. A plain Star's own `Min` is reserved before they are served. With no plain Star the rest of the row stays unused, as it does behind a `Max`-capped Star. It counts its content in an Auto Stack's natural size, and across a Stack it takes its content rather than stretching; in a Dock strip, a Grid column or a Wrap it acts as a plain Star. The shape for a status strip: labels that read in full at 1.0x, a hint that absorbs the slack, and the labels trimming only once the buttons have taken the row.
- **Collapse-below-minimum** — `.CollapseBelow(designUnits)`: when a parent `Stack` would give the node a main-axis extent under the threshold, it drops out of the arrangement entirely (not painted, no hit, no gap) and its space redistributes to the survivors. The declarative form of "show the strip only when it is at least N tall".
- **`Layout.Engine.Arrange(root, rect, ctx)`** — two-pass measure/arrange; returns a pre-order `ImmutableArray<Layout.ArrangedNode>` the painter walks in order for correct z-stacking. Generic over the coordinate type (`float` pixels / `int` cells), so it is headless-testable with a stub `IMeasureContext`.

### `Layout.Builder` DSL

Author trees with the fluent DSL rather than `new Layout.Node.X { }` initializers — it emits the same records:

```csharp
using Layout = DIR.Lib.Layout;   // alias once per project; keep `using DIR.Lib;`

Layout.Builder.HStack(
        Layout.Builder.Text(label, 14f, dim).WStar(0.35f).HStar(),
        Layout.Builder.Fill(key: "input").Stretch())
    .RowH(28f)
    .Bg(active ? activeBg : normalBg)
    .Clickable(new HitResult.ButtonHit("go"), onClick);
```

- **Factories** — `VStack` / `HStack` / `Text` / `Box` / `Fill` / `Spacer` / `Grid` / `WrapH` / `WrapV` / `Overlay` / `Split` / `Dock` (+ `Left`/`Right`/`Top`/`Bottom` dock-strip helpers).
- **Fluent modifiers** (instance methods on `Layout.Node`, each a pure `this with { … }` transform) -- `.W`/`.H`/`.WFixed`/`.WStar(weight, min, max)`/`.WStarUpToContent(weight, min)`/`.WAuto` (+ `H*`), `.WClamp`/`.HClamp(min, max)` (clamp the current kind), `.RowH(u)` (full-width row), `.ColW(u)` (fixed-width column), `.Stretch()` (fill both axes), `.Bg`, `.Radius(u)`, `.Pad`, `.Clickable(hit, onClick?)`, `.CollapseBelow(u)`, `.WithGap`/`.WithGaps`/`.WithLineGap`/`.WithColumns`.
- **Behaviour modifiers** (9.2) -- `.Pressable(hit, onPress)` (the press is handed a `PointerPress` and returns a `DragCapture` or null to decline, so a slider arms its drag from the node it was painted on), `.Activatable(onActivate)` (Enter pins where a click selects), `.WithTooltip(text)` / `.WithTooltip(text, side)`, `.Disabled(reason)` / `.Disabled(when, reason)` (dims the subtree, swallows the press with a `NotAllowed` cursor, and the list cursor steps over it), `.WithScroll(controller)` (the arranged rect becomes the viewport; `PixelWidgetBase.ScrollTargetAt(x, y)` answers which list a wheel belongs to), `.WithShortcut(key, mods)`. **They are named `With*` where a bare verb would shadow the property it sets** -- the rule `.WithCursor` and `.WithGap` already follow.
- **`Grid` column sizing** -- `.WithColumns(Sizing.Auto, Sizing.Star(), …)`: `Auto` takes its own column's widest cell, `Fixed` is fixed, `Star` shares the rest, columns past the stated ones are `Star(1)`, and stating none keeps the even split a grid has always done. That is a table without any column arithmetic at the call site.
- **`.Radius(u)` is chrome, not geometry.** It rounds the node's `.Bg` (and a `Box` leaf's own fill) by `u`
  design units, but arrange never sees it — a rounded node occupies and insets exactly the rect a square one
  would, so rounding a panel can never shift the layout inside or around it. Each surface honours it as far
  as it can: a pixel painter via `Renderer.FillRoundedRectangle` (which a GPU backend may override with one
  SDF quad), a cell painter via arc corners `╭ ╮ ╰ ╯`, since a character grid cannot round by fractions of a
  cell. A radius of `0` takes the plain `FillRectangle` path, so untouched trees paint byte-identically.
- **Consumer convention** — alias `using Layout = DIR.Lib.Layout;` (a `global using`, or a csproj `<Using Include="DIR.Lib.Layout" Alias="Layout" />`) and write the qualified `Layout.Node` / `Layout.Builder`. Do **not** `using DIR.Lib.Layout;` directly — it drops the collision-prone barewords (`Node`, `Content`, `Size<T>`) into scope. (A plain `using DIR.Lib;` does not surface the nested `Layout` namespace; a using-directive imports types, not nested namespaces.) A consumer that already owns a `Layout` type must rename it.

### Data views and the grouped tree table

The data views are builders over the same declarations, not widgets: `Builder.VirtualList` builds only the rows in view, however many there are; `TableHeader` and `TableRow` take one span of `TableColumn`, so a title sits over its values (and over a dragged width); `TreeRow` is the indent, the caret and the content of one tree row.

`GroupedTreeState` + `Builder.GroupedTreeTable` (11.54) put them together for a table whose rows sit in groups, nested to any depth (org, repo, row). It is for pilot's Pull requests page, from which it was upstreamed, and for tianwen:

- **The tree is yours.** Hand over `GroupedTreeNode<TGroup, TRow>` items (`.Group(key, payload, children)` and `.Leaf(key, payload)`), each with a stable string key that is unique among the visible items. The widget never reorders: sort each group's children before handing the tree over.
- **The state is keyed, never by object.** `GroupedTreeState` holds the folded keys and `SelectedKey`, so a tree rebuilt from fresh objects on every refresh, re-sorted or filtered, keeps its folds and its selection. Groups are open unless folded, so a new group is open. If the selected item disappears, the selection moves to the nearest row that survived.
- **One list.** Every visible row, group header or leaf, is row `i` of one list id, so the click, the cursor and the lit row agree. A header spans the whole row (caret, then your content: name, counts, actions); a leaf is a `TableRow` in the header's columns.
- **The keyboard** is `HandleTableKey`, called after the widget's own `HandleListKey`: the list keeps Up, Down and Enter, and the state adds Right (unfold, or onto the first child), Left (fold, or to the parent) and Enter on a group (toggle).
- **Folds persist.** `Save()` returns plain data (the folded keys, sorted); `Restore` / `GroupedTreeState.From` read it back, and `Changed` says when to save. Keys of groups not in the tree right now are kept, so filtering a group away does not unfold it. The selection is not part of it.

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using DIR.Lib;
using Layout = DIR.Lib.Layout;

// What a group header and a leaf row carry: your own types.
record OrgOrRepo(string Name, int Open);
record PullRequest(int Number, string Title, string Author, string Checks);

// Folds are plain data, so a source-generated context saves them with no reflection.
[JsonSerializable(typeof(Layout.GroupedTreeState.Saved))]
partial class SettingsJson : JsonSerializerContext;

sealed class PullRequestsPage : PixelWidgetBase<RgbaImage>
{
    const string ListId = "prs";

    static readonly Layout.TableColumn[] Columns =
    [
        new("#", Layout.Sizing.Fixed(56f), TextAlign.Far),
        new("Title", Layout.Sizing.Star()),
        new("Author", Layout.Sizing.Fixed(120f)),
        new("Checks", Layout.Sizing.Fixed(80f)),
    ];

    readonly Layout.GroupedTreeState _tree = new();
    readonly ListScrollController _scroll = new();
    readonly Layout.GroupedTreeTableStyle _style;
    readonly string _foldsFile;
    IReadOnlyList<Layout.GroupedTreeNode<OrgOrRepo, PullRequest>> _roots = [];
    List<Layout.GroupedTreeRow<OrgOrRepo, PullRequest>> _rows = [];

    public PullRequestsPage(Renderer<RgbaImage> renderer, Action requestFrame, string foldsFile) : base(renderer)
    {
        _foldsFile = foldsFile;
        _style = new(new Layout.TableStyle(Grey(0x20), Grey(0xe0), Grey(0x30), Grey(0xff)),
                     new Layout.TreeRowStyle(Grey(0xc0)));

        // Restore the folds of the last run; groups the file does not name are open.
        if (File.Exists(foldsFile) &&
            JsonSerializer.Deserialize(File.ReadAllText(foldsFile), SettingsJson.Default.Saved) is { } saved)
        {
            _tree.Restore(saved);
        }

        // Raised on every fold change: ask for a frame and save.
        _tree.Changed = () =>
        {
            File.WriteAllText(_foldsFile, JsonSerializer.Serialize(_tree.Save(), SettingsJson.Default.Saved));
            requestFrame();
        };

        ListCursor.Moved += i => _scroll.EnsureVisible(i);   // the arrows scroll the row they land on into view
        ListCursor.Open(ListId);                             // the table has the keyboard (Close() when it gives it up)
    }

    static RGBAColor32 Grey(byte v) => new(v, v, v, 0xff);

    // The tree is yours: key every item with something stable (an id, "org/repo"), and sort each
    // group's children yourself before handing it over, because the widget never reorders.
    public void SetPullRequests(IEnumerable<(string Org, string Repo, PullRequest Pr)> prs)
    {
        _roots =
        [
            .. prs.GroupBy(p => p.Org).OrderBy(g => g.Key).Select(org =>
                Layout.GroupedTreeNode<OrgOrRepo, PullRequest>.Group($"org:{org.Key}", new(org.Key, org.Count()),
                [
                    .. org.GroupBy(p => p.Repo).OrderBy(g => g.Key).Select(repo =>
                        Layout.GroupedTreeNode<OrgOrRepo, PullRequest>.Group($"repo:{org.Key}/{repo.Key}",
                            new(repo.Key, repo.Count()),
                            [
                                .. repo.OrderByDescending(p => p.Pr.Number).Select(p =>
                                    Layout.GroupedTreeNode<OrgOrRepo, PullRequest>.Leaf(
                                        $"pr:{org.Key}/{repo.Key}#{p.Pr.Number}", p.Pr)),
                            ])),
                ])),
        ];
    }

    public void Paint(RectF32 bounds)
    {
        BeginFrame();
        _rows = _tree.Flatten(_roots);                         // folds applied, your order kept
        _tree.SyncCursor(_rows, ListCursor, ListId);           // SelectedKey -> the row it is on now

        var table = Layout.Builder.GroupedTreeTable(Columns, _style, _rows, _tree, _scroll, ListId, rowHeight: 28f,
            groupHeader: row => Layout.Builder.HStack(         // spans the row, after the caret and the indent
                Layout.Builder.Text(row.GroupPayload!.Name, 14f, Grey(0xff)).WStar(),
                Layout.Builder.Text($"{row.GroupPayload.Open} open", 12f, Grey(0x90)).WAuto()),
            rowCells: row =>                                   // one node per column, in the columns' order
            [
                Layout.Builder.Text($"#{row.RowPayload!.Number}", 13f, Grey(0x90), TextAlign.Far),
                Layout.Builder.Text(row.RowPayload.Title, 13f, Grey(0xe0)),
                Layout.Builder.Text(row.RowPayload.Author, 13f, Grey(0xc0)),
                Layout.Builder.Text(row.RowPayload.Checks, 13f, Grey(0xc0)),
            ],
            onActivate: row => Open(row.RowPayload!));         // double-click or Enter on a leaf

        RenderLayout(table.Stretch(), bounds, fontPath: "font.ttf", scale: DesignScale.One);
    }

    public override bool HandleInput(InputEvent evt)
    {
        if (evt is not InputEvent.KeyDown(var key, var mods)) return false;

        // The list claims Up, Down and Enter on a painted row. The state follows its cursor with the
        // selection and adds Left, Right and Enter on a group. A fold raises Changed; the next Paint
        // flattens again.
        return _tree.HandleTableKey(key, HandleListKey(key, mods), ListCursor, ListId, _rows,
            row => Open(row.RowPayload!));
    }

    // SelectedKey can name a group, so look it up among the rows and check the kind.
    public PullRequest? Selected =>
        _rows.FirstOrDefault(r => r.Key == _tree.SelectedKey) is { Kind: Layout.GroupedTreeRowKind.Row } row
            ? row.RowPayload : null;

    void Open(PullRequest pr) { /* your navigation */ }
}
```

## Text Input

- **`TextInputState`** — single-line text input state machine with cursor and selection
- **`TextInputRenderer`** — renders text input using any `Renderer<T>` (blinking cursor, selection highlight, and a horizontal scroll that keeps the caret inside a box too narrow for the whole value)
- **Word motion**: `Ctrl+Left` / `Ctrl+Right` step to the start of the neighbouring word, `Shift` extends the selection with them, `Ctrl+Backspace` deletes back over one and `Ctrl+X` cuts. `InputKey.ToTextInputKey` is the one place a chord becomes a meaning, so every surface reads the keyboard the same way.
- Callbacks: `OnCommit` (async), `OnCancel`, `OnTextChanged`, `OnKeyOverride`

## Signals & Async

- **`SignalBus`** — thread-safe typed event bus. `Post<T>()` is thread-safe, `ProcessPending()` runs on the render thread.
- Built-in signals: `ActivateTextInputSignal`, `DeactivateTextInputSignal`, `RequestExitSignal`, `RequestRedrawSignal`
- **`SignalDirectory` (source-generated)** — the package ships a Roslyn source generator that emits
  `SignalDirectory.BuildFactories(SignalBus bus, …overrides)`, a `name → Action<JsonElement>` map over **every
  `*Signal` type in the consuming assembly**, with **no runtime reflection**. Each factory constructs its
  signal from a JSON payload via the reflection-free `SignalJson` scalar binders (the key is the parameter
  name, camel-cased as `System.Text.Json` does it and matched in any case, so `RA` reads `ra`; a missing
  field → the parameter's declared default; a key that names no bindable parameter is refused, with the
  keys the signal does take) and posts it to the bus. Lets a live UI inspector
  / test harness list and post any bus signal by name. Gated on the `DEBUG` symbol (nothing generated in
  Release) and on `SignalBus` being referenced; signals with a required non-scalar parameter are skipped.
- **`BackgroundTaskTracker`** — collects background tasks, checks completions per frame, logs errors via `ILogger`. Call `ProcessCompletions()` each frame, `DrainAsync()` at shutdown.

## Math Layout (`DIR.Lib.MathLayout`)

TeX-style box model for rendering mathematical expressions. Each `Box` exposes Width / Height (ascent) / Depth (descent) and paints itself relative to a (penX, baselineY) the parent provides.

- **`Box`** + **`BoxStyle`** — abstract box with baseline, plus a record of font / size / spacing parameters threaded through layout. Pixel-valued math metrics (axis height, fraction-rule thickness, …) come from the font's OpenType MATH table when available, with TeX-style ratio fallbacks.
- Box types: `GlyphBox`, `MathGlyphBox`, `HBox`, `FracBox`, `SqrtBox`, `BracketBox`, `BigOperatorBox`, `SupSubBox`, `AccentBox`, `LimitsBox`, `MatrixBox`, `OverlayBox`, `StretchyVerticalBox`
- **`BoxRasterizer.RenderToRgba(box, style)`** — rasterizes a `Box` to a transparent `RgbaImage`. Pure — no encoder coupling; callers choose PNG / sixel / half-block downstream.

## Markdown + LaTeX (`DIR.Lib.Markdown`)

A LALR.CC-driven markdown + math-mode LaTeX pipeline. The math, markdown-inline, and markdown-block grammars (`grammars/*.lalr.yaml`) are compiled at build time by the `SharpAstro.LALR.CC` source generator into partial classes baked into this assembly — no runtime parser construction, AOT-clean.

- **`MdAst`** — block & inline AST records (`MdParagraph`, `MdHeading`, `MdMathBlock`, `MdList`, `MdTable`, `MdInline`, …)
- **`MarkdownBlockVisitor`** / **`MarkdownInlineVisitor`** — visitors over the generated grammars; produce the AST.
- **`LatexUnicodeVisitor`** — math-mode LaTeX → Unicode (inline math: `$x^2$` → `x²`).
- **`BoxBuildingVisitor`** — math-mode LaTeX → deferred `Box` builders (display math: `$$..$$` → `MathLayout` box tree → `BoxRasterizer`).
- **`Mhchem`** — renders an mhchem `\ce{...}` body to Unicode (auto-subscripts, arrows, ion charges, …).
- **`MarkdownMacros`** — parser-side facade: macro expansion (`\text{}`, `\boxed{}`, `\ce{}`, `\begin/\end` environments), backslash-escape resolution, and the `RenderMathUnicode` entry point.

## Usage

```csharp
using DIR.Lib;
using DIR.Lib.MathLayout;
using DIR.Lib.Markdown;

// Rendering
renderer.FillRectangle(rect, new RGBAColor32(0x30, 0x50, 0x90, 0xff));
renderer.DrawText("Hello", fontPath, 14f, white, layout);
renderer.DrawPolyline(points, color, thickness: 2);

// Input handling
widget.HandleInput(new InputEvent.KeyDown(InputKey.Enter, InputModifier.Ctrl));

// Pixel layout
var layout = new PixelLayout(contentRect);
var header  = layout.Dock(PixelDockStyle.Top, 28f);
var sidebar = layout.Dock(PixelDockStyle.Left, 200f);
var content = layout.Fill();

// Background tasks
tracker.Run(async () => await SaveAsync(), "Save profile");
if (tracker.ProcessCompletions(logger)) needsRedraw = true;

// Math rendering (display LaTeX → RGBA image)
var unicode = MarkdownMacros.RenderMathUnicode("E = mc^2");      // "E = mc²"
var image   = BoxRasterizer.RenderToRgba(boxBuilder(style), style);
```

## Wireframe checks in unit tests

`WireframeRenderer` is a CPU renderer that records where a frame puts its text, its opaque fills and its
clickable regions, and `Wireframe.Check` says what of it a person would see as broken. A row that only
overlaps at half a window's width fails a test instead of waiting to be seen:

```csharp
foreach (var width in new uint[] { 560, 800, 1100, 1700, 2400 })
{
    using var renderer = new WireframeRenderer(width, 900);
    renderer.Begin();
    new MyTab<RgbaImage>(renderer) { FontPath = font }.Render(state, new RectF32(0, 0, width, 900));
    Wireframe.Describe(Wireframe.Check(renderer.Frame)).ShouldBeEmpty();
}
```

The rules: text over text, text half on a control, two controls sharing a place, anything past the surface,
and text a clip cut with no ellipsis. Each is decided in paint order, so the page under an open popover
collides with nothing. It records at the draw call, so text drawn by hand into a slot is seen too.
`WireframeOptions.IgnoreText` leaves out labels that lie where data puts them (a map, a chart). It cannot
see what a GPU pipeline draws, and it only finds what the state it is given produces: feed it long names.

## Debug builds (`DIR.Lib.Debug`)

The published `DIR.Lib` is built Release, so the `#if DEBUG` types (`DIR.Lib.Diagnostics`, the debug
inspector) are absent from it. `DIR.Lib.Debug` is the same library at the same version, built Debug.
Reference one or the other by configuration (Visual Studio needs a restore after switching):

```xml
<ItemGroup Condition="'$(Configuration)' == 'Debug'">
  <PackageReference Include="DIR.Lib.Debug" Version="11.40.*" />
</ItemGroup>
<ItemGroup Condition="'$(Configuration)' != 'Debug'">
  <PackageReference Include="DIR.Lib" Version="11.40.*" />
</ItemGroup>
```

## Upgrading

- **[CHANGELOG.md](CHANGELOG.md)** — what changed in each `Major.Minor`, and why. Newest first.
- **[MIGRATION.md](MIGRATION.md)** — the port recipe for each BREAKING release, before/after per API.
  A major is the only place this library removes anything; the current one is **10.0**, which deleted
  `IPixelWidget.HitTestAndDispatch`, `IKeyboardClaimant`, `PixelWidgetBase.RenderDropdownMenu`,
  `HitResult.SliderHit` and `LayoutInspection`, made `TextInputState.Activate` / `Deactivate` and the
  `IsActive` setter internal, and made `HitResult.TextInputHit.Painted` required.

The version number lives in one place, `src/Directory.Build.props` (`VersionMajorMinor`); CI reads it
back rather than restating it, so a package cannot declare a version the changelog disagrees with.

## Dependencies

- [SharpAstro.Fonts](https://www.nuget.org/packages/SharpAstro.Fonts) — pure-managed OpenType font loader & rasterizer (COLRv1, MATH table, CFF/glyf, hinting)
- [SharpAstro.LALR.CC](https://www.nuget.org/packages/SharpAstro.LALR.CC) — LALR(1) parser + source generator (compile-time, build-only)
- [Microsoft.Extensions.Logging.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions) — `ILogger` interface for `BackgroundTaskTracker`

DIR.Lib is **codec-agnostic** (4.0+): `BoxRasterizer.RenderToRgba` returns an `RgbaImage`, and consumers that need TIFF / PNG / JPEG / ICC encoding declare those packages (`SharpAstro.Tiff`, `SharpAstro.Png`, `SharpAstro.Color.Icc`, …) themselves.

## License

MIT
