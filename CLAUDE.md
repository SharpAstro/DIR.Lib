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
- `FontFallbackResolver` — coverage-driven font runs for UI text. Faces declared by role (`FromRoles`) also get Unicode's default presentation applied: a pictograph goes to the emoji face even where the primary covers it (U+2615 is the case that found it), while text-default marks (✓ ★ ⚠ ❄, arrows) stay on the primary
- `EmojiPresentation` — the `Emoji_Presentation` property behind that. **The data half is GENERATED**: `dotnet run` in `tools/gen-emoji-presentation` rewrites `src/DIR.Lib/EmojiPresentation.Data.g.cs` from unicode.org's `emoji-data.txt`. Re-run it per Unicode release; never hand-edit the table
- `ListCursor` — the keyboard's position in a list the layout tree already declares, and the counterpart of `PixelWidgetBase.Pointer`: both resolve against the regions the last paint registered, so the rows the arrows reach and the rows a click reaches are one list. A row needs no state of its own — `ListItemHit(list, index)` is the whole declaration, and a row that is not `.Clickable` registers no region and so cannot be reached. `HandleListKey` claims Up/Down/Enter and nothing else; Escape stays with the widget
- `InputRouter`: the frame's input dispatcher, so a host writes none. `Handle(evt)` walks the regions, fields, shortcuts, drag captures and scroll controllers the last paint declared, in one fixed order, and `AfterPaint()` applies the three rules that need a finished frame (blur an unpainted field, answer a `focusOnOpen` request once, expire a stale tooltip). The rule to know before touching it: a key reaches a declared `Layout.Node.Shortcut` ahead of a focused text field only when `KeyChord.BeatsFocusedField` says so, and a shortcut is matched against the PAINTED tree, never the arranged one. A host keeps its platform binding (`FocusChanged`, the clipboard) and its own `Unhandled`
- `FloatingPalette` — a floating, grip-dragged, collapsible palette of toggle rows: a `Layout.Node` tree plus the pure rules around it (`FloatingPaletteState`, `PaletteItem`, `PaletteColors`). `Build` returns the tree and touches no surface, so a palette's geometry and its click bindings are testable with a stub measure context and no GPU. `NoteArranged` must be called back every frame — the clamp and the consumer's offset diverge silently otherwise
- `BoxRasterizer.RenderToRgba` (in `DIR.Lib.MathLayout`) — math-layout entry point; returns a raw `RgbaImage` so the caller picks the encoder (PNG / sixel / half-block / …)

**Key design constraints:**
- **An optional parameter added to a record's primary constructor is a BINARY break.** It is source-compatible, so it reads as additive and the compiler says nothing -- but the old constructor is gone from the assembly, and every already-compiled caller throws `MissingMethodException`. A positional PATTERN breaks too, at compile time in the consumer: a record's synthesized `Deconstruct` takes its arity from the primary constructor, so `Evt(var a, var b)` stops binding the moment a third parameter appears, defaulted or not. So such a change is either a MAJOR, or it ships with an **explicit old-arity constructor AND an explicit old-arity `Deconstruct`**. 9.1 learned this the expensive way: it added `TextInputGeometry Painted = default` to `HitResult.TextInputHit`, called the release "additive throughout", and the published Console.Lib (which calls the one-argument form in `CellLayout.HitOf`) threw on every terminal hit test. It was invisible on a dev box, where `UseLocalSiblings` compiles the sibling from source and the package path CI takes is never exercised. Pinned by `InputEventCompatibilityTests`, whose reflection is the only thing that can see the difference -- a call binds happily to a longer constructor with defaults, so nothing written in C# can tell them apart. Prefer an **init-only property** for a new field on a record (`ArrangedNode.Depth`, `ClickableRegion.OnPress`, `Node.Grid.ColumnSizing`): it adds nothing to the constructor's identity.
- **AOT compatibility is required** (`IsAotCompatible = true`) — no reflection-based patterns. No native bindings: the font rasterizer is pure-managed.
- `AllowUnsafeBlocks` is enabled in both library and tests
- `RectInt(PointInt LowerRight, PointInt UpperLeft)` — note the unusual constructor argument order (LowerRight first)
- Uses C# 14 `extension` keyword syntax (net10.0 preview features)
- **Codec divorce (4.0+):** DIR.Lib does not depend on any image-codec package. `BoxRasterizer.RenderToRgba` returns an `RgbaImage`; consumers that need TIFF / PNG / JPEG / ICC declare those packages (`SharpAstro.Tiff`, `SharpAstro.Png`, `SharpAstro.Color.Icc`, …) themselves.

**Font dependency:** `SharpAstro.Fonts` is loaded as a local `ProjectReference` if the sibling `Fonts.Lib` repo exists at `../../../Fonts.Lib/`, otherwise falls back to a NuGet `PackageReference`. Controlled by `$(UseLocalFontsLib)`.

**Grammar / parser dependency:** `SharpAstro.LALR.CC` provides a Roslyn source generator that compiles `grammars/latex.lalr.yaml`, `grammars/markdown-inline.lalr.yaml`, and `grammars/markdown-block.lalr.yaml` at build time into partial classes (`Latex`, `MarkdownInline`, `MarkdownBlock`) in this assembly's root namespace. YamlDotNet is a build-only dependency consumed by the source generator (`PrivateAssets="all"`, passed in via `<Analyzer>`); it does not appear in the runtime closure. Like Fonts, LALR.CC switches between `ProjectReference` (sibling `LALR.CC/` checkout) and `PackageReference` via `$(UseLocalLalrCc)`.

## Test Structure

- **Framework:** xunit v3 + Shouldly assertions
- **Visual regression tests** (`RenderAcceptanceTests.cs`, `MathLayoutBaselineTests.cs`, `MathStretchyTests.cs`, `DrawLineTests.cs`, `DrawPolylineTests.cs`): compare rendered output against baseline PNG files in `Baselines/`. Set `DIR_LIB_UPDATE_BASELINES=1` to regenerate.
- **Markdown / LaTeX spike tests** (`MarkdownBlockSpikeTests.cs`, `MarkdownInlineSpikeTests.cs`, `MhchemTests.cs`): exercise the grammar visitors directly.
- **Test fonts** are in `src/DIR.Lib.Tests/Fonts/` — each fixture font has a specific purpose (e.g., Merida is chess-only, subset fonts test PDF embedding scenarios).
- The tests project declares `SharpAstro.Png` directly because the visual tests emit / decode PNG artifacts — DIR.Lib itself no longer pulls codec packages transitively.

## Package Versioning

Central Package Management via `src/Directory.Packages.props` — all package versions are defined there, never in individual `.csproj` files.

This repo's OWN version has **one place to bump**: `VersionMajorMinor` in `src/Directory.Build.props`. Local builds get `Major.Minor.0`; the workflow reads that same property back (`dotnet msbuild src/Directory.Build.props -getProperty:VersionMajorMinor`) rather than restating the number, so CI cannot stamp a version the packages disagree with.

It covers both DIR.Lib and DIR.Lib.Shaping, because CI stamps a single `-p:Version` across them. No csproj declares its own `VersionPrefix` — a per-project one silently overrides the props file, which is how DIR.Lib.Shaping once sat at 6.8.0 while DIR.Lib shipped 7.5.0.

Add the matching entry to [CHANGELOG.md](CHANGELOG.md) at the repo root, in the same commit as the
bump. Newest first, one `## Major.Minor` section each. (The notes used to live in a comment block in
`.github/workflows/dotnet.yml`; nothing ever read them there, and they had grown to 612 of that
file's 674 lines.)
