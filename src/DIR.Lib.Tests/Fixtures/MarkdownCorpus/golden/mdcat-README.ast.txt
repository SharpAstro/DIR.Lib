Heading 1
  Literal "mdcat"
Paragraph
  Literal "A tiny "
  Code "cat"
  Literal " for Markdown. Reads a Markdown file (or stdin) and renders it to"
  LineBreak soft
  Literal "styled terminal output using "
  Link url="../Console.Lib"
    Code "Console.Lib"
  Literal "'s "
  Code "MarkdownRenderer"
  LineBreak soft
  Literal "\u2014 headings, bold/italic, inline code, lists, blockquotes, fenced code blocks,"
  LineBreak soft
  Literal "OSC 8 hyperlinks, and LaTeX math."
Heading 2
  Literal "Usage"
CodeFence lang=null lines=1
  "mdcat [options] [file]"
Table columns=2 rows=1 align=Left,Left
  Header
    Cell
      Literal "Argument"
    Cell
      Literal "Meaning"
  Row
    Cell
      Code "file"
    Cell
      Literal "Markdown file to render. Use "
      Code "-"
      Literal " for stdin. Omit to read redirected stdin, else show help."
Table columns=2 rows=5 align=Left,Left
  Header
    Cell
      Literal "Option"
    Cell
      Literal "Meaning"
  Row
    Cell
      Code "-h"
      Literal ", "
      Code "--help"
    Cell
      Literal "Show help."
  Row
    Cell
      Code "--mode <encoding>"
    Cell
      Literal "Display-math and image rendering: "
      Code "unicode"
      Literal " \\"
    Cell
      Code "sixel"
      Literal " \\"
    Cell
      Code "sextant"
      Literal " \\"
    Cell
      Code "halfblock"
      Literal ". "
      Code "unicode"
      Literal " rasters nothing: math renders as text and images as their alt text. Default: auto-detect via DA1 (sixel on capable terminals, sextant otherwise). Without colour ("
      Code "--plain"
      Literal ", "
      Code "NO_COLOR"
      Literal ") nothing rasters, whatever "
      Code "--mode"
      Literal " says, since every raster encoding is escape sequences."
  Row
    Cell
      Code "--color <mode>"
    Cell
      Code "truecolor"
      Literal " \\"
    Cell
      Code "16"
      Literal " \\"
    Cell
      Code "none"
      Literal ". Default: auto-detect."
  Row
    Cell
      Code "--no-color"
      Literal ", "
      Code "--plain"
    Cell
      Literal "Plain text, no escape sequences. Shorthand for "
      Code "--color none"
      Literal "."
  Row
    Cell
      Code "--width <N>"
    Cell
      Literal "Render width in columns. Default: console width, or 80."
Paragraph
  Emphasis 2
    Literal "Color auto-detection."
  Literal " With no "
  Code "--color"
  Literal ", mdcat emits truecolor and, when it"
  LineBreak soft
  Literal "detects a 24-bit-capable terminal ("
  Code "COLORTERM=truecolor"
  Literal "/"
  Code "24bit"
  Literal ", or a known"
  LineBreak soft
  Literal "terminal via "
  Code "WT_SESSION"
  Literal " / "
  Code "TERM_PROGRAM"
  Literal "), switches to a richer GitHub-Dark"
  LineBreak soft
  Literal "palette; otherwise it uses the default 16-color-safe palette. An explicit"
  LineBreak soft
  Code "--color"
  Literal " overrides detection. "
  Code "NO_COLOR"
  Literal " (see"
  LineBreak soft
  Link url="https://no-color.org/"
    Literal "no-color.org"
  Literal ") disables color unless "
  Code "--color"
  Literal " overrides it."
Heading 3
  Literal "Examples"
CodeFence lang="bash" lines=4
  "mdcat README.md                  # render a file"
  "cat README.md | mdcat -          # render from stdin"
  "mdcat --mode unicode notes.md    # force Unicode math (no raster)"
  "mdcat --plain README.md > out.txt # strip styling for plain capture"
Heading 2
  Literal "Exit codes"
Table columns=2 rows=3 align=Left,Left
  Header
    Cell
      Literal "Code"
    Cell
      Literal "Meaning"
  Row
    Cell
      Code "0"
    Cell
      Literal "Success (also "
      Code "--help"
      Literal ")."
  Row
    Cell
      Code "1"
    Cell
      Literal "I/O error \u2014 file not found or unreadable input."
  Row
    Cell
      Code "2"
    Cell
      Literal "Bad arguments."
Heading 2
  Literal "Math"
Paragraph
  Literal "Display math rasterizes (sixel / sextant / half-block per "
  Code "--mode"
  Literal "), whether it"
  LineBreak soft
  Literal "is a block of its own:"
CodeFence lang=null lines=3
  "$$"
  "\\int_0^1 x^2\\,dx = \\frac{1}{3}"
  "$$"
Paragraph
  Literal "or written inside a paragraph as "
  Code "$$...$$"
  Literal " or "
  Code "\\[...\\]"
  Literal ". The paragraph then"
  LineBreak soft
  Literal "splits around it, as Pandoc sets it: the text before, the raster, the text after."
  LineBreak soft
  Literal "Inline "
  Code "$...$"
  Literal " and "
  Code "\\(...\\)"
  Literal " render as single-row Unicode, never rastered, and so"
  LineBreak soft
  Literal "does display math in a heading or a table cell, where a raster cannot fit in one row."
Paragraph
  Literal "A raster is held to "
  Code "--width"
  Literal ": a formula wider than the width is re-laid at a"
  LineBreak soft
  Literal "smaller size, down to two thirds of the default, and one that still does not fit"
  LineBreak soft
  Literal "renders as Unicode instead, which wraps. Below that size a raster is no longer"
  LineBreak soft
  Literal "legible."
