# DIR.Lib

**D**evice-**I**ndependent input + **R**endering library for .NET. Provides the shared foundation for both GPU (SDL3 + Vulkan) and terminal (Console) applications. Pure-managed, AOT-compatible, no native dependencies.

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

## Input Handling

- **`InputEvent`** — open record hierarchy: `KeyDown`, `TextInput`, `MouseDown`, `MouseUp`, `MouseMove`, `Scroll`, `Pinch`, `PinchEnd`
- **`InputKey`** — platform-agnostic key codes (letters, digits, function keys, navigation, symbols)
- **`InputModifier`** — modifier flags (Shift, Ctrl, Alt)
- **`MouseButton`** — Left / Middle / Right
- **`IWidget`** — shared interface with `HandleInput` for both pixel and terminal widgets

Platform bridges (in downstream packages):
- `SdlVulkan.Renderer` provides `SdlInputMapping` (SDL3 Scancode → InputKey)
- `Console.Lib` provides `ConsoleInputMapping` (ConsoleKey → InputKey)

## Widget System

- **`IPixelWidget`** — extends IWidget with pixel-coordinate hit testing and click dispatch
- **`PixelWidgetBase<TSurface>`** — base class for pixel widgets: clickable regions, text input, buttons, dropdowns, drawing helpers
- **`PixelLayout`** + **`PixelDockStyle`** — dock-based layout engine (Top/Bottom/Left/Right/Fill)
- **`DockLayout<T>`** — generic dock layout using `INumber<T>` (the integer / pixel layouts above are built on this)
- **`ClickableRegion`** + **`ClickableRegionTracker`** — registered during render, walked in reverse for hit testing
- **`HitResult`** — open discriminated union: `TextInputHit`, `ButtonHit`, `ListItemHit`, `SlotHit<T>`, `SliderHit`
- **`DropdownMenuState`** — dropdown / popup menu state machine

## Declarative Layout (`DIR.Lib.Layout`)

A surface-agnostic declarative layout engine. Describe a tree of immutable records; the engine measures + arranges it into rects; a per-surface painter (`PixelWidgetBase.PaintLayout`) draws each node and binds its click region **from the same arranged rect** — draw == hit by construction, with no separate hit-rect arithmetic that can drift.

- **`Layout.Node`** — the tree. Variants: `Stack` (vertical/horizontal), `Dock` (edge strips + a fill remainder), `Grid`, `Wrap` (children flow and wrap into new lines when out of extent — the flexbox `wrap` for toolbars/chip rows on narrow surfaces), `Overlay` (base/top, for modals/popups), `Split` (two resizable panes + a draggable divider), `Leaf` (a `Content`). Chrome lives on the base node: `Width`/`Height` (`Sizing`), `Padding`, `Background`, `Hit`, `OnClick`, `CollapseThreshold`.
- **`Layout.Content`** — leaf payload: `Text` (value + colour + alignment), `Box` (fixed icon/swatch/spacer), `Fill` (an app-drawn escape hatch — chart, image, text input; routed by `Key`).
- **`Layout.Sizing`** — `Fixed(designUnits)` | `Auto` (shrink-to-content) | `Star(weight, min, max)` (proportional split of leftover). Values are *design units* mapped to surface units (px × DPI, or character cells) by `Layout.IMeasureContext`. `Min`/`Max` clamp the resolved extent of an Auto/Star axis (0 = unclamped): a min-clamped Star holds its floor and overflows *visibly* instead of starving to zero when Fixed siblings eat the container, and a max-clamped Star's surplus redistributes to its Star siblings.
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
- **Fluent modifiers** (instance methods on `Layout.Node`, each a pure `this with { … }` transform) — `.W`/`.H`/`.WFixed`/`.WStar(weight, min, max)`/`.WAuto` (+ `H*`), `.WClamp`/`.HClamp(min, max)` (clamp the current kind), `.RowH(u)` (full-width row), `.ColW(u)` (fixed-width column), `.Stretch()` (fill both axes), `.Bg`, `.Pad`, `.Clickable(hit, onClick?)`, `.CollapseBelow(u)`, `.WithGap`/`.WithGaps`/`.WithLineGap`.
- **Consumer convention** — alias `using Layout = DIR.Lib.Layout;` (a `global using`, or a csproj `<Using Include="DIR.Lib.Layout" Alias="Layout" />`) and write the qualified `Layout.Node` / `Layout.Builder`. Do **not** `using DIR.Lib.Layout;` directly — it drops the collision-prone barewords (`Node`, `Content`, `Size<T>`) into scope. (A plain `using DIR.Lib;` does not surface the nested `Layout` namespace; a using-directive imports types, not nested namespaces.) A consumer that already owns a `Layout` type must rename it.

## Text Input

- **`TextInputState`** — single-line text input state machine with cursor, selection, undo
- **`TextInputRenderer`** — renders text input using any `Renderer<T>` (blinking cursor, selection highlight)
- Callbacks: `OnCommit` (async), `OnCancel`, `OnTextChanged`, `OnKeyOverride`

## Signals & Async

- **`SignalBus`** — thread-safe typed event bus. `Post<T>()` is thread-safe, `ProcessPending()` runs on the render thread.
- Built-in signals: `ActivateTextInputSignal`, `DeactivateTextInputSignal`, `RequestExitSignal`, `RequestRedrawSignal`
- **`SignalDirectory` (source-generated)** — the package ships a Roslyn source generator that emits
  `SignalDirectory.BuildFactories(SignalBus bus, …overrides)`, a `name → Action<JsonElement>` map over **every
  `*Signal` type in the consuming assembly**, with **no runtime reflection**. Each factory constructs its
  signal from a JSON payload via the reflection-free `SignalJson` scalar binders (camelCase key = parameter
  name; missing field → the parameter's declared default) and posts it to the bus. Lets a live UI inspector
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

## Dependencies

- [SharpAstro.Fonts](https://www.nuget.org/packages/SharpAstro.Fonts) — pure-managed OpenType font loader & rasterizer (COLRv1, MATH table, CFF/glyf, hinting)
- [SharpAstro.LALR.CC](https://www.nuget.org/packages/SharpAstro.LALR.CC) — LALR(1) parser + source generator (compile-time, build-only)
- [Microsoft.Extensions.Logging.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions) — `ILogger` interface for `BackgroundTaskTracker`

DIR.Lib is **codec-agnostic** (4.0+): `BoxRasterizer.RenderToRgba` returns an `RgbaImage`, and consumers that need TIFF / PNG / JPEG / ICC encoding declare those packages (`SharpAstro.Tiff`, `SharpAstro.Png`, `SharpAstro.Color.Icc`, …) themselves.

## License

MIT
