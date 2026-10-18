Heading 1
  Literal "Console.Lib"
Paragraph
  Literal "A .NET library for building terminal applications with dock-based layouts, widgets, mouse/keyboard input, VT styling, and Sixel graphics rendering. AOT-compatible, targeting .NET 10."
Paragraph
  Literal "This repo also ships "
  Emphasis 2
    Link url="src/MdCat/README.md"
      Literal "mdcat"
  Literal " \u2014 a "
  Code "cat"
  Literal "-for-Markdown CLI built on the library (see "
  Link url="#mdcat--cat-for-markdown"
    Literal "below"
  Literal ")."
Paragraph
  Link url="https://github.com/SharpAstro/Console.Lib/actions/workflows/dotnet.yml"
    Image url="https://github.com/SharpAstro/Console.Lib/actions/workflows/dotnet.yml/badge.svg"
      Literal "CI/CD"
Heading 2
  Literal "Features"
List ordered=False start=0 items=9
  Item
    Paragraph
      Emphasis 2
        Literal "Dock-based layout"
      Literal " \u2014 top/bottom/left/right/fill panels that recompute on terminal resize, built on DIR.Lib's surface-neutral "
      Code "DockLayout<T>"
      Literal " engine"
  Item
    Paragraph
      Emphasis 2
        Literal "Cell-surface layout"
      Literal " \u2014 "
      Code "CellLayout"
      Literal " paints DIR.Lib's arranged "
      Code "LayoutNode"
      Literal " trees (the same ones the pixel painter uses) to character cells, with click-region hit-testing"
  Item
    Paragraph
      Emphasis 2
        Literal "Widget system"
      Literal " \u2014 "
      Code "TextBar"
      Literal ", "
      Code "TextInputBar"
      Literal ", "
      Code "TextArea"
      Literal ", "
      Code "ScrollableList<T>"
      Literal ", "
      Code "TreeView<T>"
      Literal ", "
      Code "Canvas<TSurface>"
      Literal ", "
      Code "MarkdownWidget"
      Literal ", "
      Code "MenuWidget"
  Item
    Paragraph
      Emphasis 2
        Literal "Sixel graphics"
      Literal " \u2014 high-performance encoder (14\u00D7 faster than ImageMagick's built-in Sixel writer); plus Unicode sextant and half-block fallbacks for terminals without Sixel"
  Item
    Paragraph
      Emphasis 2
        Literal "VT styling"
      Literal " \u2014 "
      Code "VtStyle"
      Literal " with 16-color SGR and TrueColor modes, automatic capability detection; "
      Code "StreamColor"
      Literal " colours a CLI's plain stdout or stderr (off when redirected or under "
      Code "NO_COLOR"
      Literal ", Windows VT switched on per stream)"
  Item
    Paragraph
      Emphasis 2
        Literal "Markdown + LaTeX rendering"
      Literal " \u2014 headings, tables, lists, fenced code, inline "
      Code "[text]{color}"
      Literal " syntax, OSC 8 hyperlinks for clickable links, inline + display math ("
      Code "\\(...\\)"
      Literal ", "
      Code "$$...$$"
      Literal ", "
      Code "\\[...\\]"
      Literal "), "
      Code "\\ce{...}"
      Literal " chemistry markup, and images ("
      Code "![alt](path)"
      Literal " rasterized to Sixel / blocks, with alt-text fallback). Parsing is driven by LALR.CC grammars in "
      Link url="https://github.com/SharpAstro/DIR.Lib"
        Literal "DIR.Lib.Markdown"
      Literal "; Console.Lib is the VT-output layer."
  Item
    Paragraph
      Emphasis 2
        Literal "Input handling"
      Literal " \u2014 unified keyboard + mouse events with UTF-8 codepoint decoding, SGR mouse tracking in alternate screen"
  Item
    Paragraph
      Emphasis 2
        Literal "Menu system"
      Literal " \u2014 arrow-key navigation, digit shortcuts, mouse support, alternate/normal mode"
  Item
    Paragraph
      Emphasis 2
        Literal "Cross-platform"
      Literal " \u2014 Windows VT I/O via Win32, native VT100 on Unix/macOS"
Heading 2
  Literal "Quick start"
CodeFence lang="bash" lines=1
  "dotnet add package Console.Lib"
CodeFence lang="csharp" lines=10
  "var terminal = new VirtualTerminal();"
  "await terminal.InitAsync();"
  ""
  "terminal.EnterAlternateScreen();"
  ""
  "var panel = new Panel(terminal);"
  "var statusBar = new TextBar(panel.Dock(DockStyle.Bottom, 1));"
  "var main = panel.Fill();"
  ""
  "statusBar.Text(\" Ready\").Style(new VtStyle(SgrColor.BrightWhite, SgrColor.BrightBlack)).Render();"
Heading 2
  Literal "mdcat \u2014 "
  Code "cat"
  Literal " for Markdown"
Paragraph
  Emphasis 2
    Literal "mdcat"
  Literal " is a tiny CLI that renders a Markdown file (or stdin) to styled terminal"
  LineBreak soft
  Literal "output using Console.Lib's "
  Code "MarkdownRenderer"
  Literal " \u2014 headings, bold/italic, lists, tables,"
  LineBreak soft
  Literal "fenced code, OSC 8 hyperlinks, LaTeX math, and local images (Sixel / sextant /"
  LineBreak soft
  Literal "half-block, auto-detected). Color support is auto-detected, with a richer GitHub-Dark"
  LineBreak soft
  Literal "palette on truecolor terminals."
CodeFence lang="bash" lines=3
  "dotnet tool install --global mdcat   # .NET global tool (needs the .NET 10 runtime)"
  "mdcat README.md                      # render a file"
  "cat notes.md | mdcat -               # render from stdin"
Paragraph
  Literal "Prebuilt, self-contained native-AOT binaries (no runtime required) for Linux / Windows /"
  LineBreak soft
  Literal "macOS on x64 and arm64 are on the "
  Link url="https://github.com/SharpAstro/Console.Lib/releases"
    Literal "Releases"
  LineBreak soft
  Literal "page. See "
  Link url="src/MdCat/README.md"
    Code "src/MdCat/README.md"
  Literal " for all options, install methods,"
  LineBreak soft
  Literal "exit codes, and math-font details."
Heading 2
  Literal "Building"
CodeFence lang="bash" lines=3
  "dotnet build src/Console.Lib"
  "dotnet test src/Console.Lib.Tests"
  "dotnet run --project src/MdCat -- README.md   # build & run the mdcat CLI in place"
Heading 2
  Literal "Documentation"
Paragraph
  Literal "See "
  Link url="src/Console.Lib/README.md"
    Code "src/Console.Lib/README.md"
  Literal " for the full API reference, architecture diagram, and detailed usage guide."
Heading 2
  Literal "License"
Paragraph
  Link url="LICENSE"
    Literal "MIT"
