using System;
using System.Collections.Generic;

namespace DIR.Lib;

/// <summary>
/// The presentation values that are constant for one WINDOW and shared by every widget drawing into it:
/// the DPI scale, the text face, the emoji face and the per-codepoint fallback chain.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not a second font context -- it is what the font context is BUILT FROM.</b>
/// <see cref="PixelMeasureContext{TSurface}"/> is the font context: constructed per arrange/paint pass,
/// immutable, and contractually the same instance for measure and paint so the two cannot disagree. A
/// widget may hold several at once (<see cref="PixelMeasureContext{TSurface}.CellAuthored"/> gives a
/// different one for a cell-authored tree), so it is per-OPERATION and cannot own anything. These settings
/// are per-WINDOW and are exactly what each of those contexts is derived from.
/// </para>
/// <para>
/// <b>Held by reference, not copied.</b> A composite hands the same instance to the widgets it composes
/// (<see cref="PixelWidgetBase{TSurface}.ShareUiContext"/>), so a host setting the DPI scale on the chrome
/// has set it for every child in the same assignment. Nothing is pushed down and nothing can be missed.
/// </para>
/// <para>
/// This replaced four per-widget copies kept in agreement by four overridden setters, each naming every
/// child. That shape cost twice over: a new child had to be added to all four lists, and a new per-window
/// value had to restate the whole list again -- and both omissions are silent, because the widget simply
/// draws at the wrong scale or with no fallback face. The question that killed it is the obvious one: a
/// widget belongs to exactly one window, so why does it hold its own copy of what the window knows?
/// </para>
/// <para>
/// <b>Not hung off <see cref="Renderer{TSurface}"/></b>, though every widget already holds one and it is
/// per-window, because a renderer is shared by MORE than the widget tree: an embedded viewer that draws
/// into the same renderer and resolves its own face would overwrite the window's font for everyone. Making
/// the sharing explicit keeps that opt-out visible -- such a widget is simply never handed the context.
/// </para>
/// <para>
/// Mutable on purpose, and deliberately NOT the same object as <see cref="PixelMeasureContext{TSurface}"/>:
/// that one is built per arrange and must stay immutable, since its whole contract is that the SAME
/// instance answers measure and paint. A DPI change landing between the two would break exactly what it
/// exists to guarantee. The measure context is derived FROM this, per pass.
/// </para>
/// </remarks>
public sealed class WindowUiSettings
{
    /// <summary>Device pixels per design unit. The host sets it from the window's scale, at startup and on resize.</summary>
    public float DpiScale { get; set; } = 1f;

    /// <summary>The primary text face. Empty means "no font configured", and the text helpers then draw nothing rather than throwing.</summary>
    public string FontPath { get; set; } = string.Empty;

    /// <summary>The bold Ui face, for <see cref="Layout.TextWeight.Bold"/> runs. Null draws them in <see cref="FontPath"/>, as before.</summary>
    public string? BoldFontPath { get; set; }

    /// <summary>The monospace face, for <see cref="Layout.TextFace.Mono"/> runs. Null draws them in <see cref="FontPath"/>, as before.</summary>
    public string? MonoFontPath { get; set; }

    /// <summary>The bold monospace face; null draws bold mono runs in <see cref="MonoFontPath"/>.</summary>
    public string? MonoBoldFontPath { get; set; }

    /// <summary>The emoji face, for callers with a dedicated emoji path rather than coverage-driven fallback.</summary>
    public string? EmojiFontPath { get; set; }

    /// <summary>
    /// The per-codepoint fallback chain. Null draws with <see cref="FontPath"/> alone -- which is exactly
    /// the state in which any script the primary face lacks renders as nothing at all.
    /// </summary>
    public FontFallbackResolver? FontFallback { get; set; }

    private readonly List<PopoverState> _paintedPopovers = [];

