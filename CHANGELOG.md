# Changelog

Release notes for DIR.Lib and DIR.Lib.Shaping, one entry per `Major.Minor`, newest first.

The version NUMBER is not here: it lives in `src/Directory.Build.props` (`VersionMajorMinor`), and the
build job reads that property back rather than restating it, so a package can never declare a version
this file disagrees with. Bump it there and add the entry here, in the same commit.

Breaking changes carry their migration steps in [MIGRATION.md](MIGRATION.md); this file says what
changed and why.

## 8.11

`IconKind.Search` -- a lens with a handle -- and `Content.TextInput.LeadingIcon`, which draws a mark
inside a field at its leading edge with the text starting after it.

The kind earns its place the way `Plus` did, by consumers already drawing their own: one had
hand-rolled a lens from an ellipse and a line for a marquee-zoom tool and wanted a second for the
field its search bar is built around. On a cell surface the shape is one of the few pictograms a
terminal font is genuinely relied on to carry (U+1F50D, with U+2315 for a narrow cell).

It is the one kind whose weight comes from a **pen** rather than from its coverage, because a filled
blob at chip size is a dot with a stalk and the ring is the whole reading. It is also the only mark in
the family that meets its bounding box on a diagonal -- the ring's arc up-left, the handle's tip
down-right. That cost a fix on the way in: the handle's far end has to be offset by the full extent on
each axis, not along the diagonal, or it reaches only 0.71 of the way and the mark sits in the middle
of a square of nothing.

`LeadingIcon` belongs to the FIELD rather than being a sibling in the row, because a field paints its
own background and border: a mark placed beside it lands outside the box and reads as a button next to
an input. Inside, the box still spans the whole rect and only the text is inset, so `textX`/`textW` are
the only two things that move -- the caret, the selection, the preedit and the hit region all follow
from them already.

The room it needs is stated once, as `TextInputRenderer.LeadingRoom`, for exactly the reason
`HorizontalPadding` is: the measure pass has to reserve it and the paint has to leave it, and a literal
in each is the shape where a later tweak to one silently mis-sizes the other. A field with no mark
reserves nothing and paints byte-identically to before.

The mark takes a **cap height** (`TextInputRenderer.LeadingIconRatio`), not the x-height 8.10 gave an
unsized icon beside a run, and the difference is the point: a caret next to a label MODIFIES that
label -- it is punctuation on a phrase, and matching the lowercase body is what makes it read as part
of it -- where a field's leading mark is a PEER of the text, the first thing looked at and the thing
that says the box is a query box at all. Built at the x-height first, on the reasoning that one ratio
is tidier; at that size it measures correct and reads as a speck. Cap height is safe here where it
would not be for a filled caret, because the kinds that make sense as a field affordance are outlined,
and a ring weighs far less than a solid triangle of the same box.

The painter draws the mark, not the renderer: `TextInputRenderer` is static and has no icon drawing,
so it only leaves the room and the widget that owns `DrawLayoutIcon` fills it.

## 8.10

An icon sizes itself to the text it sits beside. `Builder.Icon(kind, color: c)` -- no size -- puts the
mark at `Content.Icon.TextSizeRatio` of the font size of the run in the same container, and records
that on the node as `Content.Icon.MatchesText`. A stated size still wins, and a container with no text
in it at all falls back to `Content.Icon.DefaultSize`, so this is additive: every existing call site
is unchanged, and `Content.Icon.Size` is still one concrete number that every painter reads.

A caret in a row next to a label is sized BY that label, and stating it separately is two copies of
one decision. In a consumer with three such chips, all three passed the same hand-written expression,
and the number had to be looked up from a neighbouring file to write the third -- a mark whose size
drifts from its own text looks wrong and warns about nothing. The fallback made a poor default too:
`DefaultSize` is a bare constant, so a tree authored in device pixels (a consumer that hands
`RenderLayout` a `dpiScale` of 1 and pre-multiplies its own constants) got a mark that scaled with
nothing. Derived from the sibling run the size is in whatever unit that run is, which is right under
either convention without the library being told which one is in force.

The ratio is an **x-height**, not a cap height: a kind inks the full square it declares where a glyph
inks perhaps 80% of its em box, so a mark at cap height reads heavier than the letters beside it.
0.54 is Noto Sans's x-height and also where those three chips had independently landed by hand.
It is a constant rather than a question put to the bound font because the resolution happens while
the tree is being built -- before any font is bound -- and because a cell surface has no x-height to
report.

Resolved in `Builder`'s container factories rather than in the engine or a painter, and that is the
load-bearing part: arrange emits a FLAT pre-order list, so by the time a painter sees a node it has
no parent and no siblings in reach. A size resolved during the walk would have to be resolved again
by everything else that reads the tree. Resolved at construction it is simply a number in the node,
which is also why `describe_layout` prints what will be drawn instead of a sentinel. It reaches an
icon that is a direct child of the container holding the run; the run itself may be nested as deep as
it likes, since the idioms that wrap one (a padded label is a one-child stack) would otherwise put it
out of reach.

## 8.9

`TextTrim.Middle` and `TextFit.TrimMiddleToWidth`: drop the MIDDLE of a run that does not fit,
ellipsis in the middle, keeping both ends.

The case neither `Start` nor `End` covers, and a path is the canonical one: the root says which
volume or install this is (a Store package under `WindowsApps`, a dev build under `bin\Debug`) and
the leaf says which file, while the dozen directories between them are what a reader skips.
Start-trimming keeps the leaf and throws away the half identifying the install; end-trimming does the
reverse. A diagnostic panel listing where an app is installed and where it searched for its models
needs both ends of every line, which is where this came from -- it existed as a private helper inside
a consumer, which is the same two-copies-of-a-rule mistake the slider primitives had to be walked
back from.

A cell surface honours it exactly as a pixel surface does, unlike `Shrink`: it is a character-count
cut, not a scale, so there is nothing to degrade. Console.Lib's `CellLayout` implements the same cut.

`TrimMiddleToWidth` binary-searches the kept-end length rather than walking down from the full run as
the Start/End case does, because this policy is called from per-FRAME paint paths on runs that can be
several times too wide; halving is O(log n) either way where the walk is O(n) when a run badly
overflows. Measured width is not linear in character count on a proportional face, so the search is
necessary and arithmetic would over- or under-shoot.

## 8.8

`LayoutDamage` answers which rects differ between two painted frames, so a surface can repaint those
instead of everything. The arranged layout tree is the pixel counterpart to Console.Lib's `CellBuffer`,
which already paints by diffing -- a clock tick there emits ONE cell rather than repainting the row.
The measurement that motivated it, from a consumer: repainting a whole window to change one number in
a status bar costs 8% GPU on an Adreno X1-85 over a 4 Mpix pane, and without damage the only two
states available are that and zero.

Damage is the SYMMETRIC DIFFERENCE of the two frames' paint signatures, which gets three cases right
that walking the current frame does not: a node that moved contributes both its old and new bounds,
one that appeared contributes its new, and one that VANISHED contributes its old. That last is the
tooltip case, and it is the one that bites -- a dismissed tooltip changes nothing in the current tree,
so a forward-only diff reports no damage and leaves it painted forever.

Two consequences fall out rather than being written: moving INSIDE a button yields a byte-identical
tree so nothing repaints, while crossing its edge damages exactly two rects; and the redraw gate
becomes derivable, since empty damage means do not render.

`PaintSignature` deliberately excludes handlers. `Node` is a record carrying `OnClick`, records compare
delegates by reference, and trees are rebuilt every frame with fresh lambdas -- so comparing nodes
would report the whole UI damaged on every frame. Two nodes differing only in which lambda they would
invoke are pixel-identical, and sizing, padding and alignment are excluded for the same reason: they
decide the arrangement, and the arrangement is already present as `Bounds`. The background it carries
is the RESOLVED one, because `HoverBackground` is chosen at paint time and a signature over declared
properties alone reports no damage on a hover transition -- highlights would silently stop appearing.
A `TextInput` leaf extracts its state by value (caret, selection anchor, the IME composition run that
paints over the text, and the placeholder that paints in its place), since `TextInputState` is a
mutable reference that record equality cannot see into. A `Fill` is opaque -- a painter callback owns
those pixels -- so `Compute` takes a `fillChanged` predicate.

Damage is deduplicated by rect: a node changing in place appears in BOTH halves of the symmetric
difference at the same bounds, and emitting it twice scissors two passes over the same pixels, which
for anything with transparency paints it twice rather than merely costing twice. Changed text is
exactly that case, so it is the common one, not an edge.

Layout capture is now UNCONDITIONAL, which is the one behaviour change here. It was gated on
`LayoutInspection.Enabled` for "zero overhead in production", and that cannot stand once the tree is
what damage diffs against. The cost is one list of structs per widget per frame, accepted deliberately
against the 8% above. `LayoutInspection` survives as an obsolete no-op rather than being deleted,
because removing a public type would mean a major bump and a re-pin across four repos for a field
nothing reads; delete it at the next major.

## 8.7

`FontResolver.ResolveEmojiFont` resolves the platform's colour-emoji face, a third role beside the
monospace default (`ResolveSystemFont`) and the per-script chain (`ResolveSystemScriptFonts`). Same shape
as both: an `extra` list is consulted first so a caller can prefer its own bundled asset, and the result is
an existing path or `""`, never a path that does not exist.

