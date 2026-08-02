# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

DIR.Lib is a Device-Independent input + Rendering library for .NET — the shared foundation for GPU (SDL3+Vulkan), terminal (Console), and browser (WebGL2 / Blazor WebAssembly) SharpAstro applications. It provides platform-agnostic rendering primitives, a widget system with hit testing, input handling, a signal bus, and a pure-managed font rasterizer (no native dependencies).

## Build & Test Commands

```bash
# Build (solution lives at the repo root)
dotnet build DIR.Lib.sln

# Run all tests
dotnet test src/DIR.Lib.Tests

# Run a single test
dotnet test src/DIR.Lib.Tests --filter "FullyQualifiedName~TestMethodName"

# Run tests in a specific class
dotnet test src/DIR.Lib.Tests --filter "FullyQualifiedName~RenderAcceptanceTests"

# Regenerate visual baselines (only after investigating failures)
DIR_LIB_UPDATE_BASELINES=1 dotnet test src/DIR.Lib.Tests --filter "FullyQualifiedName~RenderAcceptanceTests"
```

CI runs tests in Release config after building, before publishing NuGet packages.

## Architecture

**Namespaces:** Core types live in the root `DIR.Lib` namespace. Two sub-namespaces host larger subsystems:
- `DIR.Lib.MathLayout` — TeX-style box model (`Box`, `BoxStyle`, `BoxRasterizer`, and the per-construct boxes: `FracBox`, `SqrtBox`, `BracketBox`, `BigOperatorBox`, `SupSubBox`, …).
- `DIR.Lib.Markdown` — markdown / LaTeX pipeline (`MdAst`, `MarkdownBlockVisitor`, `MarkdownInlineVisitor`, `LatexUnicodeVisitor`, `BoxBuildingVisitor`, `Mhchem`, `MarkdownMacros`). Public since 4.1; no friend-assembly grants needed.

**Core abstractions:**
- `Renderer<TSurface>` — abstract generic renderer; backends (SDL/Vulkan, Console) implement this in downstream repos. Provides default polyline + dashed-line implementations on top of the abstract rect / ellipse / text primitives.
- `RgbaImageRenderer : Renderer<RgbaImage>` — pure software renderer used in tests and headless scenarios
- `IWidget` / `IPixelWidget` — widget interfaces with input handling and hit testing
- `PixelWidgetBase<TSurface>` — base class for pixel-based widgets, manages clickable regions, drawing helpers, dropdowns, text inputs
- `InputEvent` — abstract record hierarchy (open discriminated union): `KeyDown`, `TextInput`, `MouseDown`, `MouseUp`, `MouseMove`, `Scroll`, `Pinch`, `PinchEnd`
- `HitResult` — open record hierarchy for click dispatch: `TextInputHit`, `ButtonHit`, `ListItemHit`, `SlotHit<T>`, `SliderHit`
- `SignalBus` — thread-safe typed event bus; `Post<T>()` is thread-safe, `ProcessPending()` runs on render thread
- `DockLayout<T>` — generic dock layout engine using `INumber<T>`
- `ManagedFontRasterizer` — pure-managed glyph rasterizer (AOT-compatible) backed by `SharpAstro.Fonts.OpenTypeFont`; supports COLRv1 color glyphs, grayscale, and PDF subset fonts
- `FontResolver` — platform-default monospace lookup + cross-platform installed-font enumeration (including Win11 per-user font dir)
- `BoxRasterizer.RenderToRgba` (in `DIR.Lib.MathLayout`) — math-layout entry point; returns a raw `RgbaImage` so the caller picks the encoder (PNG / sixel / half-block / …)

**Key design constraints:**
- **AOT compatibility is required** (`IsAotCompatible = true`) — no reflection-based patterns. No native bindings: the font rasterizer is pure-managed.
- `AllowUnsafeBlocks` is enabled in both library and tests
- `RectInt(PointInt LowerRight, PointInt UpperLeft)` — note the unusual constructor argument order (LowerRight first)
- Uses C# 14 `extension` keyword syntax (net10.0 preview features)
- **Codec divorce (4.0+):** DIR.Lib does not depend on any image-codec package. `BoxRasterizer.RenderToRgba` returns an `RgbaImage`; consumers that need TIFF / PNG / JPEG / ICC declare those packages (`SharpAstro.Tiff`, `SharpAstro.Png`, `SharpAstro.Color.Icc`, …) themselves.

**Font dependency:** `SharpAstro.Fonts` is loaded as a local `ProjectReference` if the sibling `Fonts.Lib` repo exists at `../../../Fonts.Lib/`, otherwise falls back to a NuGet `PackageReference`. Controlled by `$(UseLocalFontsLib)`.

**Grammar / parser dependency:** `SharpAstro.LALR.CC` provides a Roslyn source generator that compiles `grammars/latex.lalr.yaml`, `grammars/markdown-inline.lalr.yaml`, and `grammars/markdown-block.lalr.yaml` at build time into partial classes (`Latex`, `MarkdownInline`, `MarkdownBlock`) in this assembly's root namespace. YamlDotNet is a build-only dependency consumed by the source generator (`PrivateAssets="all"`, passed in via `<Analyzer>`); it does not appear in the runtime closure. Like Fonts, LALR.CC switches between `ProjectReference` (sibling `LALR.CC/` checkout) and `PackageReference` via `$(UseLocalLalrCc)`.

## Test Structure

- **Framework:** xunit v3 + Shouldly assertions
- **Visual regression tests** (`RenderAcceptanceTests.cs`, `MathLayoutBaselineTests.cs`, `MathStretchyTests.cs`, `DrawLineTests.cs`, `DrawPolylineTests.cs`): compare rendered output against baseline BMP files in `Baselines/`. Set `DIR_LIB_UPDATE_BASELINES=1` to regenerate.
- **Markdown / LaTeX spike tests** (`MarkdownBlockSpikeTests.cs`, `MarkdownInlineSpikeTests.cs`, `MhchemTests.cs`): exercise the grammar visitors directly.
- **Test fonts** are in `src/DIR.Lib.Tests/Fonts/` — each fixture font has a specific purpose (e.g., Merida is chess-only, subset fonts test PDF embedding scenarios).
- The tests project declares `SharpAstro.Png` directly because the visual tests emit / decode PNG artifacts — DIR.Lib itself no longer pulls codec packages transitively.

## Package Versioning

Central Package Management via `src/Directory.Packages.props` — all package versions are defined there, never in individual `.csproj` files. The library version prefix is in `src/DIR.Lib/DIR.Lib.csproj` (`VersionPrefix`).
