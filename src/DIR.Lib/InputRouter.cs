using System;
using System.Collections.Generic;

namespace DIR.Lib;

/// <summary>
/// A tooltip that is due to be shown: the text, and the rect it belongs to.
/// </summary>
/// <remarks>
/// The anchor is the region's own arranged rect rather than a placed tooltip box, because where a
/// tooltip goes is a question about the WINDOW (which edge it would run off, whether an overlay layer
/// exists to draw it on) and the router knows only the widget. A host places it against this rect; a
/// painter with an overlay layer can clamp it there.
/// </remarks>
/// <param name="Text">What to show, from <see cref="Layout.Node.Tooltip"/> or, on a disabled node,
/// <see cref="Layout.Node.DisabledReason"/>.</param>
/// <param name="Anchor">The rect the pointer is resting on.</param>
public readonly record struct TooltipRequest(string Text, RectF32 Anchor);

/// <summary>
/// The dispatcher every host was writing: it takes an <see cref="InputEvent"/> and hands it to the
/// regions, fields, shortcuts, captures and controllers the last paint declared, in ONE fixed order.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this replaces is not missing code, it is three copies of the same code that disagree.</b> The
/// library could already place a caret under a pointer, select the word under a second click, cycle Tab
/// in paint order, walk a list with the arrows and close a popover on Escape. None of it ran unless a
/// host wrote the routing that reached it, so every surface wrote its own: one carried
/// <c>if (key == F3) return false;</c> with a comment calling F3 global, beside a hand-written
/// Ctrl+letter map that was not guarded at all, beside a bare <c>F</c> that meant a filter cycle on one
/// panel and a typed letter on another. The routing is the same on every surface. It is the platform
/// binding that is not, and that stays with the host.
/// </para>
/// <para>
/// <b>The KeyDown order is the whole point.</b> A shortcut reaches its node ahead of a focused field only
/// when <see cref="KeyChord.BeatsFocusedField"/> says it may, which is Ctrl, Alt or a function key. Both
/// directions are behaviour someone will notice: get it wrong one way and Ctrl+F cannot reach the search
/// box while another field has the keyboard, get it wrong the other and a bare letter typed into a field
/// fires the application binding for that letter instead of appearing in the box.
/// </para>
/// <para>
/// <b>Everything is resolved against what the last paint REGISTERED</b>, which is what makes a binding on
/// a panel that is not on screen inert with nobody saying so. That is the rule
/// <see cref="TextInputFocus.BlurIfUnpainted"/> and the keyboard claimant already follow, applied to
/// shortcuts, tooltips, scroll targets and presses as well.
/// </para>
/// <para>
/// Hosts keep four things: the <see cref="TextInputFocus.FocusChanged"/> binding to the platform's
/// text-input lifecycle, the clipboard delegates, a call to <see cref="AfterPaint"/> once the frame is
/// drawn, and <see cref="Unhandled"/> for the routing that is genuinely theirs (an active tab, a viewer
/// with its own gesture model).
/// </para>
/// <para>
/// Render-thread only, like the region and layout reads it is built on.
/// </para>
/// </remarks>
/// <param name="ui">The window's shared settings: who has the keyboard, who claimed it, and which rect
/// owns the pointer.</param>
/// <param name="tracker">Tracks a field's async <see cref="TextInputState.OnCommit"/>, so a failing
/// commit is reported rather than swallowed by an unobserved task.</param>
/// <param name="requestRedraw">Marks the surface dirty. Called when something the frame shows changed,
/// and not otherwise; a pointer move over inert chrome asks for nothing.</param>
public sealed class InputRouter(WindowUiSettings ui, BackgroundTaskTracker tracker, Action requestRedraw)
{
    // Scratch buffers, reused across events rather than allocated per pointer move. Render-thread only,
    // and never held across a call: each is filled from one widget, walked, and abandoned.
    private readonly List<ClickableRegion> _regions = [];
    private readonly List<Layout.ArrangedNode<float>> _nodes = [];
    private readonly List<TextInputState> _fields = [];

