using System;
using System.Collections.Generic;
using System.Linq;
using DIR.Lib.ChildWindows;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="ChildWindowsLayout"/> driven through a real <see cref="InputRouter"/> over the stub measure context:
/// no process, no clock. Every point is checked at a wide and a narrow host.
/// </summary>
public class ChildWindowsLayoutTests
{
    /// <summary>The window's border, which the title bar and body sit inside.</summary>
    private const float Border = 1f;

    private static readonly RectF32 Wide = new(0, 0, 1000, 600);
    private static readonly RectF32 Narrow = new(0, 0, 400, 800);

    public static TheoryData<float, float> Hosts => new() { { 1000f, 600f }, { 400f, 800f } };

    private sealed class Harness
    {
        private readonly DeclarationWidget _widget;
        public ChildWindowSet Set { get; } = new();
        public InputRouter Router { get; }
        public DesignScale Scale { get; set; } = DesignScale.One;
        public int Changed { get; private set; }
        public int ControlPresses { get; private set; }
        public int HostPresses { get; private set; }
        public bool HostContentPressable { get; init; }
        public Dictionary<long, float> ExtraContentHeight { get; } = [];
        public List<InputEvent> Unhandled { get; } = [];
        private readonly RectF32 _host;
        private Layout.Node _root = Layout.Builder.Spacer();

        public Harness(float w, float h)
        {
            _host = new RectF32(0, 0, w, h);
            _widget = new DeclarationWidget(new DeclarationStubRenderer((uint)w, (uint)h));
            Set.NoteHost(_host);
            Router = new InputRouter(_widget.Ui, new BackgroundTaskTracker(), () => { })
            {
                Widgets = () => [_widget],
                Unhandled = e => { Unhandled.Add(e); return false; },
            };
        }

        public ChildWindow Add(string title, float x, float y, float w = 200, float h = 150)
            => Set.Add(title, new RectF32(x, y, w, h));

        public Layout.Node Content(ChildWindow c)
        {
            var id = c.Id;
            var control = Layout.Builder.Text("ok", 12f).WFixed(40f).HFixed(20f)
                .Clickable(new HitResult.ButtonHit("ok:" + id), _ => ControlPresses++);
            var extra = ExtraContentHeight.GetValueOrDefault(id);
            return extra > 0f
                ? Layout.Builder.VStack(control, Layout.Builder.Spacer().HFixed(extra))
                : Layout.Builder.VStack(control);
        }

        public void Paint()
        {
            var host = Layout.Builder.Spacer().WStar().HStar();
            if (HostContentPressable) host = host.Clickable(new HitResult.ButtonHit("host"), _ => HostPresses++);
            _root = ChildWindowsLayout.Build(host, Set, Content,
                ChildWindowColors.Dark, null, Scale, () => Changed++);
            _widget.Render(_root, _host);
        }

        public List<Layout.ArrangedNode<float>> Arranged()
            => [.. Layout.Engine.Arrange(_root, new Rect<float>(_host.X, _host.Y, _host.Width, _host.Height), _widget.Context())];

        public RectF32 Of(long id, ChildWindowPart part, ResizeEdge edge = ResizeEdge.None)
        {
            var n = Arranged().First(a => a.Node.Hit is ChildWindowHit h && h.Id == id && h.Part == part && h.Edge == edge);
            return new RectF32(n.Bounds.X, n.Bounds.Y, n.Bounds.Width, n.Bounds.Height);
        }

        public RectF32 ControlOf(long id)
        {
            var n = Arranged().First(a => a.Node.Hit is HitResult.ButtonHit b && b.Action == "ok:" + id);
            return new RectF32(n.Bounds.X, n.Bounds.Y, n.Bounds.Width, n.Bounds.Height);
        }

        public bool Press(float x, float y, int clicks = 1)
            => Router.Handle(new InputEvent.MouseDown(x, y, MouseButton.Left, InputModifier.None, clicks));

        public bool Move(float x, float y) => Router.Handle(new InputEvent.MouseMove(x, y, MouseButton.Left));