Paragraph
  Literal "Rastering needs an OpenType math font. mdcat "
  Emphasis 2
    Literal "bundles"
  Literal " STIX Two Math"
  LineBreak soft
  Literal "("
  Code "Fonts/STIX2Math.otf"
  Literal ", SIL OFL \u2014 see "
  Code "Fonts/STIX2-OFL.txt"
  Literal ") next to the"
  LineBreak soft
  Literal "executable, so math renders the same everywhere without relying on system"
  LineBreak soft
  Literal "fonts. If that file is ever missing, "
  Code "MarkdownRenderer"
  Literal " falls back to an"
  LineBreak soft
  Literal "internal system-font search (STIX Two Math, Cambria Math, \u2026) and, failing"
  LineBreak soft
  Literal "that, a Unicode-only approximation."
Heading 2
  Literal "Images"
Paragraph
  Literal "An image that sits "
  Emphasis 2
    Literal "alone on its own line"
  Literal " ("
  Code "![alt](path)"
  Literal ") is rendered as a"
  LineBreak soft
  Literal "picture \u2014 Sixel on capable terminals, otherwise Unicode block characters"
  LineBreak soft
  Literal "(sextant / half-block), scaled to fit the render width. An image "
  Emphasis 2
    Literal "inside a line"
    LineBreak soft
    Literal "of text"
  Literal ", or one that can't be displayed, renders as its alt text instead (the"
  LineBreak soft
  Literal "file name when the alt is empty)."
CodeFence lang="markdown" lines=1
  "![architecture diagram](docs/arch.png)"
Paragraph
  Literal "Supported formats: "
  Emphasis 2
    Literal "PNG"
  Literal " and "
  Emphasis 2
    Literal "baseline JPEG"
  Literal " (plus BMP / GIF / TGA), via a"
  LineBreak soft
  Literal "pure-managed decoder. Progressive JPEG, SVG and WebP are not supported and fall"
  LineBreak soft
  Literal "back to alt text."
Paragraph
  Emphasis 2
    Literal "No network support."
  Literal " mdcat does "
  Emphasis 2
    Literal "not"
  Literal " download images. Only "
  Emphasis 2
    Literal "local files"
  LineBreak soft
  Literal "are read \u2014 relative paths resolve against the markdown file's directory (or the"
  LineBreak soft
  Literal "current directory when reading from stdin). Remote "
  Code "http(s)://"
  Literal " and "
  Code "data:"
  Literal " URLs"
  LineBreak soft
  Literal "are never fetched and render as their alt text. Add "
  Code "--no-color"
  Literal " / "
  Code "--plain"
  Literal " (or"
  LineBreak soft
  Literal "set "
  Code "NO_COLOR"
  Literal ") to force every image to alt text."
Heading 2
  Literal "Install"
Heading 3
  Literal "As a .NET global tool (needs the .NET 10 runtime)"
CodeFence lang="bash" lines=1
  "dotnet tool install --global mdcat"
Paragraph
  Literal "Auto-updatable ("
  Code "dotnet tool update -g mdcat"
  Literal ") and discoverable via"
  LineBreak soft
  Code "dotnet tool search mdcat"
  Literal ". The bundled math font ships inside the package, so"
  LineBreak soft
  Literal "display math works out of the box."
Heading 3
  Literal "Prebuilt native binary (no runtime required)"
Paragraph
  Literal "Download a self-contained binary from the"
  LineBreak soft
  Link url="https://github.com/SharpAstro/Console.Lib/releases"
    Literal "Releases"
  Literal " page. Native AOT"
  LineBreak soft
  Literal "builds are published for:"
Table columns=3 rows=3 align=Left,Left,Left
  Header
    Cell
      Literal "OS"
    Cell
      Literal "x64"
    Cell
      Literal "arm64"
  Row
    Cell
      Literal "Linux"
    Cell
      Code "linux-x64"
    Cell
      Code "linux-arm64"
  Row
    Cell
      Literal "Windows"
    Cell
      Code "win-x64"
    Cell
      Code "win-arm64"
  Row
    Cell
      Literal "macOS"
    Cell
      Code "osx-x64"
    Cell
      Code "osx-arm64"
Paragraph
  Literal "Each archive contains the "
  Code "mdcat"
  Literal " executable plus the "
  Code "Fonts/"
  Literal " directory (the"
  LineBreak soft
  Literal "bundled math font must sit next to the binary). Extract it and put the folder"
  LineBreak soft
  Literal "on your "
  Code "PATH"
  Literal ", e.g.:"
CodeFence lang="bash" lines=2
  "tar -xzf mdcat-1.0.0-linux-x64.tar.gz"
  "sudo cp -r mdcat-1.0.0-linux-x64/* /usr/local/bin/   # mdcat + Fonts/"
Paragraph
  Literal "Releases are cut by pushing a "
  Code "mdcat-vX.Y.Z"
  Literal " tag, which runs"
  LineBreak soft
  Link url="../../.github/workflows/mdcat-release.yml"
    Code ".github/workflows/mdcat-release.yml"
  Literal "."
Heading 2
  Literal "Build from source"
CodeFence lang="bash" lines=10
  "dotnet build src/MdCat                          # build"
  "dotnet run --project src/MdCat -- README.md     # run in place"
  ""
  "# native, self-contained binary for the current platform"
  "dotnet publish src/MdCat -c Release -r <rid> --self-contained -o out"
  "# (rid = linux-x64 | win-x64 | osx-arm64 | \u2026; output in out/, with out/Fonts/)"
  ""
  "# or install as a .NET global tool from a local package"
  "dotnet pack src/MdCat -c Release"
  "dotnet tool install --global --add-source src/MdCat/bin/Release mdcat"
