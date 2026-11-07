using System;
using System.Collections.Generic;

namespace DIR.Lib
{
    /// <summary>
    /// What <see cref="InputRouter"/> reaches a widget's text selection through. Not an <see cref="IPixelWidget"/>
    /// member (that would break every implementer): the router asks with <c>is</c>, so a widget that never opted in
    /// is not touched.
    /// </summary>
    public interface ITextSelectionHost
    {
        /// <summary>Whether the widget laid a selection over its page (see <c>PixelWidgetBase.WithSelection</c>).</summary>
        bool SelectionEnabled { get; }

        /// <summary>Whether a drag is in progress.</summary>
        bool SelectionDragging { get; }

        /// <summary>The selected text, or null with none.</summary>
        string? SelectedText { get; }

        /// <summary>Whether the point is over the selected text.</summary>
        bool SelectionContains(float x, float y);

        /// <summary>Lets go of the selection.</summary>
        void ClearSelection();

        /// <summary>
        /// Feeds a left press, move or release to the selection. <paramref name="inert"/> says whether the point is over
        /// nothing that acts on a press. True for what it took.
        /// </summary>
        bool HandleSelectionInput(InputEvent evt, Func<float, float, bool> inert);
    }

    public abstract partial class PixelWidgetBase<TSurface> : ITextSelectionHost
    {
        /// <summary>The Fill leaf laid over a page by <see cref="WithSelection"/>; <see cref="PaintLayout(System.Collections.Immutable.ImmutableArray{Layout.ArrangedNode{float}}, PixelMeasureContext{TSurface}, Action{Layout.Content.Fill, RectF32}?)"/> paints it itself.</summary>
        public const string SelectionKey = "dir-text-selection";

        private TextSelection<TSurface>? _selection;
        private bool _selectionOptedIn;

        /// <summary>The selection over the page's text, made on first use (it measures through the renderer).</summary>
        public TextSelection<TSurface> Selection => _selection ??= new TextSelection<TSurface>(Renderer);

        /// <summary>
        /// The palette the selection reads its colours from (<see cref="UiPalette.Selection"/> behind,
        /// <see cref="UiPalette.SelectionText"/> on it). Null falls back to a neutral blue bar with white glyphs.
        /// </summary>
        public UiPalette? SelectionPalette { get; set; }

        /// <summary>The highlight bar's colour; override to state it without a palette.</summary>
        protected virtual RGBAColor32 SelectionBackground => SelectionPalette?.Selection ?? new RGBAColor32(51, 102, 204, 255);

        /// <summary>The glyph colour drawn on the bar; override to state it without a palette.</summary>
        protected virtual RGBAColor32 SelectionForeground => SelectionPalette?.SelectionText ?? new RGBAColor32(255, 255, 255, 255);

        /// <summary>The rect a scrolled part of the page shows through (empty when nothing scrolls), so the highlight is cut at its edge.</summary>
        protected virtual RectF32 SelectionViewport => default;

        /// <summary>
        /// Opt in: <paramref name="page"/> with a Fill leaf laid over it, where the widget paints the selection itself, after
        /// the page's text and before anything laid over the page (a menu still covers the highlight). A router with a
        /// <see cref="InputRouter.SetClipboardText"/> then drags, copies and clears it with no further code.
        /// </summary>
        protected Layout.Node WithSelection(Layout.Node page)
        {
            _selectionOptedIn = true;
            return Layout.Builder.Overlay(page, Layout.Builder.Fill(key: SelectionKey).Stretch());
        }

        /// <inheritdoc/>
        bool ITextSelectionHost.SelectionEnabled => _selectionOptedIn;

        /// <inheritdoc/>
        bool ITextSelectionHost.SelectionDragging => _selection?.Dragging ?? false;

        /// <inheritdoc/>
        string? ITextSelectionHost.SelectedText => _selection?.Text;

        /// <inheritdoc/>
        bool ITextSelectionHost.SelectionContains(float x, float y) => _selection?.Contains(x, y) ?? false;

        /// <inheritdoc/>
        void ITextSelectionHost.ClearSelection() => _selection?.Clear();

        /// <inheritdoc/>
        bool ITextSelectionHost.HandleSelectionInput(InputEvent evt, Func<float, float, bool> inert)
            => _selectionOptedIn && Selection.HandleInput(evt, inert);

        // Called by PaintLayout for the reserved leaf; paints with the resolver the page was measured with.
        private void PaintSelection(PixelMeasureContext<TSurface> ctx)
        {
            Selection.Frame(SelectableTextRegions, SelectionViewport);
            Selection.Paint(
                (text, region, x, y, width, height, color) =>
                    DrawText(text, region.FontPath, ctx.Fallback, x, y, width, height, region.FontSize, color, TextAlign.Near, region.VerticalAlign),
                SelectionBackground, SelectionForeground);
        }
    }
}