It belongs here for the reason the other two roles do -- "where does this platform keep its emoji font" is
a property of the platform, not of any one app. Held privately by a consumer it gets copied: TianWen kept
these tables in its own UI layer and had grown a second copy in a second renderer.

Pair it with `FontFallbackResolver.CanRender` before committing a UI to an emoji mark. An unavailable glyph
draws NOTHING rather than a placeholder, so a control whose only mark is an emoji loses it silently.

## 8.6

`IconBaker` scales a glyph to fit its mask instead of truncating it. The size passed to `Bake` is an em
size, and nothing obliges a glyph to keep its ink inside its em box: every emoji in Noto's COLRv1 face
overruns it by about 15%, and so do the block elements in an ordinary text face. The baker centred that
oversized raster in the square and dropped whatever fell outside, so every baked mark lost one to two
pixels off each edge.

The loss was invisible on marks whose extremes are thin -- a sparkle shed a tip nobody could name -- and
obvious on one bounded by a curve, where a circle acquires a flat top and bottom. A baked globe is what
gave it away. `Bake` now shrinks its request by the overflow ratio and re-measures until the ink fits,
which converges in one or two passes; the bounds checks around the emit remain as a backstop for a face
that defeats the attempt cap.

Baked output therefore CHANGES for any glyph that was overflowing: marks get their full shape at slightly
smaller ink. Re-bake and commit any generated tables.

## 8.5

Icons can be baked from a font glyph. `IconBaker` turns a glyph into horizontal runs of constant
coverage, and `PixelWidgetBase.DrawCoverageMask` paints one as a tinted mark.

Three things that buys over drawing the glyph as text at runtime, each of which the text path costs.
It works where the face is not installed -- an app bundling no emoji font resolves none on a typical
Linux host, and a missing glyph draws NOTHING rather than a placeholder, so a button's only mark
silently disappears. It is MONOCHROME, so it takes the ink colour and dims with the label beside it,
which a COLRv1 colour glyph carrying its own palette structurally cannot. And it is identical
everywhere, where the text path varies with whichever face the host happened to resolve.

The baking is a LIBRARY API rather than only a build step, because a build-time bake cannot serve
every case: a theme that turns the whole UI one colour (a night / dark-adaptation mode) wants its
normally-full-colour emoji as tintable coverage, and which emoji those are is not known until the app
draws them. Runtime callers should cache per (codepoint, size). `ManagedFontRasterizer` is pure
managed, so the same font file and inputs give byte-identical output on any host -- which is what lets
a build pipeline VERIFY a generated file rather than trust it.

Runs rather than a bitmap, so drawing is a loop of rectangle fills and needs no new primitive on the
renderer seam -- the same reason `IconKind`'s pixel painter is built from rectangles. A 20px mark is
around 160 runs.

Two details in `DrawCoverageMask` are load-bearing. A run's coverage MODULATES the ink's alpha rather
than replacing it, which is what makes a baked mark dim exactly as its label does. And row and column
edges are snapped to whole pixels rather than passed through as floats, because `FillRect` truncates
its rect to int: a scaled row of height 0.97 rounds to nothing and leaves gaps through the mark.
Snapping each edge and taking the difference makes consecutive rows tile by construction, at any
scale.

A test caught a real bug in the quantisation. The obvious `alpha * levels / 256` maps to
`0..levels-1`, so the faintest bucket of every antialiased edge was discarded as uncovered and marks
came out slightly thin, invisibly; at one level EVERY pixel landed in bucket 0 and the whole mark
disappeared. The top bucket is also a full 255 rather than a bucket centre, which is not cosmetic --
an interior pixel is fully covered, and 223 makes the whole mark read visibly greyer than the text
beside it.

**New tool: `DIR.Lib.IconBaker`**, consumed via `dnx DIR.Lib.IconBaker`. A thin wrapper that owns the
generated-file format and the argument parsing while `IconBaker` owns the baking. It ships from this
repo rather than from a consumer so that a second app with icons to bake takes it off the feed instead
of vendoring a copy of the generator. Everything about WHICH glyphs is an argument.

    bake-icons --font <path> --out <file.g.cs> --namespace <ns> --glyph Name=U+1F300
               [--class BakedIcons] [--sizes 13,16,20] [--levels 4] [--access internal]

Bake the DPI scales that matter, not round numbers. A run is a row of PIXELS, so scaling a mask either
overlaps rows or opens gaps between them; `IconBaker.NearestSize` picks the closest bake so the
residual scale stays near 1. The default ladder is one size per common DPI scale for a 13-unit mark,
which keeps every one of them within about 2%; a ladder of pretty numbers left 1.75x resampling by 14%
and 2.5x by 25%. Each extra size costs around 200 runs, so closing those gaps is nearly free.

Baking redistributes GLYPH SHAPES, so point the tool at a face whose licence permits it (Noto is OFL).
A proprietary system face is not a valid bake source, and a runtime emoji probe legitimately falling
back to one does not make its outlines redistributable.

## 8.4

BackgroundTaskTracker grows two shapes it could not hold. `Run` and `RunGuarded` fit work that is
started and forgotten; what did not fit was work that is SUPERSEDED, and work that returns a VALUE.
Every consumer had hand-rolled both in a private field.

`RunExclusive(key, ...)` is the first: starting work under a key cancels whatever was running under
it. That is the right model when the old result is not merely unwanted but about to be WRONG -- a
second file opened while the first is still loading, star detection restarted because the image was
replaced. The token is linked to the caller's own, so shutdown cancels it too and no call site
composes that itself. `IsRunning` and `Cancel` answer for a key, and `DrainAsync` now cancels keyed
work before awaiting it, so a shutdown neither sits through a load nobody is waiting for nor
abandons work still touching state.

`RunExclusive<TResult>` plus `TryCollect<TResult>` is the second. The result is PULLED by the
consumer rather than pushed into a callback, so it is adopted on whichever thread is entitled to
adopt it -- for a UI that is the render thread, on the frame of its choosing. A callback would
deliver it on the pool, which is exactly where a render-thread-owned field must not be written.
`TryCollect` retires the slot whether or not there was a result: a run that cancelled or threw has
nothing to hand over, and an occupied slot would wedge the key against its next use. Reference types
only, because for a value type the "nothing to report" answer would be `Nullable<T>`, a different
runtime type from the one the work produced, and `TryCollect` would stop recognising it. A
superseded slot is cancelled but not DISPOSED until its task actually ends -- the outgoing work
still holds the token, and disposing the source out from under it turns an orderly supersede into an
`ObjectDisposedException`.

`DockLayout<T>.Dock` clamps to what is left. It clamped nothing, so an over-large requested extent
did two invisible things at once: a Right strip resolves its x as `Right - size`, so it walked LEFT
past the container origin and painted over its own siblings, and the fill rect was handed the
negative leftover. Neither half reads as "does not fit" at the call site, which is why it survived --
everything still looks drawn. Measured in a FITS viewer whose info panel is a Right strip inside the
second pane of a Split: at surface width 733 the panel arranged at x=283 w=450 inside a parent
starting at 459, straight over the split divider, and the image pane came out w=-176. The divider
painted, stated a resize cursor, and could not be pressed at all, which reads as a dead handle
rather than as a layout overrun.

`CompositeWidget<TSurface>` lets a widget that paints OTHER widgets state its children once, in
paint order, with `HitTest`, `HitTestAndDispatch`, `HitTestCursor`, `GetRegisteredTextInputs` and the
new `PaintedRegions` all derived from that one statement. A child's regions live on the CHILD, so a
host asking only the composite missed every control its children registered: nothing throws, the
pixels are right, and the controls simply stop answering.

The tab strip becomes a Layout tree. `TabStripTree.Build` describes it as `Layout.Node`,
`TabStripMetrics` carries what differs between surfaces (pixels vs whole cells), and two policies
carry the rest -- `TabStripOverflow { Clip, Drop }` and `TabLabelDecoration`. Drop exists because a
clipped tab leaves a region that is hit but not VISIBLE, so a press lands on something the reader
cannot see; decoration exists because a terminal's active plate is a bet on the reader's palette, and
on a monochrome one the brackets are all that says which tab is active. `TabBar` now paints that
shared tree, its 69 geometry tests passing unchanged.

`TabBarColors.HoverBackground` (nullable, null meaning `ActiveBackground`) adds a third plate tone,
for the case the old reasoning did not survive: a strip drawing NO accent renders hover and active
identically and stops being able to say which tab a click would take you to. `CanCloseTabs` and
`CanReorderTabs` both default true; closing off also stops RESERVING the box, so tabs are narrower by
it rather than holding a gap where the control would have been.

`IconKind.Plus` and `IconKind.Minus`, because a stepper is `[-] value [+]` and a terminal has plenty
of those. Both are built from rectangles: a typeset `+` is drawn by whichever face the host resolved,
at that face's stroke weight, sitting on the text baseline rather than centred in its box.

## 8.3

