using DIR.Lib;
using DIR.Lib.Markdown;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

public sealed class SubscriptsTests
{
    [Theory]
    [InlineData('0', '\u2070')]
    [InlineData('1', '\u00B9')]
    [InlineData('2', '\u00B2')]
    [InlineData('3', '\u00B3')]
    [InlineData('9', '\u2079')]
    [InlineData('+', '\u207A')]
    [InlineData('-', '\u207B')]
    public void ToSuperscript_MapsKnownChars(char input, char expected)
        => Subscripts.ToSuperscript(input).ShouldBe(expected);

    [Theory]
    [InlineData('0', '\u2080')]
    [InlineData('1', '\u2081')]
    [InlineData('9', '\u2089')]
    [InlineData('n', '\u2099')]
    public void ToSubscript_MapsKnownChars(char input, char expected)
        => Subscripts.ToSubscript(input).ShouldBe(expected);

    [Fact]
    public void PassesThroughUnknownChars()
    {
        Subscripts.ToSuperscript('Z').ShouldBe('Z');
        Subscripts.ToSubscript('Q').ShouldBe('Q');
    }

    [Fact]
    public void Super_MultiCharString()
    {
        Subscripts.Super("12").ShouldBe("\u00B9\u00B2");
        Subscripts.Super("3+4").ShouldBe("\u00B3\u207A\u2074");
    }

    [Fact]
    public void Sub_HandlesEmptyAndNull()
    {
        Subscripts.Sub("").ShouldBe("");
        Subscripts.Super("").ShouldBe("");
    }

    [Fact]
    public void Composition_H2O()
    {
        ("H" + Subscripts.Sub("2") + "O").ShouldBe("H\u2082O");
    }

    [Fact]
    public void Composition_IsotopeMass()
    {
        // ²³⁸U
        (Subscripts.Super("238") + "U").ShouldBe("\u00B2\u00B3\u2078U");
    }
}
