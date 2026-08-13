using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// The cursor as a property of a region rather than a geometry predicate the host maintains.
///
/// <para>The bug this exists to prevent has a shape worth stating. A host that answers "what should the
/// pointer look like?" from coordinates ends up with a predicate — over the page, but not the tool
/// palette, and not the expand handle, and not any open panel — and every overlay added afterwards
/// silently invalidates it, because the overlay draws over the page and the predicate goes on saying
/// page. The symptom is a drop-up menu that keeps showing the text tool's I-beam. Asking the region
/// list instead removes the class: the regions already know what is on top, since that is what they are
/// for.</para>
/// </summary>
public class CursorRegionTests
{
    [Fact]
    public void ARegionStatesTheCursorForItsRect()
    {
        var t = new ClickableRegionTracker();
        t.BeginFrame();
        t.Register(0, 0, 100, 100, new HitResult.ButtonHit("go"), cursor: CursorKind.Pointer);

        t.HitTestCursor(50, 50).ShouldBe(CursorKind.Pointer);
        t.HitTestCursor(150, 50).ShouldBeNull();       // outside: nobody has a view
    }

    [Fact]
    public void NothingStatedMeansNoOpinionRatherThanADefault()
    {
        var t = new ClickableRegionTracker();
        t.BeginFrame();
        t.Register(0, 0, 100, 100, new HitResult.ButtonHit("go"));

        // Null, not CursorKind.Default: a region that says nothing must not overrule the caller's own
        // fallback, or every plain button would stamp the arrow over a host that wanted a crosshair.
        t.HitTestCursor(50, 50).ShouldBeNull();
    }

    [Fact]
    public void TheTopmostRegionWithAViewWins()
    {
        var t = new ClickableRegionTracker();
        t.BeginFrame();
        t.RegisterCursor(0, 0, 200, 200, CursorKind.Text);              // the page, under everything
        t.RegisterCursor(50, 50, 100, 100, CursorKind.Default);         // a panel card over it

        t.HitTestCursor(100, 100).ShouldBe(CursorKind.Default);         // over the card
        t.HitTestCursor(10, 10).ShouldBe(CursorKind.Text);              // beside it, still the page
    }

    /// <summary>
    /// A row inside a card need not repeat the card's cursor: a region without an opinion is
    /// transparent, so the enclosing statement still reaches the pointer. This is what keeps the
    /// declaration in one place — on the card — instead of on every row a panel ever adds.
    /// </summary>
    [Fact]
    public void ARegionWithoutAnOpinionIsTransparentToTheOneBeneath()
    {
        var t = new ClickableRegionTracker();
        t.BeginFrame();
        t.RegisterCursor(0, 0, 200, 200, CursorKind.Default);           // the card
        t.Register(10, 10, 100, 20, new HitResult.ButtonHit("row"));    // a row on it, no cursor stated

        t.HitTestCursor(50, 15).ShouldBe(CursorKind.Default);
        // ...and the row is still what a CLICK finds, so the two questions stay independent.
        t.HitTestAndDispatch(50, 15).ShouldBeOfType<HitResult.ButtonHit>();
    }

    /// <summary>
    /// A cursor-only region still takes part in hit testing, as chrome. That is what lets a host tell
    /// "the pointer is over my overlay" from "the pointer is over the content" without re-deriving the
    /// overlay's bounds — the question the geometry predicate was really trying to answer.
    /// </summary>
    [Fact]
    public void ACursorOnlyRegionReadsAsChrome()
    {
        var t = new ClickableRegionTracker();
        t.BeginFrame();
        t.RegisterCursor(0, 0, 100, 100, CursorKind.Default);

        t.HitTest(50, 50).ShouldBeOfType<HitResult.ChromeHit>();
        t.HitTest(150, 50).ShouldBeNull();
    }

    [Fact]
    public void ANodeCarriesItsCursorLikeItCarriesItsHit()
    {
        var clickable = Layout.Builder.Text("x", 10f, default)
            .Clickable(new HitResult.ButtonHit("go"), cursor: CursorKind.Pointer);
        clickable.Hit.ShouldBeOfType<HitResult.ButtonHit>();
        clickable.Cursor.ShouldBe(CursorKind.Pointer);

        // And a node may state one WITHOUT becoming a click target -- a card saying "arrow here".
        var bare = Layout.Builder.Text("x", 10f, default).WithCursor(CursorKind.Default);
        bare.Hit.ShouldBeNull();
        bare.Cursor.ShouldBe(CursorKind.Default);
    }
}