    // Fields whose FocusOnOpen request has been honoured while they have stayed painted. Reference
    // identity, TextInputState being a plain class, which is the identity a field actually has.
    private readonly HashSet<TextInputState> _honouredFocusOnOpen = [];

    private DragCapture? _capture;
    private MouseButton _captureButton;
    private InputModifier _captureModifiers;

    private (float X, float Y)? _pointer;
    private Layout.Node? _hoverNode;
    private TooltipRequest? _tooltip;
    private DateTimeOffset _tooltipSince;
    private bool _tooltipAnnounced;

    /// <summary>
    /// The widgets of the frame, in PAINT order (back to front). The router walks it top-most first for
    /// anything the pointer resolves, and front to back for anything that follows reading order (Tab
    /// cycling, and which field a focus-on-open request belongs to).
    /// </summary>
    /// <remarks>
    /// A callback rather than a list because composition changes per frame, and a host that handed over a
    /// list would have to remember to hand over a new one. Empty by default, which routes everything to
    /// <see cref="Unhandled"/>: a router nobody has given widgets to is inert rather than wrong.
    /// <para>
    /// A <see cref="CompositeWidget{TSurface}"/> is listed ALONE, not beside the widgets it composes. It
    /// aggregates its children's regions, nodes, fields and cursor itself, so listing both would offer
    /// every child's field to Tab twice and break the cycle.
    /// </para>
    /// </remarks>
    public Func<IReadOnlyList<IPixelWidget>> Widgets { get; set; } = static () => [];

    /// <summary>
    /// The host's own routing, offered every event the declarations did not answer. Returns true when the
    /// host consumed it.
    /// </summary>
    /// <remarks>
    /// This is where an active tab's <c>HandleInput</c> lives, and a widget's own
    /// <see cref="PixelWidgetBase{TSurface}.HandleListKey"/> with it. It is LAST for every event, so a
    /// declaration always wins over a hand-written arm for the same key or the same rect. That ordering
    /// is what lets a host delete an arm the moment the tree declares the same thing, rather than having
    /// to prove the arm is unreachable first.
    /// </remarks>
    public Func<InputEvent, bool>? Unhandled { get; set; }

    /// <summary>Platform paste, handed to the focused field's Ctrl+V. Null where there is no clipboard.</summary>
    public Func<string?>? GetClipboardText { get; set; }

    /// <summary>Platform copy, handed to the focused field's Ctrl+C and Ctrl+X. Null where there is no
    /// clipboard.</summary>
    public Action<string>? SetClipboardText { get; set; }

    /// <summary>
    /// The search whose field is currently active, enabling Up/Down over its results
    /// (<see cref="TextInputInteraction.KeyContext.ActiveSearch"/>). Null when the focused field is not a
    /// search box.
    /// </summary>
    /// <remarks>
    /// A callback the host answers, because a <see cref="SearchInteraction"/> is not discoverable from the
    /// field it owns: the field holds the four callbacks the search wired into it and no reference back.
    /// Asked once per key press, not per frame.
    /// </remarks>
    public Func<SearchInteraction?>? ActiveSearch { get; set; }

    /// <summary>
    /// The clock the hover delay is measured on. <see cref="TimeProvider.System"/> unless a test says
    /// otherwise, a delay being the one thing here that cannot be driven by feeding it events.
    /// </summary>
    public TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>
    /// How long the pointer must rest on a region before its <see cref="Layout.Node.Tooltip"/> is due.
    /// </summary>
    /// <remarks>
    /// A delay at all is the half three hand-written tooltip painters were missing: a tooltip with none
    /// appears under the pointer on the way past, which is why a reader learns to move the pointer around
    /// them rather than over them.
    /// </remarks>
    public TimeSpan TooltipDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Raised for a press on a <see cref="HitResult.LinkHit"/>, with the target. A desktop host opens the
    /// OS browser; a DOM host renders a real anchor and never sees this.
    /// </summary>
    public event Action<string>? OpenUrl;

