using System.Globalization;
using System.Linq;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="TextEditorLayout"/> -- the wrap, the visual-line motions, the hit test and the grow-then-scroll
/// box -- over a fixed advance of one unit per grapheme, so no font is needed and every width is a count.
/// </summary>
public class TextEditorLayoutTests
{
    private const float LineHeight = 10f;

    // One unit per grapheme cluster: a family emoji or a letter with its accent is as wide as an 'a'.
    private static float OneUnitEach(System.ReadOnlySpan<char> text)
        => new StringInfo(text.ToString()).LengthInTextElements;

    private static TextEditorLayout Wrap(string text, float width)
        => new(text, width, OneUnitEach, LineHeight);

    private static string[] Rows(TextEditorLayout layout)
        => layout.Lines.Select(l => layout.Text[l.Start..l.End]).ToArray();

    // The editor with its layout already handed over, as the paint would.
    private static TextEditorState Editor(string text, float width, int caret = -1, int maxLines = int.MaxValue, int minLines = 1)
    {
        var editor = new TextEditorState { Text = text, MaxLines = maxLines, MinLines = minLines };
        editor.CursorPos = caret < 0 ? text.Length : caret;
        editor.SetLayout(Wrap(text, width));
        return editor;
    }

    // Three rows at width 8: "aaaaaaaa " / "bb " / "cccccccc".
    private const string ThreeRows = "aaaaaaaa bb cccccccc";

    // ---- Wrap: word boundaries ----

    [Fact]
    public void Wrap_BreaksAtTheLastWordBoundaryThatFits()
    {
        Rows(Wrap("hello world foo", 11)).ShouldBe(["hello world ", "foo"]);
        Rows(Wrap("hello world foo", 8)).ShouldBe(["hello ", "world ", "foo"]);
    }

    [Fact]
    public void Wrap_TrailingSpacesHangAtTheEdgeInsteadOfWrapping()
    {
        // "abcd" fills the box exactly; the space after it stays on the line and "efgh" starts the next.
        Rows(Wrap("abcd efgh", 4)).ShouldBe(["abcd ", "efgh"]);
        Rows(Wrap("abcd    efgh", 4)).ShouldBe(["abcd    ", "efgh"]);
    }

    [Fact]
    public void Wrap_MovesAWordThatFitsALineWholeInsteadOfBreakingIt()
    {
        Rows(Wrap("ab cdefgh", 6)).ShouldBe(["ab ", "cdefgh"]);
    }

    [Fact]
    public void Wrap_AnIndentIsNotAWordBoundary()
    {
        // Breaking after the indent would leave a line of nothing but spaces.
        Rows(Wrap("    abcdefgh", 6)).ShouldBe(["    ab", "cdefgh"]);
    }

    [Fact]
    public void Wrap_HardBreaksAreKeptAndEveryLogicalLineGetsARow()
    {
        var layout = Wrap("ab\ncd\n", 20);

        layout.Lines.ToArray().ShouldBe([
            new VisualLine(0, 2, false),
            new VisualLine(3, 5, false),
            new VisualLine(6, 6, false)]);
    }

    [Fact]
    public void Wrap_AnEmptyTextIsOneEmptyLine()
    {
        Wrap("", 10).Lines.ToArray().ShouldBe([new VisualLine(0, 0, false)]);
    }

    [Fact]
    public void Wrap_SoftLinesAreMarkedAndTheNextStartsWhereTheyEnd()
    {
        var lines = Wrap(ThreeRows, 8).Lines;

        lines.Select(l => l.IsSoftWrapped).ShouldBe([true, true, false]);
        lines[0].End.ShouldBe(lines[1].Start);
        lines[1].End.ShouldBe(lines[2].Start);
    }

    [Fact]
    public void Wrap_ANonPositiveWidthLeavesOnlyHardBreaks()
    {
        Rows(Wrap("a long line\nand another", 0)).ShouldBe(["a long line", "and another"]);
    }

    // ---- Wrap: long words and paths ----