    /// <summary>
    /// The popovers painted this cycle, in paint order, which is z-order. <see cref="InputRouter"/> offers
    /// a key to the LAST one first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The alternative -- every widget that owns an overlay adding a routing case to its own input switch,
    /// in a different file from the overlay -- is a step someone eventually forgets, and forgetting is
    /// invisible: the overlay still opens, still draws, still takes mouse clicks, and only the arrow keys
    /// do nothing. That is precisely what happened to the Live Session mode pill, which was the one
    /// dropdown of four with no routing case.
    /// </para>
    /// <para>
    /// <b>A LIST since 10.0, where 9.x had one <c>IKeyboardClaimant</c> slot.</b> One slot means the last
    /// painter wins and nothing restores, so a modal over a popover -- or a popover over the menu that
    /// opened it -- took the slot, and the thing underneath stopped answering Escape for good: dismissing
    /// the inner overlay left the outer one on screen with nothing claiming it. A stack makes the nesting
    /// behave the way it looks, and hands the keyboard back on the next frame without restoring anything,
    /// because the one underneath is still painting and the closed one is not.
    /// </para>
    /// <para>
    /// <b>Cleared per paint CYCLE, registered by being PAINTED</b> (<see cref="NoteFrameBegin"/>,
    /// <see cref="NotePopoverPainted"/>) -- the same mechanism as <see cref="PointerOwner"/>, and that is
    /// what retired the interface. The slot was never cleared, so every implementer carried the obligation
    /// to answer "I am no longer displayed" for itself, which is a contract nothing could check and which
    /// a consumer's own overlay type got wrong. A popover that closed simply stops painting and is not in
    /// the list.
    /// </para>
    /// </remarks>
    public IReadOnlyList<PopoverState> PaintedPopovers => _paintedPopovers;

    /// <summary>
    /// Records that <paramref name="popover"/> painted, putting it on top of this cycle's stack. Called by
    /// the layout painter for every OPEN popover node it draws, and by a surface that paints its own
    /// popovers (a terminal, say) and has no <c>PixelWidgetBase</c> to do it: call it once per painted open
    /// popover, after <see cref="NoteFrameBegin"/> for the cycle.
    /// </summary>
    /// <remarks>
    /// Re-registering one already in the list MOVES it to the top rather than duplicating it, so a popover
    /// drawn in more than one of a widget's paint passes is ordered by its LAST paint, which is the one
    /// actually on top.
    /// </remarks>
    public void NotePopoverPainted(PopoverState popover)
    {
        _paintedPopovers.Remove(popover);
        _paintedPopovers.Add(popover);
    }

    /// <summary>
    /// The rect that owns the pointer this frame, set by an open popover as it paints, or null when nothing
    /// does. Hover resolution consults it, so what is BEHIND a popover stops lighting up under the cursor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hover, not hit-testing. Clicks already go through the region tracker in paint order, so the backdrop
    /// takes them without help; what a raw pointer position cannot express on its own is that a row a
    /// popover is covering should look inert rather than warm. Confining hover is that rule, stated once.
    /// </para>
    /// <para>
    /// <b>Cleared once per PAINT CYCLE, not once per widget</b> (<see cref="NoteFrameBegin"/>). Per widget
    /// would be wrong in a way that only shows up with more than one: every widget clears in its own
    /// BeginFrame, so a second widget beginning its frame would wipe the owner a first widget had just
    /// painted, and the popover would confine hover only when it happened to belong to the last widget
    /// drawn. <see cref="PaintedPopovers"/> is cleared on the same boundary, for the same reason.
    /// </para>
    /// <para>
    /// <b>A cycle is not <see cref="FrameId"/>, and keying it on that was a bug.</b> The first shipped
    /// version cleared when the id CHANGED, which is correct only for a host that advances it. That property
    /// is documented as optional and defaults to 0, so on a host that leaves it there the first BeginFrame
    /// cleared and every later one returned early: an open popover then owned the pointer for the life of
    /// the process, and every hover in the window died the first time one closed. A default that quietly
    /// disables a rule is worse than no default, so the cycle is now derived from the widgets themselves.
    /// </para>
    /// </remarks>
    public RectF32? PointerOwner { get; set; }

    /// <summary>
    /// Where the primary button went down and is still held, or null. Set by <see cref="InputRouter"/> on a left
    /// press that a region took (and no surface, popover backdrop or modal scrim took), cleared on the release. The
    /// painter lights <see cref="Layout.Node.PressedBackground"/> on a node that contains both this point and the
    /// pointer. Kept here, beside the other per-window pointer state, so widgets that share these settings agree.
    /// </summary>
    public (float X, float Y)? PressOrigin { get; set; }

