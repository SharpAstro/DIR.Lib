using System;
using System.Collections.Generic;
using System.Linq;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// The raster-host wiring of <see cref="TextSelection{TSurface}"/>: a <see cref="PixelWidgetBase{TSurface}"/> that opts in with
/// <c>WithSelection</c>, routed through a real <see cref="InputRouter"/>. Pixel and clipboard assertions, no baselines.
/// </summary>
public class TextSelectionHostTests : IDisposable
{
    private static readonly RGBAColor32 Bar = new(255, 0, 0, 255);
    private static readonly RGBAColor32 Cover = new(0, 255, 0, 255);

    /// <summary>Fixed-pitch measure (half the size per char) and glyphs recorded rather than rastered.</summary>
    private sealed class StubRenderer(uint w, uint h) : RgbaImageRenderer(w, h)
    {
        public List<(string Text, RGBAColor32 Color)> Drawn { get; } = [];

        public override (float Width, float Height) MeasureText(ReadOnlySpan<char> text, string fontFamily, float fontSize)
            => (text.Length * fontSize * 0.5f, fontSize);

        public override void DrawText(ReadOnlySpan<char> text, string fontFamily, float fontSize,
            RGBAColor32 fontColor, in RectInt layout, TextAlign horizAlign = TextAlign.Near,
            TextAlign vertAlign = TextAlign.Center)
            => Drawn.Add((text.ToString(), fontColor));
    }

    private sealed class Host(Renderer<RgbaImage> renderer, bool optIn) : PixelWidgetBase<RgbaImage>(renderer)
    {
        protected override RGBAColor32 SelectionBackground => Bar;

        public void Render(Layout.Node page, Layout.Node? over = null)
        {
            BeginFrame();
            var root = optIn ? WithSelection(page) : page;
            if (over is not null)
            {
                root = Layout.Builder.Overlay(root, over);
            }

            RenderLayout(root, new RectF32(0f, 0f, 200f, 120f), fontPath: "stub.ttf", scale: DesignScale.One);
        }
    }

    private readonly StubRenderer _renderer = new(200, 120);
    private readonly List<string> _clipboard = [];
    private int _clicks;
    private int _redraws;

    public void Dispose() => _renderer.Dispose();

    private (Host Host, InputRouter Router) Fixture(bool optIn = true)
    {
        var host = new Host(_renderer, optIn);
        var router = new InputRouter(host.Ui, new BackgroundTaskTracker(), () => _redraws++)
        {
            Widgets = () => [host],
            SetClipboardText = _clipboard.Add,
        };
        return (host, router);
    }

    private Layout.Node Page(float lift = 0f, TextInputState? field = null)
    {
        var rows = new List<Layout.Node>();
        if (lift > 0f)
        {
            rows.Add(Layout.Builder.Spacer().WFixed(1f).HFixed(lift));
        }

        rows.Add(Layout.Builder.Text("hello world", 20f).Selectable().WFixed(200f).HFixed(20f));
        rows.Add(Layout.Builder.Box(80f, 20f).WFixed(80f).HFixed(20f).Clickable(new HitResult.ButtonHit("go"), _ => _clicks++));
        rows.Add(Layout.Builder.Text("below", 20f).Selectable().WFixed(200f).HFixed(20f));
        if (field is not null)
        {
            rows.Add(Layout.Builder.TextInput(field, 14f).WFixed(120f).HFixed(20f));
        }

        return Layout.Builder.VStack(rows.ToArray());
    }

    private static bool Down(InputRouter r, float x, float y, MouseButton b = MouseButton.Left, int clicks = 1)
        => r.Handle(new InputEvent.MouseDown(x, y, b, InputModifier.None, clicks));

    // "hello" is five chars at 10 px each: x 0..50 on the row at y 0..20.
    private static void DragHello(InputRouter r)
    {
        Down(r, 1f, 10f).ShouldBeTrue();
        r.Handle(new InputEvent.MouseMove(50f, 10f, MouseButton.Left));
        r.Handle(new InputEvent.MouseUp(50f, 10f, MouseButton.Left));
    }

    private bool IsColour(int x, int y, RGBAColor32 c)
    {
        var img = _renderer.Surface;
        var i = (y * img.Width + x) * 4;
        return img.Pixels[i] == c.Red && img.Pixels[i + 1] == c.Green && img.Pixels[i + 2] == c.Blue && img.Pixels[i + 3] == 255;
    }

    [Fact]
    public void DragSelectsAndCtrlCCopies()
    {
        var (host, router) = Fixture();
        host.Render(Page());

        DragHello(router);
        router.Handle(new InputEvent.KeyDown(InputKey.C, InputModifier.Ctrl)).ShouldBeTrue();

        _clipboard.ShouldBe(["hello"]);
    }

    [Fact]
    public void HighlightSitsUnderTheSelectedRunAndItsGlyphsAreRedrawn()
    {
        var (host, router) = Fixture();
        host.Render(Page());
        DragHello(router);

        _renderer.Drawn.Clear();
        host.Render(Page());

        IsColour(25, 10, Bar).ShouldBeTrue("inside the selected 'hello'");
        IsColour(80, 10, Bar).ShouldBeFalse("past it, ' world' is not selected");
        _renderer.Drawn.Count(d => d.Text == "hello").ShouldBe(1, "drawn again on the bar");
    }