    [Fact]
    public void Wrap_BreaksALongPathAfterItsLastSeparatorThatFits()
    {
        Rows(Wrap("/usr/local/bin/tool", 10)).ShouldBe(["/usr/", "local/bin/", "tool"]);
    }

    [Theory]
    [InlineData('/')]
    [InlineData('\\')]
    [InlineData('.')]
    [InlineData('-')]
    [InlineData('_')]
    [InlineData(':')]
    public void Wrap_BreaksALongWordAfterEachSeparator(char separator)
    {
        // The separator stays on the line it ends; the separator-free tail is then cut at the width.
        var text = $"aaaa{separator}bbbbbbbb";

        Rows(Wrap(text, 6)).ShouldBe([$"aaaa{separator}", "bbbbbb", "bb"]);
    }

    [Fact]
    public void Wrap_DoesNotBreakAfterALeadingSeparator_WhichWouldWasteARow()
    {
        Rows(Wrap("/abcdefghij", 6)).ShouldBe(["/abcde", "fghij"]);
        Rows(Wrap("//abcdefghij", 6)).ShouldBe(["//abcd", "efghij"]);
    }

    [Fact]
    public void Wrap_PrefersASpaceToASeparator()
    {
        // The path as a whole fits a row, so it moves down in one piece rather than splitting at its first '/'.
        Rows(Wrap("see /usr/bin", 9)).ShouldBe(["see ", "/usr/bin"]);
    }

    [Fact]
    public void Wrap_BreaksASeparatorFreeWordAnywhere()
    {
        Rows(Wrap("abcdefghij", 4)).ShouldBe(["abcd", "efgh", "ij"]);
    }

    [Fact]
    public void Wrap_NeverSplitsAGraphemeCluster()
    {
        const string family = "\U0001F468‍\U0001F469‍\U0001F467";
        const string eAcute = "é";
        var text = "abc" + family + "def" + eAcute + eAcute + eAcute + eAcute;
        var layout = Wrap(text, 4);

        foreach (var line in layout.Lines)
        {
            // A cluster start is a boundary of the text; its offset must be one of the text elements' starts.
            StringInfo.ParseCombiningCharacters(text).ShouldContain(line.Start);
        }

        Rows(layout).ShouldBe(["abc" + family, "def" + eAcute, eAcute + eAcute + eAcute]);
    }

    [Fact]
    public void Wrap_ACharacterWiderThanTheBoxStillGetsItsOwnRow()
    {
        // Nothing fits in half a unit; each cluster is one row rather than an endless loop or a split.
        Rows(Wrap("abc", 0.5f)).ShouldBe(["a", "b", "c"]);
    }

    // ---- Caret Up/Down across wrapped lines ----

    [Fact]
    public void Down_KeepsThePreferredXAcrossShorterRows()
    {
        var editor = Editor(ThreeRows, 8, caret: 6);

        editor.HandleKey(TextInputKey.Down);
        editor.CursorPos.ShouldBe(11);              // "bb " is two wide: the caret clamps to its end, before the space

        editor.HandleKey(TextInputKey.Down);
        editor.CursorPos.ShouldBe(18);              // back at x = 6 on the long row, not at the 2 it was clamped to

        editor.HandleKey(TextInputKey.Up);
        editor.CursorPos.ShouldBe(11);
        editor.HandleKey(TextInputKey.Up);
        editor.CursorPos.ShouldBe(6);
    }

    [Fact]
    public void Down_ANewVerticalRunStartsFromWhereTheCaretIs()
    {
        var editor = Editor(ThreeRows, 8, caret: 6);
        editor.HandleKey(TextInputKey.Down);        // clamped to x = 2
        editor.HandleKey(TextInputKey.Left);        // any other key ends the run
        editor.CursorPos.ShouldBe(10);

        editor.HandleKey(TextInputKey.Down);

        editor.CursorPos.ShouldBe(13);              // x = 1 on the long row, which starts at 12
    }

