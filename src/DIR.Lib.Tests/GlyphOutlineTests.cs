using System.Text;
using SharpAstro.Fonts.Outlines;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="ManagedFontRasterizer.TryDrawGlyphOutline"/>: a glyph's geometry for a vector consumer, from
/// either kind of face. The Type 1 half is the one a caller could not get any other way.
/// </summary>
public class GlyphOutlineTests
{
    private const string Cmr10 = "mem:cmr10";
    private static readonly string DejaVu = Path.Combine("Fonts", "DejaVuSans.ttf");

    [Fact]
    public void AType1GlyphHasItsOutlineInThousandthsOfAnEm()
    {
        using var r = WithCmr10();
        var sink = new BoundsSink();

        r.TryDrawGlyphOutline(Cmr10, new GlyphIdentity(0, "o"), sink, out var upem).ShouldBeTrue();

        upem.ShouldBe(1000);
        sink.Contours.ShouldBe(2); // the bowl and its counter
        // The same glyph rasterized at 64 ppem spans what its outline spans, to the pixel.
        var bitmap = r.RasterizeGlyphByType1Name(Cmr10, 64f, "o");
        (sink.Width * 64f / upem).ShouldBe(bitmap.Width, 2f);
        (sink.Height * 64f / upem).ShouldBe(bitmap.Height, 2f);
    }

    [Fact]
    public void AnSfntGlyphHasItsOutlineInTheFacesUnits()
    {
        using var r = new ManagedFontRasterizer();
        var id = r.ResolveGlyphIdentity(DejaVu, new Rune('A'), 'A', GlyphMapHint.Auto);
        var sink = new BoundsSink();

        r.TryDrawGlyphOutline(DejaVu, id, sink, out var upem).ShouldBeTrue();

        upem.ShouldBe(2048);
        sink.Contours.ShouldBe(2); // the outline and the counter
        sink.Height.ShouldBeGreaterThan(upem / 2f);
    }

    [Fact]
    public void AnIdentityOfTheWrongKindOrAMissingFaceDrawsNothing()
    {
        using var r = WithCmr10();
        var sink = new BoundsSink();

        r.TryDrawGlyphOutline(Cmr10, new GlyphIdentity(12, null), sink, out _).ShouldBeFalse();
        r.TryDrawGlyphOutline(Cmr10, new GlyphIdentity(0, "no_such_glyph_xyz"), sink, out _).ShouldBeFalse();
        r.TryDrawGlyphOutline(DejaVu, new GlyphIdentity(0, "A"), sink, out _).ShouldBeFalse();
        r.TryDrawGlyphOutline("mem:never-registered", new GlyphIdentity(5, null), sink, out var upem).ShouldBeFalse();

        upem.ShouldBe(0);
        sink.Contours.ShouldBe(0);
    }

    private static ManagedFontRasterizer WithCmr10()
    {
        var r = new ManagedFontRasterizer();
        r.RegisterFontFromMemory(Cmr10, File.ReadAllBytes(Path.Combine("Fonts", "cmr10.pfb"))).ShouldBeTrue();
        return r;
    }

    // The outline's extent over its on-curve and control points, and how many contours it has.
    private sealed class BoundsSink : IGlyphSink
    {
        private float _minX = float.MaxValue, _minY = float.MaxValue, _maxX = float.MinValue, _maxY = float.MinValue;

        public int Contours { get; private set; }
        public float Width => _maxX - _minX;
        public float Height => _maxY - _minY;

        public void MoveTo(float x, float y)
        {
            Contours++;
            Add(x, y);
        }

        public void LineTo(float x, float y) => Add(x, y);

        public void QuadTo(float cx, float cy, float x, float y)
        {
            Add(cx, cy);
            Add(x, y);
        }

        public void CubicTo(float c1x, float c1y, float c2x, float c2y, float x, float y)
        {
            Add(c1x, c1y);
            Add(c2x, c2y);
            Add(x, y);
        }

        public void Close() { }

        private void Add(float x, float y)
        {
            _minX = MathF.Min(_minX, x);
            _minY = MathF.Min(_minY, y);
            _maxX = MathF.Max(_maxX, x);
            _maxY = MathF.Max(_maxY, y);
        }
    }
}
