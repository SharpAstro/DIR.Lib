# DIR.Lib

Device-Independent Rendering primitives for .NET.

## Types

- **`PointInt`** — 2D integer point
- **`RectInt`** — 2D integer rectangle (upper-left + lower-right)
- **`RGBAColor32`** — 32-bit RGBA color
- **`TextAlign`** — Near/Center/Far alignment enum
- **`Renderer<TSurface>`** — Abstract renderer with FillRectangle, DrawRectangle, FillEllipse, DrawText
- **`GlyphBitmap`** — Raw RGBA glyph bitmap with bearing info
- **`FreeTypeGlyphRasterizer`** — FreeType2-based glyph rasterizer (requires FreeType.Native)

## Usage

```csharp
using DIR.Lib;

using var rasterizer = new FreeTypeGlyphRasterizer();
var glyph = rasterizer.RasterizeGlyph("/path/to/font.ttf", 24f, 'A');
// glyph.Rgba, glyph.Width, glyph.Height, glyph.BearingY
```

## License

MIT
