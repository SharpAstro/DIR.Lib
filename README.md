# DIR.Lib

**D**evice-**I**ndependent input + **R**endering library for .NET. Provides the shared foundation for both GPU (SDL3 + Vulkan) and terminal (Console) applications. Pure-managed, AOT-compatible, no native dependencies.

## Rendering Primitives

- **`PointInt`** — 2D integer point
- **`RectInt`** — 2D integer rectangle (note: constructor argument order is `(LowerRight, UpperLeft)`)
- **`RectF32`** — 2D float rectangle (x, y, width, height) for pixel-based layout
- **`RGBAColor32`** — 32-bit RGBA color with Lerp, WithAlpha, Luminance
- **`TextAlign`** — Near/Center/Far alignment enum
- **`Renderer<TSurface>`** — Abstract renderer: FillRectangle, DrawRectangle, FillEllipse / DrawEllipse, DrawLine, DrawLineDashed, DrawPolyline, DrawPolylineDashed, DrawText, MeasureText
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

## Text Input

- **`TextInputState`** — single-line text input state machine with cursor, selection, undo
- **`TextInputRenderer`** — renders text input using any `Renderer<T>` (blinking cursor, selection highlight)
- Callbacks: `OnCommit` (async), `OnCancel`, `OnTextChanged`, `OnKeyOverride`

## Signals & Async

- **`SignalBus`** — thread-safe typed event bus. `Post<T>()` is thread-safe, `ProcessPending()` runs on the render thread.
- Built-in signals: `ActivateTextInputSignal`, `DeactivateTextInputSignal`, `RequestExitSignal`, `RequestRedrawSignal`
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