The strip attaches to any edge, and a nav rail is the same widget. TabStripSide { Top, Bottom, Left,
Right } with orientation DERIVED from it rather than set beside it -- a separate Orientation property
would let a caller state a vertical strip on the top edge, a combination with no meaning that the bar
would then have to resolve by silently ignoring half of what it was told. The side decides three
things at once, and they move together: the axis tabs advance along, the edge the active accent takes
(the outer one, away from the content the strip heads) and the edge the bar rules against that content
(the opposite one). One flag places the last two, so they can never land on the same edge -- which
would read as a strip with no accent at all rather than as a bug.

TabSizing { Content, Uniform } comes with it and is not really separable. Content is what the strip
has always done: measured label plus the icon and close boxes, clamped. Uniform is a square cell of
the strip's own thickness, and on a vertical strip that is closer to a correctness fix than a
preference -- sizing by content there sets a tab's HEIGHT from the WIDTH of its label, and on an
icon-only rail from a label that is never drawn. A uniform tab centres its icon and draws neither the
label nor the ✕, having no room beside it; the label belongs in the host's tooltip. With no icon the
label takes the centre instead, so a uniform strip is never blank.

Rotated text is deliberately NOT part of this. A vertical strip defaults to UPRIGHT content, because
rotation is a renderer capability rather than a flag: Renderer.DrawText has no angle, so it would mean
a new primitive across every backend, meaningless on a cell surface, interacting with the SDF atlas
and the fallback chain. Upright is also what the only known consumer wants, since its tabs are emoji
and a rotated emoji is simply wrong.

Internally the painter now works on a FLOW axis (the one tabs advance along) and a CROSS axis (the
strip's thickness), with one helper mapping the pair back to x/y. That is what lets all four sides
share a body instead of re-testing the side per drawn piece, and it is why a tab's CONTENT needs no
side test at all: a vertical strip stacks tabs, but each tab still reads left to right.

New Render overloads take the strip's whole RectF32. The two-float form remains and is exactly the
rect form with cross origin 0 and Height for thickness -- pinned by rendering both and comparing the
painted surfaces -- but Bottom and Right have to be told where the far edge is, a viewport dimension
the bar does not know, so those need the rect.

TabStripTree.Build describes a tab strip as a Layout.Node tree -- one description a pixel surface
paints through PaintLayout and a cell surface through Console.Lib's CellLayout. TabStripMetrics carries
the numbers (pixels vs whole cells), and two policies carry the rest: TabStripOverflow { Clip, Drop }
and TabLabelDecoration. Everything else -- where the accent goes, which edge is ruled, how a disabled
tab reports, what a uniform cell holds -- is identical on both, which is the claim worth making.

Drop is not cosmetic: a clipped tab leaves a region that is hit but not visible, so a press lands on
something the reader cannot see. Decoration exists because a terminal's active plate is a bet on the
reader's own palette, and on a monochrome one the brackets are the only thing left saying which tab is
active.

TabBar paints through it, so the strip exists ONCE: the widget builds the tree, arranges it and lets
PaintLayout register the regions, and its 69 geometry tests pass unchanged. What stays imperative is
the "+" button alone, which belongs to a tab BAR rather than to a tab strip -- a nav rail has none and
a terminal tab bar has none. It is one rect at a position the tree does not know, so a node for it
would buy nothing; its MARK is now IconKind.Plus (below) rather than this file's own rectangles.

ITabStripSource is how the titles overload avoids materialising a TabItem list per frame: the builder
never reads a tab's VALUE, only its label, glyph and enabled state.

CompositeWidget<TSurface> is the base for a widget that paints OTHER widgets into the same surface --
an app chrome hosting a tab strip and a page. It declares its children once, in paint order, and every
aggregate query derives from that: HitTest, HitTestAndDispatch, HitTestCursor, GetRegisteredTextInputs
and a new PaintedRegions. HitTest, HitTestCursor and GetRegisteredTextInputs became virtual to allow
it.

The problem is that a child's regions live on the CHILD. A composite draws its children into its own
surface, so the frame looks whole, while hit tests, cursor queries, Tab cycling and region enumeration
all read a per-widget tracker -- so anything asking only the composite misses every control its
children registered. Nothing throws and the pixels are right; the controls simply stop answering.

Without a base for it each host restates the composition per query and they drift. The case that
forced this had ONE composite stating its child list five times, in three different orders, with one
query missing a child outright and its cursor order inverted relative to its dispatch order. No test
could have caught it, because every site is individually plausible.

Z-order: the composite's own regions answer FIRST, then children front to back. A composite's own
painting is almost always either a non-interactive background behind its children (asking first is
harmless, it registers nothing) or chrome drawn OVER them, a status bar (asking first is required).
Enumerations run in paint order instead, since they read the frame the way a person does.

TabBarColors.HoverBackground (nullable, null = ActiveBackground, i.e. unchanged) lets a strip name a
third plate tone. The default stays what it was and for the stated reason -- a hovered tab previews
what clicking gives you, and a palette naming two chrome surfaces has no third tone to offer. But that
reasoning stops holding for a strip that draws no accent: hover and active then render identically, so
the strip cannot say which tab a click would take you to. A nav rail is exactly that case, its
selected cell being a filled plate rather than a plate plus an accent.

PixelWidgetBase.HitTestAndDispatch becomes virtual, so a COMPOSITE widget can extend dispatch to the
widgets it paints. One that hosts children draws them into the same surface, but their regions live on
THEIR trackers, so a host asking only the composite silently misses every control the children
registered -- and the composite is the only thing that knows its own paint order.

CanCloseTabs and CanReorderTabs (both default true, both positive logic) switch the two affordances
off. Closing off draws no ✕ AND stops reserving the box, so tabs are narrower by it -- a strip whose
tabs cannot be closed should not hold a gap where the control would have been. Reordering off makes
SlotAt report -1 everywhere, which is the whole mechanism rather than a special case: the bar never
reorders anything itself, it nominates the slot a host would drop into, so declining to nominate one
is how it says no. ShowNewTabButton keeps its older name despite the inconsistency, because renaming a
shipped property costs a consumer more than the inconsistency does.

IconKind gains Plus and Minus, as a PAIR. Plus alone was declined while the "+" was being migrated, on
the grounds that no cell surface has a new-tab button, so the cell drawing it would owe had no
consumer. True, and the wrong question: a stepper is [-] value [+], and a terminal has plenty of
those. Both are built from rectangles like the rest of the set, and the plus is the kind where that
buys the most despite having a perfectly safe ASCII spelling -- a typeset + is drawn by whichever face
the host resolved, at that face's stroke weight, sitting on the TEXT baseline rather than centred in
its box. Every consumer that wanted one had already reached that conclusion and was drawing its own
two rectangles.

Minus is the sole kind that cannot ink its full square, a horizontal bar having no height to give. It
fills its full WIDTH and takes Plus's bar thickness and centre line verbatim, which is what makes the
two line up in the stepper that justifies them: two independently-drawn marks are exactly what drift
apart by a pixel of weight or baseline, and side by side that is the one difference a reader is
guaranteed to catch.

SOURCE-BREAKING for a named argument, which is the only reason to read this twice: Render's
contentLeft / viewportW are now contentStart / viewportEnd, and SlotAt's x is now flow, because on a
vertical strip the old names named the wrong axis. Positional callers are unaffected, and nothing in
the org passed them by name.

## 8.2

A tab carries what it MEANS. TabItem<T> and the Render<T> / HandleMouseDown<T> pair hand a press back
as TabClick<T> -- the value the tab selects -- instead of an index the host maps through a switch that
has to agree with the title order while nothing checks it. Same argument DropdownItem<T> is built on,
and it bites harder here: reordering a strip of bare titles silently selects the wrong page. The
titles overload is untouched and lays out identically (pinned by comparing the painted surfaces), so
this is purely additive.
An item also carries an Icon, and it is a STRING rather than a Layout.Content.Icon named by meaning.
That inverts the usual rule for a reason: the rule exists because a symbol character may not be
covered by the bound font, and PixelWidgetBase.DrawText already resolves exactly that -- it splits a
run by coverage through FontFallback and routes supplementary-plane codepoints to EmojiFontPath even
without one. A mark built from rectangles could not draw a telescope or a ringed planet at all, so
naming the meaning would make this whole class of tab icon inexpressible. Width for the glyph is a
FIXED box, never measured: a pictograph's advance varies by face, so measuring would make tab width
depend on which fallback happened to resolve.
IsEnabled + Tooltip complete the item. A disabled tab is drawn greyed (TabBarColors.DisabledText, the
SEPARATOR weight rather than a third text tone -- WCAG exempts inactive components from the contrast
minimums, and a text role would read as merely quiet) and is inert: no press, no cursor, no ✕. It
registers under its own region id, TabBarRegions.DisabledTabs, which is what makes every "a tab you
can press" query exclude it with no second copy of the enabled test -- while keeping the region
PRESENT, so SlotAt's position walk stays dense. Drop it instead and every tab after a disabled one
answers a drag with its neighbour's slot.
TabItem stores IsEnabled inverted so that `new TabItem<T>()` and `default` come out ENABLED: a record
struct ignores a primary-constructor property initialiser, so the obvious `= true` reads correctly and
manufactures a silently unselectable tab for anyone reaching the parameterless form.
TabBar.HoveredIndex reports the tab under the pointer, resolved while the tabs are laid out. The bar
deliberately does not draw the tooltip: it is painted OUTSIDE the strip, over whatever is adjacent,
and a widget that clips to its own bounds cannot put it there.

## 8.1

A widget can be told where the pointer is, and chrome lights under it. PixelWidgetBase.Pointer
is the generalisation of TabBar's own (which is now the inherited one, same type and
semantics, so a host that sets it reads unchanged): a position rather than a hovered index,
because the widget owns the geometry it drew and a host asked for the index would have to
hit-test the PREVIOUS frame's.
Two things read it. Layout.Node.HoverBackground / .BgHover(colour) paints a second fill while
the pointer is inside a node's rect, resolved where the ordinary background is already
painted -- so what lights up and what the pointer is over are the same rectangle, the
guarantee Node.Hit has had all along. That is the whole reason it belongs here: a consumer
cannot get the rect out of the tree until it has been painted, which is after the fill had to
be chosen, so every one of them recomputed it by hand and drifted the moment a pad, a spacer
or a row count changed -- a button lighting while the pointer sits a pad above it.
RenderDropdownMenu now also highlights the row under the pointer, which no consumer could do
at all: the highlight tracked HighlightIndex, and nothing but the keyboard ever moved it, so a
dropdown answered the arrow keys and stayed dead under the mouse. Hovering deliberately does
NOT move HighlightIndex -- that is where the KEYBOARD is, and a pointer crossing the list on
its way elsewhere must not silently become what Enter takes.
Inert unless asked for: Pointer is null by default, so a node with no hover fill, and every
host written before this, paints exactly as before. A host that DOES set it must repaint on
pointer motion -- motion is not otherwise a reason to draw, and a highlight resolved during
paint cannot notice the cursor left if no paint happens; it stays lit behind a pointer that
is somewhere else.