    [Fact]
    public void UpAndDown_PastTheFirstAndLastRowGoToTheTextsEnds()
    {
        var editor = Editor(ThreeRows, 8, caret: 3);

        editor.HandleKey(TextInputKey.Up);
        editor.CursorPos.ShouldBe(0);

        editor.HandleKey(TextInputKey.Down);
        editor.HandleKey(TextInputKey.Down);
        editor.HandleKey(TextInputKey.Down);
        editor.CursorPos.ShouldBe(ThreeRows.Length);
    }

    [Fact]
    public void ShiftUpDown_ExtendTheSelectionByRow()
    {
        var editor = Editor(ThreeRows, 8, caret: 4);

        editor.HandleKey(TextInputKey.Down, extend: true);

        editor.SelectionAnchor.ShouldBe(4);
        editor.CursorPos.ShouldBe(11);
    }

    [Fact]
    public void AnIndexAtASoftBreakIsDrawnAtTheStartOfTheNextRow()
    {
        var layout = Wrap(ThreeRows, 8);

        layout.LineOf(9).ShouldBe(1);               // the end of row 0 is the start of row 1
        layout.XOf(9).ShouldBe(0f);
        layout.LineOf(8).ShouldBe(0);
    }

    [Fact]
    public void WithoutALayout_UpAndDownStayLogical()
    {
        var editor = new TextEditorState { Text = "abc\nde", CursorPos = 1 };

        editor.HandleKey(TextInputKey.Down);

        editor.CursorPos.ShouldBe(5);
    }

    // ---- Home / End on a visual row ----

    [Fact]
    public void HomeAndEnd_GoToTheEndsOfTheVisualRow()
    {
        var editor = Editor(ThreeRows, 8, caret: 10);          // second row, "bb "

        editor.HandleKey(TextInputKey.Home);
        editor.CursorPos.ShouldBe(9);

        editor.HandleKey(TextInputKey.End);
        editor.CursorPos.ShouldBe(11);                         // before the hanging space; 12 is drawn on the next row
    }

    [Fact]
    public void End_OnTheFirstSoftRowStaysOnIt_AndOnTheLastRowGoesToTheTextsEnd()
    {
        var editor = Editor(ThreeRows, 8, caret: 3);

        editor.HandleKey(TextInputKey.End);
        editor.CursorPos.ShouldBe(8);
        editor.VisualLayout!.LineOf(editor.CursorPos).ShouldBe(0);

        editor.MoveCaretTo(15);
        editor.HandleKey(TextInputKey.Home);
        editor.CursorPos.ShouldBe(12);
        editor.HandleKey(TextInputKey.End);
        editor.CursorPos.ShouldBe(20);
    }

    [Fact]
    public void ShiftHomeEnd_ExtendTheSelectionToTheRowEnd()
    {
        var editor = Editor(ThreeRows, 8, caret: 3);

        editor.HandleKey(TextInputKey.End, extend: true);

        (editor.SelectionStart, editor.SelectionEnd).ShouldBe((3, 8));
    }

    [Fact]
    public void HomeAndEnd_StopAtAHardBreakToo()
    {
        var editor = Editor("ab\ncd", 20, caret: 4);

        editor.HandleKey(TextInputKey.Home);
        editor.CursorPos.ShouldBe(3);

        editor.MoveCaretTo(0);
        editor.HandleKey(TextInputKey.End);
        editor.CursorPos.ShouldBe(2);
    }

    // ---- PageUp / PageDown ----

    private const string TenRows = "l0\nl1\nl2\nl3\nl4\nl5\nl6\nl7\nl8\nl9";

    [Fact]
    public void PageDownAndUp_MoveTheViewportsLineCount_KeepingX()
    {
        var editor = Editor(TenRows, 20, caret: 1, maxLines: 3);

        editor.HandleKey(TextInputKey.PageDown);
        editor.CursorPos.ShouldBe(10);              // row 3, x = 1
        editor.HandleKey(TextInputKey.PageDown);
        editor.CursorPos.ShouldBe(19);              // row 6

        editor.HandleKey(TextInputKey.PageUp);
        editor.CursorPos.ShouldBe(10);
    }