        public bool Release(float x, float y) => Router.Handle(new InputEvent.MouseUp(x, y, MouseButton.Left));
    }

    private static (float X, float Y) Mid(RectF32 r) => (r.X + r.Width / 2f, r.Y + r.Height / 2f);

    [Theory, MemberData(nameof(Hosts))]
    public void EveryWindowIsArrangedAtTheModelsBounds(float w, float h)
    {
        var t = new Harness(w, h);
        var a = t.Add("A", 20, 40);
        var b = t.Add("B", 90, 120, 180, 130);
        t.Paint();
        foreach (var c in new[] { a, b })
        {
            var title = t.Of(c.Id, ChildWindowPart.Title);
            title.X.ShouldBe(c.Bounds.X + Border, 0.01f);
            title.Y.ShouldBe(c.Bounds.Y + Border, 0.01f);
            title.Width.ShouldBe(c.Bounds.Width - 2f * Border, 0.01f);
            title.Height.ShouldBe(ChildWindowSet.TitleBarHeight, 0.01f);
        }
    }

    [Theory, MemberData(nameof(Hosts))]
    public void APressOnTheTitleTheBodyOrAControlBringsTheWindowToTheFrontAndActivatesIt(float w, float h)
    {
        var t = new Harness(w, h);
        var back = t.Add("Back", 10, 10);
        var front = t.Add("Front", 260, 10);
        t.Paint();

        foreach (var pick in new Func<(float, float)>[]
        {
            () => Mid(t.Of(back.Id, ChildWindowPart.Title)),
            () => { var r = t.Of(back.Id, ChildWindowPart.Body); return (r.X + r.Width - 10, r.Y + r.Height - 10); },
            () => Mid(t.ControlOf(back.Id)),
        })
        {
            t.Set.BringToFront(front.Id);
            t.Paint();
            t.Set.Active!.Id.ShouldBe(front.Id);
            var (x, y) = pick();
            t.Press(x, y).ShouldBeTrue();
            t.Release(x, y);
            t.Set.Active!.Id.ShouldBe(back.Id);
            t.Set.Children[0].Id.ShouldBe(back.Id);
        }
    }

    [Theory, MemberData(nameof(Hosts))]
    public void AControlInsideAWindowStillGetsItsOwnPress(float w, float h)
    {
        var t = new Harness(w, h);
        var back = t.Add("Back", 10, 10);
        t.Add("Front", 260, 10);
        t.Paint();

        var (x, y) = Mid(t.ControlOf(back.Id));
        t.Press(x, y);
        t.ControlPresses.ShouldBe(1);
        t.Set.Active!.Id.ShouldBe(back.Id);
    }

    [Theory, MemberData(nameof(Hosts))]
    public void OnlyTheTopmostWindowUnderThePointTakesThePress(float w, float h)
    {
        var t = new Harness(w, h);
        var back = t.Add("Back", 10, 10, 220, 160);
        var mid = t.Add("Mid", 60, 40, 220, 160);
        var front = t.Add("Front", 110, 70, 220, 160);
        t.Set.BringToFront(mid.Id);
        t.Set.BringToFront(front.Id);
        t.Paint();

        // A point inside all three bodies, away from every control and handle.
        var (x, y) = (200f, 150f);
        t.Press(x, y);
        t.Release(x, y);
        t.Set.Children.Select(c => c.Id).ToArray().ShouldBe([front.Id, mid.Id, back.Id], "the front window already took it; nothing else moved");

        // A point only the two back windows share, where Mid is the upper one.
        t.Paint();
        t.Press(100f, 160f);
        t.Release(100f, 160f);
        t.Set.Active!.Id.ShouldBe(mid.Id, "Mid is on top there, so Back is not told");
        t.Set.Children.Select(c => c.Id).ToArray().ShouldBe([mid.Id, front.Id, back.Id]);
        t.ControlPresses.ShouldBe(0);
    }

