using System;
using System.IO;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// The themed controls draw from the theme alone: no source of Button, Hyperlink or ControlStyles states a colour.
/// A colour belongs in a palette, so a restyle is one <c>with</c> on the theme.
/// </summary>
public class ControlLiteralInvariantTests
{
    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "DIR.Lib", "ControlStyles.cs"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("source tree not found above " + AppContext.BaseDirectory);
    }

    [Theory]
    [InlineData("src/DIR.Lib/Layout/Button.cs")]
    [InlineData("src/DIR.Lib/Layout/Hyperlink.cs")]
    [InlineData("src/DIR.Lib/Layout/Chip.cs")]
    [InlineData("src/DIR.Lib/Layout/SplitButton.cs")]
    [InlineData("src/DIR.Lib/Layout/ToggleTile.cs")]
    [InlineData("src/DIR.Lib/Layout/Slider.cs")]
    [InlineData("src/DIR.Lib/Layout/Dropdown.cs")]
    [InlineData("src/DIR.Lib/ControlStyles.cs")]
    [Trait("Category", "Invariant")]
    public void The_source_states_no_colour_literal(string relative)
    {
        var text = File.ReadAllText(Path.Combine(SourceRoot(), relative));
        text.ShouldNotContain("new RGBAColor32(", Case.Sensitive, relative);
        text.ShouldNotContain("new(0x", Case.Sensitive, relative);
    }
}
