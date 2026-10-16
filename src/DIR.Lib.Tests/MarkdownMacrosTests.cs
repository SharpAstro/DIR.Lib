using DIR.Lib.Markdown;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="MarkdownMacros"/>'s plain-text passes, driven directly where nothing in a formula reaches
/// them yet.
/// </summary>
public sealed class MarkdownMacrosTests
{
    /// <summary>
    /// An environment body renders <c>\text{X}</c> as the math pass does: with any <c>\mathrm</c>
    /// unwrapped, as Mhchem wraps every chem letter, and its backslash-non-letter macros resolved. It ran
    /// a pass of its own that skipped the unwrap, so <c>\text{\mathrm{H}_{2}\mathrm{O}}</c> kept its
    /// wrappers there. Nothing reaches it from <c>\ce</c> today, which is not expanded inside an
    /// environment, so this calls the body pass itself and compares it with the math pass's
    /// <c>\text</c>.
    /// </summary>
    [Theory]
    [InlineData(@"\mathrm{H}_{2}\mathrm{O}", "H_{2}O")]
    [InlineData(@"\mathrm{\mathrm{x}}", "x")]
    [InlineData(@"(\mathrm{aq})", "(aq)")]
    [InlineData(@"Yes,\ 131", "Yes, 131")]
    public void AnEnvironmentBodyRendersTextAsTheMathPassDoes(string text, string expected)
    {
        var source = @"\text{" + text + "}";
        MarkdownMacros.RenderMathUnicode(source).ShouldBe(expected, "the math pass's \\text");
        MarkdownMacros.RenderEnvironmentBody(source).ShouldBe(expected, "an environment body's \\text");
    }
}