    [Fact]
    public void PageDownAndUp_PastTheEndsGoToTheTextsEnds()
    {
        var editor = Editor(TenRows, 20, caret: 19, maxLines: 3);

        editor.HandleKey(TextInputKey.PageDown);
        editor.HandleKey(TextInputKey.PageDown);
        editor.CursorPos.ShouldBe(TenRows.Length);

        editor.MoveCaretTo(4);
        editor.HandleKey(TextInputKey.PageUp);
        editor.CursorPos.ShouldBe(0);
    }

    [Fact]
    public void PageDown_CountsWrappedRowsNotLogicalLines()
    {
        // Six rows at width 4; a page of 2 rows from the first lands on the third.
        var editor = Editor("aaaabbbbccccddddeeeeffff", 4, caret: 1, maxLines: 2);

        editor.HandleKey(TextInputKey.PageDown);

        editor.CursorPos.ShouldBe(9);
    }

    [Fact]
    public void PageDown_WithAnUnboundedBoxFallsBackToPageLines()
    {
        var editor = Editor(TenRows, 20, caret: 1);
        editor.PageLines = 4;

        editor.HandleKey(TextInputKey.PageDown);

        editor.CursorPos.ShouldBe(13);              // row 4
    }

    [Fact]
    public void PageDown_ScrollsTheCaretIntoView()
    {
        var editor = Editor(TenRows, 20, caret: 1, maxLines: 3);

        editor.HandleKey(TextInputKey.PageDown);    // row 3 is the fourth row of a three-row box

        editor.ScrollOffsetY.ShouldBe(10f);
    }

    // ---- Hit testing ----

    [Fact]
    public void CaretIndexAt_ResolvesToTheNearerBoundaryOnTheRowUnderThePoint()
    {
        var layout = Wrap(ThreeRows, 8);

        layout.CaretIndexAt(3.4f, 2f).ShouldBe(3);
        layout.CaretIndexAt(3.6f, 2f).ShouldBe(4);
        layout.CaretIndexAt(1f, 12f).ShouldBe(10);
        layout.CaretIndexAt(5f, 25f).ShouldBe(17);
    }

    [Fact]
    public void CaretIndexAt_PastTheEndOfARowLandsAtItsEnd()
    {
        var layout = Wrap(ThreeRows, 8);

        layout.CaretIndexAt(100f, 2f).ShouldBe(8);      // a soft row: before the hanging space
        layout.CaretIndexAt(100f, 12f).ShouldBe(11);
        layout.CaretIndexAt(100f, 25f).ShouldBe(20);    // the hard row: the text's end
        layout.CaretIndexAt(-5f, 12f).ShouldBe(9);
    }

    [Fact]
    public void CaretIndexAt_BelowTheLastRowIsTheTextsEnd_AndAboveTheFirstIsRowZero()
    {
        var layout = Wrap(ThreeRows, 8);

        layout.CaretIndexAt(2f, 1000f).ShouldBe(ThreeRows.Length);
        layout.CaretIndexAt(2f, -7f).ShouldBe(2);
    }

    [Fact]
    public void CaretIndexAt_OnTheStateAddsTheScroll()
    {
        var editor = Editor(TenRows, 20, caret: 0, maxLines: 3);
        editor.HandleKey(TextInputKey.DocumentEnd);     // scrolls so rows 7..9 show: offset 70

        editor.ScrollOffsetY.ShouldBe(70f);
        editor.CaretIndexAt(1f, 5f).ShouldBe(22);       // the top of the box is row 7, "l7"
    }

    [Fact]
    public void CaretIndexAt_ASurrogatePairOrClusterIsOnePosition()
    {
        const string family = "\U0001F468‍\U0001F469‍\U0001F467";
        var layout = Wrap("a" + family + "b", 20);

        layout.CaretIndexAt(1.4f, 0f).ShouldBe(1);
        layout.CaretIndexAt(1.6f, 0f).ShouldBe(1 + family.Length);
    }

