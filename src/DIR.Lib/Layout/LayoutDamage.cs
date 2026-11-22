using System;
using System.Collections.Generic;

namespace DIR.Lib
{
    /// <summary>
    /// Which rects changed between two painted frames, so a surface can repaint those instead of
    /// everything.
    /// </summary>
    /// <remarks>
    /// <para>The arranged layout tree is the pixel surface's counterpart to Console.Lib's
    /// <c>CellBuffer</c>, which already paints by diffing -- a clock tick there emits ONE cell rather
    /// than repainting the row. This is the same idea one surface over, and it is what stops a mouse
    /// move costing a full-window repaint.</para>
    /// <para><b>Damage is the SYMMETRIC DIFFERENCE of the two frames' paint signatures</b>, taking the
    /// bounds of every entry present in one frame and not the other. Stated that way it is
    /// order-independent and gets three cases right that walking the current frame index by index does
    /// not: a node that MOVED contributes both its old and its new bounds; a node that APPEARED
    /// contributes its new bounds; and a node that VANISHED contributes its old bounds. That last one is
    /// the tooltip case, and it is the one that bites -- a dismissed tooltip changes nothing in the
    /// current tree, so a diff that only looked forward would leave it painted on screen forever. The
    /// same goes for dropdowns and the split divider.</para>
    /// <para>Two consequences are the entire point. Moving INSIDE a button produces a byte-identical
    /// tree, so damage is empty and nothing repaints; crossing its boundary flips two nodes' resolved
    /// background, so damage is those two rects. "Repaint on transition, not on motion" is not a case
    /// anyone writes -- it is what the diff says. And the redraw GATE becomes derivable: empty damage
    /// means do not render, replacing a hand-maintained predicate of the kind that produced a
    /// full-window repaint for a pixel that was not visible.</para>
    /// </remarks>
    public static class LayoutDamage
    {
        /// <summary>
        /// A node's identity AS PAINTED. Two nodes with equal signatures and equal bounds produce
        /// identical pixels, so neither needs repainting.
        /// </summary>
        /// <remarks>
        /// <para><b>It cannot be the node itself.</b> <see cref="Layout.Node"/> is a record carrying
        /// <see cref="Layout.Node.OnClick"/>, an <c>Action</c>, and records compare delegates by
        /// REFERENCE -- while trees are rebuilt every frame with fresh lambdas. So every clickable node
        /// would compare unequal every frame and the diff would report the whole UI damaged, always.
        /// Excluding handlers is not a shortcut: two nodes differing only in which lambda they would
        /// invoke are pixel-identical. Sizing, padding, alignment and the collapse threshold are excluded
        /// for the same reason -- they decide the arrangement, and the arrangement is already here as
        /// <see cref="Bounds"/>.</para>
        /// <para><b>The background is the RESOLVED one.</b> <see cref="Layout.Node.HoverBackground"/> is
        /// chosen at paint time against the widget's pointer, so a hovered and an unhovered node have
        /// identical DECLARED properties. A signature over declared properties alone reports no damage on
        /// a hover transition, and highlights silently stop appearing.</para>
        /// </remarks>
        public readonly record struct PaintSignature(
            Rect<float> Bounds,
            RGBAColor32? Background,
            float CornerRadius,
            ContentShape Shape,
            string? Text,
            string? Aux,
            float Size,
            RGBAColor32 Color,
            int Discriminator,
            int Caret,
            int SelectionAnchor)
        {
            /// <summary>The node's border, or null. Init-only, set in <see cref="Signature"/>, so the positional
            /// constructor keeps its arity (a new positional parameter would be a binary break). An unbordered
            /// node leaves it null, so its signature is what it was before borders existed.</summary>
            public Layout.BorderStyle? Border { get; init; }

            /// <summary>The node's shadow, or null. Init-only for the same reason as <see cref="Border"/>.
            /// <see cref="Bounds"/> stays the node's rect; the damage a signature reports grows by this shadow's
            /// extent (see <see cref="LayoutDamage.DamageRect"/>).</summary>
            public Layout.ShadowStyle? Shadow { get; init; }
        }

