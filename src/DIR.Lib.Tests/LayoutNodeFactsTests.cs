using DIR.Lib.Layout;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// The engine's per-node facts live in two generations of strong references, not in weak tables whose GC handles a tree built per
/// frame churned (SharpAstro/DIR.Lib#341): a node that goes on being asked about keeps its facts, one nobody asks about goes within
/// two generations, and a copy is a node of its own.
/// </summary>
public class LayoutNodeFactsTests
{
    private sealed class Known(int n)
    {
        public int N { get; } = n;
    }

    private static Node Leaf(int i) => Builder.Text($"row {i}", 13f);

    [Fact]
    public void ANodeAskedAboutAgainKeepsItsFactsWhileTwoGenerationsOfOthersComeAndGo()
    {
        var facts = new NodeFacts<Known>();
        var kept = Leaf(-1);
        facts.GetValue(kept, new Known(1));

        // Four generations of nodes built once and dropped, as a tree built per frame makes; the kept node is asked about between.
        for (var i = 0; i < NodeFacts<Known>.GenerationSize * 4; i++)
        {
            facts.GetValue(Leaf(i), new Known(i));
            if (i % 1000 == 0)
            {
                facts.TryGetValue(kept, out var again).ShouldBeTrue();
                again.N.ShouldBe(1);
            }
        }

        facts.Turns.ShouldBeGreaterThanOrEqualTo(3);
        facts.TryGetValue(kept, out var last).ShouldBeTrue();
        last.N.ShouldBe(1);
    }

    [Fact]
    public void ANodeNobodyAsksAboutIsLetGoWithinTwoGenerations()
    {
        var facts = new NodeFacts<Known>();
        var once = Leaf(-1);
        facts.GetValue(once, new Known(1));

        for (var i = 0; i < NodeFacts<Known>.GenerationSize * 2 + 1; i++)
        {
            facts.GetValue(Leaf(i), new Known(i));
        }

        facts.TryGetValue(once, out _).ShouldBeFalse();
    }

    [Fact]
    public void ACopyMadeWithWithIsANodeOfItsOwn()
    {
        // By reference: an equal-looking copy shares nothing, so a copy that changed nothing measures once more and never reads a
        // fact its source holds for a size it was not given.
        var facts = new NodeFacts<Known>();
        var source = Leaf(1);
        facts.GetValue(source, new Known(1));

        var copy = source with { };

        copy.ShouldBe(source);
        facts.TryGetValue(copy, out _).ShouldBeFalse();
        facts.GetValue(source, new Known(2)).N.ShouldBe(1);
    }
}
