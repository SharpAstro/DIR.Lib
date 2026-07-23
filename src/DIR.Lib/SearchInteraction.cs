using System;
using System.Collections.Immutable;
using System.Threading.Tasks;

namespace DIR.Lib;

/// <summary>
/// Shared interaction machinery for a text-search control: input-callback wiring, a selected-result
/// index, and the Up/Down/Enter/Escape key-nav protocol. This is the logic that is otherwise hand-rolled
/// (and split three ways) around a <see cref="TextInputState"/> -- e.g. a planner autocomplete box and a
/// sky-map search modal that each re-implement "type -> results -> arrow to highlight -> Enter/click to
/// commit -> Escape to dismiss". It is <c>TResult</c>-free so a host key router can hold the currently
/// active search without knowing its result type; the typed result list lives on
/// <see cref="SearchInteraction{TResult}"/>.
///
/// <para>The constructor wires all four <see cref="TextInputState"/> callbacks, so a subclass never
/// re-assigns them:</para>
/// <list type="bullet">
///   <item><see cref="TextInputState.OnTextChanged"/> -> record the query + <see cref="Requery"/> (skipped when the text is unchanged).</item>
///   <item><see cref="TextInputState.OnCommit"/> (Enter with NO highlighted result) -> <see cref="CommitRawQuery"/>.</item>
///   <item><see cref="TextInputState.OnCancel"/> (Escape with nothing to collapse) -> <see cref="Dismiss"/>.</item>
///   <item><see cref="TextInputState.OnKeyOverride"/> -> Enter-on-highlight -> <see cref="CommitSelected"/>; Escape -> collapse-or-cancel; Backspace/Delete fall through to text editing.</item>
/// </list>
///
/// <para>Up/Down navigation is NOT a <see cref="TextInputKey"/> (the arrow keys don't map through
/// <c>ToTextInputKey</c>), so it stays an explicit <see cref="InputKey"/> seam the host key router calls
/// via <see cref="HandleNavKey"/> while <see cref="Input"/> is the active field -- that is the ONE place
/// the arrow protocol lives.</para>
/// </summary>
public abstract class SearchInteraction
{
    private readonly Action _requestRedraw;

    /// <summary>
    /// Focus-release hook (the host's deactivate-text-input) invoked by the default <see cref="Dismiss"/>.
    /// Null when the subclass releases focus its own way (e.g. a modal that closes via a signal whose
    /// handler already deactivates the input).
    /// </summary>
    protected Action? ReleaseFocus { get; }

    /// <param name="input">The backing field; the base takes ownership of its four callbacks.</param>
    /// <param name="requestRedraw">Marks the host surface dirty after a state change.</param>
    /// <param name="releaseFocus">Optional focus-release hook for the default <see cref="Dismiss"/>.</param>
    protected SearchInteraction(TextInputState input, Action requestRedraw, Action? releaseFocus = null)
    {
        Input = input;
        _requestRedraw = requestRedraw;
        ReleaseFocus = releaseFocus;

        input.OnTextChanged = text =>
        {
            // Unchanged text (e.g. a caret-move key that fired OnTextChanged, or a re-entrant edit) must
            // not re-resolve -- this is the planner's LastSuggestionQuery guard, hoisted for both searches.
            if (text == LastQuery)
            {
                return;
            }
            LastQuery = text;
            Requery(text);
            _requestRedraw();
        };
        input.OnCommit = text =>
        {
            // Enter reaches here only when OnKeyOverride did NOT consume it, i.e. no result is highlighted.
            CommitRawQuery(text);
            return Task.CompletedTask;
        };
        input.OnCancel = () =>
        {
            Dismiss();
            _requestRedraw();
        };
        input.OnKeyOverride = HandleOverrideKey;
    }

    /// <summary>The backing text field. The base wires its four callbacks; the subclass never re-assigns them.</summary>
    public TextInputState Input { get; }

    /// <summary>Index of the highlighted result, -1 = none (raw-query mode).</summary>
    public int SelectedIndex { get; set; } = -1;

    /// <summary>The query text that produced the current results; guards redundant re-resolves.</summary>
    public string LastQuery { get; protected set; } = "";

    /// <summary>Number of current results (subclass returns <c>Results.Length</c>).</summary>
    public abstract int ResultCount { get; }

    /// <summary>Marks the host surface dirty. Exposed so a subclass commit/reset can request a repaint on
    /// paths the host does not already redraw (e.g. a mouse dropdown click routed through <see cref="CommitAt"/>).</summary>
    protected void RequestRedraw() => _requestRedraw();

    /// <summary>
    /// Whether Up at index 0 deselects (returns to raw-query mode). A planner autocomplete does; a modal
    /// that always keeps a highlight clamps at 0. Default false.
    /// </summary>
    protected virtual bool AllowDeselectOnUp => false;

    /// <summary>
    /// Whether Escape first collapses a non-empty result list (keeping focus) before it cancels the field.
    /// A planner dropdown collapses on the first Escape and cancels on the second; a modal closes on the
    /// first Escape (so it leaves this false and lets <see cref="Dismiss"/> run). Default false.
    /// </summary>
    protected virtual bool CollapseResultsOnEscape => false;