    /// <summary>
    /// Raised when a declared <see cref="Layout.Node.Shortcut"/> fired, with the chord and the node it
    /// reached. For telemetry, and for a test that needs to see WHICH node answered rather than only that
    /// something did.
    /// </summary>
    public event Action<KeyChord, Layout.Node>? ShortcutFired;

    /// <summary>
    /// The tooltip that is due, or null. Null until the pointer has rested on a region carrying one for
    /// <see cref="TooltipDelay"/>, and null again the moment it moves to a region carrying a different one
    /// (or none).
    /// </summary>
    public TooltipRequest? Tooltip
        => _tooltip is { } due && Clock.GetUtcNow() - _tooltipSince >= TooltipDelay ? due : null;

    /// <summary>
    /// Routes one event. Returns true when it was consumed, so a host loop can stop.
    /// </summary>
    public bool Handle(InputEvent evt) => evt switch
    {
        InputEvent.MouseDown down => HandleMouseDown(down),
        InputEvent.MouseMove move => HandleMouseMove(move),
        InputEvent.MouseUp up => HandleMouseUp(up),
        InputEvent.Scroll scroll => HandleScroll(scroll),
        InputEvent.KeyDown key => HandleKeyDown(key),
        InputEvent.TextInput text => HandleTextInput(text),
        _ => Unhandled?.Invoke(evt) ?? false,
    };

    /// <summary>
    /// Call once the whole frame is painted, before the next event is routed.
    /// </summary>
    /// <remarks>
    /// Three rules live here because all three need the same fact, and it is a fact only a finished frame
    /// has: what was actually on screen. A focused field that is no longer painted loses the keyboard
    /// (<see cref="TextInputFocus.BlurIfUnpainted"/>); a field that asked for the keyboard as it appeared
    /// gets it, once; and a tooltip whose region has gone expires. Called before the paint, or with one
    /// surface's widgets when the frame draws several, each of them does the opposite of what it is for.
    /// </remarks>
    public void AfterPaint()
    {
        var widgets = Widgets();

        _fields.Clear();
        for (var i = 0; i < widgets.Count; i++)
        {
            _fields.AddRange(widgets[i].GetRegisteredTextInputs());
        }

        if (ui.Focus.BlurIfUnpainted(_fields))
        {
            requestRedraw();
        }

        ApplyFocusOnOpen(widgets);
        ExpireTooltip();
    }

    /// <summary>
    /// What the pointer should look like at this point: the topmost region that states a cursor, across
    /// the widgets top-most first. Null where nothing under the pointer had a view, which is the host's
    /// own default rather than an arrow.
    /// </summary>
    public CursorKind? CursorAt(float x, float y)
    {
        var widgets = Widgets();
        for (var i = widgets.Count - 1; i >= 0; i--)
        {
            if (widgets[i].HitTestCursor(x, y) is { } cursor)
            {
                return cursor;
            }
        }

        return null;
    }

    // ---- pointer ----------------------------------------------------------------------------

    private bool HandleMouseDown(InputEvent.MouseDown down)
    {
        _pointer = (down.X, down.Y);
        var press = new PointerPress(down.X, down.Y, down.Button, down.Modifiers, down.ClickCount);
        var widgets = Widgets();

        for (var w = widgets.Count - 1; w >= 0; w--)
        {
            var widget = widgets[w];
            _regions.Clear();
            widget.CollectPaintedRegions(_regions);

            for (var i = _regions.Count - 1; i >= 0; i--)
            {
                var region = _regions[i];
                if (!Contains(region, down.X, down.Y))
                {
                    continue;
                }

                var acted = DispatchPress(widget, region, press);

                // A press somewhere other than a field takes the keyboard off whichever field had it,
                // and does so AFTER the dispatch so a handler reading the field still reads what the
                // reader saw. A disabled region reaches here with nothing dispatched, which is right:
                // the press was swallowed, and a swallowed press is still a press somewhere else.
                if (region.Result is not HitResult.TextInputHit)
                {
                    acted |= BlurFocusedField();
                }

                if (acted)
                {
                    requestRedraw();
                }

                return true;
            }
        }

        // Nothing under the pointer. The press is still a press somewhere other than a field.
        if (BlurFocusedField())
        {
            requestRedraw();
        }

        return Unhandled?.Invoke(down) ?? false;
    }

