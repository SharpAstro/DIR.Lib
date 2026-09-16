using System.Collections.Generic;
using DIR.Lib;

namespace DIR.Lib.Tests;

/// <summary>
/// Drives a press through an <see cref="InputRouter"/> over one widget -- what a test used to spell
/// <c>widget.HitTestAndDispatch(x, y)</c>.
/// </summary>
/// <remarks>
/// <para>
/// 10.0 retired the widget-level dispatch, so a press has exactly one path: the router walks what the
/// last paint registered, topmost first, and runs whatever that region declared. A test that dispatched
/// through the widget was exercising a second implementation of that walk -- which is the divergence the
/// router exists to remove, so the tests go the way the hosts did.
/// </para>
/// <para>
/// <b>Routing is not the same as dispatching, and the difference is worth knowing before reading a
/// converted assertion.</b> The router consumes the press for ANY region under the pointer whether or not
/// a handler ran, blurs a focused field on a press that is not over one, and answers <c>bool</c> rather
/// than the hit. A test that wants to know WHAT is under a point still asks
/// <see cref="IPixelWidget.HitTest"/>, which stayed public for exactly that.
/// </para>
/// <para>
/// A router per call, deliberately: a router holds a drag capture between a press and a release, so
/// sharing one across unrelated gestures would let one test's unfinished drag answer the next one's move.
/// Use <see cref="Over"/> where a gesture spans several events.
/// </para>
/// </remarks>
internal static class Routing
{
    /// <summary>A router over <paramref name="widgets"/>, in paint order (back to front).</summary>
    internal static InputRouter Over(params IPixelWidget[] widgets)
        => new(widgets[0].Ui, new BackgroundTaskTracker(), static () => { })
        {
            Widgets = () => widgets,
        };

    /// <summary>Routes one press at <paramref name="x"/>, <paramref name="y"/>. Returns whether it was
    /// consumed, which a region under the pointer does whether or not it acted.</summary>
    internal static bool Press(IPixelWidget widget, float x, float y,
        InputModifier modifiers = InputModifier.None, MouseButton button = MouseButton.Left, int clicks = 1)
        => Over(widget).Handle(new InputEvent.MouseDown(x, y, button, modifiers, clicks));

    /// <summary>Routes one key. The painted popovers answer first, topmost down, which is the order a test
    /// asking a claimant by hand used to assume rather than exercise.</summary>
    internal static bool Key(IPixelWidget widget, InputKey key, InputModifier modifiers = InputModifier.None)
        => Over(widget).Handle(new InputEvent.KeyDown(key, modifiers));
}