    /// <summary>
    /// Up/Down navigation over the result list, called by the host key router while <see cref="Input"/> is
    /// the active field (the single home of the arrow protocol). Returns true when the selection moved (the
    /// host then requests a redraw); false when there is nothing to navigate.
    /// </summary>
    public bool HandleNavKey(InputKey key)
    {
        if (ResultCount == 0)
        {
            return false;
        }
        switch (key)
        {
            case InputKey.Down:
                SelectedIndex = Math.Min(SelectedIndex + 1, ResultCount - 1);
                return true;
            case InputKey.Up:
                if (SelectedIndex > 0)
                {
                    SelectedIndex--;
                }
                else if (SelectedIndex == 0 && AllowDeselectOnUp)
                {
                    SelectedIndex = -1;
                }
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Commit the result at <paramref name="index"/> -- the mouse-click counterpart of the keyboard
    /// Enter-on-highlight path (both route through <see cref="CommitSelected"/> so they behave identically).
    /// </summary>
    public void CommitAt(int index)
    {
        if (index >= 0 && index < ResultCount)
        {
            SelectedIndex = index;
            CommitSelected();
        }
    }

    private bool HandleOverrideKey(TextInputKey key)
    {
        switch (key)
        {
            case TextInputKey.Backspace or TextInputKey.Delete:
                // Let the field edit; OnTextChanged then re-resolves the results.
                return false;
            case TextInputKey.Enter when SelectedIndex >= 0 && SelectedIndex < ResultCount:
                CommitSelected();
                return true;
            case TextInputKey.Escape when CollapseResultsOnEscape && ResultCount > 0:
                CollapseResults();
                return true;
            default:
                // Enter-with-no-highlight -> IsCommitted -> OnCommit -> CommitRawQuery.
                // Escape-with-nothing-to-collapse -> IsCancelled -> OnCancel -> Dismiss.
                return false;
        }
    }

    /// <summary>
    /// Clear the selection + last-query so the next keystroke re-resolves. The base collapse (selection +
    /// query); <see cref="SearchInteraction{TResult}"/> also drops the result list. Used by the
    /// collapse-on-Escape path.
    /// </summary>
    protected virtual void CollapseResults()
    {
        SelectedIndex = -1;
        LastQuery = "";
    }

    /// <summary>Re-resolve results for <paramref name="text"/> (domain query).</summary>
    protected abstract void Requery(string text);

    /// <summary>Commit the currently-highlighted result (<see cref="SelectedIndex"/> is valid on entry).</summary>
    protected abstract void CommitSelected();

    /// <summary>
    /// Commit with no highlighted result (Enter on the raw query text). Default no-op -- a modal that
    /// auto-highlights the first result never reaches this; a planner searches by the typed text.
    /// </summary>
    protected virtual void CommitRawQuery(string text)
    {
    }

    /// <summary>
    /// Cancel / dismiss: Escape with nothing to collapse, or the field's OnCancel. The default releases
    /// focus; subclasses clear their own state (and may close a modal) before/instead.
    /// </summary>
    protected virtual void Dismiss() => ReleaseFocus?.Invoke();
}

/// <summary>
/// Adds a typed, atomically-swappable result list to <see cref="SearchInteraction"/>. The domain subclass
/// implements <see cref="Query"/> (resolve) and <see cref="Commit"/> (act on a chosen result); everything
/// else -- input wiring, key-nav, selected index, collapse/dismiss -- comes from the base.
/// </summary>
/// <typeparam name="TResult">A single result row (a string suggestion, a typed catalog match, ...).</typeparam>
public abstract class SearchInteraction<TResult> : SearchInteraction
{
    /// <inheritdoc cref="SearchInteraction(TextInputState, Action, Action?)"/>
    protected SearchInteraction(TextInputState input, Action requestRedraw, Action? releaseFocus = null)
        : base(input, requestRedraw, releaseFocus)
    {
    }

    /// <summary>
    /// Current results. <see cref="ImmutableArray{T}"/> so a render thread can read a torn-free snapshot
    /// while a query rebuilds it (atomic reference swap).
    /// </summary>
    public ImmutableArray<TResult> Results { get; protected set; } = [];

    /// <inheritdoc/>
    public sealed override int ResultCount => Results.Length;

    /// <summary>
    /// Whether a fresh result set auto-highlights index 0. A modal that always commits "the current result"
    /// on Enter does; a planner leaves it false so Enter searches the raw text until the user arrows down.
    /// Default false.
    /// </summary>
    protected virtual bool AutoSelectFirstResult => false;

    /// <inheritdoc/>
    protected sealed override void Requery(string text)
    {
        Results = Query(text);
        SelectedIndex = AutoSelectFirstResult && Results.Length > 0 ? 0 : -1;
        OnResultsChanged();
    }

    /// <inheritdoc/>
    protected sealed override void CommitSelected() => Commit(Results[SelectedIndex]);

    /// <inheritdoc/>
    protected override void CollapseResults()
    {
        Results = [];
        base.CollapseResults();
    }

    /// <summary>
    /// Resolve results for the query. Pure with respect to shared UI state -- it returns the new list
    /// rather than mutating fields the render thread reads (the base performs the atomic swap).
    /// </summary>
    protected abstract ImmutableArray<TResult> Query(string text);

    /// <summary>Act on a committed result (slew / select / pin ...). Called for both keyboard Enter and mouse <see cref="SearchInteraction.CommitAt"/>.</summary>
    protected abstract void Commit(TResult result);

    /// <summary>Hook invoked after <see cref="Results"/> changes (e.g. reset a scroll offset). Default no-op.</summary>
    protected virtual void OnResultsChanged()
    {
    }
}