    [Theory, MemberData(nameof(Hosts))]
    public void ATitleDragMovesByTheDesignUnitDelta(float w, float h)
    {
        var t = new Harness(w, h);
        var a = t.Add("A", 40, 40);
        t.Paint();
        var (x, y) = Mid(t.Of(a.Id, ChildWindowPart.Title));

        t.Press(x, y).ShouldBeTrue();
        t.Move(x + 30, y + 20).ShouldBeTrue();
        a.Bounds.X.ShouldBe(70f, 0.01f);
        a.Bounds.Y.ShouldBe(60f, 0.01f);
        t.Release(x + 30, y + 20);
        t.Unhandled.ShouldBeEmpty("no part of the gesture reached the host");
    }

    [Fact]
    public void ADragDividesThePointerDeltaByTheScale()
    {
        var t = new Harness(1000, 600) { Scale = new DesignScale(2f, 2f) };
        var a = t.Add("A", 40, 40);
        t.Paint();
        var (x, y) = Mid(t.Of(a.Id, ChildWindowPart.Title));
        t.Press(x, y);
        t.Move(x + 40, y + 20);
        t.Release(x + 40, y + 20);
        a.Bounds.X.ShouldBe(60f, 0.01f);
        a.Bounds.Y.ShouldBe(50f, 0.01f);
    }

    [Theory, MemberData(nameof(Hosts))]
    public void AHandleDragResizesByTheDeltaAndTheOppositeEdgeHoldsStill(float w, float h)
    {
        var t = new Harness(w, h);
        var a = t.Add("A", 40, 40, 200, 150);
        t.Paint();

        var (rx, ry) = Mid(t.Of(a.Id, ChildWindowPart.Handle, ResizeEdge.Right));
        t.Press(rx, ry);
        t.Move(rx + 25, ry);
        t.Release(rx + 25, ry);
        a.Bounds.Width.ShouldBe(225f, 0.01f);
        a.Bounds.X.ShouldBe(40f, 0.01f);

        t.Paint();
        var (cx, cy) = Mid(t.Of(a.Id, ChildWindowPart.Handle, ResizeEdge.Bottom | ResizeEdge.Right));
        t.Press(cx, cy);
        t.Move(cx + 10, cy + 30);
        t.Release(cx + 10, cy + 30);
        a.Bounds.Width.ShouldBe(235f, 0.01f);
        a.Bounds.Height.ShouldBe(180f, 0.01f);

        t.Paint();
        var (lx, ly) = Mid(t.Of(a.Id, ChildWindowPart.Handle, ResizeEdge.Left));
        t.Press(lx, ly);
        t.Move(lx + 15, ly);
        t.Release(lx + 15, ly);
        a.Bounds.X.ShouldBe(55f, 0.01f);
        a.Bounds.Right.ShouldBe(275f, 0.01f);
    }

    [Theory, MemberData(nameof(Hosts))]
    public void AResizeNeverGoesBelowTheMinimumAndRecoversWhenThePointerReturns(float w, float h)
    {
        var t = new Harness(w, h);
        var a = t.Add("A", 40, 40, 200, 150);
        t.Paint();
        var (x, y) = Mid(t.Of(a.Id, ChildWindowPart.Handle, ResizeEdge.Right));
        t.Press(x, y);
        t.Move(x - 500, y);
        a.Bounds.Width.ShouldBe(ChildWindowSet.MinWidth, 0.01f);
        t.Move(x - 20, y);
        a.Bounds.Width.ShouldBe(180f, 0.01f, "the total delta from the press decides, so the edge follows the pointer back");
        t.Release(x - 20, y);
    }

    [Theory, MemberData(nameof(Hosts))]
    public void CloseRemovesTheWindow(float w, float h)
    {
        var t = new Harness(w, h);
        var a = t.Add("A", 10, 10);
        var b = t.Add("B", 260, 10);
        t.Paint();
        var (x, y) = Mid(t.Of(b.Id, ChildWindowPart.Close));
        t.Press(x, y).ShouldBeTrue();
        t.Release(x, y);
        t.Set.Children.Select(c => c.Id).ToArray().ShouldBe([a.Id]);
        t.Set.Active!.Id.ShouldBe(a.Id);
    }

