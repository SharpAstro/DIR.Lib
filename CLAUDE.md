# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

DIR.Lib is a Device-Independent input + Rendering library for .NET — the shared foundation for both GPU (SDL3+Vulkan) and terminal (Console) SharpAstro applications. It provides platform-agnostic rendering primitives, a widget system with hit testing, input handling, a signal bus, and a pure-managed font rasterizer (no native dependencies).

## Build & Test Commands

```bash
# Build
dotnet build src/DIR.Lib.sln

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

**Single namespace:** Everything is in `DIR.Lib` (no sub-namespaces).

**Core abstractions:**
- `Renderer<TSurface>` — abstract generic renderer; backends (SDL/Vulkan, Console) implement this in downstream repos
- `RgbaImageRenderer : Renderer<RgbaImage>` — pure software renderer used in tests and headless scenarios
- `IWidget` / `IPixelWidget` — widget interfaces with input handling and hit testing
- `PixelWidgetBase<TSurface>` — base class for pixel-based widgets, manages clickable regions, drawing helpers, dropdowns, text inputs
- `InputEvent` — abstract record hierarchy (open discriminated union): `KeyDown`, `TextInput`, `MouseDown`, `MouseUp`, `MouseMove`, `Scroll`
- `HitResult` — open record hierarchy for click dispatch: `TextInputHit`, `ButtonHit`, `ListItemHit`, `SlotHit<T>`, `SliderHit`
- `SignalBus` — thread-safe typed event bus; `Post<T>()` is thread-safe, `ProcessPending()` runs on render thread
- `DockLayout<T>` — generic dock layout engine using `INumber<T>`
- `ManagedFontRasterizer` — pure-managed glyph rasterizer (AOT-compatible) backed by `SharpAstro.Fonts.OpenTypeFont`; supports COLRv1 color glyphs, grayscale, and PDF subset fonts

**Key design constraints:**
- **AOT compatibility is required** (`IsAotCompatible = true`) — no reflection-based patterns
- `AllowUnsafeBlocks` is enabled in both library and tests
- `RectInt(PointInt LowerRight, PointInt UpperLeft)` — note the unusual constructor argument order (LowerRight first)
- Uses C# 14 `extension` keyword syntax (net10.0 preview features)

**Font dependency:** `SharpAstro.Fonts` is loaded as a local `ProjectReference` if the sibling `Fonts.Lib` repo exists at `../../../Fonts.Lib/`, otherwise falls back to a NuGet `PackageReference`. Controlled by `$(UseLocalFontsLib)`.

## Test Structure

- **Framework:** xunit v3 + Shouldly assertions
- **Visual regression tests** (`RenderAcceptanceTests.cs`): compare rendered output against baseline BMP files in `Baselines/`. Set `DIR_LIB_UPDATE_BASELINES=1` to regenerate.
- **Test fonts** are in `src/DIR.Lib.Tests/Fonts/` — each fixture font has a specific purpose (e.g., Merida is chess-only, subset fonts test PDF embedding scenarios)
- `SharpAstro.FreeTypeBindings` appears only in tests as a ground-truth reference — it is not part of the library

## Package Versioning

Central Package Management via `src/Directory.Packages.props` — all package versions are defined there, never in individual `.csproj` files. The library version prefix is in `src/DIR.Lib/DIR.Lib.csproj` (`VersionPrefix`).
