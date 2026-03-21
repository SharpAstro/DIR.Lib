# DIR.Lib

**D**evice-**I**ndependent input + **R**endering library for .NET. Provides the shared foundation for both GPU (SDL3 + Vulkan) and terminal (Console) applications.

## Rendering Primitives

- **`PointInt`** — 2D integer point
- **`RectInt`** — 2D integer rectangle (upper-left + lower-right)
- **`RectF32`** — 2D float rectangle (x, y, width, height) for pixel-based layout
- **`RGBAColor32`** — 32-bit RGBA color with Lerp, WithAlpha, Luminance
- **`TextAlign`** — Near/Center/Far alignment enum
- **`Renderer<TSurface>`** — Abstract renderer: FillRectangle, DrawRectangle, FillEllipse, DrawText, MeasureText
- **`GlyphBitmap`** — Raw RGBA glyph bitmap with bearing/advance info
- **`FreeTypeGlyphRasterizer`** — FreeType2-based glyph rasterizer with COLRv1 color emoji support

## Input Handling

- **`InputKey`** — Platform-agnostic key codes (letters, digits, function keys, navigation, symbols)
- **`InputModifier`** — Modifier flags (Shift, Ctrl, Alt)
- **`IWidget`** — Shared interface with `HandleKeyDown` and `HandleMouseWheel` for both pixel and terminal widgets

Platform bridges (in downstream packages):
- `SdlVulkan.Renderer` provides `SdlInputMapping` (SDL3 Scancode → InputKey)
- `Console.Lib` provides `ConsoleInputMapping` (ConsoleKey → InputKey)

## Widget System

- **`IPixelWidget`** — Extends IWidget with pixel-coordinate hit testing and click dispatch
- **`PixelWidgetBase<TSurface>`** — Base class for pixel widgets: clickable regions, text input, buttons, drawing helpers
- **`PixelLayout`** + **`PixelDockStyle`** — Dock-based layout engine (Top/Bottom/Left/Right/Fill)
- **`ClickableRegion`** — Registered during render, walked in reverse for hit testing
- **`HitResult`** — Open discriminated union: TextInputHit, ButtonHit, ListItemHit, SlotHit\<T\>, SliderHit

## Text Input

- **`TextInputState`** — Single-line text input state machine with cursor, selection, undo
- **`TextInputKey`** — Abstract key actions (Backspace, Delete, Left, Right, Home, End, Enter, Escape)
- **`TextInputRenderer`** — Renders text input using any `Renderer<T>` (blinking cursor, selection highlight)
- Callbacks: `OnCommit` (async), `OnCancel`, `OnTextChanged`, `OnKeyOverride`

## Async Operations

- **`BackgroundTaskTracker`** — Collects background tasks, checks completions per frame, logs errors via `ILogger`. Call `ProcessCompletions()` each frame, `DrainAsync()` at shutdown.

## Usage

```csharp
using DIR.Lib;

// Rendering
renderer.FillRectangle(rect, new RGBAColor32(0x30, 0x50, 0x90, 0xff));
renderer.DrawText("Hello", fontPath, 14f, white, layout);

// Input handling (SDL3 example)
var key = evt.Key.Scancode.ToInputKey;       // via SdlVulkan.Renderer
var mod = evt.Key.Mod.ToInputModifier;
widget.HandleKeyDown(key, mod);

// Pixel layout
var layout = new PixelLayout(contentRect);
var header = layout.Dock(PixelDockStyle.Top, 28f);
var sidebar = layout.Dock(PixelDockStyle.Left, 200f);
var content = layout.Fill();

// Background tasks
tracker.Run(async () => await SaveAsync(), "Save profile");
if (tracker.ProcessCompletions(logger)) needsRedraw = true;
```

## Dependencies

- [SharpAstro.FreeTypeBindings](https://www.nuget.org/packages/SharpAstro.FreeTypeBindings) — FreeType2 native bindings
- [Microsoft.Extensions.Logging.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions) — ILogger interface for BackgroundTaskTracker

## License

MIT