    [Theory, MemberData(nameof(Hosts))]
    public void AContentSizeChangeInOneWindowMovesNoOtherWindow(float w, float h)
    {
        var t = new Harness(w, h);
        var a = t.Add("A", 10, 10);
        var b = t.Add("B", 260, 10);
        t.Paint();
        var before = (t.Of(a.Id, ChildWindowPart.Title), t.Of(b.Id, ChildWindowPart.Title), t.Of(b.Id, ChildWindowPart.Body));

        t.ExtraContentHeight[a.Id] = 900f;
        t.Paint();

        t.Of(b.Id, ChildWindowPart.Title).ShouldBe(before.Item2);
        t.Of(b.Id, ChildWindowPart.Body).ShouldBe(before.Item3);
        t.Of(a.Id, ChildWindowPart.Title).ShouldBe(before.Item1);
    }

    [Theory, MemberData(nameof(Hosts))]
    public void ADraggedAwayWindowKeepsItsTitleStripReachable(float w, float h)
    {
        var t = new Harness(w, h);
        var a = t.Add("A", 40, 40);
        t.Paint();
        var (x, y) = Mid(t.Of(a.Id, ChildWindowPart.Title));
        t.Press(x, y);
        t.Move(x + 5000, y + 5000);
        t.Release(x + 5000, y + 5000);

        var host = new RectF32(0, 0, w, h);
        a.Bounds.X.ShouldBeLessThanOrEqualTo(host.Right - ChildWindowSet.KeepReachableStrip + 0.01f);
        a.Bounds.Y.ShouldBeLessThanOrEqualTo(host.Bottom - ChildWindowSet.TitleBarHeight + 0.01f);

        t.Paint();
        var title = t.Of(a.Id, ChildWindowPart.Title);
        title.X.ShouldBe(a.Bounds.X + Border, 0.01f);
        title.Y.ShouldBe(a.Bounds.Y + Border, 0.01f);

        // Reachable means a press on the part still inside the host takes the title and drags it back.
        var (px, py) = ((a.Bounds.X + host.Right) / 2f, title.Y + title.Height / 2f);
        var before = a.Bounds;
        t.Press(px, py).ShouldBeTrue();
        t.Move(px - 100f, py - 100f);
        t.Release(px - 100f, py - 100f);
        a.Bounds.X.ShouldBe(before.X - 100f, 0.01f, "the visible strip of the title bar took the press and moved the window");
        a.Bounds.Y.ShouldBe(before.Y - 100f, 0.01f);
    }

    [Theory, MemberData(nameof(Hosts))]
    public void ThePressOnEveryPartOfTheCloseButtonClosesAndNeverResizes(float w, float h)
    {
        // The close button sits at the top right, where the top edge, the right edge and a corner would otherwise reach.
        var t = new Harness(w, h);
        var a = t.Add("A", 40, 40, 200, 150);
        foreach (var (fx, fy) in new[] { (0.98f, 0.02f), (0.98f, 0.5f), (0.5f, 0.02f), (0.02f, 0.02f), (0.98f, 0.98f) })
        {
            t.Set.Find(a.Id).ShouldNotBeNull();
            t.Paint();
            var close = t.Of(a.Id, ChildWindowPart.Close);
            var (x, y) = (close.X + close.Width * fx, close.Y + close.Height * fy);
            t.Press(x, y).ShouldBeTrue();
            t.Move(x + 20f, y - 20f);
            t.Release(x + 20f, y - 20f);
            t.Set.Find(a.Id).ShouldBeNull($"a press at ({fx}, {fy}) of the close button closes the window");
            a = t.Add("A", 40, 40, 200, 150);
        }

        // The window's top-right corner point itself (on the border, beside the close button) is not a resize either.
        t.Paint();
        // That 1-unit border strip is still inside the window: the press is handled (it does not fall through to the host) and closes nothing.
        t.Press(a.Bounds.Right - 0.5f, a.Bounds.Y + 0.5f).ShouldBeTrue();
        t.Set.Find(a.Id).ShouldNotBeNull();
        t.Move(a.Bounds.Right + 20f, a.Bounds.Y - 20f);
        t.Release(a.Bounds.Right + 20f, a.Bounds.Y - 20f);
        a.Bounds.Width.ShouldBe(200f, 0.01f);
        a.Bounds.Height.ShouldBe(150f, 0.01f);
    }

