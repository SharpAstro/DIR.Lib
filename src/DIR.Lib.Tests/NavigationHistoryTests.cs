using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="NavigationHistory{TPlace}"/>: the order of a window's moves. A move pushes the place it left, Back and Forward
/// trade the current place for the one they return to, and a new move forgets what was forward, as a browser does.
/// </summary>
public class NavigationHistoryTests
{
    [Fact]
    public void ANewHistoryHasNowhereToGo()
    {
        var history = new NavigationHistory<string>();

        history.CanGoBack.ShouldBeFalse();
        history.CanGoForward.ShouldBeFalse();
        history.Back("here", out var back).ShouldBeFalse();
        back.ShouldBeNull();
        history.Forward("here", out var forward).ShouldBeFalse();
        forward.ShouldBeNull();
    }

    [Fact]
    public void BackReturnsThePlacesInTheReverseOrderTheyWereLeft()
    {
        var history = new NavigationHistory<string>();
        history.Push("A");
        history.Push("B");

        history.Back("C", out var first).ShouldBeTrue();
        history.Back(first, out var second).ShouldBeTrue();

        first.ShouldBe("B");
        second.ShouldBe("A");
        history.CanGoBack.ShouldBeFalse();
        history.BackCount.ShouldBe(0);
        history.ForwardCount.ShouldBe(2);
    }

    [Fact]
    public void ForwardUndoesBackWithTheSamePlacesInTheSameOrder()
    {
        var history = new NavigationHistory<string>();
        history.Push("A");
        history.Push("B");
        history.Back("C", out var atB).ShouldBeTrue();
        history.Back(atB, out var atA).ShouldBeTrue();

        history.Forward(atA, out var forwardToB).ShouldBeTrue();
        history.Forward(forwardToB, out var forwardToC).ShouldBeTrue();

        forwardToB.ShouldBe("B");
        forwardToC.ShouldBe("C");
        history.CanGoForward.ShouldBeFalse();
        // Back is whole again: a round trip leaves both stacks as they were.
        history.BackCount.ShouldBe(2);
    }

    [Fact]
    public void ANewMoveAfterGoingBackDropsThePlacesForwardOfIt()
    {
        var history = new NavigationHistory<string>();
        history.Push("A");
        history.Push("B");
        history.Back("C", out _).ShouldBeTrue();
        history.CanGoForward.ShouldBeTrue();

        history.Push("B");

        history.CanGoForward.ShouldBeFalse();
        history.ForwardCount.ShouldBe(0);
        history.Forward("D", out _).ShouldBeFalse();
    }

    [Fact]
    public void TheOldestPlaceFallsOffTheBackStackAtCapacity()
    {
        var history = new NavigationHistory<int>(capacity: 3);
        for (var i = 1; i <= 5; i++)
        {
            history.Push(i);
        }

        history.Capacity.ShouldBe(3);
        history.BackCount.ShouldBe(3);
        var current = 6;
        var seen = new List<int>();
        while (history.Back(current, out var place))
        {
            seen.Add(place);
            current = place;
        }
        seen.ShouldBe([5, 4, 3]);
    }

    [Fact]
    public void ACapacityBelowOneIsOne()
    {
        var history = new NavigationHistory<int>(capacity: 0);

        history.Push(1);
        history.Push(2);

        history.Capacity.ShouldBe(1);
        history.BackCount.ShouldBe(1);
        history.Back(3, out var place).ShouldBeTrue();
        place.ShouldBe(2);
    }

    [Fact]
    public void ADefaultOfAValuePlaceIsNotMistakenForNowhere()
    {
        // A place may be a value type whose default is a real place: only the bool says whether there was one.
        var history = new NavigationHistory<int>();
        history.Push(0);

        history.Back(7, out var place).ShouldBeTrue();

        place.ShouldBe(0);
    }
}
