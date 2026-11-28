using DIR.Lib.Layout;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// The engine's per-node facts live in a two-generation cache keyed by node reference, not in weak tables whose GC handles a tree
/// built per frame churned (SharpAstro/DIR.Lib#341): a node that goes on being asked about keeps its facts, one nobody asks about
/// goes within two generations of entries or of time, and a copy is a node of its own.
/// </summary>
public class LayoutNodeFactsTests
{
    private sealed class Known(int n)
    {
        public int N { get; } = n;
    }

    private sealed class Clock : TimeProvider
    {
        private long _ticks;

        public override long GetTimestamp() => _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }

    private static Node Leaf(int i) => Builder.Text($"row {i}", 13f);

    [Fact]
    public void ANodeAskedAboutAgainKeepsItsFactsWhileGenerationsOfOthersComeAndGo()
    {
        var facts = new NodeFacts<Known>();
        var kept = Leaf(-1);
        facts.GetValue(kept, new Known(1));

        // Four generations of nodes built once and dropped, as a tree built per frame makes; the kept node is asked about between,
        // once through each lookup, so both move a node found in the previous generation forward.
        for (var i = 0; i < NodeFacts<Known>.GenerationSize * 4; i++)
        {
            facts.GetValue(Leaf(i), new Known(i));
            if (i % 2000 == 0)
            {
                facts.TryGetValue(kept, out var again).ShouldBeTrue();
                again.N.ShouldBe(1);
            }
            else if (i % 1000 == 0)
            {
                facts.GetValue(kept, new Known(99)).N.ShouldBe(1);
            }
        }

        facts.TryGetValue(kept, out var last).ShouldBeTrue();
        last.N.ShouldBe(1);
        facts.Count.ShouldBeLessThanOrEqualTo(2 * NodeFacts<Known>.GenerationSize);
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
    public void AHostThatStillReadsButNoLongerBuildsLetsGoOfWhatItDroppedWithinTwoTurnsOfTime()
    {
        // A kept tree is read every frame and adds nothing, so no count would ever push a dropped node out: age does.
        var clock = new Clock();
        var facts = new NodeFacts<Known>(clock);
        var kept = Leaf(1);
        var dropped = Leaf(2);
        facts.GetValue(kept, new Known(1));
        facts.GetValue(dropped, new Known(2));

        for (var turn = 0; turn < 3; turn++)
        {
            clock.Advance(NodeFacts<Known>.TurnAfter);
            facts.TryGetValue(kept, out var again).ShouldBeTrue();
            again.N.ShouldBe(1);
        }

        facts.TryGetValue(dropped, out _).ShouldBeFalse();
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