    [Fact]
    public void APopoverLaidOverThePageCoversTheHighlight()
    {
        var (host, router) = Fixture();
        host.Render(Page());
        DragHello(router);

        var over = Layout.Builder.Box(200f, 20f).WFixed(200f).HFixed(20f).Bg(Cover);
        host.Render(Page(), over);

        IsColour(25, 10, Cover).ShouldBeTrue();
        IsColour(25, 10, Bar).ShouldBeFalse();
    }

    [Fact]
    public void APressOnAButtonStillFiresItAndSelectsNothing()
    {
        var (host, router) = Fixture();
        host.Render(Page());

        Down(router, 10f, 30f).ShouldBeTrue();
        router.Handle(new InputEvent.MouseMove(60f, 10f, MouseButton.Left));
        router.Handle(new InputEvent.MouseUp(60f, 10f, MouseButton.Left));

        _clicks.ShouldBe(1);
        host.Selection.HasSelection.ShouldBeFalse();
        router.Handle(new InputEvent.KeyDown(InputKey.C, InputModifier.Ctrl));
        _clipboard.ShouldBeEmpty();
    }

    // A list row, as pilot's flights table draws one: clickable as a whole, its text a selectable run inside a cell that carries
    // its own whole-text tooltip (a region of plain chrome painted over the row).
    private Layout.Node Row() =>
        Layout.Builder.HStack(
            Layout.Builder.Text("hello world", 20f).Selectable().WithTooltip("hello world, whole").WFixed(200f).HFixed(20f))
            .Clickable(new HitResult.ListItemHit("rows", 0), _ => _clicks++)
            .WFixed(200f).HFixed(20f);

    [Fact]
    public void AClickWithNoDragOnTheTextOfAClickableRowIsTheRowsClick()
    {
        // pilot, 2026-11-09: a click on a flight row's text, or the blank part of a cell, started an empty selection and the
        // row was never selected; arrow keys did select it.
        var (host, router) = Fixture();
        host.Render(Row());

        Down(router, 25f, 10f).ShouldBeTrue();
        router.Handle(new InputEvent.MouseUp(25f, 10f, MouseButton.Left));
        _clicks.ShouldBe(1);
        host.Selection.HasSelection.ShouldBeFalse();

        // Past the text, inside the cell: the same.
        Down(router, 180f, 10f).ShouldBeTrue();
        router.Handle(new InputEvent.MouseUp(180f, 10f, MouseButton.Left));
        _clicks.ShouldBe(2);
    }

    [Fact]
    public void ADragOnTheTextOfAClickableRowSelectsItAndDoesNotClickTheRow()
    {
        var (host, router) = Fixture();
        host.Render(Row());

        DragHello(router);
        router.Handle(new InputEvent.KeyDown(InputKey.C, InputModifier.Ctrl));

        _clipboard.ShouldBe(["hello"]);
        _clicks.ShouldBe(0, "the gesture was a selection");
    }

    [Fact]
    public void APressElsewhereClearsTheSelection()
    {
        var (host, router) = Fixture();
        host.Render(Page());
        DragHello(router);

        Down(router, 10f, 30f);

        host.Selection.HasSelection.ShouldBeFalse();
    }

    [Fact]
    public void CtrlCWithAFocusedFieldIsTheFields()
    {
        var (host, router) = Fixture();
        var field = new TextInputState { Text = "abc" };
        host.Render(Page(field: field));
        DragHello(router);
        host.Ui.Focus.Focus(field);

        router.Handle(new InputEvent.KeyDown(InputKey.C, InputModifier.Ctrl));

        _clipboard.ShouldNotContain("hello", "the focused field keeps its own Ctrl+C");
    }

    [Fact]
    public void AScrollThatMovesTheTextKeepsTheSelection()
    {
        var (host, router) = Fixture();
        host.Render(Page());
        DragHello(router);

        host.Render(Page(lift: 20f));
        router.Handle(new InputEvent.KeyDown(InputKey.C, InputModifier.Ctrl)).ShouldBeTrue();

        _clipboard.ShouldBe(["hello"]);
        IsColour(25, 30, Bar).ShouldBeTrue("the bar followed the text down");
    }

    [Fact]
    public void RightPressOverTheSelectionAsksTheHostsMenuHook()
    {
        var (host, router) = Fixture();
        string? offered = null;
        router.OpenSelectionMenu = (_, text) => offered = text;
        host.Render(Page());
        DragHello(router);
        host.Render(Page());

        Down(router, 25f, 10f, MouseButton.Right).ShouldBeTrue();
        offered.ShouldBe("hello");

        offered = null;
        Down(router, 10f, 30f, MouseButton.Right);
        offered.ShouldBeNull("the button is not selected text");
    }

    [Fact]
    public void WithNoHookARightPressIsRoutedAsBefore()
    {
        var (host, router) = Fixture();
        host.Render(Page());
        DragHello(router);
        host.Render(Page());

        Down(router, 25f, 10f, MouseButton.Right).ShouldBeFalse();
    }

    [Fact]
    public void AWidgetThatDidNotOptInNeitherPaintsNorConsumes()
    {
        var (host, router) = Fixture(optIn: false);
        host.Render(Page());

        Down(router, 1f, 10f).ShouldBeFalse();
        router.Handle(new InputEvent.MouseMove(50f, 10f, MouseButton.Left)).ShouldBeFalse();
        router.Handle(new InputEvent.MouseUp(50f, 10f, MouseButton.Left)).ShouldBeFalse();
        router.Handle(new InputEvent.KeyDown(InputKey.C, InputModifier.Ctrl)).ShouldBeFalse();
        host.Render(Page());

        _clipboard.ShouldBeEmpty();
        IsColour(25, 10, Bar).ShouldBeFalse();
    }
}
