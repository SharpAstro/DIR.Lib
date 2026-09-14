using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// The software backend's content→device mapping. A GPU backend folds the transform into its
/// projection and every vertex follows; there is none here, so the mapping happens where pixels are
/// written — and the thing worth proving is that it stays <b>exact</b>. The constraint is what buys
/// that: a quarter turn takes a rect to a rect and a pixel to a pixel, so nothing is resampled and
/// the map is a permutation of the buffer.
/// </summary>
public class RgbaImageContentTransformTests
{
    private static readonly RGBAColor32 Red = new(255, 0, 0, 255);
    private static readonly RGBAColor32 Blue = new(0, 0, 255, 255);

    private static RgbaImage Turned(int w, int h, Rotation90 rotation)
    {
        var img = new RgbaImage(w, h);
        img.SetContentTransform(ContentTransform.CenteredRotation(rotation, w, h));
        return img;
    }

    private static RGBAColor32 PixelAt(RgbaImage img, int x, int y)
    {
        var i = (y * img.Width + x) * 4;
        return new RGBAColor32(img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2], img.Pixels[i + 3]);
    }

    /// <summary>
    /// The property everything else rests on: across the whole content area, distinct pixels land on
    /// distinct device pixels and together they cover the surface exactly. A map that is off by one
    /// anywhere shows up here as a collision or a hole, which is precisely the bug a rotation invites
    /// (a pixel is the box [x, x+1), so under a turn pixel x becomes Tx−x−1, not Tx−x).
    /// </summary>
    [Theory]
    [InlineData(Rotation90.None)]
    [InlineData(Rotation90.Cw90)]
    [InlineData(Rotation90.Half)]
    [InlineData(Rotation90.Cw270)]
    public void EveryContentPixel_LandsOnItsOwnDevicePixel_CoveringTheSurface(Rotation90 rotation)
    {
        // Square, so all four turns stay inside the same surface and the cover is total.
        const int n = 17;
        var img = Turned(n, n, rotation);
        var seen = new bool[n * n];

        for (var y = 0; y < n; y++)
        {
            for (var x = 0; x < n; x++)
            {
                var (dx, dy) = img.MapPixel(x, y);
                dx.ShouldBeInRange(0, n - 1, $"content ({x},{y}) left the surface");
                dy.ShouldBeInRange(0, n - 1, $"content ({x},{y}) left the surface");

                var slot = dy * n + dx;
                seen[slot].ShouldBeFalse($"two content pixels both landed on device ({dx},{dy})");
                seen[slot] = true;
            }
        }

        seen.ShouldAllBe(hit => hit);   // no holes either
    }

    /// <summary>The 180° turn spelled out, because it is the across-the-table flip and the one worth
    /// being able to read rather than derive: the corner opposite.</summary>
    [Fact]
    public void Half_SendsEachPixelToTheOppositeCorner()
    {
        var img = Turned(8, 5, Rotation90.Half);

        img.MapPixel(0, 0).ShouldBe((7, 4));
        img.MapPixel(7, 4).ShouldBe((0, 0));
        img.MapPixel(3, 1).ShouldBe((4, 3));
    }

    /// <summary>
    /// A fill is still one rectangle after the turn — that is the constraint earning its keep — and it
    /// covers the mirrored region rather than the original one.
    /// </summary>
    [Fact]
    public void Half_FillsTheMirroredRectangle()
    {
        var img = Turned(8, 8, Rotation90.Half);

        img.FillRect(0, 0, 2, 3, Red);   // top-left corner, in CONTENT space

        PixelAt(img, 7, 7).ShouldBe(Red);   // ...comes out bottom-right
        PixelAt(img, 6, 5).ShouldBe(Red);
        PixelAt(img, 5, 5).ShouldBe(new RGBAColor32(0, 0, 0, 0));   // just outside
        PixelAt(img, 0, 0).ShouldBe(new RGBAColor32(0, 0, 0, 0));
    }

    /// <summary>
    /// Under a quarter turn a horizontal run comes out vertical. This is the difference between
    /// mapping the pixels and merely moving the box — and it is why text rotates rather than sliding.
    /// </summary>
    [Fact]
    public void Cw90_TurnsAHorizontalRunIntoAVerticalOne()
    {
        var img = Turned(8, 8, Rotation90.Cw90);

        img.FillRect(1, 0, 5, 1, Red);   // 4 wide, 1 tall, in content space

        var (x0, y0, x1, y1) = img.MapRect(1, 0, 5, 1);
        (x1 - x0).ShouldBe(1);   // one column...
        (y1 - y0).ShouldBe(4);   // ...four rows

        for (var y = y0; y < y1; y++) PixelAt(img, x0, y).ShouldBe(Red);
    }

    /// <summary>
    /// A blit is a pixel permutation, so the IMAGE turns. Asserted with an asymmetric source, because
    /// a symmetric one passes whether the glyph rotated or merely moved.
    /// </summary>
    [Fact]
    public void Blit_RotatesTheSourceImage_NotJustItsPosition()
    {
        var img = Turned(4, 4, Rotation90.Cw90);

        // A 2x1 source: red then blue, left to right.
        var src = new byte[] { 255, 0, 0, 255, 0, 0, 255, 255 };
        img.BlitRgba(0, 0, src, 2, 1);

        var red = img.MapPixel(0, 0);
        var blue = img.MapPixel(1, 0);
        PixelAt(img, red.X, red.Y).ShouldBe(Red);
        PixelAt(img, blue.X, blue.Y).ShouldBe(Blue);
        // Turned a quarter: the two pixels are now stacked, not side by side.
        red.X.ShouldBe(blue.X);
        red.Y.ShouldNotBe(blue.Y);
    }

    /// <summary>
    /// The clip is set in content space and guards device pixels, so it has to be mapped like anything
    /// else. Unmapped, a rotated frame would be trimmed on the wrong edge — content would vanish at
    /// one side of the screen while spilling out of the other.
    /// </summary>
    [Fact]
    public void Clip_IsMappedToo_SoItTrimsThePhysicallyCorrectEdge()
    {
        var img = Turned(8, 8, Rotation90.Half);

        img.SetClip(0, 0, 4, 8);          // the LEFT half, in content space
        img.FillRect(0, 0, 8, 8, Red);    // ask for everything

        PixelAt(img, 7, 0).ShouldBe(Red);                          // content-left is device-right
        PixelAt(img, 0, 0).ShouldBe(new RGBAColor32(0, 0, 0, 0));  // and device-left stays untouched
    }

    /// <summary>
    /// Identity has to be free, and provably so: an image that has been handed the identity must be
    /// byte-for-byte what it would have been with no transform at all. This is what lets the feature
    /// land in a shared library without auditing every existing consumer.
    /// </summary>
    [Fact]
    public void Identity_DrawsExactlyWhatAnUntransformedImageDraws()
    {
        var plain = new RgbaImage(16, 9);
        var identity = new RgbaImage(16, 9);
        identity.SetContentTransform(ContentTransform.Identity);

        foreach (var img in new[] { plain, identity })
        {
            img.Clear(new RGBAColor32(10, 20, 30, 255));
            img.FillRect(2, 3, 9, 7, Red);
            img.DrawHLine(0, 16, 1, Blue);
            img.BlendPixelAt(4, 4, new RGBAColor32(0, 255, 0, 128));
            img.BlitRgba(11, 2, new byte[] { 1, 2, 3, 200, 4, 5, 6, 255 }, 2, 1);
        }

        identity.Pixels.ShouldBe(plain.Pixels);
        identity.IsContentMapped.ShouldBeFalse();
    }

    /// <summary>
    /// A scale is the one component that cannot be applied to finished pixels without inventing some,
    /// and the ordering rule says it belongs in the measure context instead — applied to design units
    /// before layout resolves them. Refusing says so at the point of the mistake; blurring the frame
    /// would say it much later and much less clearly.
    /// </summary>
    [Fact]
    public void AScale_IsRefusedRatherThanResampled()
    {
        var img = new RgbaImage(8, 8);

        var ex = Should.Throw<NotSupportedException>(
            () => img.SetContentTransform(new ContentTransform(Rotation90.None, 2f, 0f, 0f)));

        ex.Message.ShouldContain("measure context");
        img.IsContentMapped.ShouldBeFalse();   // and nothing was half-applied
    }

    /// <summary>A refused transform must not leave the renderer claiming one the pixels are not
    /// getting — the property and the surface have to agree or hit-testing drifts from drawing.</summary>
    [Fact]
    public void ARefusedTransform_LeavesTheRendererUnchanged()
    {
        var renderer = new RgbaImageRenderer(8, 8);

        Should.Throw<NotSupportedException>(
            () => renderer.ContentTransform = new ContentTransform(Rotation90.Half, 1.5f, 0f, 0f));

        renderer.ContentTransform.ShouldBe(ContentTransform.Identity);
    }

    /// <summary>
    /// The renderer's whole-frame turn, through the public drawing API rather than the image's: what a
    /// host actually does when it flips the frame for the player on the other side of the table.
    /// </summary>
    [Fact]
    public void Renderer_TurnsTheWholeFrame()
    {
        var renderer = new RgbaImageRenderer(10, 10);
        renderer.ContentTransform = ContentTransform.CenteredRotation(Rotation90.Half, 10, 10);

        renderer.FillRectangle(new RectInt(new PointInt(0, 0), new PointInt(3, 2)), Red);

        PixelAt(renderer.Surface, 9, 9).ShouldBe(Red);
        PixelAt(renderer.Surface, 0, 0).ShouldBe(new RGBAColor32(0, 0, 0, 0));
    }
}
