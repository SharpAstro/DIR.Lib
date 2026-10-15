using System;
using System.Linq;
using System.Reflection;
using DIR.Lib.Markdown;
using DIR.Lib.MathLayout;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="MdMathInline.IsDisplay"/> records which delimiter a math span
/// was written with, so a renderer can set display math that sits inside a
/// paragraph as a block. Driven through the inline parser the way
/// <see cref="MarkdownInlineSpikeTests"/> drives it.
/// </summary>
public sealed class MathDisplayFlagTests
{
    private readonly MarkdownInlineVisitor _visitor = new();

    private MdMathInline SingleMath(string source) =>
        _visitor.Parse(source).OfType<MdMathInline>().Single();

    [Theory]
    [InlineData("$$x$$")]
    [InlineData(@"\[x\]")]
    public void DisplayDelimiters_SetIsDisplay(string source)
    {
        var math = SingleMath(source);
        math.Source.ShouldBe("x");
        math.IsDisplay.ShouldBeTrue();
    }

    [Theory]
    [InlineData("$x$")]
    [InlineData(@"\(x\)")]
    [InlineData(@"\boxed{x}")]
    public void InlineDelimiters_LeaveIsDisplayFalse(string source)
    {
        var math = SingleMath(source);
        math.Source.ShouldBe("x");
        math.IsDisplay.ShouldBeFalse();
    }

    [Fact]
    public void DoubleDollar_MidSentence_IsDisplay()
    {
        var spans = _visitor.Parse("a $$x$$ b");
        spans.Count.ShouldBe(3);
        spans[0].ShouldBeOfType<MdLiteral>().Text.ShouldBe("a ");
        var math = spans[1].ShouldBeOfType<MdMathInline>();
        math.Source.ShouldBe("x");
        math.IsDisplay.ShouldBeTrue();
        spans[2].ShouldBeOfType<MdLiteral>().Text.ShouldBe(" b");
    }

    [Fact]
    public void TheFlagLeavesTheConstructorAndDeconstructArityAlone()
    {
        // Console.Lib consumes this record from a published package, where a
        // changed constructor or Deconstruct is a MissingMethodException (see
        // InputEventCompatibilityTests). The flag is an init-only property, so
        // both keep their three parameters.
        var ctor = typeof(MdMathInline).GetConstructors(BindingFlags.Public | BindingFlags.Instance).ShouldHaveSingleItem();
        ctor.GetParameters().Select(p => p.ParameterType)
            .ShouldBe([typeof(string), typeof(string), typeof(Func<BoxStyle, Box>)]);
        typeof(MdMathInline).GetMethods().Where(m => m.Name == "Deconstruct").ShouldHaveSingleItem()
            .GetParameters().Length.ShouldBe(3);

        MdInline span = new MdMathInline("x", "x", null);
        (span is MdMathInline(var source, _, null) && source == "x").ShouldBeTrue();
        ((MdMathInline)span).IsDisplay.ShouldBeFalse();
    }
}
