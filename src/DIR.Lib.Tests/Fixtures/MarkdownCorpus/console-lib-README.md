# Console.Lib

A .NET library for building terminal applications with dock-based layouts, widgets, mouse/keyboard input, VT styling, and Sixel graphics rendering. AOT-compatible, targeting .NET 10.

This repo also ships **[mdcat](src/MdCat/README.md)** — a `cat`-for-Markdown CLI built on the library (see [below](#mdcat--cat-for-markdown)).

[![CI/CD](https://github.com/SharpAstro/Console.Lib/actions/workflows/dotnet.yml/badge.svg)](https://github.com/SharpAstro/Console.Lib/actions/workflows/dotnet.yml)

## Features

- **Dock-based layout** — top/bottom/left/right/fill panels that recompute on terminal resize, built on DIR.Lib's surface-neutral `DockLayout<T>` engine
- **Cell-surface layout** — `CellLayout` paints DIR.Lib's arranged `LayoutNode` trees (the same ones the pixel painter uses) to character cells, with click-region hit-testing
- **Widget system** — `TextBar`, `TextInputBar`, `TextArea`, `ScrollableList<T>`, `TreeView<T>`, `Canvas<TSurface>`, `MarkdownWidget`, `MenuWidget`
- **Sixel graphics** — high-performance encoder (14× faster than ImageMagick's built-in Sixel writer); plus Unicode sextant and half-block fallbacks for terminals without Sixel
- **VT styling** — `VtStyle` with 16-color SGR and TrueColor modes, automatic capability detection; `StreamColor` colours a CLI's plain stdout or stderr (off when redirected or under `NO_COLOR`, Windows VT switched on per stream)
- **Markdown + LaTeX rendering** — headings, tables, lists, fenced code, inline `[text]{color}` syntax, OSC 8 hyperlinks for clickable links, inline + display math (`\(...\)`, `$$...$$`, `\[...\]`), `\ce{...}` chemistry markup, and images (`![alt](path)` rasterized to Sixel / blocks, with alt-text fallback). Parsing is driven by LALR.CC grammars in [DIR.Lib.Markdown](https://github.com/SharpAstro/DIR.Lib); Console.Lib is the VT-output layer.
- **Input handling** — unified keyboard + mouse events with UTF-8 codepoint decoding, SGR mouse tracking in alternate screen
- **Menu system** — arrow-key navigation, digit shortcuts, mouse support, alternate/normal mode
- **Cross-platform** — Windows VT I/O via Win32, native VT100 on Unix/macOS

## Quick start

```bash
dotnet add package Console.Lib
```

```csharp
var terminal = new VirtualTerminal();
await terminal.InitAsync();

terminal.EnterAlternateScreen();

var panel = new Panel(terminal);
var statusBar = new TextBar(panel.Dock(DockStyle.Bottom, 1));
var main = panel.Fill();

statusBar.Text(" Ready").Style(new VtStyle(SgrColor.BrightWhite, SgrColor.BrightBlack)).Render();
```

## mdcat — `cat` for Markdown

**mdcat** is a tiny CLI that renders a Markdown file (or stdin) to styled terminal
output using Console.Lib's `MarkdownRenderer` — headings, bold/italic, lists, tables,
fenced code, OSC 8 hyperlinks, LaTeX math, and local images (Sixel / sextant /
half-block, auto-detected). Color support is auto-detected, with a richer GitHub-Dark
palette on truecolor terminals.

```bash
dotnet tool install --global mdcat   # .NET global tool (needs the .NET 10 runtime)
mdcat README.md                      # render a file
cat notes.md | mdcat -               # render from stdin
```

Prebuilt, self-contained native-AOT binaries (no runtime required) for Linux / Windows /
macOS on x64 and arm64 are on the [Releases](https://github.com/SharpAstro/Console.Lib/releases)
page. See [`src/MdCat/README.md`](src/MdCat/README.md) for all options, install methods,
exit codes, and math-font details.

## Building

```bash
dotnet build src/Console.Lib
dotnet test src/Console.Lib.Tests
dotnet run --project src/MdCat -- README.md   # build & run the mdcat CLI in place
```

## Documentation

See [`src/Console.Lib/README.md`](src/Console.Lib/README.md) for the full API reference, architecture diagram, and detailed usage guide.

## License

[MIT](LICENSE)