    /// <summary>
    /// The pointer claim in force for whatever is being painted NOW: <see cref="PointerOwner"/> once an open
    /// popover has painted this cycle, and before that, the claim the last cycle ended with, for as long as the
    /// popover that made it is still open. Hover painting confines itself to this.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A claim made by being painted reaches only what is painted after it</b>, and a popover paints LAST,
    /// over the chrome it covers. Read from <see cref="PointerOwner"/> alone, everything painted before the
    /// popover in a frame saw no claim: a declared button under an open menu's backdrop lit under the pointer,
    /// which is why consumers kept a hand-written "does an overlay own the pointer" prediction beside their
    /// chrome. Carrying the claim across the frame boundary, while its popover stays open, is that prediction
    /// stated once.
    /// </para>
    /// <para>
    /// The frame a popover OPENS in has nothing to carry, so a node painted before it can light for that one
    /// frame; <see cref="PointerClaimSettled"/> is false then, and the router asks for the next frame, which
    /// carries the claim.
    /// </para>
    /// </remarks>
    public RectF32? PointerClaim => PointerOwner ?? _carriedPointerOwner;

    /// <summary>
    /// Whether this cycle's paint ended with the claim it started with (the carried one), so nothing painted
    /// before the popover was confined to a different rect than the popover then claimed. False the frame a
    /// popover opens, moves or closes; <see cref="InputRouter.AfterPaint"/> asks for another frame then.
    /// </summary>
    public bool PointerClaimSettled => PointerOwner == _carriedPointerOwner && SurfacesSettled;

    /// <summary>
    /// The id of the declared list that holds the keyboard (<see cref="DeclaredList.Active"/>'s first two rules: the list
    /// holding the focused field, else the one the pointer last pressed), or null. Published by <see cref="InputRouter"/>
    /// after a press and after each paint, and read by the painter for a container that declares a list AND a
    /// <see cref="Layout.Node.FocusRing"/>, which rings while its list holds the keyboard. The "only list on the frame"
    /// rule is not applied: a lone control nobody touched is not ringed.
    /// </summary>
    public string? KeyboardListId { get; set; }

    // ---- surfaces (Layout.Node.IsSurface) -------------------------------------------------------------------------
    //
    // The same claim, made by a node painted OVER something rather than by an open popover: within the surface's rect,
    // nothing painted before it may answer the pointer. It rides the popover claim's machinery whole: made by being
    // painted, cleared once per paint cycle (NoteFrameBegin), carried into the next cycle so a node painted BEFORE the
    // surface is confined too, and folded into PointerClaimSettled, so the frame a surface appears, moves or goes asks
    // the router for one more. Where they differ is only what is confined: a popover confines everything outside its own
    // content, a surface only what lies under it and was painted before it.

    private int _paintOrder;
    private readonly List<PaintedSurface> _surfaces = [];
    private PaintedSurface[] _carriedSurfaces = [];

    /// <summary>A surface painted this cycle: its rect, and the paint order of its node (<see cref="NotePaintedNode"/>).</summary>
    public readonly record struct PaintedSurface(RectF32 Rect, int Order);

    /// <summary>
    /// The paint order of the next node: how many nodes every widget sharing these settings has painted this cycle. Hand-
    /// painted chrome is confined at this order (it lies over what was painted before it and under what follows).
    /// </summary>
    public int PaintOrder => _paintOrder;

    /// <summary>Takes the next paint order, for a node being painted. Called by the layout painter once per painted node,
    /// and by a non-pixel surface that paints declared nodes itself and wants <see cref="IsCoveredBySurface"/> to hold.</summary>
    public int NotePaintedNode() => _paintOrder++;

    /// <summary>The surfaces painted this cycle so far, in paint order.</summary>
    public IReadOnlyList<PaintedSurface> PaintedSurfaces => _surfaces;

    /// <summary>
    /// Records that a surface (<see cref="Layout.Node.IsSurface"/>) painted at <paramref name="rect"/>, as the node of paint
    /// order <paramref name="order"/>. Called by the layout painter; a non-pixel surface that paints one itself calls it too.
    /// </summary>
    public void NoteSurfacePainted(RectF32 rect, int order) => _surfaces.Add(new PaintedSurface(rect, order));

