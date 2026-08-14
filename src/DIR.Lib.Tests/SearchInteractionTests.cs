using System;
using System.Collections.Immutable;
using System.Linq;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Headless unit tests for <see cref="SearchInteraction{TResult}"/> -- the shared search-control
/// interaction (input wiring + key-nav protocol + typed results). Keys are fed exactly as the host key
/// router feeds them: <see cref="TextInputState.OnTextChanged"/> for edits, the wired
/// <see cref="TextInputState.OnKeyOverride"/> for Enter/Escape, <see cref="TextInputState.OnCommit"/>/
/// <see cref="TextInputState.OnCancel"/> for the fall-through commit/cancel, and
/// <see cref="SearchInteraction.HandleNavKey"/> for Up/Down.
/// </summary>
public class SearchInteractionTests
{
    private static readonly string[] Corpus = ["M31", "M32", "M33", "M41", "NGC 224"];

    /// <summary>Minimal domain subclass: prefix-matches an in-memory corpus and records commits.</summary>
    private sealed class TestSearch : SearchInteraction<string>
    {
        private readonly bool _autoSelect;
        private readonly bool _deselectUp;
        private readonly bool _collapseEsc;
        private readonly bool _wrap;

        public int CommitCount;
        public string? LastCommitted;
        public int RawQueryCount;
        public string? LastRawQuery;
        public int DismissCount;
        public int ResultsChangedCount;

        public TestSearch(TextInputState input, bool autoSelect = false, bool deselectUp = false,
            bool collapseEsc = false, Action? releaseFocus = null, Action? redraw = null, bool wrap = false)
            : base(input, requestRedraw: redraw ?? (() => { }), releaseFocus: releaseFocus)
        {
            _autoSelect = autoSelect;
            _deselectUp = deselectUp;
            _collapseEsc = collapseEsc;
            _wrap = wrap;
        }

        protected override bool AutoSelectFirstResult => _autoSelect;
        protected override bool AllowDeselectOnUp => _deselectUp;
        protected override bool CollapseResultsOnEscape => _collapseEsc;
        protected override bool WrapsAround => _wrap;

        protected override ImmutableArray<string> Query(string text)
            => text.Length < 2
                ? []
                : [.. Corpus.Where(c => c.StartsWith(text, StringComparison.OrdinalIgnoreCase))];

        protected override void Commit(string result)
        {
            CommitCount++;
            LastCommitted = result;
        }

        protected override void CommitRawQuery(string text)
        {
            RawQueryCount++;
            LastRawQuery = text;
        }

        protected override void Dismiss()
        {
            DismissCount++;
            base.Dismiss();
        }

        protected override void OnResultsChanged() => ResultsChangedCount++;
    }

    private static TestSearch Make(out TextInputState input, bool autoSelect = false, bool deselectUp = false,
        bool collapseEsc = false, Action? releaseFocus = null, Action? redraw = null, bool wrap = false)
    {
        input = new TextInputState();
        return new TestSearch(input, autoSelect, deselectUp, collapseEsc, releaseFocus, redraw, wrap);
    }

    // ── Requery ──────────────────────────────────────────────────────────────

    [Fact]
    public void TextChange_ResolvesResults()
    {
        var s = Make(out var input);
        input.OnTextChanged!.Invoke("M3");
        s.Results.ShouldBe(["M31", "M32", "M33"]);
        s.LastQuery.ShouldBe("M3");
    }

    [Fact]
    public void TextChange_UnchangedText_DoesNotRequery()
    {
        var s = Make(out var input);
        input.OnTextChanged!.Invoke("M3");
        var firstCount = s.ResultsChangedCount;
        input.OnTextChanged!.Invoke("M3"); // same query
        s.ResultsChangedCount.ShouldBe(firstCount);
    }

    [Fact]
    public void ShortQuery_YieldsNoResults()
    {
        var s = Make(out var input);
        input.OnTextChanged!.Invoke("M");
        s.Results.ShouldBeEmpty();
        s.SelectedIndex.ShouldBe(-1);
    }

    // ── Auto-select policy ────────────────────────────────────────────────────

    [Fact]
    public void Requery_NoAutoSelect_LeavesSelectionCleared()
    {
        var s = Make(out var input, autoSelect: false);
        input.OnTextChanged!.Invoke("M3");
        s.SelectedIndex.ShouldBe(-1);
    }

