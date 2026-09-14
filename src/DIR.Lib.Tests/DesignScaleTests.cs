using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// The design→surface mapping components exchange, in place of the bare float they each used to keep a
/// copy of. Most of this is arithmetic; the parts worth pinning are the ones that used to be open-coded
/// at every call site and got them subtly different — the zero guard, and the second axis.
/// </summary>
public class DesignScaleTests
{
    [Fact]
    public void OneIsTheNoOp_AndTheDefaultStructReadsAsIt()
    {
        DesignScale.One.ToSurface(7f).ShouldBe(7f);
        DesignScale.One.IsUniform.ShouldBeTrue();

        // `default` is what an omitted optional parameter is, so it has to mean "unscaled" rather than
        // "scale everything to nothing".
        default(DesignScale).OrOne().ShouldBe(DesignScale.One);
    }

    [Fact]
    public void AUniformScale_MapsBothAxesTheSame()
    {
        var scale = new DesignScale(2f);

        scale.IsUniform.ShouldBeTrue();
        scale.ToSurfaceX(10f).ShouldBe(20f);
        scale.ToSurfaceY(10f).ShouldBe(20f);
        scale.ToSurface(10f).ShouldBe(20f);
    }

    /// <summary>
    /// The reason this is a pair and not a float. A terminal cell is about 8 units across and 16 down,
    /// so one design unit is not square — and a caller multiplying by "the" scale would be right on one
    /// axis and wrong on the other, which is the failure that looks like a layout bug rather than a
    /// unit bug.
    /// </summary>
    [Fact]
    public void ANonUniformScale_KeepsTheAxesApart()
    {
        var cell = new DesignScale(8f, 16f);

        cell.IsUniform.ShouldBeFalse();
        cell.ToSurfaceX(1f).ShouldBe(8f);
        cell.ToSurfaceY(1f).ShouldBe(16f);
    }

    [Fact]
    public void SurfaceLengthsConvertBackToDesignUnits()
    {
        var scale = new DesignScale(1.5f, 3f);

        scale.ToDesignX(150f).ShouldBe(100f);
        scale.ToDesignY(150f).ShouldBe(50f);
    }

    [Fact]
    public void ConvertingThereAndBack_ReturnsWhatWentIn()
    {
        var scale = new DesignScale(1.25f, 2.5f);

        scale.ToDesignX(scale.ToSurfaceX(37f)).ShouldBe(37f, tolerance: 1e-4);
        scale.ToDesignY(scale.ToSurfaceY(37f)).ShouldBe(37f, tolerance: 1e-4);
    }

    /// <summary>
    /// A host can hand out a zero scale mid-resize — a minimized window reports a zero-size client
    /// area — and the old code guarded against it separately at four call sites, each spelling the
    /// guard slightly differently. Dividing a stored drag offset by zero puts a palette at infinity,
    /// from which no later resize recovers it.
    /// </summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void ADegenerateScale_FallsBackToUnity(float bad)
    {
        new DesignScale(bad).OrOne().ShouldBe(DesignScale.One);

        // ...and converting through one directly still returns a finite number rather than infinity.
        new DesignScale(bad).ToDesignX(120f).ShouldBe(120f);
        new DesignScale(bad).ToDesignY(120f).ShouldBe(120f);
    }

    /// <summary>A half-degenerate scale is degenerate: one good axis does not make the pair usable, and
    /// the axis that is zero is the one that would divide to infinity.</summary>
    [Fact]
    public void AScaleDegenerateOnOneAxisOnly_IsStillRejected()
    {
        new DesignScale(2f, 0f).OrOne().ShouldBe(DesignScale.One);
        new DesignScale(0f, 2f).OrOne().ShouldBe(DesignScale.One);
    }

    /// <summary>
    /// The measure context is where the scale comes from, and handing out its own mapping is what stops
    /// a widget keeping a second copy. So the pair it gives must be the pair it measures with.
    /// </summary>
    [Fact]
    public void AMeasureContext_HandsOutTheMappingItMeasuresWith()
    {
        using var renderer = new RgbaImageRenderer(64, 64);

        var isotropic = new PixelMeasureContext<RgbaImage>(renderer, string.Empty, dpiScale: 2f);
        isotropic.Scale.ShouldBe(new DesignScale(2f));
        isotropic.Scale.ToSurfaceX(5f).ShouldBe(isotropic.ToSurfaceX(5f));
        isotropic.Scale.ToSurfaceY(5f).ShouldBe(isotropic.ToSurfaceY(5f));

        var cellAuthored = PixelMeasureContext<RgbaImage>.CellAuthored(renderer, string.Empty);
        cellAuthored.Scale.IsUniform.ShouldBeFalse();
        cellAuthored.Scale.ToSurfaceX(1f).ShouldBe(cellAuthored.ToSurfaceX(1f));
        cellAuthored.Scale.ToSurfaceY(1f).ShouldBe(cellAuthored.ToSurfaceY(1f));
    }
}