## 8.0

BREAKING, see MIGRATION.md. TabBar becomes TabBar<TSurface> : PixelWidgetBase<TSurface>. It
takes its Renderer at construction like every other widget, and reads the window's font,
fallback chain and DPI from the shared WindowUiSettings instead of being handed each one
separately -- so TabBar.Scale is gone (it is DpiScale) and the font/fallback constructor
arguments are gone with it. Render drops its renderer parameter.
The payoff is not tidiness: the bar now REGISTERS each tab, each ✕ and the + as it paints
them, so HandleMouseDown / HitNewTabButton / SlotAt all report from the rects the tabs were
drawn in rather than from a private copy of the layout -- and, through 7.32's frame stamp,
report nothing at all on a frame the host did not draw the strip in. A tab bar does meet that
second case: a host carrying a torn-out tab as its own small window paints it as a chip and
draws no strip, leaving the bar holding the layout of a strip that is gone. Whether a press
reaches it there was the host's own guards to get right; now it is not.
ClickableRegionTracker.Regions / PixelWidgetBase.RegisteredRegions are the additive half: a
widget can read back its own regions without the per-call copy GetRegisteredRegions makes,
which is what lets SlotAt walk the tab rects on every pointer move during a drag.
PixelWidgetBase.DrewThisFrame is the other addition: the hit reads already decline a stale
region set, but a host resolves some input by GEOMETRY rather than by region -- a wheel over a
panel's column, a resize gutter beside it, a swallow that stops a click on chrome starting a
drag on the content underneath -- and those predicates are the remaining way a widget the host
stopped drawing goes on taking input. Now it can just ask the widget. True for a host that
does not count frames, like everything else in 7.32.

## 7.32

A widget is hit only on a frame it drew. WindowUiSettings.FrameId is bumped once per frame by
the host and stamped by BeginFrame; every read -- hit test, dispatch, cursor, Tab order, the
inspector's region and layout capture -- declines a set that is not the current frame's.
Registering as you paint already made a widget un-hittable WHERE it is not drawn; it said
nothing about WHEN, so a host that simply stopped calling a widget's render left the last
frame's regions standing, and a control no longer on screen went on taking clicks and RUNNING
their handlers -- which nothing inside the widget can notice, since from in there "not
rendered" and "not rendered yet" are the same thing. Costs nothing to a host that does not
count frames: the id stays 0, so does every stamp, and every comparison matches as before.
Also SearchInteraction.WrapsAround (default false, i.e. unchanged): a find list traverses one
document, where Down past the last hit means "start over", while a suggestion menu's end is
information rather than a wall to bounce off.

## 7.31

TabBar.Font / .Pad / .Border become public, joining .Height. Additive. They are what a host
needs when it has to draw a tab somewhere the bar does not -- a torn-out tab carried as its
own small window has to paint itself as one, and the bar is not what paints it. Copied
instead they drift: a consumer had two literals and a comment naming the constants they came
from, so changing the bar's type size silently stopped matching the window pretending to be
one of its tabs. Exposed SCALED like Height rather than as the base constants, because a
copier working from those applies a scale of its own and nothing makes that the same number
as TabBar.Scale.

## 7.30

WindowUiSettings.Focus: which field in the window has the keyboard now rides on the per-window
context with the DPI scale and the fonts, and is shared the same way (ShareUiContext). Additive.
Focus is a singleton because there is one keyboard, which makes it the same KIND of fact as
those -- and a window whose fields sit in more than one widget had to hand-thread ONE
TextInputFocus through the constructor of each, or they each believe they hold the keyboard.
That failure is silent: two carets blink, one is dead, Tab cycles the wrong list, and nothing
throws. It is also exactly the shape WindowUiSettings exists to retire, so leaving focus out of
it left the last per-widget copy in place. Get-only and created here: a window has exactly one,
and replacing it orphans the fields already registered with the old one.
WindowUiSettings.CaretRect moves there too, for the same reason: there is one caret, and held
per widget a host had to know WHICH widget painted the focused field in order to ask the right
one -- a question with no stable answer, since the field holding the keyboard moves between
them. PixelWidgetBase.CaretRect still answers, now by forwarding.
The practical payoff is that TextInputInteraction.HandleKey becomes reachable from a consumer's
single key entry point -- so the rules it owns (the IME composing guard above all) stop being
restated per input control, which is where they get forgotten.

## 7.29

Non-Latin text, and per-window values stop being copied per widget. BREAKING in three places.
* IME composition. TextInputState carries the in-progress preedit (Composition /
  CompositionCursor / CompositionLength / IsComposing); TextInputRenderer draws it inline,
  underlined, and now RETURNS the caret rect so a host can answer SDL_SetTextInputArea. A field
  handling only committed text cannot accept CJK at all: with an IME every keystroke before the
  commit is composition and nothing else arrives.
* A field draws through the FALLBACK CHAIN. The layout painter splits TEXT LEAVES into coverage
  runs, but a field's content is not a leaf, so TextInputRenderer takes the resolver and measures
  the caret through the same chain. Without it a field holding correct CJK renders blank -- the
  characters are there and the face has no glyphs. FontResolver.ResolveSystemScriptFonts()
  resolves the platform's CJK/Indic faces (portable Noto names appended on EVERY platform, not
  as a Linux else-branch).
* WindowUiSettings: DPI scale, fonts and the fallback chain live once per WINDOW and are SHARED
  by reference (PixelWidgetBase.ShareUiContext), instead of each widget holding a copy kept in
  agreement by an overridden setter naming every child. A widget belongs to one window; it does
  not need its own copy of what the window knows. IPixelWidget gains Ui.
* DropdownMenuState is generic over what an entry MEANS (DropdownItem<T>), so selecting hands
  back the entry rather than an index the caller maps through a switch that has to agree with
  the label order. Entries carry IsEnabled + Tooltip: a disabled row greys, states its reason ON
  the row, is skipped by the arrows, and swallows its click instead of falling through to the
  backdrop (which would close the menu, making it behave like a working row). The custom entry
  is gone as a special case -- an item carrying OnChoose IS it, retiring three Open parameters,
  three properties and an index-past-the-end check repeated in nav, Enter and the painter.
* An open overlay claims the keyboard by BEING PAINTED (WindowUiSettings.KeyboardClaimant +
  IKeyboardClaimant), and the host asks once. Every widget owning a dropdown previously needed
  its own routing case in its own input switch; of four dropdowns one lacked it, and the only
  symptom was arrow keys doing nothing. Paint order is z-order, so the topmost claims last and
  wins; a claimant no longer displayed declines, so a stale claim needs no clearing.

## 7.28

A text field is a DECLARATION: Layout.Builder.TextInput(state, fontSize) is the whole thing.
PixelWidgetBase.PaintLayout draws it, registers its TextInputHit over the arranged rect and
states CursorKind.Text, so click-to-focus, blur-on-outside-click, Tab order and the I-beam all
follow from one registration the painter cannot forget. It replaces a keyed Fill plus a painter
dictionary entry plus a dispatcher, whose real cost was the IDENTITY: a magic string shared
between a tree and a dictionary that nothing checks, so a typo was a silently blank field.
Console.Lib paints the same leaf on a terminal, which is what makes a field one declaration on
both surfaces. Intrinsic width comes from the placeholder or an explicit WidthSample, never the
live text, because a box that resizes while you type is a bug.
TextInputFocus owns which field has the keyboard. Focus IS global (one keyboard; WinForms has
the same singleton in Form.ActiveControl) -- what was wrong is that a settable pointer separates
the transition from its platform side effects, so any code assigning it left the app taking no
input while the IME stayed up. A host binds FocusChanged ONCE and nothing else knows the
platform calls exist. Blur gates on the OWNER's record, not the field's IsActive flag, so a
caller that cleared the flag by hand cannot make the blur a no-op. BlurIfUnpainted(painted)
drops focus a field kept after it stopped being drawn; the CALLER supplies what was painted,
because only a host knows what its frame is composed of.
TextInputInteraction moves here from a consumer's UI project, which is what makes it testable
at all. BREAKING: KeyContext's IPixelWidget ActiveTab becomes a lazy TabFields callback (that
interface was the one thing keeping a host-agnostic class from working on a terminal), the
Deactivate/SetActive callbacks become the focus owner, and HandleKey drops its activeInput
parameter for ctx.Focus.Current -- two ways to name the focused field is one too many.
TextInputRenderer gains HorizontalPadding (stated once, since measure and paint are now
separate halves) and no-ops without a font, matching the layout text helpers: a headless render
of a tree with a LABEL worked while the same tree with a FIELD threw.