    /// <summary>
    /// Whether the point lies under a surface painted after the node of paint order <paramref name="order"/>, by what the
    /// last cycle painted (a surface painted later in THIS cycle is not known yet when an earlier node paints; the carry
    /// is the popover claim's, and so is the extra frame when it changes). Hover consults it, so a row under a toast stops
    /// lighting.
    /// </summary>
    public bool IsCoveredBySurface(float x, float y, int order) =>
        Covers(_carriedSurfaces, x, y, order) || Covers(_surfacesFromOtherSettings, x, y, order);

    private static bool Covers(PaintedSurface[] surfaces, float x, float y, int order)
    {
        foreach (var s in surfaces)
        {
            var r = s.Rect;
            if (s.Order > order && x >= r.X && x < r.X + r.Width && y >= r.Y && y < r.Y + r.Height)
            {
                return true;
            }
        }

        return false;
    }

    // Surfaces painted by widgets that do NOT share these settings but are painted after a widget that does, each with
    // the paint order (of THESE settings) below which it covers: the order at which the next widget sharing these
    // settings began, since that one is painted over the surface. Set by InputRouter.AfterPaint, which alone knows the
    // paint order of every widget in the window (its list), so a toast in a widget of its own darkens the page's row
    // beneath it without the host sharing one settings object between them.
    private PaintedSurface[] _surfacesFromOtherSettings = [];

    /// <summary>
    /// Replaces the surfaces that widgets painted with OTHER settings lay over the nodes painted with these, each as the
    /// paint order below which it covers. Returns whether they changed, so the caller asks for one more frame. Called by
    /// <see cref="InputRouter.AfterPaint"/>.
    /// </summary>
    internal bool CarrySurfacesFromOtherSettings(List<PaintedSurface> surfaces)
    {
        if (surfaces.Count == _surfacesFromOtherSettings.Length)
        {
            var same = true;
            for (var i = 0; i < surfaces.Count && same; i++)
            {
                same = surfaces[i] == _surfacesFromOtherSettings[i];
            }

            if (same)
            {
                return false;
            }
        }

        _surfacesFromOtherSettings = surfaces.Count == 0 ? [] : [.. surfaces];
        return true;
    }

    // The paint order at which each widget began this cycle (NoteFrameBegin), so the router can tell which of the nodes
    // painted with these settings a widget of its list painted.
    private readonly Dictionary<object, int> _frameStartOrders = new(ReferenceEqualityComparer.Instance);

    /// <summary>The paint order at which <paramref name="widget"/> began this cycle, or null when it has not.</summary>
    internal int? FrameStartOrder(object widget) => _frameStartOrders.TryGetValue(widget, out var order) ? order : null;

    private bool SurfacesSettled
    {
        get
        {
            if (_surfaces.Count != _carriedSurfaces.Length) return false;
            for (var i = 0; i < _surfaces.Count; i++)
            {
                if (_surfaces[i] != _carriedSurfaces[i]) return false;
            }

            return true;
        }
    }

    // The popover whose content set PointerOwner, so the next cycle can tell whether the claim still stands,
    // and the claim carried into this cycle from the last one.
    private PopoverState? _pointerOwnerPopover;
    private RectF32? _carriedPointerOwner;

    /// <summary>Records that <paramref name="popover"/>'s content, at <paramref name="content"/>, claims the
    /// pointer this cycle. Called by the layout painter, and by a non-pixel surface that paints a popover
    /// itself, right after <see cref="NotePopoverPainted"/>, so a press inside the content reaches the
    /// popover's regions and not the chrome (a declared list's rows) beneath it. The claim is carried to the
    /// next cycle while <paramref name="popover"/> stays open.</summary>
    public void ClaimPointer(RectF32 content, PopoverState popover)
    {
        PointerOwner = content;
        _pointerOwnerPopover = popover;
    }

    /// <summary>
    /// The modal overlay painted this cycle (<see cref="Layout.Node.Overlay.Modal"/>), or null. The topmost one
    /// wins: a later paint replaces an earlier. <see cref="InputRouter"/> reads it to confine input.
    /// </summary>
    /// <remarks>
    /// Registered by being PAINTED, like <see cref="PaintedPopovers"/>, and cleared on the same cycle boundary, so a
    /// modal that closed simply stops painting. The layout painter makes the call; a surface that paints a modal
    /// itself (a terminal) calls <see cref="NoteModalPainted"/> then, once per painted modal.
    /// </remarks>
    public ModalClaim? PaintedModal { get; private set; }

