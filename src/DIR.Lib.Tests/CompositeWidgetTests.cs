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

        /// <summary>How many presses this leaf's own region has answered -- which leaf a ROUTED press
        /// reached, where a returned hit only said which one was on top.</summary>
        public int Clicks { get; private set; }

        public void Paint(float x, float y, float w, float h, CursorKind cursor)
        {
            BeginFrame();
            RegisterClickable(x, y, w, h, new HitResult.ButtonHit(id), _ => Clicks++, cursor);
            // In a corner of the leaf, so it never shadows the button at the points these tests probe:
            // regions resolve last-registered-first, and a field over the whole rect would win every hit.
            RegisterClickable(x, y, 10, 10, new HitResult.TextInputHit(Field, default), cursor: CursorKind.Text);
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
            RegisterClickable(190, 0, 10, 20, new HitResult.TextInputHit(OwnField, default), cursor: CursorKind.Text);
        }
    }

    /// <summary>A composite that paints one child and then a region of its own OVER it.</summary>
    private sealed class Host(Renderer<RgbaImage> renderer) : CompositeWidget<RgbaImage>(renderer)
    {
        private readonly List<PixelWidgetBase<RgbaImage>> _children = [];

        protected override IReadOnlyList<PixelWidgetBase<RgbaImage>> Children => _children;

        /// <summary>How many presses the host's own region has answered.</summary>
        public int Clicks { get; private set; }

        public void Paint(PixelWidgetBase<RgbaImage> child, float x, float y, float w, float h)
        {
            BeginFrame();
            _children.Clear();
            _children.Add(child);

            // Painted after the child, so over it.
            RegisterClickable(x, y, w, h, new HitResult.ButtonHit("own"), _ => Clicks++, CursorKind.Pointer);
        }
    }

    private static readonly KeyChord Chord = new(InputKey.K, InputModifier.Ctrl);
    private static readonly RGBAColor32 Lit = new(0x40, 0x40, 0x60, 0xff);

    /// <summary>
    /// One layer of a composed frame: a node filling its bounds that takes a press, states a cursor, scrolls,
    /// lights under the pointer and answers <see cref="Chord"/>, so every combined query finds it.
    /// </summary>
    private sealed class Layer(string id, CursorKind cursor)
    {
        /// <summary>How many times its handler ran, by a press or by the chord.</summary>
        public int Acted { get; private set; }

        public ListScrollController Scroll { get; } = new();

        public Layout.Node Node() => Layout.Builder.Spacer().Stretch()
            .Clickable(new HitResult.ButtonHit(id), _ => Acted++, cursor)
            .BgHover(Lit)
            .WithShortcut(Chord)
            .WithScroll(Scroll);
    }

    /// <summary>A widget painting one layout tree.</summary>
    private sealed class Pane(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer)
    {
        public void Paint(Layout.Node root, RectF32 bounds)
        {
            BeginFrame();
            RenderLayout(root, bounds, fontPath: Font, scale: DesignScale.One);
        }
    }

    /// <summary>A composite painting its own tree among its children, at <see cref="ChildrenUnderOwn"/>.</summary>
    private sealed class Layered(Renderer<RgbaImage> renderer, int? childrenUnderOwn = null) : CompositeWidget<RgbaImage>(renderer)
    {
        private readonly List<PixelWidgetBase<RgbaImage>> _children = [];

        protected override IReadOnlyList<PixelWidgetBase<RgbaImage>> Children => _children;

        protected override int ChildrenUnderOwn => childrenUnderOwn ?? base.ChildrenUnderOwn;

        /// <param name="children">In paint order, back to front; the first <see cref="ChildrenUnderOwn"/>
        /// of them under <paramref name="own"/> and the rest over it.</param>
        public void Paint(Layout.Node own, RectF32 bounds, params PixelWidgetBase<RgbaImage>[] children)
        {
            BeginFrame();
            _children.Clear();
            _children.AddRange(children);
            RenderLayout(own, bounds, fontPath: Font, scale: DesignScale.One);
        }
    }

    private static Pane Painted(Renderer<RgbaImage> renderer, Layer layer, RectF32 bounds)
    {
        var pane = new Pane(renderer) { FontPath = Font };
        pane.Paint(layer.Node(), bounds);
        return pane;
    }

    /// <summary>The ids of the ButtonHits a list holds, in its order.</summary>
    private static IEnumerable<string?> Ids(IEnumerable<HitResult?> hits)
        => hits.Select(hit => (hit as HitResult.ButtonHit)?.Action).Where(action => action is not null);

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

        // And a ROUTED press keeps that order, which is the path a host uses: the router walks
        // CollectPaintedRegions, and the bar has to come out of it ahead of the child it covers.
        Routing.Press(chrome, 50f, 10f).ShouldBeTrue();
        under.Clicks.ShouldBe(0);
        Routing.Press(chrome, 50f, 90f).ShouldBeTrue();
        under.Clicks.ShouldBe(1);
    }

    [Fact]
    public void AnOverlayTheCompositePaintsOverAChildTakesTheRoutedPress()
    {
        // TianWen's Live Session tab, which paints its Planetary child and then its mode dropdown over it.
        // The child's capture panel scrolls, so it registers a region over its whole column, and a press
        // on a menu row reached that region first and was swallowed.
        var renderer = new StubRenderer(200, 200);
        var host = new Host(renderer) { FontPath = Font };
        var panel = new Leaf(renderer, "panel") { FontPath = Font };
        panel.Paint(0, 0, 200, 200, CursorKind.Default);
        host.Paint(panel, 20, 50, 60, 20);   // a menu row, over the panel

        Routing.Press(host, 50f, 60f).ShouldBeTrue();
        host.Clicks.ShouldBe(1);
        panel.Clicks.ShouldBe(0);
        host.HitTest(50f, 60f).ShouldBe(new HitResult.ButtonHit("own"));   // the router and HitTest agree

        // Beside the row the panel still answers.
        Routing.Press(host, 150f, 150f).ShouldBeTrue();
        panel.Clicks.ShouldBe(1);
        host.Clicks.ShouldBe(1);
    }

    [Fact]
    public void AChildPaintedBehindTheCompositeIsCoveredByIt()
    {
        // A backdrop child drawn wholly behind the composite's own content, the default order: a sky with
        // a photograph painted over it. A label the sky registered under the photograph is covered there,
        // and answers only where it shows. (TianWen's image viewer is not this shape: its sky map also
        // floats a palette over the picture, so it keeps the map wholly over, ChildrenUnderOwn = 0, below.)
        var renderer = new StubRenderer(200, 200);
        var host = new Host(renderer) { FontPath = Font };
        var sky = new Leaf(renderer, "label") { FontPath = Font };
        sky.Paint(100, 100, 80, 20, CursorKind.Pointer);   // a label on the sky
        host.Paint(sky, 60, 60, 80, 80);                   // the photograph, over part of it

        Routing.Press(host, 120f, 110f).ShouldBeTrue();    // label and photograph both
        host.Clicks.ShouldBe(1);
        sky.Clicks.ShouldBe(0);

        Routing.Press(host, 170f, 110f).ShouldBeTrue();    // the label, clear of the photograph
        sky.Clicks.ShouldBe(1);
        host.Clicks.ShouldBe(1);
    }

    [Fact]
    public void EveryAggregateQueryUsesTheSameOrder()
    {
        // The failure this class replaces: one composite stating its child list per query, in three
        // different orders, with one query missing a child outright. One declaration, so they cannot
        // disagree.
        var (chrome, back, front) = Composed();

        chrome.HitTest(50f, 90f).ShouldBe(new HitResult.ButtonHit("front"));
        chrome.HitTestCursor(50f, 90f).ShouldBe(CursorKind.Pointer);

        // And a press RUNS the front child's handler, not the back one's. Routed rather than dispatched
        // through the composite, so the aggregation under test is the one a host actually uses: the
        // router walks CollectPaintedRegions from the end.
        Routing.Press(chrome, 50f, 90f).ShouldBeTrue();
        front.Clicks.ShouldBe(1);
        back.Clicks.ShouldBe(0);

        // The region enumeration runs in PAINT order: the children back to front, then the chrome painted
        // over them. The router walks it from the end, which is how the chrome is asked first.
        chrome.PaintedRegions()
            .Select(r => (r.Result as HitResult.ButtonHit)?.Action)
            .Where(action => action is not null)
            .ShouldBe(["back", "front", "chrome"]);

        // Tab cycling reads the frame the way a person does instead, the chrome's bar above its children.
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

    [Fact]
    public void ByDefaultTheCompositesOwnContentIsOverEveryChildForEveryQuery()
    {
        var renderer = new StubRenderer(200, 200);
        var (own, under) = (new Layer("own", CursorKind.Pointer), new Layer("under", CursorKind.Text));
        var host = new Layered(renderer) { FontPath = Font };
        host.Paint(own.Node(), new RectF32(50, 50, 100, 100), Painted(renderer, under, new RectF32(0, 0, 200, 200)));

        Routing.Press(host, 75f, 75f).ShouldBeTrue();
        own.Acted.ShouldBe(1);
        under.Acted.ShouldBe(0);
        host.HitTest(75f, 75f).ShouldBe(new HitResult.ButtonHit("own"));
        host.HitTestCursor(75f, 75f).ShouldBe(CursorKind.Pointer);
        host.ScrollTargetAt(75f, 75f).ShouldBeSameAs(own.Scroll);

        // A chord both declare reaches the one on top, as a press there does.
        Routing.Key(host, Chord.Key, Chord.Modifiers).ShouldBeTrue();
        own.Acted.ShouldBe(2);
        under.Acted.ShouldBe(0);
    }

    [Fact]
    public void AChildDeclaredOverTheCompositesOwnContentAnswersFirstForEveryQuery()
    {
        // ChildrenUnderOwn = 0: TianWen's image viewer, whose sky map floats its palette over the picture.
        var renderer = new StubRenderer(200, 200);
        var (own, over) = (new Layer("own", CursorKind.Pointer), new Layer("over", CursorKind.Move));
        var host = new Layered(renderer, childrenUnderOwn: 0) { FontPath = Font };
        host.Paint(own.Node(), new RectF32(50, 50, 100, 100), Painted(renderer, over, new RectF32(0, 0, 200, 200)));

        Routing.Press(host, 75f, 75f).ShouldBeTrue();
        over.Acted.ShouldBe(1);
        own.Acted.ShouldBe(0);
        host.HitTest(75f, 75f).ShouldBe(new HitResult.ButtonHit("over"));
        host.HitTestCursor(75f, 75f).ShouldBe(CursorKind.Move);
        host.ScrollTargetAt(75f, 75f).ShouldBeSameAs(over.Scroll);

        Routing.Key(host, Chord.Key, Chord.Modifiers).ShouldBeTrue();
        over.Acted.ShouldBe(2);
        own.Acted.ShouldBe(0);
    }

    [Fact]
    public void ASplitInTheMiddlePutsTheCompositesOwnContentBetweenItsChildren()
    {
        // TianWen's chrome: the active tab and the rail under its status bar, the night calendar over it.
        var renderer = new StubRenderer(200, 200);
        var under = new Layer("under", CursorKind.Text);
        var own = new Layer("own", CursorKind.Pointer);
        var over = new Layer("over", CursorKind.Move);
        var host = new Layered(renderer, childrenUnderOwn: 1) { FontPath = Font };
        host.Paint(own.Node(), new RectF32(50, 50, 100, 100),
            Painted(renderer, under, new RectF32(0, 0, 200, 200)),
            Painted(renderer, over, new RectF32(100, 100, 100, 100)));

        // Paint order, back to front, for regions and nodes alike: the router walks both from the end.
        Ids(host.PaintedRegions().Select(r => (HitResult?)r.Result)).ShouldBe(["under", "own", "over"]);
        var nodes = new List<Layout.ArrangedNode<float>>();
        host.CollectPaintedNodes(nodes);
        Ids(nodes.Select(n => n.Node.Hit)).ShouldBe(["under", "own", "over"]);

        foreach (var (x, y, layer, cursor) in new[]
                 {
                     (125f, 125f, over, CursorKind.Move),     // all three
                     (75f, 75f, own, CursorKind.Pointer),     // own over the under child
                     (25f, 25f, under, CursorKind.Text),      // the under child alone
                 })
        {
            var before = layer.Acted;
            Routing.Press(host, x, y).ShouldBeTrue();
            layer.Acted.ShouldBe(before + 1, $"a press at ({x}, {y})");
            host.HitTestCursor(x, y).ShouldBe(cursor, $"the cursor at ({x}, {y})");
            host.ScrollTargetAt(x, y).ShouldBeSameAs(layer.Scroll, $"the wheel at ({x}, {y})");
        }

        host.HitTest(125f, 125f).ShouldBe(new HitResult.ButtonHit("over"));
        host.HitTest(75f, 75f).ShouldBe(new HitResult.ButtonHit("own"));
        host.HitTest(25f, 25f).ShouldBe(new HitResult.ButtonHit("under"));

        // The chord all three declare reaches the topmost.
        var (overBefore, ownBefore, underBefore) = (over.Acted, own.Acted, under.Acted);
        Routing.Key(host, Chord.Key, Chord.Modifiers).ShouldBeTrue();
        over.Acted.ShouldBe(overBefore + 1);
        own.Acted.ShouldBe(ownBefore);
        under.Acted.ShouldBe(underBefore);
    }

    [Theory]
    [InlineData(null, 1)]   // own rows over the child: moving between them changes the lit row
    [InlineData(0, 0)]      // the child over the rows: one lit node under both points, nothing changes
    public void HoverFollowsTheSameOrderAsAPress(int? childrenUnderOwn, int redraws)
    {
        // Two lit rows of the composite's own over one lit node of a child. Which node the router resolves
        // under the pointer decides whether a move between the rows asks for a frame.
        var renderer = new StubRenderer(200, 200);
        var child = new Pane(renderer) { FontPath = Font };
        child.Paint(Layout.Builder.Spacer().Stretch().BgHover(Lit), new RectF32(0, 0, 200, 200));
        var host = new Layered(renderer, childrenUnderOwn) { FontPath = Font };
        host.Paint(
            Layout.Builder.VStack(
                Layout.Builder.Spacer().WStar().RowH(50f).BgHover(Lit),
                Layout.Builder.Spacer().WStar().RowH(50f).BgHover(Lit)).Stretch(),
            new RectF32(50, 50, 100, 100),
            child);

        var requested = 0;
        var router = new InputRouter(host.Ui, new BackgroundTaskTracker(), () => requested++)
        {
            Widgets = () => [host],
        };
        router.Handle(new InputEvent.MouseMove(75f, 75f));   // onto the first row
        requested = 0;

        router.Handle(new InputEvent.MouseMove(75f, 125f));  // onto the second

        requested.ShouldBe(redraws);
    }
}