    [Fact]
    public void Requery_AutoSelect_HighlightsFirst()
    {
        var s = Make(out var input, autoSelect: true);
        input.OnTextChanged!.Invoke("M3");
        s.SelectedIndex.ShouldBe(0);
    }

    [Fact]
    public void Requery_AutoSelect_EmptyResults_ClearsSelection()
    {
        var s = Make(out var input, autoSelect: true);
        input.OnTextChanged!.Invoke("zz"); // no matches
        s.Results.ShouldBeEmpty();
        s.SelectedIndex.ShouldBe(-1);
    }

    // ── Nav ───────────────────────────────────────────────────────────────────

    [Fact]
    public void NavDown_AdvancesAndClampsAtEnd()
    {
        var s = Make(out var input, autoSelect: true);
        input.OnTextChanged!.Invoke("M3"); // 3 results, selected 0
        s.HandleNavKey(InputKey.Down).ShouldBeTrue();
        s.SelectedIndex.ShouldBe(1);
        s.HandleNavKey(InputKey.Down);
        s.SelectedIndex.ShouldBe(2);
        s.HandleNavKey(InputKey.Down); // clamp
        s.SelectedIndex.ShouldBe(2);
    }

    [Fact]
    public void NavUp_ClampsAtZero_WhenDeselectDisabled()
    {
        var s = Make(out var input, autoSelect: true, deselectUp: false);
        input.OnTextChanged!.Invoke("M3");
        s.HandleNavKey(InputKey.Up); // already at 0
        s.SelectedIndex.ShouldBe(0);
    }

    [Fact]
    public void NavUp_DeselectsAtZero_WhenAllowed()
    {
        var s = Make(out var input, autoSelect: true, deselectUp: true);
        input.OnTextChanged!.Invoke("M3");
        s.HandleNavKey(InputKey.Up).ShouldBeTrue();
        s.SelectedIndex.ShouldBe(-1);
    }

    // ── Wrap ──────────────────────────────────────────────────────────────────

    [Fact]
    public void NavDown_WrapsToTheFirstResult()
    {
        var s = Make(out var input, autoSelect: true, wrap: true);
        input.OnTextChanged!.Invoke("M3");      // 3 results, highlight on 0
        s.HandleNavKey(InputKey.Down);
        s.HandleNavKey(InputKey.Down);
        s.SelectedIndex.ShouldBe(2);            // the last one

        s.HandleNavKey(InputKey.Down);
        s.SelectedIndex.ShouldBe(0);
    }

    [Fact]
    public void NavUp_WrapsToTheLastResult()
    {
        var s = Make(out var input, autoSelect: true, wrap: true);
        input.OnTextChanged!.Invoke("M3");
        s.SelectedIndex.ShouldBe(0);

        // -1 % n is -1 in C#, so a bare modulo would leave the selection where it started.
        s.HandleNavKey(InputKey.Up);
        s.SelectedIndex.ShouldBe(2);
    }

    [Fact]
    public void WrappingDown_FromNothingHighlighted_EntersAtTheFirstResult()
    {
        var s = Make(out var input, wrap: true);
        input.OnTextChanged!.Invoke("M3");
        s.SelectedIndex.ShouldBe(-1);           // no auto-select: nothing is highlighted yet

        s.HandleNavKey(InputKey.Down);
        s.SelectedIndex.ShouldBe(0);
    }

    [Fact]
    public void WrappingUp_FromNothingHighlighted_EntersAtTheLastResult()
    {
        var s = Make(out var input, wrap: true);
        input.OnTextChanged!.Invoke("M3");

        // Symmetrical with Down, and the reason Up is worth having at all on a list nothing has entered:
        // reaching the last result should not cost a walk through every one before it.
        s.HandleNavKey(InputKey.Up);
        s.SelectedIndex.ShouldBe(2);
    }

    [Fact]
    public void WrapBeatsDeselectOnUp()
    {
        // A list that wraps has no top to fall off, so the deselect rule has nothing to describe.
        var s = Make(out var input, autoSelect: true, deselectUp: true, wrap: true);
        input.OnTextChanged!.Invoke("M3");

        s.HandleNavKey(InputKey.Up);
        s.SelectedIndex.ShouldBe(2);
    }

    [Fact]
    public void Nav_NoResults_ReturnsFalse()
    {
        var s = Make(out var input);
        s.HandleNavKey(InputKey.Down).ShouldBeFalse();
        s.HandleNavKey(InputKey.Up).ShouldBeFalse();
    }

