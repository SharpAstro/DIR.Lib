using System;
using System.Collections.Generic;
using DIR.Lib;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests
{
    /// <summary>
    /// Damage between two painted frames: which rects a surface must repaint, so a mouse move stops
    /// costing a full-window repaint.
    /// </summary>
    /// <remarks>
    /// Pure geometry and equality, so all of it is pinnable offline -- which matters because the failure
    /// modes are invisible on screen in opposite directions. Too little damage leaves stale pixels that
    /// look like a rendering bug; too much silently gives back the whole saving while everything still
    /// LOOKS right, so nothing would ever fail.
    /// </remarks>
    public class LayoutDamageTests
    {
        private static readonly RGBAColor32 Plain = new(20, 20, 20, 255);
        private static readonly RGBAColor32 Lit = new(60, 60, 60, 255);

        [Fact]
        public void AnIdenticalFrameHasNoDamage()
        {
            // The case that has to be exactly zero, because it is the steady state: a frame drawn for
            // some other reason must not repaint anything it did not change.
            var frame = new[] { Text("Zoom 100%", 0, 0, 100, 20), Box(0, 20, 100, 40) };

            Damage(frame, frame).ShouldBeEmpty();
        }

        [Fact]
        public void ChangedTextDamagesOnlyItsOwnRect()
        {
            var before = new[] { Text("Stars", 0, 0, 100, 20), Text("HFR 3.7", 0, 30, 100, 20) };
            var after = new[] { Text("Stars: 5893", 0, 0, 100, 20), Text("HFR 3.7", 0, 30, 100, 20) };

            var damage = Damage(before, after);

            damage.Count.ShouldBe(1);
            damage[0].Y.ShouldBe(0f, "the unchanged second run must not be repainted");
        }

        /// <summary>
        /// Moving within a control changes nothing; crossing its edge changes two.
        /// </summary>
        /// <remarks>
        /// This is the behaviour the whole design is for, and it is not written anywhere as a rule -- it
        /// is what the diff says once hover is resolved into the signature.
        /// </remarks>
        [Fact]
        public void HoverDamagesOnlyOnTransitionNotOnMotion()
        {
            var tree = new[] { Hoverable(0, 0, 100, 40), Hoverable(0, 40, 100, 40) };

            // Two positions inside the SAME control: byte-identical resolved trees.
            Damage(tree, tree, (10f, 10f), (80f, 30f))
                .ShouldBeEmpty("moving inside one control must not repaint anything");

            // Across the boundary: the control being left and the one being entered.
            var crossing = Damage(tree, tree, (10f, 10f), (10f, 50f));
            crossing.Count.ShouldBe(2);

            // Entering from outside lights exactly one.
            Damage(tree, tree, null, (10f, 10f)).Count.ShouldBe(1);
        }

        /// <summary>
        /// A node that VANISHED damages its old rect, which is the tooltip case.
        /// </summary>
        /// <remarks>
        /// A dismissed tooltip changes nothing in the current frame, so a diff that only walked the
        /// current tree would report no damage and leave it painted on screen forever. Its damage is
        /// entirely historical, which is why the rule is a symmetric difference rather than a forward
        /// comparison. Dropdowns and the split divider are the same shape.
        /// </remarks>
        [Fact]
        public void ADismissedTooltipDamagesTheRectItUsedToOccupy()
        {
            var withTooltip = new[] { Text("Open", 0, 0, 60, 20), Text("Open a file", 200, 300, 120, 24) };
            var without = new[] { Text("Open", 0, 0, 60, 20) };

            var damage = Damage(withTooltip, without);

            damage.Count.ShouldBe(1);
            damage[0].X.ShouldBe(200f);
            damage[0].Y.ShouldBe(300f);
        }

        [Fact]
        public void AnAppearedNodeDamagesItsNewRect()
        {
            var without = new[] { Text("Open", 0, 0, 60, 20) };
            var withTooltip = new[] { Text("Open", 0, 0, 60, 20), Text("Open a file", 200, 300, 120, 24) };

            var damage = Damage(without, withTooltip);

            damage.Count.ShouldBe(1);
            damage[0].X.ShouldBe(200f);
        }

        [Fact]
        public void AMovedNodeDamagesBothWhereItWasAndWhereItIs()
        {
            // Both rects, or the trail it left behind is never cleaned up.
            var before = new[] { Text("row", 0, 0, 100, 20) };
            var after = new[] { Text("row", 0, 40, 100, 20) };

            var damage = Damage(before, after);

            damage.Count.ShouldBe(2);
            damage.ShouldContain(r => r.Y == 0f);
            damage.ShouldContain(r => r.Y == 40f);
        }

        /// <summary>
        /// A handler difference is not a paint difference.
        /// </summary>
        /// <remarks>
        /// Node is a record carrying an Action, records compare delegates by reference, and trees are
        /// rebuilt every frame with fresh lambdas -- so comparing nodes directly would report the entire
        /// UI damaged on every single frame and the feature would be a pure cost. This is the test that
        /// says the signature excludes handlers on purpose.
        /// </remarks>
        [Fact]
        public void AFreshLambdaEachFrameIsNotDamage()
        {
            var before = new[] { Clickable(0, 0, 100, 40, _ => { }) };
            var after = new[] { Clickable(0, 0, 100, 40, _ => { }) };

            before[0].Node.ShouldNotBe(after[0].Node, "the premise: the nodes themselves differ");
            Damage(before, after).ShouldBeEmpty("but nothing about them paints differently");
        }

        [Fact]
        public void SizingAndPaddingAreNotDamageBecauseTheArrangementAlreadyIs()
        {
            // Two nodes that would arrange differently but were arranged into the SAME rect: the inputs
            // to arrangement are not paint inputs, and Bounds already carries the outcome.
            var before = new[] { Arranged(Layout.Builder.Text("x").WFixed(50f), 0, 0, 100, 20) };
            var after = new[] { Arranged(Layout.Builder.Text("x").WStar(2f).Pad(4f), 0, 0, 100, 20) };

            Damage(before, after).ShouldBeEmpty();
        }

        [Fact]
        public void AFillIsDamagedOnlyWhenItsOwnerSaysSo()
        {
            // A painter callback owns those pixels, so the tree cannot see them change -- an image pane
            // whose content was replaced looks identical here.
            var frame = new[] { Fill("image", 0, 0, 800, 600) };

            Damage(frame, frame, null, null, fillChanged: null)
                .ShouldBeEmpty("with nobody reporting, an unchanged tree is unchanged");

            var reported = Damage(frame, frame, null, null, fillChanged: key => key == "image");
            reported.Count.ShouldBe(1);
            reported[0].Width.ShouldBe(800f);

            Damage(frame, frame, null, null, fillChanged: key => key == "histogram")
                .ShouldBeEmpty("a different fill reporting must not damage this one");
        }

        [Fact]
        public void CoalesceMergesOverlappingRectsAndLeavesDisjointOnesAlone()
        {
            // Overlapping scissored passes paint the intersection twice, which for anything with
            // transparency is wrong rather than merely wasteful.
            var rects = new List<Rect<float>>
            {
                new Rect<float>(0, 0, 100, 100),
                new Rect<float>(50, 50, 100, 100),
                new Rect<float>(500, 500, 10, 10),
            };

            LayoutDamage.Coalesce(rects);

            rects.Count.ShouldBe(2);
            rects.ShouldContain(r => r.X == 0 && r.Y == 0 && r.Width == 150 && r.Height == 150);
            rects.ShouldContain(r => r.X == 500);
        }

        [Fact]
        public void CoalesceMergesAChainThatOnlyConnectsThroughAMiddleRect()
        {
            // A and C do not touch; both touch B. One pass over the list would leave A and C separate,
            // so the merge has to repeat until nothing overlaps.
            var rects = new List<Rect<float>>
            {
                new Rect<float>(0, 0, 20, 20),
                new Rect<float>(200, 0, 20, 20),
                new Rect<float>(10, 0, 195, 20),
            };

            LayoutDamage.Coalesce(rects);

            rects.Count.ShouldBe(1);
            rects[0].Width.ShouldBe(220f);
        }

        // ---- helpers ----

        private static List<Rect<float>> Damage(
            IReadOnlyList<Layout.ArrangedNode<float>> before,
            IReadOnlyList<Layout.ArrangedNode<float>> after,
            (float X, float Y)? beforePointer = null,
            (float X, float Y)? afterPointer = null,
            Func<string?, bool>? fillChanged = null)
        {
            var damage = new List<Rect<float>>();
            LayoutDamage.Compute(before, after, beforePointer, afterPointer, fillChanged, damage);
            return damage;
        }

        private static Layout.ArrangedNode<float> Arranged(Layout.Node node,
            float x, float y, float w, float h)
            => new Layout.ArrangedNode<float>(node, new Rect<float>(x, y, w, h));

        private static Layout.ArrangedNode<float> Text(string value, float x, float y, float w, float h)
            => Arranged(Layout.Builder.Text(value), x, y, w, h);

        private static Layout.ArrangedNode<float> Box(float x, float y, float w, float h)
            => Arranged(Layout.Builder.Box(w, h), x, y, w, h);

        private static Layout.ArrangedNode<float> Fill(string key, float x, float y, float w, float h)
            => Arranged(Layout.Builder.Fill(key: key), x, y, w, h);

        private static Layout.ArrangedNode<float> Hoverable(float x, float y, float w, float h)
            => Arranged(Layout.Builder.Text("btn").Bg(Plain) with { HoverBackground = Lit }, x, y, w, h);

        private static Layout.ArrangedNode<float> Clickable(float x, float y, float w, float h,
            Action<InputModifier> onClick)
            => Arranged(Layout.Builder.Text("btn") with { OnClick = onClick }, x, y, w, h);
    }
}