    [Theory, MemberData(nameof(Hosts))]
    public void NoHandleOverlapsTheCloseButton(float w, float h)
    {
        var t = new Harness(w, h);
        var a = t.Add("A", 40, 40, 200, 150);
        t.Paint();
        var close = t.Of(a.Id, ChildWindowPart.Close);
        foreach (var n in t.Arranged().Where(n => n.Node.Hit is ChildWindowHit { Part: ChildWindowPart.Handle }))
        {
            var r = n.Bounds;
            var overlaps = r.X < close.X + close.Width && close.X < r.X + r.Width && r.Y < close.Y + close.Height && close.Y < r.Y + r.Height;
            overlaps.ShouldBeFalse($"{((ChildWindowHit)n.Node.Hit!).Edge} handle at {r.X},{r.Y} {r.Width}x{r.Height}");
        }
    }

    [Theory, MemberData(nameof(Hosts))]
    public void EachCornerHandleShowsADiagonalCursor(float w, float h)
    {
        var t = new Harness(w, h);
        var a = t.Add("A", 40, 40, 200, 150);
        t.Paint();
        var handles = t.Arranged().Where(n => n.Node.Hit is ChildWindowHit { Part: ChildWindowPart.Handle }).ToList();
        foreach (var n in handles)
        {
            var edge = ((ChildWindowHit)n.Node.Hit!).Edge;
            var expected = edge switch
            {
                ResizeEdge.Top or ResizeEdge.Bottom => CursorKind.ResizeNS,
                ResizeEdge.Left or ResizeEdge.Right => CursorKind.ResizeEW,
                ResizeEdge.Top | ResizeEdge.Left or ResizeEdge.Bottom | ResizeEdge.Right => CursorKind.ResizeNWSE,
                _ => CursorKind.ResizeNESW,
            };
            n.Node.Cursor.ShouldBe(expected, edge.ToString());
        }

        handles.ShouldNotContain(n => n.Node.Cursor == CursorKind.Crosshair);
    }

    [Theory, MemberData(nameof(Hosts))]
    public void AMaximisedChildFillsTheHostWithNoHandlesAndAMinimisedOneIsLeftToTheHost(float w, float h)
    {
        var t = new Harness(w, h);
        var a = t.Add("A", 40, 40);
        var b = t.Add("B", 80, 80);
        t.Set.SetMaximised(a.Id, true);
        t.Set.SetMinimised(b.Id, true);
        t.Paint();

        var title = t.Of(a.Id, ChildWindowPart.Title);
        // The maximised child is the host rect inside its one border, as every child is inside its own bounds.
        title.X.ShouldBe(Border, 0.01f);
        title.Y.ShouldBe(Border, 0.01f);
        title.Width.ShouldBe(w - 2f * Border, 0.01f);
        var body = t.Of(a.Id, ChildWindowPart.Body);
        (body.Y + body.Height).ShouldBe(h - Border, 0.01f);
        var hits = t.Arranged().Select(n => n.Node.Hit).OfType<ChildWindowHit>().ToList();
        hits.Count(x => x.Part == ChildWindowPart.Handle && x.Id == a.Id).ShouldBe(0, "a maximised child has no handles");
        hits.Count(x => x.Id == b.Id).ShouldBe(0, "the minimised child is the host's to show");

        // A press and drag on a maximised title moves nothing.
        var before = a.Bounds;
        var (x, y) = Mid(title);
        t.Press(x, y);
        t.Move(x + 30, y + 30);
        t.Release(x + 30, y + 30);
        a.Bounds.ShouldBe(before);
    }