    /// <summary>
    /// Records that a modal's Top began painting in <paramref name="widget"/>. <paramref name="regionStart"/> and
    /// <paramref name="nodeStart"/> are how many regions and painted nodes the widget held BEFORE Top painted, so
    /// those at or after them are Top's. <paramref name="card"/> is where Top draws (a press outside it is an
    /// outside press) and <paramref name="dismiss"/> what closes it.
    /// </summary>
    public ModalClaim NoteModalPainted(object widget, int regionStart, int nodeStart, RectF32 card, Action? dismiss)
        => PaintedModal = new ModalClaim(widget, regionStart, nodeStart, card, dismiss);

    /// <summary>
    /// How many times <see cref="NoteFrameBegin"/> has been called on these settings. The router compares it with the count
    /// it saw at the previous <c>AfterPaint</c>: a settings object whose count did not move had no widget paint since, so
    /// the widgets listed with it are not on screen, whatever object they noted their frame under. Its cycle only turns
    /// over when a widget begins, so what it still holds (its surfaces) is stale.
    /// </summary>
    internal int FrameBeginCount { get; private set; }

    private readonly HashSet<object> _begunThisCycle = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Notes that <paramref name="widget"/> has begun a frame, and clears <see cref="PointerOwner"/> and
    /// <see cref="PaintedPopovers"/> when that begins a NEW cycle. Called by every widget's BeginFrame.
    /// </summary>
    /// <remarks>
    /// A cycle ends when a widget begins a second time, which needs nothing of the host: one widget or
    /// twenty, in any paint order, the first repeat is the boundary. Within a cycle the owner survives every
    /// other widget's BeginFrame, which is the property the whole mechanism exists for, since a popover in
    /// one widget must confine hover in the widgets painted after it.
    /// A non-pixel surface calls it at the start of each of its paint passes, passing itself (or any stable
    /// object), so the claim and the painted popovers clear once per cycle.
    /// </remarks>
    public void NoteFrameBegin(object widget)
    {
        FrameBeginCount++;
        if (_begunThisCycle.Add(widget))
        {
            _frameStartOrders[widget] = _paintOrder;
            return;
        }

        _begunThisCycle.Clear();
        _begunThisCycle.Add(widget);
        // Carried only while the popover that made it is open: one that closed simply stops claiming.
        _carriedPointerOwner = PointerOwner is { } owner && _pointerOwnerPopover is { IsOpen: true } ? owner : null;
        _pointerOwnerPopover = null;
        PointerOwner = null;
        // Surfaces are carried whole: one that is no longer painted is simply absent from the next cycle's list.
        _carriedSurfaces = _surfaces.Count == 0 ? [] : [.. _surfaces];
        _surfaces.Clear();
        _paintOrder = 0;
        _frameStartOrders.Clear();
        _frameStartOrders[widget] = 0;
        _paintedPopovers.Clear();
        PaintedModal = null;
        IsMoving = false;
        _knobCycle++;
        ForgetUnpaintedKnobs();
    }

    /// <summary>
    /// Which text field in this window has the keyboard, and the one way to move it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per-window for the reason focus is a singleton at all: there is one keyboard, so one owner names the
    /// one field receiving it, and every widget drawing into the window has to agree about which that is.
    /// That makes it the same KIND of fact as the DPI scale and the font -- and it lives here so it is
    /// shared the same way, by <see cref="PixelWidgetBase{TSurface}.ShareUiContext"/>, rather than
    /// hand-threaded through the constructor of every widget that happens to own a field.
    /// </para>
    /// <para>
    /// The threading is not hypothetical work avoided. A window whose fields sit in more than one widget --
    /// a search box on a panel, an editable readout in the chrome -- has to give both the SAME
    /// <see cref="TextInputFocus"/> or they each believe they hold the keyboard, and the symptom is two
    /// caret blinks on screen with one of them dead. Reached through the context, there is no second
    /// instance to create.
    /// </para>
    /// <para>
    /// Created here rather than injected, and get-only: a window always has exactly one, and being able to
    /// replace it is being able to orphan the fields that already registered with the old one.
    /// </para>
    /// </remarks>
    public TextInputFocus Focus { get; } = new();

