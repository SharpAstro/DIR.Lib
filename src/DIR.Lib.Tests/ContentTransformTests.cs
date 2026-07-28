using System.Numerics;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Headless unit tests for <see cref="ContentTransform"/> — the constrained content→device affine. They
/// pin the algebra the GPU compose and the host input inverse both rely on: exact 90°-multiple rotation,
/// centred-rotation translation, round-trip invertibility, composition, and that <see cref="Matrix3x2"/>
/// transport agrees with <see cref="ContentTransform.Apply"/>.
/// </summary>
public class ContentTransformTests
{
    private const float Eps = 1e-4f;

    private static void ShouldBeApprox(Vector2 actual, float x, float y)
    {
        actual.X.ShouldBe(x, Eps);
        actual.Y.ShouldBe(y, Eps);
    }

    [Fact]
    public void Identity_IsNoOp()
    {
        var id = ContentTransform.Identity;
        id.IsIdentity.ShouldBeTrue();
        id.Rotation.ShouldBe(Rotation90.None);
        id.Scale.ShouldBe(1f);
        id.ToMatrix3x2().ShouldBe(Matrix3x2.Identity);
        ShouldBeApprox(id.Apply(new Vector2(37f, -11f)), 37f, -11f);
    }

    [Fact]
    public void CenteredHalf_FlipsCornersAndFixesCentre()
    {
        // 180° about the centre of a 100 × 80 surface.
        var t = ContentTransform.CenteredRotation(Rotation90.Half, 100f, 80f);
        t.Rotation.ShouldBe(Rotation90.Half);
        t.Tx.ShouldBe(100f, Eps);
        t.Ty.ShouldBe(80f, Eps);

        ShouldBeApprox(t.Apply(new Vector2(0f, 0f)), 100f, 80f);   // top-left  → bottom-right
        ShouldBeApprox(t.Apply(new Vector2(100f, 80f)), 0f, 0f);   // bottom-right → top-left
        ShouldBeApprox(t.Apply(new Vector2(50f, 40f)), 50f, 40f);  // centre is fixed
    }

    [Theory]
    [InlineData(Rotation90.None)]
    [InlineData(Rotation90.Cw90)]
    [InlineData(Rotation90.Half)]
    [InlineData(Rotation90.Cw270)]
    public void ApplyThenInvert_RoundTrips(Rotation90 rot)
    {
        var t = ContentTransform.CenteredRotation(rot, 120f, 90f, scale: 1.75f);
        foreach (var p in new[] { new Vector2(0f, 0f), new Vector2(120f, 90f), new Vector2(17f, 63f) })
        {
            var back = t.Invert(t.Apply(p));
            ShouldBeApprox(back, p.X, p.Y);
        }
    }

    [Fact]
    public void CenteredRotations_MapContentBoxOntoSurface()
    {
        // 90° about the centre of a square keeps the box on the surface (corner cycles, no scale).
        var t = ContentTransform.CenteredRotation(Rotation90.Cw90, 100f, 100f);
        ShouldBeApprox(t.Apply(new Vector2(0f, 0f)), 100f, 0f);    // (x,y)->(-y,x)+centre-fix: TL → TR
        ShouldBeApprox(t.Apply(new Vector2(50f, 50f)), 50f, 50f);  // centre fixed
    }

    [Fact]
    public void Matrix3x2_Transport_AgreesWithApply()
    {
        // Vector2.Transform through ToMatrix3x2() must equal Apply — the GPU path relies on it.
        var t = new ContentTransform(Rotation90.Cw270, 2.5f, 12f, -7f);
        var p = new Vector2(9f, 4f);
        ShouldBeApprox(Vector2.Transform(p, t.ToMatrix3x2()), t.Apply(p).X, t.Apply(p).Y);
    }

    [Fact]
    public void Compose_AppliesInnerThenOuter()
    {
        var inner = new ContentTransform(Rotation90.Cw90, 2f, 3f, 5f);
        var outer = new ContentTransform(Rotation90.Cw90, 1.5f, -1f, 4f);
        var composed = outer.Compose(inner);

        // Rotations add (90° + 90° = 180°); scales multiply.
        composed.Rotation.ShouldBe(Rotation90.Half);
        composed.Scale.ShouldBe(3f, Eps);

        // And the composed map equals doing inner first, then outer, for arbitrary points.
        foreach (var p in new[] { new Vector2(0f, 0f), new Vector2(10f, -6f), new Vector2(-4f, 8f) })
        {
            var sequential = outer.Apply(inner.Apply(p));
            ShouldBeApprox(composed.Apply(p), sequential.X, sequential.Y);
        }
    }

    [Fact]
    public void ComposeWithInverseRotation_ReturnsToNoRotation()
    {
        var a = new ContentTransform(Rotation90.Cw90, 1f, 0f, 0f);
        var b = new ContentTransform(Rotation90.Cw270, 1f, 0f, 0f);
        a.Compose(b).Rotation.ShouldBe(Rotation90.None);
    }
}