        /// <summary>
        /// The rect a signature repaints: its bounds, grown on every side by its shadow's extent. The unit is the bounds':
        /// the shadow's extent is in design units, so the bounds must be in design units too (a surface at a
        /// <c>DesignScale</c> other than 1 must pass bounds it has divided by that scale, or grow the result itself).
        /// </summary>
        public static Rect<float> DamageRect(in PaintSignature sig)
        {
            if (sig.Shadow is not { Color.Alpha: > 0 } s)
            {
                return sig.Bounds;
            }

            // One pixel more than the extent: the default shadow reaches n + 1 pixels out, and rounding of the offset.
            var g = MathF.Ceiling(s.Extent) + 1f;
            var b = sig.Bounds;
            return new Rect<float>(b.X - g, b.Y - g, b.Width + 2f * g, b.Height + 2f * g);
        }

        /// <summary>Which kind of content a signature describes.</summary>
        public enum ContentShape
        {
            /// <summary>A container: only its background and corners paint.</summary>
            Container = 0,
            Text,
            Box,
            Icon,
            TextInput,

            /// <summary>
            /// A painter callback owns these pixels, so the tree cannot see them change. Compared like
            /// any other node (its rect and background still matter), but its CONTENT is invisible here,
            /// which is why <see cref="Compute"/> takes a <c>fillChanged</c> predicate: the only thing
            /// that knows whether an image pane or a histogram moved is whatever draws it.
            /// </summary>
            Fill,

            /// <summary>A multi-line editor: a field's paint plus its scroll. Last, so no existing value moves.</summary>
            TextEditor,
        }

        /// <summary>Whether a pointer is inside an arranged rect: top and left inclusive, bottom and
        /// right exclusive. The rule the hover resolution uses, shared so the two cannot drift.</summary>
        public static bool Contains(in Rect<float> r, (float X, float Y)? pointer)
            => pointer is { } p && p.X >= r.X && p.X < r.X + r.Width && p.Y >= r.Y && p.Y < r.Y + r.Height;

        /// <summary>The signature of one arranged node, resolving hover against <paramref name="pointer"/>.</summary>
        public static PaintSignature Signature(in Layout.ArrangedNode<float> node, (float X, float Y)? pointer)
            => Signature(node, pointer, null);

        /// <summary>The signature of one arranged node, resolving hover against <paramref name="pointer"/> and
        /// <see cref="Layout.Node.PressedBackground"/> against <paramref name="pressOrigin"/> (where the primary
        /// button is held down, or null). A hovered underlined text run signs differently from a resting one.</summary>
        public static PaintSignature Signature(in Layout.ArrangedNode<float> node, (float X, float Y)? pointer,
            (float X, float Y)? pressOrigin)
            => Signature(node, pointer, pressOrigin, node.Bounds);

        /// <summary>As the public overload, with the hover underline resolved against <paramref name="underlineScope"/>:
        /// the rect of the nearest node with a hit that holds this one (itself included), as the painter resolves it.</summary>
        private static PaintSignature Signature(in Layout.ArrangedNode<float> node, (float X, float Y)? pointer,
            (float X, float Y)? pressOrigin, in Rect<float> underlineScope)
        {
            var n = node.Node;
            var bounds = node.Bounds;

            var background = n.PressedBackground is { } pressed && !n.IsDisabled
                    && Contains(bounds, pointer) && Contains(bounds, pressOrigin) ? pressed
                : n.HoverBackground is { } hover && !n.IsDisabled && Contains(bounds, pointer) ? hover
                : n.Background;

            var signature = n is Layout.Node.Leaf { Content: { } content }
                ? SignatureOfContent(content, bounds, background, n.CornerRadius)
                : new PaintSignature(bounds, background, n.CornerRadius, ContentShape.Container,
                    null, null, 0f, default, 0, 0, 0);
            if (n is Layout.Node.Leaf { Content: Layout.Content.Text { UnderlineOnHover: true } }
                && !n.IsDisabled && Contains(underlineScope, pointer))
            {
                // Caret is unused by a text run: it carries "underline showing".
                signature = signature with { Caret = 1 };
            }

            if (n.BorderStyle is { } border)
            {
                signature = signature with { Border = border };
            }

            return n.ShadowStyle is { } shadow ? signature with { Shadow = shadow } : signature;
        }