    /// <summary>
    /// One press onto one region, in the order a region can answer it. Returns whether anything acted,
    /// which is what decides a redraw; the press is consumed either way, the topmost region under the
    /// pointer being the one that owns it.
    /// </summary>
    private bool DispatchPress(IPixelWidget widget, in ClickableRegion region, in PointerPress press)
    {
        if (region.Result is HitResult.TextInputHit field)
        {
            BeginFieldPress(widget, field, press);
            return true;
        }

        var acted = false;

        // A link is a DECLARATION on the node (see HitResult.LinkHit), so it composes with a handler
        // rather than replacing one: a node carrying both a target and an OnClick means both, and a
        // router that stopped here would drop whichever half the consumer cared about with no error.
        if (region.Result is HitResult.LinkHit link && OpenUrl is { } open)
        {
            open(link.Url);
            acted = true;
        }

        if (region.OnPress is { } onPress)
        {
            if (onPress(press) is { } capture)
            {
                _capture = capture;
                _captureButton = press.Button;
                _captureModifiers = press.Modifiers;
                return true;
            }

            // Declining is the documented way to say "not this press", so it falls through to the click
            // exactly as a node carrying only a click would.
        }

        if (region.OnClick is { } click)
        {
            click(press.Modifiers);
            acted = true;
        }

        return acted;
    }

    /// <summary>
    /// A press over a field: focus it, place the caret through the widget that PAINTED the text, and take
    /// the gesture, so dragging out of the press extends the selection.
    /// </summary>
    /// <remarks>
    /// The move and the release are placed with a click count of 1 and <c>extend</c> true whatever the
    /// press was. A drag that began as a double click is still a drag; re-running the word selection on
    /// every move would re-select the word under the pointer instead of extending from the anchor.
    /// </remarks>
    private void BeginFieldPress(IPixelWidget widget, HitResult.TextInputHit hit, in PointerPress press)
    {
        var ctx = new TextInputInteraction.PointerContext(ui.Focus, requestRedraw);
        var extend = (press.Modifiers & InputModifier.Shift) != 0;

        TextInputInteraction.HandlePointer(
            hit.Input, widget.CaretIndexAt(hit, press.X), press.Clicks, extend, ctx);

        void Extend(PointerMove at)
            => TextInputInteraction.HandlePointer(hit.Input, widget.CaretIndexAt(hit, at.X), 1, true, ctx);

        _capture = new DragCapture(Extend, Extend);
        _captureButton = press.Button;
        _captureModifiers = press.Modifiers;
    }

    private bool HandleMouseMove(InputEvent.MouseMove move)
    {
        _pointer = (move.X, move.Y);

        // A gesture in flight owns every move until the button comes up, and it is told the button and
        // the modifiers the PRESS carried: a reader who lets go of Shift mid-drag has not changed what
        // the drag is, and a move event carries no modifiers to ask anyway.
        if (_capture is { } capture)
        {
            capture.Move(new PointerMove(move.X, move.Y, _captureButton, _captureModifiers));
            requestRedraw();
            return true;
        }

        var widgets = Widgets();
        for (var i = 0; i < widgets.Count; i++)
        {
            widgets[i].Pointer = _pointer;
        }

        // Motion is not a reason to draw a frame by itself. It is a reason when it changed something the
        // frame shows, which is a lit background or a tooltip, and those are the two the router can see.
        if (NoteHover(move.X, move.Y))
        {
            requestRedraw();
        }

        return Unhandled?.Invoke(move) ?? false;
    }

