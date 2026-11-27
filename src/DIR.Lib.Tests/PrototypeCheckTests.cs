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
        // Gate on what the two-argument form itself resolves (PrototypeFaces.Platform: Arial, else the system face), not on
        // the system face alone: a machine with Arial and no system monospace face still resolves a font.
        PrototypeFaces? platform = null;
        try { platform = PrototypeFaces.Platform(); } catch (FileNotFoundException) { }
        if (platform is null)
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
    public void ANodeShownOnlyOnACompactTargetIsReportedAtThatTargetByName()
    {
        var findings = Prototype.Check(Load("targets.json"), PrototypeKit.Theme, fontPath: FontPath);

        findings.ShouldNotBeEmpty();
        findings.ShouldAllBe(f => f.Target == "phone" && f.State == "default" && f.Width == 780 && f.Height == 1688);
        findings.ShouldContain(f => f.Finding.Rule == WireframeRules.TextOverText);
        findings.ShouldNotContain(f => f.Target == "desktop");
        findings[0].ToString().ShouldContain("@phone");
    }

    [Fact]
    public void TheSizedFormLeavesTheTargetEmpty()
    {
        var findings = Prototype.Check(Load("overlap.json"), [(360u, 640u)], PrototypeKit.Theme, fontPath: FontPath);

        findings.ShouldAllBe(f => f.Target == "");
    }

    [Fact]
    public void AVariantNeverSetsTheTargetState()
    {
        var findings = Prototype.Check(Load("targets.json"), PrototypeKit.Theme, fontPath: FontPath);

        findings.ShouldNotContain(f => f.State.StartsWith("target="));
    }

    [Fact]
    public void AMissingFontFailsLoudlyRatherThanDrawingNoText()
    {
        var missing = Path.Combine(AppContext.BaseDirectory, "Fonts", "no-such-font.ttf");

        Should.Throw<FileNotFoundException>(() => Prototype.Check(Load("overlap.json"), [(360u, 640u)], fontPath: missing));
        Should.Throw<FileNotFoundException>(() => Prototype.Render(Load("overlap.json"), (360u, 640u), fontPath: missing));
    }
}