    // ---- Growth, then scroll ----

    [Fact]
    public void Box_GrowsWithItsContentUpToTheMaxAndThenStops()
    {
        var editor = Editor("", 20, maxLines: 3);

        editor.ViewportHeight.ShouldBe(10f);
        editor.VisibleLines.ShouldBe(1);

        editor.InsertText("abc");
        editor.ViewportHeight.ShouldBe(10f);

        editor.HandleKey(TextInputKey.Enter);
        editor.ViewportHeight.ShouldBe(20f);
        editor.HandleKey(TextInputKey.Enter);
        editor.ViewportHeight.ShouldBe(30f);
        editor.HandleKey(TextInputKey.Enter);
        editor.ViewportHeight.ShouldBe(30f);            // capped: the fourth row is a scroll, not more box
        editor.VisibleLines.ShouldBe(3);
    }

    [Fact]
    public void Box_GrowsWhenWrappingAddsRows()
    {
        var editor = Editor("", 5, maxLines: 4);

        editor.InsertText("aaaaaaaaaaaa");                 // 12 characters at width 5: three rows

        editor.ViewportHeight.ShouldBe(30f);
    }

    [Fact]
    public void Box_NeverShrinksBelowMinLines()
    {
        var editor = Editor("", 20, maxLines: 5, minLines: 2);

        editor.ViewportHeight.ShouldBe(20f);
        editor.InsertText("a\nb\nc");
        editor.ViewportHeight.ShouldBe(30f);
    }

    [Fact]
    public void Scroll_KeepsTheCaretsRowInViewAfterEveryEditAndMotion()
    {
        var editor = Editor("", 20, maxLines: 3);
        editor.InsertText("abc");

        editor.HandleKey(TextInputKey.Enter);
        editor.HandleKey(TextInputKey.Enter);
        editor.ScrollOffsetY.ShouldBe(0f);              // three rows still fit

        editor.HandleKey(TextInputKey.Enter);           // fourth row: slide up by one
        editor.ScrollOffsetY.ShouldBe(10f);
        editor.HandleKey(TextInputKey.Enter);
        editor.ScrollOffsetY.ShouldBe(20f);

        editor.HandleKey(TextInputKey.Up);              // caret on row 3 of 0..4: inside the window of rows 2..4
        editor.ScrollOffsetY.ShouldBe(20f);
        editor.HandleKey(TextInputKey.Up);              // row 2 is the top visible row
        editor.ScrollOffsetY.ShouldBe(20f);
        editor.HandleKey(TextInputKey.Up);
        editor.ScrollOffsetY.ShouldBe(10f);             // row 1: slides down just enough
        editor.HandleKey(TextInputKey.Up);
        editor.ScrollOffsetY.ShouldBe(0f);

        editor.HandleKey(TextInputKey.DocumentEnd);
        editor.ScrollOffsetY.ShouldBe(20f);
        editor.HandleKey(TextInputKey.DocumentStart);
        editor.ScrollOffsetY.ShouldBe(0f);
    }

    [Fact]
    public void Scroll_FollowsAWrappedRowNotTheLogicalLine()
    {
        // One logical line of five rows: the caret's row, not its line, decides the offset.
        var editor = Editor("", 4, maxLines: 2);

        editor.InsertText("aaaabbbbccccddddeeee");

        editor.ScrollOffsetY.ShouldBe(30f);             // row 4 of five, in a two-row box
    }

    [Fact]
    public void Scroll_IsHeldInsideTheContentWhenTheTextShrinks()
    {
        var editor = Editor("", 20, maxLines: 3);
        editor.InsertText("a");
        for (var i = 0; i < 4; i++)
        {
            editor.HandleKey(TextInputKey.Enter);
        }

        editor.ScrollOffsetY.ShouldBe(20f);

        editor.Undo();
        editor.Undo();

        editor.ScrollOffsetY.ShouldBe(0f);              // three rows left: nothing to scroll
    }