    private bool HandleMouseUp(InputEvent.MouseUp up)
    {
        _pointer = (up.X, up.Y);

        if (_capture is { } capture)
        {
            // Cleared BEFORE the release runs, so a handler that arms a fresh gesture from its own
            // release keeps it rather than having it wiped on the way out.
            _capture = null;
            capture.Release(new PointerMove(up.X, up.Y, _captureButton, _captureModifiers));
            requestRedraw();
            return true;
        }

        return Unhandled?.Invoke(up) ?? false;
    }

    private bool HandleScroll(InputEvent.Scroll scroll)
    {
        var widgets = Widgets();
        for (var i = widgets.Count - 1; i >= 0; i--)
        {
            // The innermost scrollable under the pointer, which is the one that declared itself last.
            // A controller that declines (nothing to scroll, or the wheel landed outside its viewport)
            // leaves the wheel to whatever is behind it rather than swallowing it, which is what makes a
            // list inside a pane scroll the pane once the list is at its end.
            if (widgets[i].ScrollTargetAt(scroll.X, scroll.Y) is { } controller && controller.HandleInput(scroll))
            {
                requestRedraw();
                return true;
            }
        }

        return Unhandled?.Invoke(scroll) ?? false;
    }

    // ---- keyboard ---------------------------------------------------------------------------

    private bool HandleKeyDown(InputEvent.KeyDown key)
    {
        // An overlay that is on screen owns the keyboard: Escape closes a popover before anything else
        // reads the key. Topmost first, which is the LAST one painted -- so a modal over a popover takes
        // the key, and dismissing it gives the keyboard back to the one underneath on the next frame,
        // that one still being painted while the closed one is not.
        var popovers = ui.PaintedPopovers;
        for (var i = popovers.Count - 1; i >= 0; i--)
        {
            if (popovers[i].HandleKeyDown(key.Key))
            {
                requestRedraw();
                return true;
            }
        }

        var chord = new KeyChord(key.Key, key.Modifiers);

        // THE precedence rule, in one place. A chord carrying Ctrl or Alt, or a function key, reaches its
        // node whatever has the keyboard; anything else waits until no field does, so a bare letter typed
        // into a field is a letter. Stated on the chord (KeyChord.BeatsFocusedField) rather than here, so
        // a test and this line read the same fact.
        if ((chord.BeatsFocusedField || ui.Focus.Current is null) && TryFireShortcut(chord))
        {
            requestRedraw();
            return true;
        }

        if (TextInputInteraction.HandleKey(key.Key, key.Modifiers, KeyContext()))
        {
            return true;
        }

        return Unhandled?.Invoke(key) ?? false;
    }

    private bool HandleTextInput(InputEvent.TextInput text)
    {
        if (ui.Focus.Current is { } field)
        {
            TextInputInteraction.HandleText(field, text.Text);
            requestRedraw();
            return true;
        }

        return Unhandled?.Invoke(text) ?? false;
    }

    private TextInputInteraction.KeyContext KeyContext()
        => new(tracker, ui.Focus, requestRedraw, ActiveSearch?.Invoke(), TabFields,
            GetClipboardText, SetClipboardText);

    /// <summary>
    /// Every field painted this frame, in paint order across the widgets, which is the visual order and so
    /// needs no maintaining. Built on Tab and not before: a list rebuilt per keystroke would serve the one
    /// key in a hundred that reads it.
    /// </summary>
    private IReadOnlyList<TextInputState> TabFields()
    {
        var widgets = Widgets();
        var fields = new List<TextInputState>();
        for (var i = 0; i < widgets.Count; i++)
        {
            fields.AddRange(widgets[i].GetRegisteredTextInputs());
        }

        return fields;
    }