        private static PaintSignature SignatureOfContent(Layout.Content content, in Rect<float> bounds,
            RGBAColor32? background, float cornerRadius) => content switch
            {
                Layout.Content.Text t => new PaintSignature(bounds, background, cornerRadius,
                    ContentShape.Text, t.Value, t.WidthSample, t.FontSize, t.Color,
                    // The alignments and the trim decide WHERE the run lands and how much of it
                    // survives, so two otherwise-identical runs can paint differently. WidthSample is
                    // in Aux because it changes the measured box, hence where a centred run sits.
                    ((int)t.HAlign << 8) | ((int)t.VAlign << 4) | (int)t.Trim, 0, 0),

                Layout.Content.Box b => new PaintSignature(bounds, background, cornerRadius,
                    ContentShape.Box, null, null, b.Width, b.Color, (int)b.Height, 0, 0),

                Layout.Content.Icon i => new PaintSignature(bounds, background, cornerRadius,
                    ContentShape.Icon, null, null, i.Size, i.Color, (int)i.Kind, 0, 0),

                // TextInputState is a MUTABLE reference, so record equality sees nothing at all when
                // the user types, moves the caret or drags a selection. Everything painted has to be
                // extracted BY VALUE, and there is more of it than there looks: the caret position and
                // the selection anchor both paint, the IME composition run paints over the text, and
                // the placeholder paints in its place while the field is empty. Miss one and that one
                // stops updating on screen while everything around it works.
                Layout.Content.TextInput ti => new PaintSignature(bounds, background, cornerRadius,
                    ContentShape.TextInput, ti.State.Text, SecondaryText(ti.State), ti.FontSize, default,
                    ti.State.IsActive ? 1 : 0, ti.State.CursorPos, ti.State.SelectionAnchor),

                // The editor paints everything the field does, and one more thing the field cannot have: how far its
                // text is scrolled. That is a float on the state; folded into the discriminator beside the focus
                // bit as whole pixels, because a sub-pixel move paints nothing.
                Layout.Content.TextEditor te => new PaintSignature(bounds, background, cornerRadius,
                    ContentShape.TextEditor, te.State.Text, SecondaryText(te.State), te.FontSize, default,
                    ((int)MathF.Round(te.State.ScrollOffsetY) << 1) | (te.State.IsActive ? 1 : 0),
                    te.State.CursorPos, te.State.SelectionAnchor),

                Layout.Content.Fill f => new PaintSignature(bounds, background, cornerRadius,
                    ContentShape.Fill, f.Key, null, 0f, default, 0, 0, 0),

                _ => new PaintSignature(bounds, background, cornerRadius, ContentShape.Container,
                    null, null, 0f, default, 0, 0, 0),
            };

        /// <summary>
        /// The second string a field paints: the IME composition run while one is in flight,
        /// otherwise the placeholder while the field is empty. Never both -- a composition replaces
        /// what is shown, and a field with a composition is not empty.
        /// </summary>
        private static string? SecondaryText(TextInputState state)
            => state.Composition.Length > 0 ? state.Composition
            : state.Text.Length == 0 ? state.Placeholder
            : null;

        /// <summary>
        /// Appends the rects that differ between <paramref name="previous"/> and
        /// <paramref name="current"/> to <paramref name="damage"/>.
        /// </summary>
        /// <param name="fillChanged">
        /// Asked once per <see cref="ContentShape.Fill"/> leaf in the current frame, with the fill's key:
        /// true damages that leaf's rect. A painter callback's pixels are invisible to the tree, so this
        /// is the only way an image pane or a histogram can say it changed. Null means no fill ever
        /// changed, which is right for a surface with none and wrong for a viewer -- pass one.
        /// </param>
        public static void Compute(
            IReadOnlyList<Layout.ArrangedNode<float>> previous,
            IReadOnlyList<Layout.ArrangedNode<float>> current,
            (float X, float Y)? previousPointer,
            (float X, float Y)? currentPointer,
            Func<string?, bool>? fillChanged,
            List<Rect<float>> damage)
            => Compute(previous, current, previousPointer, currentPointer, null, null, fillChanged, damage);

        /// <summary>
        /// The signatures of a frame's nodes in paint (pre-)order. A hover underline answers to the nearest enclosing node
        /// with a hit (a link's whole region, its arrow included), tracked by depth as the painter does.
        /// </summary>
        private static HashSet<PaintSignature> SignAll(IReadOnlyList<Layout.ArrangedNode<float>> frame,
            (float X, float Y)? pointer, (float X, float Y)? press)
        {
            var signatures = new HashSet<PaintSignature>(frame.Count);
            var scopes = new Stack<(int Depth, Rect<float> Bounds)>();
            for (var i = 0; i < frame.Count; i++)
            {
                var node = frame[i];
                while (scopes.Count > 0 && scopes.Peek().Depth >= node.Depth)
                {
                    scopes.Pop();
                }

                if (node.Node.Hit is not null)
                {
                    scopes.Push((node.Depth, node.Bounds));
                }

                signatures.Add(Signature(node, pointer, press, scopes.Count > 0 ? scopes.Peek().Bounds : node.Bounds));
            }

            return signatures;
        }