## 7.27

PushClip/PopClip nest. A push inside a push draws in the INTERSECTION of the two, and a pop
restores the enclosing clip rather than the whole surface, so a child states its own bounds
and cannot escape its parent's. The pair was single-level, which reads as a simplification
and is not one: a panel clipping to its bounds and then per row had to intersect the two by
hand and re-push its OWN rect to get back, so the inner draw had to know the outer widget's
geometry -- and "pop" degenerated into "reset", which works only while every backend sets the
region absolutely. An unmatched pop now throws instead of silently unclipping. BREAKING for
backends: PushClip/PopClip are no longer virtual; a clipping backend overrides ApplyClip(rect)
and ClearClip() instead, one absolute rect and no history, since the stack lives in the base.
Renderer.ClipDepth reports the nesting, and RectInt gains Normalized() + Intersect().

## 7.26

Renderer.DrawTriangles: a triangle list, x/y pairs, three vertices per triangle. Anything not
made of rectangles, ellipses and text IS a triangle list -- an arrowhead, a chevron, a chart's
filled area -- and a widget that cannot say so has to reach past the abstraction to whichever
backend can, which is enough on its own to pin a whole UI layer to one renderer. The default
is a scanline fill written in terms of FillRectangle, so every backend has it; a GPU renderer
overrides it with one draw call. Rows are tested at their CENTRE, so two triangles sharing an
edge neither double-claim a row nor drop it, and a span the rounding would collapse still
draws one pixel -- an arrowhead whose tip vanished reads as blunt rather than as slightly off.

## 7.25

Clipping a software backend honours, and a widget can state. RgbaImageRenderer overrode
neither PushClip nor PopClip, which the base permits -- clipping is called an optimization
there, because on a GPU it is. That reasoning does not survive a widget TEST: a control that
trims content to its bounds draws over the whole picture on a renderer that ignores the clip,
so a headless render disagrees with the app about what was drawn, and the disagreement reads
as a widget bug. RgbaImage now carries the clip itself, so every primitive respects it for the
cost of different constants in bounds checks it was already doing -- including Clear, which
still REPLACES rather than blends, and BlitGlyphTinted, the one text path that writes Pixels
directly and so was the only thing still painting outside a clip once the fills stopped.
PixelWidgetBase gains PushClip(x, y, w, h) / PopClip(), the x/y/w/h form: Renderer.PushClip
takes a RectInt, whose corners go in the opposite order to every other rect a widget states.
Single-level, unchanged: a second push replaces the first. 767 tests.

## 7.24

Padding per axis: Node.PaddingY, .Pad(across, down) and .PadX(...). Padding was one scalar
for all four sides, which a FIXED-HEIGHT bar cannot use: a chip in a 33-unit bar wants ten
units either side of its label and nothing above or below, because there is nothing above or
below to give. Padded symmetrically it gets a three-unit content box, and the failure is
asymmetric enough to hide -- text overflows its rect and goes on looking right, while an icon,
which is square by the smaller side, collapses to a stub. PaddingY null means "same as
Padding", so every existing tree is unchanged. 744 tests.

## 7.23

Two marks a consumer had to hand-compute, because the tree could not say them.
(a) IconKind.CaretUp / CaretDown. Every consumer wanting the mark on a drop-up chip was
drawing its own triangle from raw vertices; that is the tell that the family was missing a
member, not that the mark was app-specific. Filled rather than a two-stroke chevron because at
the ten-or-fewer pixels a chip affords, a stroked mark is two hairlines with a hole between
them and the hole goes first. Rows are snapped to whole pixels and never thinner than one, so
the tip survives; it reaches all four edges like every other kind.
(b) Content.Text.WidthSample, and a widthSample: on Builder.Text. Measure the node as if it
held this text instead of its value -- a readout reserves the room "1000%" needs so the thing
beside it does not shuffle as the number changes. Callers were measuring a sample themselves,
pinning a fixed width and caching it against every input that could invalidate it, which
re-derives the measure pass in the one place that cannot see the font the painter will use.
Pair with HAlign Center, or the shorter live value sits at one end of its reserved room.
Both additive; 743 tests.

## 7.22

Two things a host had no way to SAY, so it said them with a global and a geometry predicate.
(a) CursorKind, and a Cursor on ClickableRegion bound to the same arranged rect as the hit. A
cursor is a statement about what is under the pointer, and the region list already knows that;
a host answering it from coordinates ends up with a predicate (over the content, but not the
palette, and not any open panel) that every overlay added later silently invalidates, because
the overlay draws over the content and the predicate goes on saying content. A region with no
stated cursor is TRANSPARENT to HitTestCursor, so a card declares one and its rows inherit it;
RegisterCursor states one without becoming a click target and reads as ChromeHit, which is how
a host tells its own overlay from the content beneath. Null means no opinion, NOT Default, or a
plain button would stamp the arrow over a host that wanted a crosshair. A text field now
carries the I-beam itself. Additive: existing callers compile and behave the same.
(b) TextInputRenderer.Render and PixelWidgetBase.RenderTextInput take an optional
TextInputColors. The static stays the default, but a field that genuinely differs had no way
through except assigning the static, drawing, and assigning it back, which is a race the moment
anything else paints. 6 tests.

## 7.21

Two things a consumer had to work around by hand, fixed where they belong.
(a) Layout.CrossAlign (Start / Center / End) on a Stack, honoured by the arrange pass, plus
.Align(...) and .CrossCenter(). A Stack placed every child at the cross-axis START, so a
Fixed-height control in a taller row hugged the top and sat half the slack above centre. The
workaround was padding the container or wrapping each child in a spacer sandwich, both of which
re-derive a position from the parent inner size at the call site. Default is Start, so nothing
moves unless asked. A Star child already fills the axis and is unaffected.
(b) Every IconKind now inks the FULL square it declares, so a row of different marks at one
size comes out one height. Measured before and after: the same declared size used to produce
ink spanning 63 percent (the half-disc) to 100 percent (the grid) of it, a 1.6x spread that no
amount of centring can hide; it is now 95 to 100 percent, the residue being rasteriser rounding
on stroke ends. VISIBLE CHANGE for 7.18-7.20 consumers: List, Auto and the three theme marks
all draw larger at the same declared size. Reduce the declared size to match the old ink.

## 7.20

An icon is now DRAWN at the size it declares, centred in and clamped by its arranged rect,
instead of being stretched to fill that rect. Content.Icon.Size was previously consulted only
at measure time, which made it meaningless the moment a node carried explicit sizing, and every
real icon does since it lives in a button. The visible symptom is a mark beside a text run: a
13-unit icon in a 20-unit cell painted at 20, standing 38 percent taller than the word cap
height and reading as vertically misaligned even though both were centred on the same row.
BEHAVIOUR CHANGE for anyone already on 7.18 or 7.19: an icon in a cell larger than its declared
size now draws smaller than before. Restore the old look by declaring the size you want, which
is the point of the field.

## 7.19

Three theme marks join IconKind: ThemeSystem (a disc half filled, half outlined),
ThemeLight (a rayed disc) and ThemeDark (a crescent). They arrive as a family because an app
with a light/dark setting needs all three or none, and both surfaces can say each one;
Console.Lib 4.22 maps them to the geometric-shapes circles (empty / half / full), which keep
the light-to-dark ordering without gambling on a monospace face covering the sun and moon of
Miscellaneous Symbols. Each is built from SCANLINE SPANS, so a curve needs no path support:
notably the crescent is the spans an offset disc does not cover, NOT the usual trick of
over-painting that disc in the button background -- that needs to know the ground, so it
breaks over a gradient, an image or a transparent node, and this painter is handed ink and a
rect and nothing else. Also weights the Auto brackets to match (a hairline bracket beside a
solid crescent read as two families) and floors the sun gap at 1.5 px, without which the rays
closed on the disc at the 13 px a header actually uses. Additive.

## 7.18

Layout.Content.Icon: a leaf that names a pictogram by MEANING, so each surface draws it the
way that surface can. The pixel painter constructs it from rectangles (PixelWidgetBase's new
DrawLayoutIcon, beside DrawTrackSlider); Console.Lib 4.21 maps the same node to a glyph from
the block-element range. Naming rather than spelling is the point: a symbol codepoint has to
exist in the bound font and arrives as .notdef when it does not, which is an empty box exactly
where the icon should be, while a character grid cannot draw rectangles at all. Additive:
Content.Icon, IconKind (Grid, List, Auto), Builder.Icon, DrawLayoutIcon. IconKind is
deliberately closed and tiny, since every kind costs a drawing here AND a glyph choice in
every cell painter; a one-off belongs in a Content.Fill the app draws itself.

