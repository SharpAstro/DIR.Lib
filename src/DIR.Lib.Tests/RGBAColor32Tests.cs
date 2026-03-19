using Shouldly;

namespace DIR.Lib.Tests;

public class RGBAColor32Tests
{
    [Fact]
    public void Lerp_AtZero_ReturnsFirst()
    {
        var a = new RGBAColor32(255, 0, 0, 255);
        var b = new RGBAColor32(0, 0, 255, 255);
        RGBAColor32.Lerp(a, b, 0f).ShouldBe(a);
    }

    [Fact]
    public void Lerp_AtOne_ReturnsSecond()
    {
        var a = new RGBAColor32(255, 0, 0, 255);
        var b = new RGBAColor32(0, 0, 255, 255);
        RGBAColor32.Lerp(a, b, 1f).ShouldBe(b);
    }

    [Fact]
    public void Lerp_AtHalf_ReturnsMidpoint()
    {
        var a = new RGBAColor32(0, 0, 0, 255);
        var b = new RGBAColor32(200, 100, 50, 255);
        var mid = RGBAColor32.Lerp(a, b, 0.5f);
        mid.Red.ShouldBe((byte)100);
        mid.Green.ShouldBe((byte)50);
        mid.Blue.ShouldBe((byte)25);
        mid.Alpha.ShouldBe((byte)255);
    }

    [Fact]
    public void WithAlpha_FullMask_PreservesAlpha()
    {
        var color = new RGBAColor32(255, 0, 0, 200);
        var result = color.WithAlpha(255);
        result.Alpha.ShouldBe((byte)200);
        result.Red.ShouldBe((byte)255);
    }

    [Fact]
    public void WithAlpha_HalfMask_HalvesAlpha()
    {
        var color = new RGBAColor32(255, 0, 0, 255);
        var result = color.WithAlpha(128);
        result.Alpha.ShouldBeInRange((byte)127, (byte)129);
    }

    [Fact]
    public void WithAlpha_ZeroMask_ReturnsZeroAlpha()
    {
        var color = new RGBAColor32(255, 0, 0, 255);
        color.WithAlpha(0).Alpha.ShouldBe((byte)0);
    }

    [Fact]
    public void Luminance_White()
    {
        new RGBAColor32(255, 255, 255, 255).Luminance.ShouldBe((byte)255);
    }

    [Fact]
    public void Luminance_Black()
    {
        new RGBAColor32(0, 0, 0, 255).Luminance.ShouldBe((byte)0);
    }
}