    // ── Commit ──────────────────────────────────────────────────────────────

    [Fact]
    public void Enter_OnHighlight_CommitsSelected()
    {
        var s = Make(out var input, autoSelect: true);
        input.OnTextChanged!.Invoke("M3");
        input.OnKeyOverride!.Invoke(TextInputKey.Enter).ShouldBeTrue();
        s.CommitCount.ShouldBe(1);
        s.LastCommitted.ShouldBe("M31");
        s.RawQueryCount.ShouldBe(0);
    }

    [Fact]
    public void Enter_NoHighlight_FallsThroughToRawQuery()
    {
        var s = Make(out var input, autoSelect: false);
        input.OnTextChanged!.Invoke("M3"); // results but nothing highlighted
        // OnKeyOverride declines Enter (no selection) -> the field commits -> OnCommit fires.
        input.OnKeyOverride!.Invoke(TextInputKey.Enter).ShouldBeFalse();
        input.OnCommit!.Invoke("M3");
        s.CommitCount.ShouldBe(0);
        s.RawQueryCount.ShouldBe(1);
        s.LastRawQuery.ShouldBe("M3");
    }

    [Fact]
    public void CommitAt_MatchesKeyboardCommit()
    {
        var s = Make(out var input, autoSelect: true);
        input.OnTextChanged!.Invoke("M3");
        s.CommitAt(2);
        s.SelectedIndex.ShouldBe(2);
        s.CommitCount.ShouldBe(1);
        s.LastCommitted.ShouldBe("M33");
    }

    [Fact]
    public void CommitAt_OutOfRange_NoOp()
    {
        var s = Make(out var input, autoSelect: true);
        input.OnTextChanged!.Invoke("M3");
        s.CommitAt(99);
        s.CommitCount.ShouldBe(0);
    }

    // ── Backspace passthrough ──────────────────────────────────────────────────

    [Theory]
    [InlineData(TextInputKey.Backspace)]
    [InlineData(TextInputKey.Delete)]
    public void EditKeys_FallThrough(TextInputKey key)
    {
        var s = Make(out var input, autoSelect: true);
        input.OnTextChanged!.Invoke("M3");
        input.OnKeyOverride!.Invoke(key).ShouldBeFalse();
        s.CommitCount.ShouldBe(0);
    }

    // ── Escape / dismiss ───────────────────────────────────────────────────────

    [Fact]
    public void Escape_Collapse_ClearsResultsAndKeepsField()
    {
        var released = 0;
        var s = Make(out var input, autoSelect: true, collapseEsc: true, releaseFocus: () => released++);
        input.OnTextChanged!.Invoke("M3");
        // First Escape collapses the result list (consumed by OnKeyOverride) -- no cancel.
        input.OnKeyOverride!.Invoke(TextInputKey.Escape).ShouldBeTrue();
        s.Results.ShouldBeEmpty();
        s.SelectedIndex.ShouldBe(-1);
        s.LastQuery.ShouldBe("");
        s.DismissCount.ShouldBe(0);
        released.ShouldBe(0);
    }

    [Fact]
    public void Escape_Collapse_NoResults_FallsThroughToDismiss()
    {
        var released = 0;
        var s = Make(out var input, collapseEsc: true, releaseFocus: () => released++);
        // No results -> OnKeyOverride declines Escape -> field cancels -> OnCancel -> Dismiss.
        input.OnKeyOverride!.Invoke(TextInputKey.Escape).ShouldBeFalse();
        input.OnCancel!.Invoke();
        s.DismissCount.ShouldBe(1);
        released.ShouldBe(1);
    }

    [Fact]
    public void Escape_NoCollapsePolicy_AlwaysDismisses()
    {
        var released = 0;
        var s = Make(out var input, autoSelect: true, collapseEsc: false, releaseFocus: () => released++);
        input.OnTextChanged!.Invoke("M3"); // results present, but collapse disabled
        input.OnKeyOverride!.Invoke(TextInputKey.Escape).ShouldBeFalse(); // declined -> field cancels
        input.OnCancel!.Invoke();
        s.DismissCount.ShouldBe(1);
        released.ShouldBe(1);
    }

    [Fact]
    public void TextChange_RequestsRedraw()
    {
        var redraws = 0;
        var s = Make(out var input, redraw: () => redraws++);
        input.OnTextChanged!.Invoke("M3");
        redraws.ShouldBe(1);
    }
}
