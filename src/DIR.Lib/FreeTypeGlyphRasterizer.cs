using System.Runtime.InteropServices;

namespace DIR.Lib;

/// <summary>
/// Rasterizes glyphs to RGBA bitmaps using FreeType2.
/// Requires FreeType.Native for the native freetype library.
/// </summary>
public sealed unsafe class FreeTypeGlyphRasterizer : IDisposable
{
    private nint _library;
    private readonly Dictionary<string, nint> _faces = new();

    public FreeTypeGlyphRasterizer()
    {
        nint lib;
        if (FT.Init_FreeType(&lib) != 0)
            throw new InvalidOperationException("FT_Init_FreeType failed");
        _library = lib;
    }

    public GlyphBitmap RasterizeGlyph(string fontPath, float fontSize, char character)
    {
        var face = GetOrLoadFace(fontPath);

        FT.Set_Pixel_Sizes(face, 0, (uint)MathF.Round(fontSize));

        if (FT.Load_Char(face, character, FT.FT_LOAD_RENDER | FT.FT_LOAD_TARGET_LIGHT) != 0)
            return default;

        var glyphSlot = *(nint*)((byte*)face + FT.FaceGlyphOffset);
        if (glyphSlot == 0)
            return default;

        var slotBase = (byte*)glyphSlot;
        var bitmapBase = slotBase + FT.SlotBitmapOffset;
        var rows = *(uint*)bitmapBase;
        var width = *(uint*)(bitmapBase + 4);
        var pitch = *(int*)(bitmapBase + 8);
        var buffer = *(byte**)(bitmapBase + FT.BitmapBufferFieldOffset);
        var bitmapLeft = *(int*)(slotBase + FT.SlotBitmapLeftOffset);
        var bitmapTop = *(int*)(slotBase + FT.SlotBitmapTopOffset);

        // advance.x is FT_Pos (CLong) in 26.6 fixed-point
        float advanceX;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            advanceX = *(int*)(slotBase + FT.SlotAdvanceXOffset) / 64f;
        else
            advanceX = *(nint*)(slotBase + FT.SlotAdvanceXOffset) / 64f;

        if (width == 0 || rows == 0 || buffer == null)
            return default;

        var rgba = new byte[width * rows * 4];
        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var alpha = buffer[y * pitch + x];
                var offset = (int)((y * width + x) * 4);
                rgba[offset] = 255;     // R
                rgba[offset + 1] = 255; // G
                rgba[offset + 2] = 255; // B
                rgba[offset + 3] = alpha;
            }
        }

        return new GlyphBitmap(rgba, (int)width, (int)rows, bitmapLeft, bitmapTop, advanceX);
    }

    private nint GetOrLoadFace(string fontPath)
    {
        if (_faces.TryGetValue(fontPath, out var existing))
            return existing;

        var pathPtr = Marshal.StringToCoTaskMemUTF8(fontPath);
        try
        {
            nint face;
            if (FT.New_Face(_library, (byte*)pathPtr, 0, &face) != 0)
                throw new InvalidOperationException($"FT_New_Face failed for '{fontPath}'");
            _faces[fontPath] = face;
            return face;
        }
        finally
        {
            Marshal.FreeCoTaskMem(pathPtr);
        }
    }

    public void Dispose()
    {
        foreach (var face in _faces.Values)
            FT.Done_Face(face);
        _faces.Clear();

        if (_library != 0)
        {
            FT.Done_FreeType(_library);
            _library = 0;
        }
    }

    /// <summary>
    /// Minimal FreeType P/Invoke with computed struct offsets.
    /// C 'long' is 4 bytes on Windows (LLP64), pointer-sized on Unix (LP64).
    /// </summary>
    internal static class FT
    {
        private const string Lib = "freetype";

        public const int FT_LOAD_RENDER = 4;
        public const int FT_LOAD_TARGET_LIGHT = 1 << 16; // FT_RENDER_MODE_LIGHT

        private static readonly int CLong = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? 4 : nint.Size;

        // FT_FaceRec → glyph field offset
        public static readonly int FaceGlyphOffset = ComputeFaceGlyphOffset();

        // FT_GlyphSlotRec → bitmap field offset
        public static readonly int SlotBitmapOffset = ComputeSlotBitmapOffset();

        // FT_Bitmap → buffer field offset
        public static readonly int BitmapBufferFieldOffset = Align(sizeof(uint) * 3, nint.Size);

        // FT_GlyphSlotRec → advance.x (FT_Pos, 26.6 fixed-point)
        // advance is right before format in the slot layout
        public static readonly int SlotAdvanceXOffset = ComputeSlotAdvanceOffset();

        // FT_GlyphSlotRec → bitmap_left (FT_Int) follows bitmap
        // FT_Bitmap size: rows(4) + width(4) + pitch(4) + pad(4 on 64-bit) + buffer(ptr)
        //               + num_grays(2) + pixel_mode(1) + palette_mode(1) + pad + palette(ptr)
        public static readonly int SlotBitmapLeftOffset = SlotBitmapOffset + ComputeBitmapSize();
        public static readonly int SlotBitmapTopOffset = SlotBitmapLeftOffset + sizeof(int);

        private static int ComputeFaceGlyphOffset()
        {
            var off = CLong * 5;                       // num_faces..num_glyphs
            off = Align(off, nint.Size);
            off += nint.Size * 2;                      // family_name, style_name
            off += sizeof(int);                        // num_fixed_sizes
            off = Align(off, nint.Size);
            off += nint.Size;                          // available_sizes
            off += sizeof(int);                        // num_charmaps
            off = Align(off, nint.Size);
            off += nint.Size;                          // charmaps
            off += nint.Size * 2;                      // FT_Generic
            off += CLong * 4;                          // FT_BBox
            off += 2 + 2 * 3 + 2 * 2 + 2 * 2;        // units_per_EM..underline_thickness (shorts)
            off = Align(off, nint.Size);               // align to pointer
            return off;
        }

        private static int ComputeSlotBitmapOffset()
        {
            var off = nint.Size * 3;                   // library, face, next
            off += sizeof(uint);                       // glyph_index
            off = Align(off, nint.Size);
            off += nint.Size * 2;                      // FT_Generic
            off += CLong * 8;                          // FT_Glyph_Metrics
            off += CLong * 2;                          // linearHoriAdvance, linearVertAdvance
            off += CLong * 2;                          // FT_Vector advance
            off += CLong;                              // format (FT_Glyph_Format = unsigned long)
            off = Align(off, nint.Size);               // align for FT_Bitmap (contains pointer)
            return off;
        }

        private static int ComputeSlotAdvanceOffset()
        {
            var off = nint.Size * 3;                   // library, face, next
            off += sizeof(uint);                       // glyph_index
            off = Align(off, nint.Size);
            off += nint.Size * 2;                      // FT_Generic
            off += CLong * 8;                          // FT_Glyph_Metrics
            off += CLong * 2;                          // linearHoriAdvance, linearVertAdvance
            return off;                                // advance.x starts here
        }

        private static int ComputeBitmapSize()
        {
            var off = sizeof(uint) * 3;                // rows, width, pitch
            off = Align(off, nint.Size);
            off += nint.Size;                          // buffer
            off += 2 + 1 + 1;                         // num_grays, pixel_mode, palette_mode
            off = Align(off, nint.Size);
            off += nint.Size;                          // palette
            return off;
        }

        private static int Align(int offset, int alignment) =>
            (offset + alignment - 1) & ~(alignment - 1);

        [DllImport(Lib, EntryPoint = "FT_Init_FreeType")]
        public static extern int Init_FreeType(nint* library);

        [DllImport(Lib, EntryPoint = "FT_Done_FreeType")]
        public static extern int Done_FreeType(nint library);

        [DllImport(Lib, EntryPoint = "FT_New_Face")]
        public static extern int New_Face(nint library, byte* filePath, int faceIndex, nint* face);

        [DllImport(Lib, EntryPoint = "FT_Done_Face")]
        public static extern int Done_Face(nint face);

        [DllImport(Lib, EntryPoint = "FT_Set_Pixel_Sizes")]
        public static extern int Set_Pixel_Sizes(nint face, uint charWidth, uint charHeight);

        [DllImport(Lib, EntryPoint = "FT_Load_Char")]
        public static extern int Load_Char(nint face, uint charCode, int loadFlags);
    }
}
