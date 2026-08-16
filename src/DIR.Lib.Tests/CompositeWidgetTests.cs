using DIR.Lib;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// Covers <see cref="CompositeWidget{TSurface}"/>: a widget that paints others into the same surface
/// answers for them too, in one stated order, across every aggregate query.
/// </summary>
/// <remarks>
/// The bug this class exists for is silent — a child's regions live on the child, so a composite that
/// forgets one still paints a correct frame while that child's controls stop answering. These tests are
/// therefore about what is REACHABLE, not about what is drawn.
/// </remarks>
public class CompositeWidgetTests
{
    private const string Font = "stub.ttf";

    private sealed class StubRenderer(uint w, uint h) : RgbaImageRenderer(w, h)
    {
        public override (float Width, float Height) MeasureText(ReadOnlySpan<char> text, string fontFamily, float fontSize)
            => (text.Length * fontSize * 0.5f, fontSize);

        public override void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize,
            RGBAColor32 fontColor, in RectInt layout, TextAlign horizAlign = TextAlign.Near,
            TextAlign vertAlign = TextAlign.Center)
        { }
    }

    /// <summary>A leaf that registers one region and one field wherever it is told.</summary>
    private sealed class Leaf(Renderer<RgbaImage> renderer, string id) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public TextInputState Field { get; } = new();

        public void Paint(float x, float y, float w, float h, CursorKind cursor)
        {
            BeginFrame();
            RegisterClickable(x, y, w, h, new HitResult.ButtonHit(id), cursor: cursor);
            // In a corner of the leaf, so it never shadows the button at the points these tests probe:
            // regions resolve last-registered-first, and a field over the whole rect would win every hit.
            RegisterClickable(x, y, 10, 10, new HitResult.TextInputHit(Field), cursor: CursorKind.Text);
        }
    }

    /// <summary>A chrome that paints its own bar plus whatever children it was given.</summary>
    private sealed class Chrome(Renderer<RgbaImage> renderer) : CompositeWidget<RgbaImage>(renderer)
    {
        private readonly List<PixelWidgetBase<RgbaImage>> _children = [];

        protected override IReadOnlyList<PixelWidgetBase<RgbaImage>> Children => _children;

        public TextInputState OwnField { get; } = new();

        /// <param name="children">In paint order, back to front.</param>
        public void Paint(params PixelWidgetBase<RgbaImage>[] children)
        {
            BeginFrame();
            _children.Clear();
            _children.AddRange(children);

            // The composite's own chrome: a bar across the top, painted OVER the children.
            RegisterClickable(0, 0, 200, 20, new HitResult.ButtonHit("chrome"), cursor: CursorKind.Default);
            RegisterClickable(190, 0, 10, 20, new HitResult.TextInputHit(OwnField), cursor: CursorKind.Text);
        }
    }

    private static (Chrome Chrome, Leaf Back, Leaf Front) Composed()
    {
        var renderer = new StubRenderer(200, 200);
        var chrome = new Chrome(renderer) { FontPath = Font };
        var back = new Leaf(renderer, "back") { FontPath = Font };
        var front = new Leaf(renderer, "front") { FontPath = Font };

        // Both leaves cover the same rect below the chrome bar, so only z-order separates them.
        back.Paint(0, 40, 100, 100, CursorKind.Text);
        front.Paint(0, 40, 100, 100, CursorKind.Pointer);
        chrome.Paint(back, front);

        return (chrome, back, front);
    }

    [Fact]
    public void AChildsRegionAnswersThroughTheComposite()
    {
        // The whole point: a host holding only the composite reaches the controls its children drew.
        var (chrome, _, _) = Composed();

        chrome.HitTest(50f, 90f).ShouldBe(new HitResult.ButtonHit("front"));
    }

    [Fact]
    public void TheTopmostChildAnswersFirst()
    {
        // Declared back to front, so the LAST child painted is the one a press lands on.
        var (chrome, _, _) = Composed();

        chrome.HitTest(50f, 90f).ShouldBe(new HitResult.ButtonHit("front"));
        chrome.HitTestCursor(50f, 90f).ShouldBe(CursorKind.Pointer);
    }

    [Fact]
    public void TheCompositesOwnChromeAnswersBeforeItsChildren()
    {
        // Its own painting is either a background registering nothing, or chrome drawn OVER the
        // children -- a status bar. Asking it first is what makes the second case right.
        var renderer = new StubRenderer(200, 200);
        var chrome = new Chrome(renderer) { FontPath = Font };
        var under = new Leaf(renderer, "under") { FontPath = Font };

        // The child covers the whole surface, including the region the chrome bar occupies.
        under.Paint(0, 0, 200, 200, CursorKind.Text);
        chrome.Paint(under);

        chrome.HitTest(50f, 10f).ShouldBe(new HitResult.ButtonHit("chrome"));   // inside the bar
        chrome.HitTest(50f, 90f).ShouldBe(new HitResult.ButtonHit("under"));    // below it
    }

    [Fact]
    public void EveryAggregateQueryUsesTheSameOrder()
    {
        // The failure this class replaces: one composite stating its child list per query, in three
        // different orders, with one query missing a child outright. One declaration, so they cannot
        // disagree.
        var (chrome, back, front) = Composed();

        chrome.HitTest(50f, 90f).ShouldBe(new HitResult.ButtonHit("front"));
        chrome.HitTestAndDispatch(50f, 90f).ShouldBe(new HitResult.ButtonHit("front"));
        chrome.HitTestCursor(50f, 90f).ShouldBe(CursorKind.Pointer);

        // Enumerations run in PAINT order instead -- they read the frame the way a person does.
        chrome.PaintedRegions()
            .Select(r => (r.Result as HitResult.ButtonHit)?.Action)
            .Where(action => action is not null)
            .ShouldBe(["chrome", "back", "front"]);
        chrome.GetRegisteredTextInputs().ShouldBe([chrome.OwnField, back.Field, front.Field]);
    }

    [Fact]
    public void TextInputsAcrossTheFrameAreReachableForTabCycling()
    {
        // Asking only the composite, or only the active child, blurs a live field every frame -- which
        // looks exactly like the bug that composing them fixes.
        var (chrome, back, front) = Composed();

        chrome.GetRegisteredTextInputs().Count.ShouldBe(3);
        chrome.GetRegisteredTextInputs().ShouldContain(front.Field);
    }

    [Fact]
    public void AChildTheCompositeStoppedPaintingStopsAnswering()
    {
        // Composition is restated per frame, so dropping a child drops its controls with it. Without
        // this a page switched away from keeps taking clicks.
        var (chrome, back, _) = Composed();
        chrome.HitTest(50f, 90f).ShouldBe(new HitResult.ButtonHit("front"));

        chrome.Paint(back);   // repainted with the front child gone

        chrome.HitTest(50f, 90f).ShouldBe(new HitResult.ButtonHit("back"));
    }

    [Fact]
    public void ACompositeWithNoChildrenIsJustAWidget()
    {
        var renderer = new StubRenderer(200, 200);
        var chrome = new Chrome(renderer) { FontPath = Font };
        chrome.Paint();

        chrome.HitTest(50f, 10f).ShouldBe(new HitResult.ButtonHit("chrome"));
        chrome.HitTest(50f, 90f).ShouldBeNull();
        chrome.PaintedRegions().Count.ShouldBe(2);   // its button and its own field
    }
}