    /// <summary>
    /// Where the focused field's caret was last drawn, or <c>default</c> if no active field has been
    /// painted. A host passes this to its platform's caret-location call (<c>SDL_SetTextInputArea</c>) so
    /// an input method can put its candidate window beside the caret rather than over the text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set at PAINT time by <see cref="PixelWidgetBase{TSurface}.RenderTextInput(TextInputState, int, int, int, int, string, float, TextInputColors?, float, bool)"/>, from the same call
    /// that draws the caret, for the same reason a click binds to the arranged rect: anything that
    /// recomputes the position separately can disagree with what the user is looking at, and here that
    /// disagreement puts the candidate window in the wrong place -- invisible in every test that does not
    /// involve a real IME.
    /// </para>
    /// <para>
    /// Per-window rather than per-widget because there is one caret, for the same reason there is one
    /// <see cref="Focus"/>. Held per widget, a host had to know which widget painted the focused field in
    /// order to ask the right one -- a question with no stable answer, since the field that has the
    /// keyboard moves between them.
    /// </para>
    /// <para>
    /// Deliberately NOT cleared per paint: a widget's PaintLayout runs more than once a frame (chrome,
    /// then content), so a reset inside it would let whichever ran last wipe the caret the other just
    /// recorded. Staleness cannot bite in practice, because the only caller that matters asks while a
    /// field is focused and <see cref="TextInputFocus.BlurIfUnpainted"/> guarantees a focused field was
    /// painted this frame -- so if there is a focus, this rect is from that frame.
    /// </para>
    /// </remarks>
    public RectInt CaretRect { get; set; }

    /// <summary>
    /// The caret's blink phase for the frame being painted. One phase per window, for the same reason there
    /// is one caret: the loop that owns the frames sets it once per frame from
    /// <see cref="CaretBlink.PhaseAt(TimeProvider)"/>, and every widget sharing this object draws its text
    /// inputs in that phase, so no host sets it per widget.
    /// </summary>
    public long CaretPhase { get; set; }

    /// <summary>
    /// The motion time of the frame being painted, in milliseconds on any monotonic clock
    /// (<see cref="MotionClock.MillisecondsAt(TimeProvider)"/>). The host sets it once per frame, as it sets
    /// <see cref="CaretPhase"/>; a spinner's turn and a switch knob's slide are read from it, so a frame nobody
    /// drew advances nothing and a test sets it to a fixed value.
    /// </summary>
    public long MotionMilliseconds { get; set; }

    /// <summary>
    /// Whether the person asked for less motion. A spinner turns slowly and a switch knob lands at once.
    /// </summary>
    public bool ReducedMotion { get; set; }

    /// <summary>
    /// Whether something painted in this cycle moves with <see cref="MotionMilliseconds"/> (a spinner, a sliding
    /// knob). A host asks for another frame while it is up and stops asking when it falls, so an idle window
    /// costs no frames. Cleared when a new paint cycle begins (<see cref="NoteFrameBegin"/>).
    /// </summary>
    public bool IsMoving { get; private set; }

    /// <summary>Raised by the painter when it paints something that moves.</summary>
    public void NoteMoving() => IsMoving = true;

    // The side each switch was last painted at, by its hit, and when that side began. A switch absent from a whole
    // paint cycle is forgotten at the next boundary, so a later cycle's first paint of it is a first paint.
    // Keyed by the hit and its ordinal among the same hits painted this cycle: a host that leaves the default hit on every
    // switch (ButtonHit("switch")) still gets one memory per switch, in the tree's order.
    private readonly Dictionary<(HitResult Hit, int Ordinal), KnobTrack> _knobs = new();
    private readonly HashSet<(HitResult Hit, int Ordinal)> _knobsSeen = new();
    private readonly Dictionary<HitResult, int> _knobOrdinals = new();

