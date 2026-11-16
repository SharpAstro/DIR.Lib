using DIR.Lib.Prototyping;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary><see cref="Prototype.Check"/> over the fixture documents, drawn with the real test font.</summary>
public sealed class PrototypeCheckTests
{
    private static readonly string FontPath = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");

    private static PrototypeDocument Load(string name)
    {
        var result = Prototype.Load(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Prototype", name)));
        result.Problems.ShouldBeEmpty();
        return result.Document!;
    }

    [Fact]
    public void ADeliberateOverlapIsReportedWithItsScreenAndSize()
    {
        var findings = Prototype.Check(Load("overlap.json"), [(360u, 640u)], PrototypeKit.Theme, fontPath: FontPath);

        findings.ShouldNotBeEmpty();
        findings.ShouldAllBe(f => f.Screen == "stacked" && f.State == "default" && f.Width == 360 && f.Height == 640);
        findings.ShouldContain(f => f.Finding.Rule == WireframeRules.TextOverText);
    }

    [Fact]
    public void TheSampleIsCleanAtThreeSizesOneOfThemPortrait()
    {
        var findings = Prototype.Check(Load("sample.json"), [(360u, 640u), (640u, 360u), (800u, 1200u)],
            PrototypeKit.Theme, fontPath: FontPath);

        string.Join("\n", findings).ShouldBeEmpty();
    }

    [Fact]
    public void TheDocumentedTwoArgumentFormResolvesAFontAndReportsTheOverlap()
    {
        if (FontResolver.ResolveSystemFont().Length == 0)
        {
            // A font-less image has nothing to resolve: the documented form must fail loudly there, not draw no text.
            Should.Throw<FileNotFoundException>(() => Prototype.Check(Load("overlap.json"), [(360u, 640u)]));
            return;
        }

        var findings = Prototype.Check(Load("overlap.json"), [(360u, 640u)]);

        findings.ShouldContain(f => f.Screen == "stacked" && f.Width == 360 && f.Height == 640
            && f.Finding.Rule == WireframeRules.TextOverText);
    }

    [Fact]
    public void AMissingFontFailsLoudlyRatherThanDrawingNoText()
    {
        var missing = Path.Combine(AppContext.BaseDirectory, "Fonts", "no-such-font.ttf");

        Should.Throw<FileNotFoundException>(() => Prototype.Check(Load("overlap.json"), [(360u, 640u)], fontPath: missing));
        Should.Throw<FileNotFoundException>(() => Prototype.Render(Load("overlap.json"), (360u, 640u), fontPath: missing));
    }
}
