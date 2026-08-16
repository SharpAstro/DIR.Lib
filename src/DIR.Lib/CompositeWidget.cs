namespace DIR.Lib
{
    /// <summary>
    /// A widget that paints OTHER widgets into the same surface — an application chrome hosting a tab
    /// strip and a page, a panel hosting an editor. It declares its children once, in
    /// <see cref="Children"/>, and every aggregate query below is derived from that one statement.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The problem this exists for is that a child's regions live on the CHILD.</b> A composite draws
    /// its children into its own surface, so the frame looks whole — but hit tests, cursor queries, Tab
    /// cycling and region enumeration all read a per-widget tracker, so anything asking only the
    /// composite silently misses every control its children registered. Silently is the operative word:
    /// nothing throws and the pixels are right; the controls simply stop answering.
    /// </para>
    /// <para>
    /// Without a base for it, each host restates the composition per query, and they drift. The case that
    /// motivated this had ONE composite stating its child list five times — cursor, dispatch, regions,
    /// text inputs, caret — in three different orders, with one query missing a child outright and its
    /// cursor order inverted relative to its dispatch order. Nothing could have caught that, because each
    /// site is individually plausible.
    /// </para>
    /// <para>
    /// <b>Z-order: this widget's own regions are asked FIRST, then <see cref="Children"/> front to back.</b>
    /// A composite's own painting is almost always one of two things — a non-interactive background
    /// behind its children, where asking first is harmless because it registers nothing, or chrome drawn
    /// OVER them (a status bar, an overlay), where asking first is required. A composite that genuinely
    /// paints interactive content behind its children overrides these and states its own order.
    /// </para>
    /// </remarks>
    public abstract class CompositeWidget<TSurface>(Renderer<TSurface> renderer) : PixelWidgetBase<TSurface>(renderer)
    {
        /// <summary>
        /// The widgets this one paints, in PAINT order — back to front, the order they were drawn in.
        /// Queries walk it in reverse, so the topmost child answers first.
        /// </summary>
        /// <remarks>
        /// A list rather than an enumerable, and rebuilt by the composite when its composition changes
        /// (typically per frame, beside the painting), so the input path walks it without allocating.
        /// Empty is fine: a composite with nothing to host behaves exactly like a plain widget.
        /// </remarks>
        protected abstract IReadOnlyList<PixelWidgetBase<TSurface>> Children { get; }

        /// <inheritdoc/>
        public override HitResult? HitTest(float x, float y)
            => base.HitTest(x, y) ?? FromChildren(child => child.HitTest(x, y));

        /// <inheritdoc/>
        public override HitResult? HitTestAndDispatch(float x, float y, InputModifier modifiers = InputModifier.None)
            => base.HitTestAndDispatch(x, y, modifiers)
               ?? FromChildren(child => child.HitTestAndDispatch(x, y, modifiers));

        /// <inheritdoc/>
        public override CursorKind? HitTestCursor(float x, float y)
            => base.HitTestCursor(x, y) ?? FromChildren(child => child.HitTestCursor(x, y));

        /// <summary>
        /// Every text field painted this frame, this widget's and its children's, in paint order — which
        /// is what makes Tab cycling follow the VISUAL order across a composed frame automatically.
        /// </summary>
        /// <remarks>
        /// Paint order here, not the reversed hit order: cycling reads the frame the way a person does,
        /// while a hit resolves what is on top. Feeding the result to
        /// <see cref="TextInputFocus.BlurIfUnpainted"/> is what stops a field keeping the keyboard after
        /// it leaves the screen — and asking only the composite, or only the active child, blurs a live
        /// field every frame, which looks identical to the bug it fixes.
        /// </remarks>
        public override List<TextInputState> GetRegisteredTextInputs()
        {
            var inputs = base.GetRegisteredTextInputs();
            var children = Children;
            for (var i = 0; i < children.Count; i++)
            {
                inputs.AddRange(children[i].GetRegisteredTextInputs());
            }

            return inputs;
        }

        /// <summary>
        /// Every clickable region painted this frame, this widget's and its children's, in paint order.
        /// For a debug inspector or an accessibility surface enumerating what is on screen.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="PixelWidgetBase{TSurface}.GetRegisteredRegions"/> rather than an
        /// override of it, because that one means "the regions I registered" and is what the hit tests
        /// above read per widget. Conflating the two would make a composite's own hit test walk its
        /// children twice.
        /// </remarks>
        public IReadOnlyList<ClickableRegion> PaintedRegions()
        {
            var regions = new List<ClickableRegion>(GetRegisteredRegions());
            var children = Children;
            for (var i = 0; i < children.Count; i++)
            {
                regions.AddRange(children[i].GetRegisteredRegions());
            }

            return regions;
        }

        /// <summary>Asks each child front to back (the reverse of paint order) for the first non-null answer.</summary>
        private T? FromChildren<T>(Func<PixelWidgetBase<TSurface>, T?> ask) where T : class
        {
            var children = Children;
            for (var i = children.Count - 1; i >= 0; i--)
            {
                if (ask(children[i]) is { } answer)
                {
                    return answer;
                }
            }

            return null;
        }

        /// <summary>Value-typed counterpart of the above, for a query answering a struct.</summary>
        private T? FromChildren<T>(Func<PixelWidgetBase<TSurface>, T?> ask) where T : struct
        {
            var children = Children;
            for (var i = children.Count - 1; i >= 0; i--)
            {
                if (ask(children[i]) is { } answer)
                {
                    return answer;
                }
            }

            return null;
        }
    }
}