        /// <summary>As the shorter overload, also resolving the pressed fill against where the button was held
        /// down before and now (<see cref="WindowUiSettings.PressOrigin"/>).</summary>
        public static void Compute(
            IReadOnlyList<Layout.ArrangedNode<float>> previous,
            IReadOnlyList<Layout.ArrangedNode<float>> current,
            (float X, float Y)? previousPointer,
            (float X, float Y)? currentPointer,
            (float X, float Y)? previousPress,
            (float X, float Y)? currentPress,
            Func<string?, bool>? fillChanged,
            List<Rect<float>> damage)
        {
            ArgumentNullException.ThrowIfNull(previous);
            ArgumentNullException.ThrowIfNull(current);
            ArgumentNullException.ThrowIfNull(damage);

            var before = SignAll(previous, previousPointer, previousPress);
            var after = SignAll(current, currentPointer, currentPress);

            // Deduplicated by RECT, because a node that changed in place appears in both halves of
            // the symmetric difference -- once as the old signature vanishing and once as the new one
            // arriving -- at the same bounds. Emitting it twice would scissor two passes over the same
            // pixels, which for anything with transparency paints it twice rather than merely costing
            // twice. This is the common case, not an edge one: changed text is exactly it.
            var seen = new HashSet<Rect<float>>();

            // Present now but not before: appeared, moved here, or repainted differently.
            foreach (var sig in after)
            {
                if (!before.Contains(sig) && DamageRect(sig) is var rect && seen.Add(rect))
                {
                    damage.Add(rect);
                }
            }

            // Present before but not now: vanished, or moved away. The tooltip case.
            foreach (var sig in before)
            {
                if (!after.Contains(sig) && DamageRect(sig) is var rect && seen.Add(rect))
                {
                    damage.Add(rect);
                }
            }

            if (fillChanged is null)
            {
                return;
            }

            // Fills last, and against the CURRENT frame only: a fill that vanished is already covered
            // above by its signature disappearing.
            for (var i = 0; i < current.Count; i++)
            {
                if (current[i].Node is Layout.Node.Leaf { Content: Layout.Content.Fill fill }
                    && fillChanged(fill.Key)
                    && seen.Add(current[i].Bounds))
                {
                    damage.Add(current[i].Bounds);
                }
            }
        }

        /// <summary>
        /// Merges overlapping rects in place, so a surface sets fewer scissors. Repeats until no pair
        /// overlaps, which terminates because every merge removes one rect.
        /// </summary>
        /// <remarks>
        /// Merging is not just tidiness: two overlapping scissored passes paint the intersection twice,
        /// and for anything with transparency that is visibly wrong, not merely wasteful.
        /// </remarks>
        public static void Coalesce(List<Rect<float>> rects)
        {
            ArgumentNullException.ThrowIfNull(rects);

            var merged = true;
            while (merged)
            {
                merged = false;
                for (var i = 0; i < rects.Count && !merged; i++)
                {
                    for (var k = i + 1; k < rects.Count; k++)
                    {
                        if (!Overlaps(rects[i], rects[k]))
                        {
                            continue;
                        }

                        rects[i] = Union(rects[i], rects[k]);
                        rects.RemoveAt(k);
                        merged = true;
                        break;
                    }
                }
            }
        }

        private static bool Overlaps(in Rect<float> a, in Rect<float> b)
            => a.X < b.X + b.Width && b.X < a.X + a.Width
            && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

        private static Rect<float> Union(in Rect<float> a, in Rect<float> b)
        {
            var x = MathF.Min(a.X, b.X);
            var y = MathF.Min(a.Y, b.Y);
            var right = MathF.Max(a.X + a.Width, b.X + b.Width);
            var bottom = MathF.Max(a.Y + a.Height, b.Y + b.Height);
            return new Rect<float>(x, y, right - x, bottom - y);
        }
    }
}