    [Fact]
    public void Scroll_AHostMovedScrollSurvivesTheNextFramesLayout_ButANewWidthBringsTheCaretBack()
    {
        var editor = Editor(TenRows, 20, caret: 0, maxLines: 3);
        editor.ScrollOffsetY = 40f;                     // the host scrolled away from the caret

        editor.SetLayout(Wrap(TenRows, 20));            // the same layout again, as the next frame builds it
        editor.ScrollOffsetY.ShouldBe(40f);

        editor.SetLayout(Wrap(TenRows, 19));            // the box was resized
        editor.ScrollOffsetY.ShouldBe(0f);
    }

    [Fact]
    public void Scroll_AFontSizeChange_BringsTheCaretBackIntoView()
    {
        var editor = Editor(TenRows, 20, maxLines: 3);   // caret at the end, so scrolled to the last rows
        editor.ScrollOffsetY = 0f;                       // a host scrolled away from the caret

        editor.SetLayout(new TextEditorLayout(TenRows, 20, OneUnitEach, LineHeight * 2f));

        editor.ScrollOffsetY.ShouldBeGreaterThan(0f);
    }

    [Fact]
    public void InsertText_ResetsTheStickyX_OfAWrappedLayout()
    {
        // Down from column 6 into the short line remembers x = 6; typing there must forget it.
        var editor = Editor("abcdefgh\nab\nabcdefgh", 20, caret: 6);

        editor.HandleKey(TextInputKey.Down);
        editor.InsertText("x");
        editor.HandleKey(TextInputKey.Down);

        editor.CursorPos.ShouldBe("abcdefgh\nabx\n".Length + 3);
    }

    [Fact]
    public void Clear_ResetsTheScroll()
    {
        var editor = Editor(TenRows, 20, maxLines: 3);
        editor.ScrollOffsetY.ShouldBeGreaterThan(0f);

        editor.Clear();

        editor.ScrollOffsetY.ShouldBe(0f);
    }

    [Fact]
    public void ALayoutBuiltForOlderTextIsRewrappedBeforeItIsUsed()
    {
        var editor = Editor("abcdefgh", 4, caret: 0);

        editor.Text = "abcdefghijkl";                    // a host assigned the text between paints

        editor.VisualLayout!.LineCount.ShouldBe(3);
    }

    // ---- The pure pieces ----

    [Fact]
    public void VisibleLines_ClampsBetweenMinAndMax()
    {
        var layout = Wrap("a\nb\nc\nd\ne", 20);

        layout.VisibleLines(1, 3).ShouldBe(3);
        layout.VisibleLines(1, 100).ShouldBe(5);
        layout.VisibleLines(7, 100).ShouldBe(7);
        layout.VisibleLines(4, 2).ShouldBe(4);          // a max under the min is the min
    }

    [Fact]
    public void RevealLine_MovesTheLeastThatShowsTheWholeRow()
    {
        var layout = Wrap("a\nb\nc\nd\ne\nf", 20);       // six rows, a box of three

        layout.RevealLine(0, 0f, 30f).ShouldBe(0f);
        layout.RevealLine(2, 0f, 30f).ShouldBe(0f);
        layout.RevealLine(3, 0f, 30f).ShouldBe(10f);
        layout.RevealLine(5, 0f, 30f).ShouldBe(30f);
        layout.RevealLine(1, 30f, 30f).ShouldBe(10f);
        layout.RevealLine(0, 999f, 30f).ShouldBe(0f);
    }

    [Fact]
    public void MoveVertically_UsesTheGivenX()
    {
        var layout = Wrap(ThreeRows, 8);

        layout.MoveVertically(0, 1, 5f).ShouldBe(11);
        layout.MoveVertically(0, 2, 5f).ShouldBe(17);
        layout.MoveVertically(17, -2, 5f).ShouldBe(5);
        layout.MoveVertically(17, -3, 5f).ShouldBe(0);
        layout.MoveVertically(17, 1, 5f).ShouldBe(20);
    }
}
