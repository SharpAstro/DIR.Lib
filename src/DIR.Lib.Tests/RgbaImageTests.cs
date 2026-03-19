using Shouldly;

namespace DIR.Lib.Tests;

public class RgbaImageTests
{
    [Fact]
    public void Constructor_CreatesTransparentBlackImage()
    {
        var img = new RgbaImage(4, 4);

        img.Width.ShouldBe(4);
        img.Height.ShouldBe(4);
        img.Pixels.Length.ShouldBe(4 * 4 * 4);
        img.Pixels.ShouldAllBe(b => b == 0);
    }

    [Fact]
    public void Clear_FillsEntireBuffer()
    {
        var img = new RgbaImage(2, 2);
        var red = new RGBAColor32(255, 0, 0, 255);

        img.Clear(red);

        for (var i = 0; i < img.Pixels.Length; i += 4)
        {
            img.Pixels[i].ShouldBe((byte)255);     // R
            img.Pixels[i + 1].ShouldBe((byte)0);   // G
            img.Pixels[i + 2].ShouldBe((byte)0);   // B
            img.Pixels[i + 3].ShouldBe((byte)255); // A
        }
    }

    [Fact]
    public void FillRect_OpaqueFill()
    {
        var img = new RgbaImage(4, 4);
        img.Clear(new RGBAColor32(0, 0, 0, 255));
        var green = new RGBAColor32(0, 255, 0, 255);

        img.FillRect(1, 1, 3, 3, green);

        // Pixel at (1,1) should be green
        GetPixel(img, 1, 1).ShouldBe(green);
        // Pixel at (2,2) should be green
        GetPixel(img, 2, 2).ShouldBe(green);
        // Pixel at (0,0) should be black
        GetPixel(img, 0, 0).ShouldBe(new RGBAColor32(0, 0, 0, 255));
        // Pixel at (3,3) should be black (exclusive bounds)
        GetPixel(img, 3, 3).ShouldBe(new RGBAColor32(0, 0, 0, 255));
    }

    [Fact]
    public void FillRect_AlphaBlend()
    {
        var img = new RgbaImage(2, 2);
        img.Clear(new RGBAColor32(255, 0, 0, 255)); // solid red
        var semiBlue = new RGBAColor32(0, 0, 255, 128);

        img.FillRect(0, 0, 2, 2, semiBlue);

        var pixel = GetPixel(img, 0, 0);
        // Should be a blend of red and blue
        pixel.Red.ShouldBeLessThan((byte)255);
        pixel.Blue.ShouldBeGreaterThan((byte)0);
        pixel.Alpha.ShouldBe((byte)255);
    }

    [Fact]
    public void FillRect_ZeroAlpha_NoChange()
    {
        var img = new RgbaImage(2, 2);
        var white = new RGBAColor32(255, 255, 255, 255);
        img.Clear(white);
        var transparent = new RGBAColor32(0, 0, 0, 0);

        img.FillRect(0, 0, 2, 2, transparent);

        GetPixel(img, 0, 0).ShouldBe(white);
    }

    [Fact]
    public void FillRect_ClampsToImageBounds()
    {
        var img = new RgbaImage(4, 4);
        var blue = new RGBAColor32(0, 0, 255, 255);

        // Should not throw even with out-of-bounds coords
        img.FillRect(-2, -2, 10, 10, blue);

        GetPixel(img, 0, 0).ShouldBe(blue);
        GetPixel(img, 3, 3).ShouldBe(blue);
    }

    [Fact]
    public void BlitRgba_FullyOpaque()
    {
        var img = new RgbaImage(4, 4);
        img.Clear(new RGBAColor32(0, 0, 0, 255));

        // 2x2 white glyph
        byte[] src = [
            255, 255, 255, 255, 255, 255, 255, 255,
            255, 255, 255, 255, 255, 255, 255, 255
        ];

        img.BlitRgba(1, 1, src, 2, 2);

        GetPixel(img, 1, 1).ShouldBe(new RGBAColor32(255, 255, 255, 255));
        GetPixel(img, 2, 2).ShouldBe(new RGBAColor32(255, 255, 255, 255));
        GetPixel(img, 0, 0).ShouldBe(new RGBAColor32(0, 0, 0, 255));
    }

    [Fact]
    public void BlitRgba_TransparentPixelsPreserveBackground()
    {
        var img = new RgbaImage(4, 4);
        var bg = new RGBAColor32(100, 100, 100, 255);
        img.Clear(bg);

        // 2x1: first pixel opaque white, second transparent
        byte[] src = [
            255, 255, 255, 255, 0, 0, 0, 0
        ];

        img.BlitRgba(0, 0, src, 2, 1);

        GetPixel(img, 0, 0).ShouldBe(new RGBAColor32(255, 255, 255, 255));
        GetPixel(img, 1, 0).ShouldBe(bg); // unchanged
    }

    [Fact]
    public void BlendPixelAt_OpaqueOnBlack()
    {
        var img = new RgbaImage(4, 4);
        img.Clear(new RGBAColor32(0, 0, 0, 255));
        var red = new RGBAColor32(255, 0, 0, 255);

        img.BlendPixelAt(1, 1, red);

        GetPixel(img, 1, 1).ShouldBe(red);
    }

    [Fact]
    public void BlendPixelAt_SemiTransparent()
    {
        var img = new RgbaImage(2, 2);
        img.Clear(new RGBAColor32(0, 0, 255, 255)); // blue
        var semiRed = new RGBAColor32(255, 0, 0, 128);

        img.BlendPixelAt(0, 0, semiRed);

        var pixel = GetPixel(img, 0, 0);
        pixel.Red.ShouldBeGreaterThan((byte)0);
        pixel.Blue.ShouldBeGreaterThan((byte)0);
        pixel.Alpha.ShouldBe((byte)255);
    }

    [Fact]
    public void BlendPixelAt_OutOfBounds_NoThrow()
    {
        var img = new RgbaImage(2, 2);
        img.BlendPixelAt(-1, 0, new RGBAColor32(255, 0, 0, 255));
        img.BlendPixelAt(0, 5, new RGBAColor32(255, 0, 0, 255));
        // Should not throw
    }

    [Fact]
    public void Resize_AllocatesNewBuffer()
    {
        var img = new RgbaImage(2, 2);
        img.Clear(new RGBAColor32(255, 0, 0, 255));

        img.Resize(4, 4);

        img.Width.ShouldBe(4);
        img.Height.ShouldBe(4);
        img.Pixels.Length.ShouldBe(4 * 4 * 4);
        // New buffer should be zeroed
        img.Pixels.ShouldAllBe(b => b == 0);
    }

    private static RGBAColor32 GetPixel(RgbaImage img, int x, int y)
    {
        var i = (y * img.Width + x) * 4;
        return new RGBAColor32(img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2], img.Pixels[i + 3]);
    }
}