    [Theory, MemberData(nameof(Hosts))]
    public void ExactlyOneChildIsActive(float w, float h)
    {
        // The set has one active child (the one a host sends keys to; the library routes no keys itself); the drawing says so on exactly that one bar.
        var t = new Harness(w, h);
        var a = t.Add("A", 10, 10);
        var b = t.Add("B", 120, 60);
        var c = t.Add("C", 60, 200);

        void Check()
        {
            t.Paint();
            var active = t.Set.Active.ShouldNotBeNull();
            var labels = t.Arranged().Where(n => n.Node is Layout.Node.Leaf { Content: Layout.Content.Text { Value: "active" } }).ToList();
            labels.Count.ShouldBe(1, "one bar says active");
            var bar = t.Of(active.Id, ChildWindowPart.Title);
            var label = labels[0].Bounds;
            (label.Y >= bar.Y - 0.5f && label.Y < bar.Y + bar.Height && label.X >= bar.X && label.X < bar.X + bar.Width)
                .ShouldBeTrue("the word sits in the active child's own title bar");
            t.Set.Children[0].Id.ShouldBe(active.Id, "the active child is the front one");
        }

        Check();
        var (x, y) = Mid(t.Of(a.Id, ChildWindowPart.Body));
        t.Press(x, y);
        t.Release(x, y);
        t.Set.Active!.Id.ShouldBe(a.Id);
        Check();

        t.Set.Close(a.Id);
        Check();
        t.Set.Children.Select(k => k.Id).ShouldContain(t.Set.Active!.Id);
        new[] { b.Id, c.Id }.ShouldContain(t.Set.Active!.Id);
    }

    [Theory, MemberData(nameof(Hosts))]
    public void CascadeAndTileReproduceTheModelsGeometry(float w, float h)
    {
        var t = new Harness(w, h);
        for (var i = 0; i < 4; i++) t.Add("W" + i, 10 + i * 5, 10 + i * 5, 150, 100);
        var host = new RectF32(0, 0, w, h);

        t.Set.Cascade(host);
        t.Paint();
        foreach (var c in t.Set.Children)
            t.Of(c.Id, ChildWindowPart.Title).X.ShouldBe(c.Bounds.X + Border, 0.01f);

        t.Set.Tile(host);
        t.Paint();
        foreach (var c in t.Set.Children)
        {
            var title = t.Of(c.Id, ChildWindowPart.Title);
            title.X.ShouldBe(c.Bounds.X + Border, 0.01f);
            title.Y.ShouldBe(c.Bounds.Y + Border, 0.01f);
            title.Width.ShouldBe(c.Bounds.Width - 2f * Border, 0.01f);
            var body = t.Of(c.Id, ChildWindowPart.Body);
            (body.Y + body.Height).ShouldBe(c.Bounds.Bottom - Border, 0.01f);
        }
    }

    [Theory, MemberData(nameof(Hosts))]
    public void TabsDrawTheTabStripAndOnlyTheActiveWindowAndATabPressSelects(float w, float h)
    {
        var t = new Harness(w, h);
        var a = t.Add("Alpha", 10, 10);
        var b = t.Add("Beta", 20, 20);
        t.Set.ShowAsTabs(new RectF32(0, 0, w, h));
        t.Paint();

        t.Set.Active!.Id.ShouldBe(b.Id);
        t.Arranged().Count(n => n.Node.Hit is HitResult.ButtonHit { Action: var act } && act.StartsWith("ok:")).ShouldBe(1, "only the active window's content is drawn");
        t.ControlOf(b.Id).ShouldNotBe(default);

        // The tab strip: a press on the left tab (stable order by id: Alpha first) selects Alpha.
        t.Press(10f, ChildWindowSet.TabStripHeight / 2f).ShouldBeTrue();
        t.Release(10f, ChildWindowSet.TabStripHeight / 2f);
        t.Set.Active!.Id.ShouldBe(a.Id);

        t.Paint();
        t.ControlOf(a.Id).ShouldNotBe(default);
        t.Set.Arrangement.ShouldBe(ChildWindowArrangement.Tabs);
    }