## 7.17

Dependency refresh: SharpAstro.Fonts / .Fonts.Shaping 1.9 -> 1.11, which MOVES rendered
output, in every case from wrong to right. 1.10 fixes the TrueType hinting interpreter,
where three defects each masked the next (a twilight zone reporting zero points forever, so
every twilight op was a silent no-op; an out-of-range guard that returned without popping,
which hung 'g' and 'x'; and 'cvt ' read unsigned when FWORD is signed, mangling 26 of
NotoSans-Regular's 150 control values), and propagates GPOS mark attachment the RTL way, so
an Arabic mark no longer sits one base-glyph advance from where it belongs. 1.11 stops an
embedded PDF subset whose ONLY cmap is Mac Roman (1,0) from falling through to the
char-code-as-glyph-id guess, which is what turned Korean body text into plausible-but-wrong
Hangul and made small Latin subsets render as nothing at all.
What this means for a consumer: hinted TrueType text and RTL text with marks change shape
where they were previously malformed, and a golden image containing either differs. Nothing
in this repo's own baselines moved.

## 7.16

Additive. TabBar.Pointer hands the bar the mouse position and it hovers itself from there:
the tab under the pointer takes the active plate and label, and the close mark inside that
tab gets a plate of its own. NewTabHovered still works and now ORs with it. A position
rather than a hovered index, for the same reason the + button lives here: the bar owns the
tab widths, so a host supplying an index would be hit-testing the PREVIOUS frame's
geometry, which lags on the frame a tab opens, closes or is dragged past a still pointer.
Hover reuses ActiveBackground rather than adding a palette role. It previews what clicking
gives you, UiPalette names no hover surface, and blending one would paint a colour the
theme never chose; the accent strip stays exclusive to the active tab, and that is what
keeps the two readable apart. The close plate is Separator, the one role guaranteed to read
against both the panel and the header surface, so it needs no colour of its own in either
theme. A consumer that leaves Pointer null renders exactly as before.

## 7.15

Additive. TabBar can draw a "+" after the last tab, the way a terminal or a browser does:
ShowNewTabButton, NewTabActive, NewTabHovered, and HitNewTabButton to report the click. A
consumer that sets nothing is unchanged. It belongs here rather than in the host because the
bar owns the tab widths and published no edge to place it against, so an app wanting the
affordance had to either duplicate the width arithmetic -- silently drifting the day this
file changes -- or park the button at the strip's far end, which reads as a toolbar rather
than as the next slot. HandleMouseDown deliberately returns null over the button rather than
inventing a tab index for it, so a host that forgets HitNewTabButton swallows the click
instead of activating tab 0. The mark is two filled bars, not a typeset "+": it has to be
there whatever face the host passed in, and geometry stays crisp at a 30px strip height.
The button is dropped, not clipped, when the tabs have used up the width -- reporting hits on
a control the clip hides is worse than not offering it.

## 7.14

BEHAVIOUR CHANGE, and a fix no consumer could work around. TextFit.ShrinkToWidth returned a
size it had never measured, so a run fitted with TextTrim.Shrink could be drawn PAST the rect
it was just fitted to. It refined by ratio -- size *= maxWidth / measured, up to four passes
-- then returned the last estimate unverified, on the premise its own remarks stated out
loud: "advance widths scale linearly with the size, so one division lands on the answer".
They scale linearly in the IDEAL advance. A rasterizer quantizes the size to whole pixels
before measuring, so measured width is a STEP function of the requested size and every
estimate inside one step measures identically. The ratio then reapplies the same factor
every pass and converges to a fixed point still ABOVE the budget. "Move History" against a
182.54px budget went 30.25 -> 28.07 -> 27.91 -> 27.75 -> 27.59, and 27.59 measures 183.60 --
it rounds to 28, exactly as 27.91 and 27.75 already had. Now, once the ratio stops making
progress the search steps down WHOLE sizes -- the grid the rasterizer actually has, so each
step is the next distinct width available rather than another estimate inside the same one --
and returns the first size MEASURED to fit, which is now a guarantee the remarks can state.
minFontSize stays the only value allowed to overflow: a run that cannot fit even at the floor
should overflow where a reader can see it rather than shrink away to nothing. The fast path
(it already fits) is untouched, so the common case does not pay for this.
What MOVES, and for whom: a run fitted with Shrink that used to sit a fraction of a pixel over
its rect now takes the next whole size down, so a golden image containing shrunk text differs
by one size step. TextTrim.Shrink is OPT-IN and the default is End, so a consumer that never
asks for it renders byte-identically -- which is why this is a minor and not a major.
Also makes the tests able to see this class of bug at all. The stub renderer they measured
with was CONTINUOUS (length * fontSize * 0.5), so no plateau existed and the bug was
unreachable by construction; a quantizing oracle now sweeps budgets and asserts the
INVARIANT -- what comes back fits -- instead of one arithmetic answer.

## 7.13

A glyph the SDF atlas can never rasterize is given up on after three attempts and recorded
blank, instead of being retried forever. A failed rasterization released its in-flight claim
so a later frame could retry, which is right for the one failure that heals itself: an
embedded "mem:" subset font losing the race with its own registration. But nothing counted
the attempts, and the caller re-offers the whole visible glyph set every frame, so a glyph
that could NEVER rasterize was re-claimed indefinitely and _rasterizeInFlight never emptied
-- which pins IsDirty true. Any permanent font failure therefore presented as "the render
never settles", with nothing on screen or in the log naming a font: downstream it arrived as
an offscreen golden-image diff and cost a day in the render pipeline. Past the bound the
glyph draws as nothing, the atlas reports clean, and one line per FONT (not per glyph -- a
font that never registers fails hundreds) says which glyph, how many attempts, and whether
the font is registered by the time we gave up. The counters reset on evict-all, so a font
registered later still gets a fresh bound. Also adds ManagedFontRasterizer.IsFontRegistered,
the predicate every rasterize entry point already failed on implicitly -- a background
caller can now state it in its own diagnostics rather than quote a stale exception message.
ALSO IN 7.13, and missing from these notes until 7.14 was written -- a BEHAVIOUR CHANGE that
shipped unannounced, recorded here late rather than left unrecorded: a whitespace codepoint's
advance now comes from the font's own hmtx table instead of being borrowed from the 'n' glyph.
An ink-free outline threw its advance away, and measurement fell back to 'n'. In DejaVu Sans a
space is 651/2048 em and 'n' is 1303/2048, so every measured space was 1.99x too wide. Any
layout whose width depends on spaces therefore MOVES on 7.13: narrower, by nearly half a space
per space. Specifically, a column padded with spaces that lined up only because a space
measured like a digit stops lining up -- chess's move-history index column was exactly that,
and the fix downstream is to pad with U+2007 FIGURE SPACE, which a font defines to advance
like a digit, rather than to widen a tolerance around a premise that was never true. Cell
surfaces are unaffected, since there one character is one cell whatever the font says.

## 7.12

Additive. TextInputRenderer takes a palette, the same shape TabBar got in 7.10: a
TextInputColors record whose defaults are the values it has always drawn, a FromPalette
factory, and a settable TextInputRenderer.Colors derived when the theme MOVES rather
than per frame. A consumer that sets nothing is unchanged. The renderer previously held
eight private static readonly colours, which are the first consumer's dark scheme frozen
into a shared widget: a themed app could restyle every surface it draws itself and still
get a slate-blue box in the middle of it, which is what TianWen's night mode hit. Two
derivation choices worth knowing: the field's ground comes from ContentBg rather than
PanelBg, because a field sits on a panel and has to read as recessed into it with focus
lifting it halfway back; and the caret takes the accent rather than the text colour,
since on a palette with one hue to spend a caret in the text colour is invisible against
the text beside it.

## 7.11

BREAKING, see MIGRATION.md. UiPalette gains eight roles (Accent, AccentAlt, Focus,
SeparatorStrong, Success, Info, Warn, Error) and becomes a sealed record with required
members instead of a positional readonly record struct. Two reasons, both load-bearing.
A record struct always has an implicit parameterless constructor that property
initializers do not run, so default(UiPalette) was all-zero: transparent black, painted
silently with nothing on screen to say why. required makes the omission a compile
error. And a positional record cannot grow without breaking every call site,
which is exactly what a palette has to do. Five roles are DERIVED (SeparatorStrong,
HeaderText, AccentAlt, Focus, Success default to the role they extend) so a palette with
one rule weight or one accent need not invent a second; the fallback is stored in
nullable backing fields, so `with` keeps an unstated role unstated. HeaderText moves
from required to derived-from-Accent, so a palette that omits it now gets the accent
rather than a compile error. Adds MenuColors.FromPalette, the menu counterpart of
TabBarColors.FromPalette from 7.10, and UiPalette.IsDark computed from ContentBg so it
cannot disagree with the colours it describes.

## 7.10

TabBar takes a colour palette instead of hard-coding one. Its eight colours were private
static fields, so a consumer could not theme the strip at all -- an app offering a light mode
had a dark band across the top of every window and no way to change it. TabBarColors is a
record whose defaults are the values the bar has always drawn, so a consumer that sets nothing
is unchanged, and TabBar.Colors is settable (like Scale) because a theme can flip while the bar
is alive. Surfaces and text are separated from ActiveAccent deliberately: the accent means
"this is the tab you are on", which reads the same on a light strip as a dark one, so theming
it changes what it communicates rather than how it reads. Additive; five tests, two rendering
through RgbaImageRenderer so an override is proven to reach the pixels.

## 7.9

A PDF font's own /Encoding decides which glyph a character code selects, so an embedded
name-keyed CFF subset stops drawing unrelated glyphs. A simple font can name its encoding in the
PDF (a base encoding, /Differences, or both), and for a name-keyed CFF that naming is the ONLY
thing that resolves a code to a glyph: the subset carries no cmap and often no CFF Encoding
either, so a code fell through to being used as a glyph index directly. Page numbers rendered as
whatever glyph happened to sit at index 49, 50, 51 -- 'W', 'X', 'Y' where the document said 1, 2,
3 -- which reads as a font bug rather than a lookup one.
RegisterPdfEncoding takes the code -> glyph-name map a consumer parsed out of the PDF, and glyph
resolution consults it before falling back to the cmap, resolving the name through the new
SharpAstro.Fonts 1.8 GidForName. The old RegisterType1Encoding stays as an [Obsolete] forwarder:
the map was never Type 1-specific, it was only ever registered from that path.
Needs SharpAstro.Fonts 1.8, which also loads a PDF subset font whose cmap a subsetter
malformed -- one bad subtable used to reject the whole font, dropping every glyph to a system
face. Additive apart from the rename; consumers that never register an encoding are unaffected.

## 7.8

The PIXEL painter grows one, so a text run can no longer escape the rect the engine gave it.
7.7 shipped Trim as an author's declaration that only the cell painter acted on; on a pixel
surface DrawText starts at its rect's edge and keeps going, so an over-wide run drew straight
over its neighbour -- silently, and only at the surface sizes where it happened not to fit,
which is the worst way to find out. The engine already owned the rect and the run already said
which half of itself mattered; the painter was the piece honouring neither. PaintLayout now
fits every Content.Text to its arranged rect through the new TextFit, which is also public for
the hand-placed draw paths a tree does not cover.
TextTrim gains two members a pixel surface can express and a character grid cannot, each with a
stated degradation so one tree still paints on both: Shrink scales the run DOWN and keeps every
character (for a run where a smaller WHOLE beats a larger fragment -- a chess move, a reading, a
coordinate; a cell surface end-trims), and None draws it whole and lets it overhang, which is
exactly what every pixel run did before this (a cell surface hard-clips, with no ellipsis to
claim a removal the author declined). PixelWidgetBase.FitFontSize is the Shrink answer for the
widget-level helpers -- a status bar, a strip either side of a camera cutout -- measured through
the widget's FontFallback, which every hand-rolled consumer copy of this loop got wrong.
BEHAVIOUR CHANGE, not additive: a run wide enough to overflow and left on the DEFAULT
TextTrim.End now ellipsizes on pixel surfaces where it used to overhang. That is the contract
7.7 wrote down and the cell painter has always kept, so it is a fix rather than a new policy --
but it IS visible, and a run that meant to overhang now says TextTrim.None. Fitting costs one
measurement per text leaf per frame, early-outing on the run that already fits.
An EMPTY font path still means "draw no text" and never measures, keeping FontPath's unresolved
contract; a non-empty font the renderer cannot load throws exactly where DrawText already did.
Console.Lib 4.16 carries the cell-side degradations. 15 tests.
Later in 7.8, no X.Y bump: AssemblyVersion is now DERIVED from VersionMajorMinor in
src/Directory.Build.props like every other version value, instead of being restated as a literal
in each csproj. Both DIR.Lib and DIR.Lib.Shaping carried <AssemblyVersion>6.4.0.0</AssemblyVersion>
and had published it unchanged since 6.5 -- so every package from 6.5 through 7.8.1841 claims
assembly identity 6.4 while its informational version says 7.8. It survived the version
single-sourcing because CI stamps -p:Version and -p:FileVersion but NOT -p:AssemblyVersion, so
unlike a stale VersionPrefix (which only spoils a local pack) the literal won in the build that
ships, and nothing compares the two. Now 7.8.0.0.
Deliberately NOT a minor bump: the value only moves UP, toward the version the package already
advertised, and the runtime rejects a loaded assembly LOWER than the compiled reference, never
higher -- so anything already built against 6.4.0.0 keeps loading. Consumers on a floating 7.8.*
pin take the correction on their next restore with no props change. Only Major.Minor is
significant, the build counter stays out, so republishing 7.8 does not churn identity again.
The same correction lands in SdlVulkan.Renderer 7.6 (6.11.0.0 and two 6.0.0.0), and the property
now sits in the props file in all seven sibling repos with none left in any csproj.

## 7.7

A LAYOUT TREE can state a hyperlink, so one authored tree is a real link on every surface that
has one. HitResult.LinkHit was the declared way to say "this points somewhere" and PaintLayout
already bound it as a click region -- but the only route to SelectableTextRegion.Href was the
immediate-mode DrawSelectableText, which no layout tree calls. So a web host, the one host that
can render a real <a href>, got a bare clickable rect and had to reimplement new-tab / open /
copy-link itself. PaintLayout now resolves the nearest enclosing LinkHit (depth-keyed, matching
Console.Lib's CellLayout exactly, so the same tree cannot mean different things per surface) and
routes text under it through DrawSelectableText with Href set. The pixel counterpart to the OSC 8
wrap CellLayout paints for the same node. ADDITIVE: only LINKED text takes the new route, so
ordinary layout text neither changes nor starts landing in the host's selection layer; the click
binding is untouched; a raster host still rasters. HostRendersSelectableText is honoured, so a
DOM host does not double-draw the linked run. Adds a fallback-aware DrawSelectableText overload
mirroring the DrawText pair -- without it a linked run would silently split on the widget's
resolver rather than the one its measure used. 8 tests.
ALSO: Layout.Content.Text gains Trim (TextTrim.End default / Start), naming which end an
overlong run sacrifices. Intrinsic to the run for the same reason Color and HAlign are: only
the author knows which half carries the meaning. A label keeps its head; a PATH must keep its
tail, because every path on a machine shares its head -- end-trimmed, "C:\Users\seb\repos\so…"
identifies nothing while "…\ftw\Program.cs" is the part being read. Callers used to work around
the fixed rule by pre-truncating against the column width, and a row's width is exactly what the
layout engine took over, so after rows became layout trees the workaround stopped existing and
path columns silently lost their filenames. ADDITIVE (new property with the previous behaviour
as its default, new optional Builder.Text parameter). Honoured by painters that ellipsize --
Console.Lib's CellLayout does; the pixel painter does not ellipsize at all today (an overlong
run is clipped by its rect), so it is inert there until it grows one.

## 7.6

Collection faces reach the last two consumers of a font id (completes 7.5 / closes #29).
FIX: TryGetOpenTypeFont -- the shaper's seam -- loaded the raw id, so the FIRST shape of a
fresh "path#index" face threw FileNotFoundException (shaping runs before rasterization, so
nothing had populated the cache the id would have hit); it now resolves through the same
loader drawing uses. FIX: SdfGlyphDiskCache probed File.Exists on the raw id, so every
collection face silently bypassed the disk cache; a face now hashes as its container's
content hash with the face index folded in (one more FNV-1a step), giving each face its own
.sdfg -- faces have independent glyph-id spaces, so sharing one file would serve one face's
bitmaps under another's gids. No FormatVersion bump: bare-path hashes are unchanged, and
collection ids never reached the cache before. README: WebGl.Renderer joins
SdlVulkan.Renderer + Console.Lib as the third platform bridge (closes #26). 2 tests.

## 7.5

Fonts are found by the family they DECLARE, and every face of a collection is reachable.
FontResolver indexed families by guessing a file name ("<family>.ttf") plus a table of the
standard 14 -- which finds Segoe UI only because Windows happens to call that file segoeui.ttf,
never finds Segoe UI Symbol (seguisym.ttf), and cannot name ANY face past the first of a .ttc,
since those have no file name of their own. It now indexes each installed face's own 'name'
table (SharpAstro.Fonts 1.7 FontFaceReader, which seeks to 'name'/'OS/2' rather than loading the
font: ~25ms warm over 500 files). The two cheap probes still run first; the index builds lazily
behind them. Faces are named by FontFaceId -- a plain path, or path#index for a collection --
honoured by ManagedFontRasterizer and FontFallbackResolver; since the id is already every
glyph/atlas/shaper cache key, two faces of one file separate with no further plumbing.
FontFallbackResolver gains TryResolveFont/CanRender (it could not previously report "nothing
covers this", so callers duplicated its coverage cache to get a nullable answer) and role-based
construction (FromRoles: primary/symbol/emoji/scripts). PixelWidgetBase.FontFallback +
PixelMeasureContext.Fallback carry per-run fallback into the declarative painter, so a text leaf
splits into runs each drawn with a font that covers it instead of being drawn whole in one --
the general form of the emoji-only split, which stays for widgets that declare just EmojiFontPath.
The split is allocation-free and gated on an allocation-free PrimaryCoversAll, so text needing no
fallback costs nothing. FIX: the all-ASCII shortcut now checks that the primary covers ASCII.
ADDITIVE. 31 tests.

## 7.4

A CELL-authored tree arranges on a pixel surface: PixelMeasureContext gains per-axis scales plus a
CellAuthored factory (nominal 8x16, pass the real cell size when known) -- the exact mirror of
Console.Lib's CellMeasureContext.PixelAuthored, so a tree authored in either unit convention now
arranges on either surface. fontSize rides the VERTICAL scale (an em is a height): fontSize 1f is
one cell of text on both sides. Alongside it, PixelWidgetBase's Arrange/Paint/RenderLayout gain
overloads taking the CONTEXT itself, and the painter reads FontPath/FontScale/radius from it --
previously dpiScale was a scalar threaded separately into measure and paint, two copies kept in
step by hand, which is exactly the drift a per-axis context would have turned into wrong text
sizes. The scalar overloads delegate to the context ones (isotropic), so existing callers paint
byte-identically. ADDITIVE. 5 tests, incl. measure-and-paint-agree-by-construction.

## 7.3

The inspector core can express a command that spans FRAMES, which is what let SdlVulkan.Renderer's
inspector fold onto it (see SdlVulkan.Renderer 7.3). Pump could previously only run commands that
finished within one call, so a host with frame-spanning verbs had to keep a private scheduler -- and,
because a scheduler needs feeding, a private copy of the transport too. New IDebugInspectorOperation
(Exclusive / Timeout / Advance) plus IDebugInspectorSteppedHost (SteppedMethods / Begin) express both
timings the SDL inspector actually needed, and they are genuinely different: an EXCLUSIVE operation
owns the pump so one step happens per iteration with a real frame in between (a batch), while a
BACKGROUND one advances every pump WITHOUT blocking the queue, so observe verbs are answered while it
runs (a press-and-hold -- the whole point being to inspect the UI the hold put on screen). Two rules
fall out and are tested: one background operation at a time, and an exclusive one may not start on
top of a background one, since two scripts driving one surface would interleave by frame timing.
SteppedMethods is a DECLARED set and is consulted before Begin, because Begin ACTS (a hold presses
the button) -- asking it speculatively whether it knows a method would press the button as a side
effect of the question. `batch` and `wait` are now CORE verbs: stepping one command per iteration is
pure scheduling with no surface in it, so every host gets them, and nesting is refused rather than
given a scheduler stack. A per-operation Timeout rides alongside CommandTimeout, resolved in two
stages by the socket side, so a five-minute hold is not cut off at ten seconds while a host that
never pumps still fails fast. ALSO: Detached + Submit drive the scheduler with no listener and no
multicast bind (the assertions are about ordering, not ports); a DiscoveryExtras default member lets
a host add fields to its discovery reply, read per reply so a window title that changes stays
current; Start takes enableDiscovery, preserving the SDL inspector's ability to run unadvertised.
ADDITIVE -- Console.Lib's host compiles unchanged, verified against its working tree. 17 tests.

## 6.21

Layout.Node.CornerRadius + the .Radius(designUnits) fluent, so a rounded panel is a
property of the tree rather than something a host hand-draws. Chrome only: arrange never
sees it, so a rounded node occupies and insets exactly the rect a square one would (pinned
by Radius_DoesNotChangeArrangement). The pixel painter routes Background and a Box leaf's
own fill through Renderer.FillRoundedRectangle when the radius is non-zero, and through
the untouched FillRectangle when it is 0, so existing trees paint byte-identically.
Design units like every other chrome measure, so it scales with DpiScale. A cell surface
approximates it with arc corners; a surface that cannot express it fills square, which is
why it is a hint rather than a guarantee.

## 6.20

Renderer.FillRoundedRectangle(rect, colour, cornerRadius): a virtual on the base
renderer, so every backend gains rounded fills at once. The default implementation
decomposes the shape into NON-OVERLAPPING horizontal spans (one FillRectangle per
scanline band) rather than the obvious centre-cross-plus-four-corner-ellipses, because
overlapping primitives double-blend a translucent colour and leave the corners darker
than the middle. A GPU backend can override with its existing SDF ellipse path.
Additive: a zero radius is exactly FillRectangle, so nothing renders differently
until a caller asks for a radius.
BEHAVIOUR CHANGE in the same release: RgbaImage.FillRect's SIMD alpha compositing is
fixed. It skipped the Porter-Duff alpha fix-up whenever the FIRST pixel of a vector had
an opaque destination, and applied that one verdict to all Count/4 pixels -- leaving the
RGB-formula value (192 for 50% over opaque) where the scalar tail correctly wrote 255.
A translucent fill over an opaque surface now yields alpha 255 as it always should have;
RGB is bit-identical. Consumers holding RGBA goldens that contain a translucent fill over
an opaque backdrop will need to re-record them (one baseline changed here).

## 6.17

DeviceTransform: a constrained content->device affine (Rotation90 in {0,90,180,270} +
uniform scale + translation) exposed as Renderer.DeviceTransform. GPU backends fold it into
the projection so the whole frame (text included) rotates/scales as one; the compose stays a
Matrix3x2 (2D affine) and only widens to mat4 at the GPU boundary. Additive: new type + new
virtual property defaulting to Identity (rendering byte-identical until a consumer sets it).

## 6.16

Hyperlink support on the selectable-text + click channels: SelectableTextRegion.Href
(optional) + DrawSelectableText(..., string? href) so a DOM host can render a real <a>;
and HitResult.LinkHit(Url) so a raster host can open the URL / drive a hover cursor.
Additive (new optional field, new method overload, new HitResult subtype).

## 6.12

Selectable-text channel: DrawSelectableText registers a SelectableTextRegion
per frame (zero-copy SelectableTextRegions span view, mirrors the clickable
tracker lifecycle); Renderer.HostRendersSelectableText lets a host that renders
native text (web DOM span layer) skip the glyph raster. Additive, default-off.

## 6.8

Rebuild picking up SharpAstro.Fonts.Shaping 1.5.551 (Fonts.Lib F6 zero-alloc bidi
path + F7 HarfBuzz-style per-lookup coverage-digest skipping): ≈3-4× faster shaping
(Latin sentence 280 -> 64 ns/char), zero-alloc preserved. Transitive via the
DIR.Lib.Shaping satellite; no DIR.Lib API change. The floating 1.5.* pin resolves
the new engine at build time, so the published nuspec floor moves to >= 1.5.551.

## 6.7

DIR.Lib.Shaping bidi: ShapingTextShaper now runs the full UAX #9 bidirectional
algorithm (via SharpAstro.Fonts.Shaping's BidiScriptItemizer) so mixed LTR/RTL
text orders correctly, with a ParagraphDirection option
(Auto/LeftToRight/RightToLeft). DIR.Lib core is unchanged.

## 6.0 (breaking)

Layout namespace + DSL. The layout engine moved into a new
DIR.Lib.Layout namespace and dropped the redundant `Layout` prefix: LayoutNode
-> Layout.Node, LayoutEngine -> Layout.Engine, LayoutContent -> Layout.Content,
LayoutAxis -> Layout.Axis (Sizing/SizeKind/Size<T>/DockChild/DockSide/ArrangedNode/
IMeasureContext also move into the namespace). A new Layout.Builder DSL + fluent
Node modifiers (.RowH()/.WStar()/.Bg()/.Clickable()/...) build the same records.
Source-breaking: consumers alias `using Layout = DIR.Lib.Layout;` (or import it)
and write Layout.Node / Layout.Builder / Layout.Sizing etc.

## 5.1

Markdown images: the inline grammar gains an `![` image
opener + imageSpan production (mirroring linkSpan), and a new MdImage
(Alt, Url) AST node + MarkdownInlineVisitor.Visit(ImageSpan). `![alt](url)`
now parses to MdImage instead of a literal `!` + link; renderers (Console.Lib)
rasterize or fall back to alt text.

## 4.2

Mhchem Phase-2: \ce{...} bodies now emit LaTeX math source
(Mhchem.Render → Mhchem.ToLatex) so chem flows through the same parser +
visitor pipeline as ordinary math. Display chem picks up real sub/super
box layout under BoxBuildingVisitor (Sixel / sextant / half-block) — the
box renderer earns proper layout for free. Latex grammar gains
\rightleftharpoons / \leftrightarrow as rel tokens; both visitors gain
\plus / \minus sign atoms (mhchem rewrites postfix +/- in script bodies
to these because the grammar treats bare +/- as binary operators).

## 4.1

Markdown subsystem (grammars + AST + visitors + mhchem)
moved here from Console.Lib so the parser layer is reusable beyond TUIs.

## 4.0 (breaking)

Codec divorce: DIR.Lib no longer pulls SharpAstro.Tiff /
.Exif / .Png / .Color.Icc transitively. BoxRasterizer.RenderToRgba returns an
RgbaImage; the (byte[], int, int) tuple form + RenderToPng helper are gone.
Consumers that need PNG/TIFF/JPEG encode the RgbaImage themselves.
