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
    /// <b>Z-order: by default this widget's own regions are asked FIRST, then <see cref="Children"/> front
    /// to back.</b> A composite's own painting is almost always one of two things: a non-interactive
    /// background behind its children, where asking first is harmless because it registers nothing, or
    /// chrome drawn OVER them (a status bar, an overlay), where asking first is required. A composite that
    /// paints a child over its own content (a popover hung from its own bar) states where its own content
    /// falls among its children with <see cref="ChildrenUnderOwn"/>, and every query below follows it.
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

        /// <summary>
        /// How many of <see cref="Children"/>, counted from the back, this widget's own content is painted
        /// OVER: children <c>[0, k)</c> are under it and <c>[k, Count)</c> over it. The default, every child,
        /// is the contract above: a composite's own content is chrome over what it hosts.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Every combined query honours it: <see cref="CollectPaintedRegions"/> and
        /// <see cref="CollectPaintedNodes"/> (so a routed press, the hover highlight and a declared shortcut),
        /// <see cref="HitTest"/>, <see cref="HitTestCursor"/> and <see cref="ScrollTargetAt"/>. A composite
        /// whose popover child hangs from its own bar states the number of children painted before the bar.
        /// </para>
        /// <para>
        /// One split, so the order is exact per CHILD, not per region. A child drawn in two passes, part under
        /// this widget's own content and part over it (a map behind a picture whose palette floats over the
        /// picture), cannot be described by it; such a composite keeps that child wholly over its own content
        /// (0, for a single child), and the part drawn underneath then answers over it too.
        /// </para>
        /// <para>
        /// Read on every query, clamped to <c>[0, Count]</c>, so a composite whose children change per frame
        /// can state it beside its painting, where the order is decided, as it does <see cref="Children"/>.
        /// </para>
        /// </remarks>
        protected virtual int ChildrenUnderOwn => Children.Count;

        /// <inheritdoc/>
        public override HitResult? HitTest(float x, float y)
        {
            var (children, split) = Layers();
            Func<PixelWidgetBase<TSurface>, HitResult?> ask = child => child.HitTest(x, y);
            return FromChildren(children, split, children.Count, ask) ?? base.HitTest(x, y) ?? FromChildren(children, 0, split, ask);
        }

        /// <inheritdoc/>
        public override CursorKind? HitTestCursor(float x, float y)
        {
            var (children, split) = Layers();
            Func<PixelWidgetBase<TSurface>, CursorKind?> ask = child => child.HitTestCursor(x, y);
            return FromChildren(children, split, children.Count, ask) ?? base.HitTestCursor(x, y) ?? FromChildren(children, 0, split, ask);
        }

        /// <inheritdoc/>
        public override ListScrollController? ScrollTargetAt(float x, float y)
        {
            var (children, split) = Layers();
            Func<PixelWidgetBase<TSurface>, ListScrollController?> ask = child => child.ScrollTargetAt(x, y);
            return FromChildren(children, split, children.Count, ask) ?? base.ScrollTargetAt(x, y) ?? FromChildren(children, 0, split, ask);
        }

        /// <summary>
        /// Where the pointer is, passed on to every child as it is set.
        /// </summary>
        /// <remarks>
        /// Hover resolves during PAINT, against this value, so a child left at null never lights anything
        /// it drew. Whatever routes the motion can only set what it holds, and what it holds is the
        /// composite, so the composite is what passes it down. Written on the way in rather than read on
        /// the way out, because a child reads its own copy mid-paint and there is nowhere to intercept
        /// that.
        /// </remarks>
        public override (float X, float Y)? Pointer
        {
            get => base.Pointer;
            set
            {
                base.Pointer = value;
                var children = Children;
                for (var i = 0; i < children.Count; i++)
                {
                    children[i].Pointer = value;
                }
            }
        }

        /// <summary>
        /// Appends the regions of the frame in paint order, back to front: the children under this widget's
        /// own content, its own, then the children over it (see <see cref="ChildrenUnderOwn"/>).
        /// </summary>
        /// <remarks>
        /// Back to front because the reader walks the list from the END: <see cref="InputRouter"/> asks the
        /// last region first, so this is what makes a routed press agree with <see cref="HitTest"/>. It used
        /// to list this widget's own first, so the router asked the children first, and a child's region
        /// swallowed the press aimed at an overlay the composite had painted over it.
        /// </remarks>
        public override void CollectPaintedRegions(List<ClickableRegion> into)
            => CollectInPaintOrder(into,
                static (child, list) => child.CollectPaintedRegions(list),
                static (self, list) => self.CollectOwnRegions(list));

        /// <summary>
        /// Appends the nodes of the frame in paint order, by the same walk as
        /// <see cref="CollectPaintedRegions"/>.
        /// </summary>
        /// <remarks>
        /// The router walks this list from the end too, for the hover highlight and for a declared shortcut,
        /// so one walk is what keeps a press, a lit row and a chord agreeing on what is on top.
        /// </remarks>
        public override void CollectPaintedNodes(List<Layout.ArrangedNode<float>> into)
            => CollectInPaintOrder(into,
                static (child, list) => child.CollectPaintedNodes(list),
                static (self, list) => self.CollectOwnNodes(list));

        private void CollectOwnRegions(List<ClickableRegion> into) => base.CollectPaintedRegions(into);

        private void CollectOwnNodes(List<Layout.ArrangedNode<float>> into) => base.CollectPaintedNodes(into);

        /// <summary>
        /// The one paint-order walk the region and node lists share: each child under this widget's own
        /// content, its own, then each child over it. Static delegates, so the pointer path allocates nothing.
        /// </summary>
        private void CollectInPaintOrder<T>(List<T> into,
            Action<PixelWidgetBase<TSurface>, List<T>> collect, Action<CompositeWidget<TSurface>, List<T>> collectOwn)
        {
            var (children, split) = Layers();
            for (var i = 0; i < split; i++)
            {
                collect(children[i], into);
            }

            collectOwn(this, into);

            for (var i = split; i < children.Count; i++)
            {
                collect(children[i], into);
            }
        }

        /// <summary>The children, and how many of them are under this widget's own content.</summary>
        private (IReadOnlyList<PixelWidgetBase<TSurface>> Children, int Split) Layers()
        {
            var children = Children;
            return (children, Math.Clamp(ChildrenUnderOwn, 0, children.Count));
        }

        /// <summary>
        /// Every text field painted this frame, this widget's and then its children's in paint order, which
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
        /// Every clickable region painted this frame, this widget's and its children's, in paint order (see
        /// <see cref="ChildrenUnderOwn"/>). For a debug inspector or an accessibility surface enumerating what
        /// is on screen.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="PixelWidgetBase{TSurface}.GetRegisteredRegions"/> rather than an
        /// override of it, because that one means "the regions I registered" and is what the hit tests
        /// above read per widget. Conflating the two would make a composite's own hit test walk its
        /// children twice.
        /// <para>
        /// The walk itself is <see cref="CollectPaintedRegions"/>'s, not a second copy of it: this is the
        /// snapshot form, for a reader that wants a list it can keep rather than one it fills.
        /// </para>
        /// </remarks>
        public IReadOnlyList<ClickableRegion> PaintedRegions()
        {
            var regions = new List<ClickableRegion>();
            CollectPaintedRegions(regions);
            return regions;
        }

        /// <summary>Asks the children in <c>[from, to)</c> front to back (the reverse of paint order) for the
        /// first non-null answer.</summary>
        private static T? FromChildren<T>(IReadOnlyList<PixelWidgetBase<TSurface>> children, int from, int to,
            Func<PixelWidgetBase<TSurface>, T?> ask) where T : class
        {
            for (var i = to - 1; i >= from; i--)
            {
                if (ask(children[i]) is { } answer)
                {
                    return answer;
                }
            }

            return null;
        }

        /// <summary>Value-typed counterpart of the above, for a query answering a struct.</summary>
        private static T? FromChildren<T>(IReadOnlyList<PixelWidgetBase<TSurface>> children, int from, int to,
            Func<PixelWidgetBase<TSurface>, T?> ask) where T : struct
        {
            for (var i = to - 1; i >= from; i--)
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