    [Theory, MemberData(nameof(Hosts))]
    public void APressRightOfTheLastTabReachesNothingBehindTheStrip(float w, float h)
    {
        var t = new Harness(w, h) { HostContentPressable = true };
        t.Add("Alpha", 10, 10);
        var b = t.Add("Beta", 20, 20);
        t.Set.ShowAsTabs(new RectF32(0, 0, w, h));
        t.Paint();

        var (x, y) = (w - 4f, ChildWindowSet.TabStripHeight / 2f);
        t.Press(x, y).ShouldBeTrue("the strip's empty part takes the press");
        t.Release(x, y);
        t.HostPresses.ShouldBe(0, "the host content hidden behind the tabs is not reached");
        // A release with no drag capture goes to Unhandled for every click (a button's too); the press is what must not.
        t.Unhandled.OfType<InputEvent.MouseDown>().ShouldBeEmpty("the press stopped at the strip");
        t.Set.Active!.Id.ShouldBe(b.Id, "an empty part of the strip selects no tab");
    }

    [Fact]
    public void TheTabLabelEstimateIsWideEnoughForAShortTitle()
    {
        // Pins the arranged label width (the estimate), not the painted glyphs; childwindows_tabs.png pins the painting. The default estimate must cover the real advance of a proportional face; DejaVu Sans "Camera" at 12 is about 47 units.
        var t = new Harness(800, 480);
        t.Add("Camera", 10, 10);
        t.Add("Mount", 20, 20);
        t.Set.ShowAsTabs(new RectF32(0, 0, 800, 480));
        t.Paint();
        var label = t.Arranged().First(n => n.Node is Layout.Node.Leaf { Content: Layout.Content.Text { Value: "Camera" } });
        label.Bounds.Width.ShouldBeGreaterThanOrEqualTo(6 * 12f * 0.75f - 0.01f);
    }

    [Theory, MemberData(nameof(Hosts))]
    public void TheActiveWindowIsSaidInWordsAsWellAsColour(float w, float h)
    {
        var t = new Harness(w, h);
        var a = t.Add("A very long title that cannot possibly fit in a narrow window", 10, 10, 160, 100);
        var b = t.Add("B", 200, 10);
        t.Paint();

        string? Tip(long id)
        {
            var bar = t.Of(id, ChildWindowPart.Title);
            return t.Arranged().Where(n => n.Node.Tooltip is not null)
                .Select(n => (n.Node.Tooltip, Rect: n.Bounds))
                .Where(n => Math.Abs(n.Rect.Y - bar.Y) < 0.5f && n.Rect.X >= bar.X - 0.5f && n.Rect.X < bar.X + bar.Width
                    && n.Tooltip!.EndsWith("window"))
                .Select(n => n.Tooltip).FirstOrDefault();
        }

        Tip(b.Id).ShouldBe("Active window");
        Tip(a.Id).ShouldBe("Inactive window");

        // In visible words, not only on hover: the active bar draws the label, the idle one does not.
        bool SaysActive(long id)
        {
            var bar = t.Of(id, ChildWindowPart.Title);
            return t.Arranged().Any(n => n.Node is Layout.Node.Leaf { Content: Layout.Content.Text { Value: "active" } }
                && n.Bounds.Y >= bar.Y - 0.5f && n.Bounds.Y < bar.Y + bar.Height && n.Bounds.X >= bar.X && n.Bounds.X < bar.X + bar.Width);
        }

        SaysActive(b.Id).ShouldBeTrue();
        SaysActive(a.Id).ShouldBeFalse();
        t.Arranged().Any(n => n.Node.Tooltip == a.Title).ShouldBeTrue("the whole title is on hover, since the bar ellipsises it");
    }
}