    /// <summary>
    /// The knob's slide progress for a switch painted on side <paramref name="on"/>, from 0 (at the other side) to
    /// 1 (arrived). A first paint, a <paramref name="locked"/> switch and reduced motion are at 1; a side that
    /// changed since the last paint starts a slide of <see cref="MotionClock.KnobSlideMilliseconds"/> and raises
    /// <see cref="IsMoving"/> while it runs.
    /// </summary>
    public float NoteKnob(HitResult hit, bool on, bool locked)
    {
        _knobOrdinals.TryGetValue(hit, out var ordinal);
        _knobOrdinals[hit] = ordinal + 1;
        var key = (hit, ordinal);
        _knobsSeen.Add(key);
        if (!_knobs.TryGetValue(key, out var track))
        {
            _knobs[key] = new KnobTrack(on, long.MinValue, _knobCycle);
            return 1f;
        }
        if (track.On != on)
        {
            track = new KnobTrack(on, locked || ReducedMotion ? long.MinValue : MotionMilliseconds, _knobCycle);
            _knobs[key] = track;
        }
        if (locked || ReducedMotion || track.Since == long.MinValue) return 1f;
        if (track.Cycle != _knobCycle && MotionMilliseconds == track.Since)
        {
            // A later paint cycle and the clock has not moved: the host does not run it, so a slide would never
            // finish and the knob would sit on its old side. Land it.
            _knobs[key] = track with { Since = long.MinValue };
            return 1f;
        }
        var progress = (MotionMilliseconds - track.Since) / (float)MotionClock.KnobSlideMilliseconds;
        if (progress >= 1f) return 1f;
        NoteMoving();
        return Math.Max(0f, progress);
    }

    private long _knobCycle;

    private readonly record struct KnobTrack(bool On, long Since, long Cycle);

    private void ForgetUnpaintedKnobs()
    {
        if (_knobs.Count > 0)
        {
            List<(HitResult Hit, int Ordinal)>? gone = null;
            foreach (var key in _knobs.Keys)
            {
                if (!_knobsSeen.Contains(key)) (gone ??= []).Add(key);
            }
            if (gone is not null) foreach (var key in gone) _knobs.Remove(key);
        }
        _knobsSeen.Clear();
        _knobOrdinals.Clear();
    }

    /// <summary>
    /// Which frame the window is on. A host bumps it once per frame, before anything draws; every widget
    /// sharing these settings then agrees on what "this frame" means.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What it buys is one rule: <b>a widget can only be hit where it was drawn, on the frame it was
    /// drawn</b>. <see cref="PixelWidgetBase{TSurface}.BeginFrame"/> stamps the regions it is about to
    /// collect with this value, and the hit tests answer only regions carrying the current one — so a
    /// widget the host stopped drawing goes silent by itself, on the frame it stops.
    /// </para>
    /// <para>
    /// Registering as you paint already makes a widget un-hittable where it is not drawn. It does NOT make
    /// it un-hittable WHEN it is not drawn: a host that simply stops calling a widget's render — an early
    /// return for a loading screen, a modal covering the window — leaves the last frame's regions standing,
    /// and they answer clicks for a control that is no longer on screen. There is nothing local to the
    /// widget to notice this, which is why it is the window that counts frames.
    /// </para>
    /// <para>
    /// Left at 0 by a host that does not count frames, and then every region is stamped 0 too and every
    /// comparison matches — the previous behaviour, unchanged.
    /// </para>
    /// </remarks>
    public long FrameId { get; set; }
}

/// <summary>A modal painted this cycle, as <see cref="WindowUiSettings.NoteModalPainted"/> recorded it.</summary>
public sealed class ModalClaim(object widget, int regionStart, int nodeStart, RectF32 card, Action? dismiss)
{
    /// <summary>The widget whose paint held the modal; widgets painted before it lie beneath.</summary>
    public object Widget { get; } = widget;

    /// <summary>Regions the widget held before Top painted.</summary>
    public int RegionStart { get; } = regionStart;

    /// <summary>Painted nodes the widget held before Top painted.</summary>
    public int NodeStart { get; } = nodeStart;

    /// <summary>Regions the widget held when Top's subtree ended; what it registered after belongs to nodes painted
    /// after the modal, which the modal does not cover. int.MaxValue while Top runs to the end of the widget.</summary>
    public int RegionEnd { get; set; } = int.MaxValue;

    /// <summary>Painted nodes the widget held when Top's subtree ended (see <see cref="RegionEnd"/>).</summary>
    public int NodeEnd { get; set; } = int.MaxValue;

    /// <summary>Where Top's card draws, in the surface's own coordinates. Settable because an anchored Top's card is
    /// the node after the one that begins it.</summary>
    public RectF32 Card { get; set; } = card;

    /// <summary>What closes the modal, or null.</summary>
    public Action? Dismiss { get; } = dismiss;
}