    /// <summary>
    /// The first painted node whose declared shortcut is this chord, and what that node does about it.
    /// Top-most first, so the binding a reader can see wins over one behind it, and exactly one fires.
    /// </summary>
    private bool TryFireShortcut(KeyChord chord)
    {
        var widgets = Widgets();
        for (var w = widgets.Count - 1; w >= 0; w--)
        {
            _nodes.Clear();
            widgets[w].CollectPaintedNodes(_nodes);

            for (var i = _nodes.Count - 1; i >= 0; i--)
            {
                var node = _nodes[i].Node;
                if (node.Shortcut != chord || node.IsDisabled)
                {
                    continue;
                }

                if (TryActOnShortcut(node, chord))
                {
                    ShortcutFired?.Invoke(chord, node);
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// What a matched shortcut DOES, which depends on the node: a field takes the keyboard, a node that
    /// can be acted on is acted on, a popover toggles.
    /// </summary>
    /// <remarks>
    /// False for a node that declared a chord and nothing to do with it. Consuming the key there would
    /// swallow it silently, and the declaration is a mistake worth leaving visible rather than one worth
    /// covering for; the walk carries on to the next node instead.
    /// </remarks>
    private bool TryActOnShortcut(Layout.Node node, KeyChord chord)
    {
        if (node is Layout.Node.Leaf { Content: Layout.Content.TextInput field })
        {
            // Seeded with its own value, which is what selects it: the shortcut that reaches a search box
            // is the one that means "search for something else", so the first keystroke should replace
            // what is in there rather than append to it. Idempotent, so holding the chord does nothing.
            ui.Focus.Focus(field.State, field.State.Text);
            return true;
        }

        if ((node.OnActivate ?? node.OnClick) is { } act)
        {
            act(chord.Modifiers);
            return true;
        }

        if (node.Popover is { } popover)
        {
            // Only reachable while the popover is OPEN, its subtree not being painted otherwise, so this
            // is the binding that dismisses one. The binding that OPENS a popover belongs on the button
            // that opens it, which is painted either way.
            popover.Toggle();
            return true;
        }

        return false;
    }

    // ---- after the paint --------------------------------------------------------------------

    /// <summary>
    /// The first painted field that asked for the keyboard and has not been given it since it last
    /// appeared.
    /// </summary>
    /// <remarks>
    /// Never off a field being typed in: with a focused field the request simply waits, which it can do
    /// because <see cref="TextInputFocus.BlurIfUnpainted"/> has already run and a focused field is
    /// therefore one that is still on screen.
    /// </remarks>
    private void ApplyFocusOnOpen(IReadOnlyList<IPixelWidget> widgets)
    {
        // Forget a field that has left the screen, so the same field asking again when it comes back is a
        // fresh request. That is what "once, since it was last not painted" means.
        _honouredFocusOnOpen.RemoveWhere(field => !_fields.Contains(field));

        if (ui.Focus.Current is not null)
        {
            return;
        }

        for (var w = 0; w < widgets.Count; w++)
        {
            _regions.Clear();
            widgets[w].CollectPaintedRegions(_regions);

            for (var i = 0; i < _regions.Count; i++)
            {
                var region = _regions[i];
                if (!region.FocusOnOpen
                    || region.Result is not HitResult.TextInputHit hit
                    || !_honouredFocusOnOpen.Add(hit.Input))
                {
                    continue;
                }

                ui.Focus.Focus(hit.Input);
                requestRedraw();
                return;
            }
        }
    }

    private void ExpireTooltip()
    {
        if (_tooltip is null)
        {
            return;
        }

        if (_pointer is not { } p)
        {
            SetTooltip(null);
            return;
        }

        // Re-resolved rather than remembered, which covers both ways a tooltip stops being true: the
        // region moved out from under a pointer that has not moved, or the widget stopped painting it.
        var target = ResolveTooltip(p.X, p.Y);
        if (target != _tooltip)
        {
            SetTooltip(target);
            return;
        }

        // The delay elapsing is a change to what the frame shows, and nothing else is going to notice it.
        if (!_tooltipAnnounced && Clock.GetUtcNow() - _tooltipSince >= TooltipDelay)
        {
            _tooltipAnnounced = true;
            requestRedraw();
        }
    }

    // ---- hover ------------------------------------------------------------------------------

    /// <summary>
    /// Records what the pointer is now over, and answers whether the frame would look different for it:
    /// a node whose <see cref="Layout.Node.HoverBackground"/> lights, or a region whose tooltip is now the
    /// one being waited on.
    /// </summary>
    private bool NoteHover(float x, float y)
    {
        var node = ResolveHoverNode(x, y);
        var changed = !ReferenceEquals(node, _hoverNode);
        _hoverNode = node;

        var target = ResolveTooltip(x, y);
        if (target != _tooltip)
        {
            SetTooltip(target);
            changed = true;
        }

        return changed;
    }

    private void SetTooltip(TooltipRequest? target)
    {
        _tooltip = target;
        _tooltipSince = Clock.GetUtcNow();
        _tooltipAnnounced = false;
    }

    /// <summary>
    /// The node the painter would light: the innermost painted node carrying a hover background whose
    /// arranged rect contains the point.
    /// </summary>
    /// <remarks>
    /// Asked with the popover's pointer claim applied, exactly as
    /// <c>PixelWidgetBase.PointerWithin</c> asks it, or the router would request a redraw for a row an
    /// open popover is covering and the painter would then decline to light it.
    /// </remarks>
    private Layout.Node? ResolveHoverNode(float x, float y)
    {
        if (ui.PointerOwner is { } owner
            && !(x >= owner.X && x < owner.X + owner.Width && y >= owner.Y && y < owner.Y + owner.Height))
        {
            return null;
        }

        var widgets = Widgets();
        for (var w = widgets.Count - 1; w >= 0; w--)
        {
            _nodes.Clear();
            widgets[w].CollectPaintedNodes(_nodes);

            for (var i = _nodes.Count - 1; i >= 0; i--)
            {
                var (node, bounds) = _nodes[i];
                if (node.HoverBackground is not null
                    && x >= bounds.X && x < bounds.X + bounds.Width
                    && y >= bounds.Y && y < bounds.Y + bounds.Height)
                {
                    return node;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The topmost region under the point that states a tooltip. Regions with nothing to say are
    /// transparent to this, the same rule the cursor lookup follows, so a row inside a card with a tooltip
    /// shows the card's.
    /// </summary>
    private TooltipRequest? ResolveTooltip(float x, float y)
    {
        var widgets = Widgets();
        for (var w = widgets.Count - 1; w >= 0; w--)
        {
            _regions.Clear();
            widgets[w].CollectPaintedRegions(_regions);

            for (var i = _regions.Count - 1; i >= 0; i--)
            {
                var region = _regions[i];
                if (region.Tooltip is { } text && Contains(region, x, y))
                {
                    return new TooltipRequest(text, new RectF32(region.X, region.Y, region.Width, region.Height));
                }
            }
        }

        return null;
    }

    // ---- shared -----------------------------------------------------------------------------

    private bool BlurFocusedField()
    {
        if (ui.Focus.Current is null)
        {
            return false;
        }

        ui.Focus.Blur();
        return true;
    }

    /// <summary>Top and left inclusive, bottom and right exclusive, so two regions sharing an edge never
    /// both claim it. The rule <see cref="ClickableRegionTracker.HitTest"/> uses.</summary>
    private static bool Contains(in ClickableRegion r, float x, float y)
        => x >= r.X && x < r.X + r.Width && y >= r.Y && y < r.Y + r.Height;
}
