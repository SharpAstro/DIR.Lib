using System;
using System.Collections.Generic;
using System.Linq;
using DIR.Lib.Layout;

namespace DIR.Lib.ChildWindows
{
    /// <summary>Which part of a child window a <see cref="ChildWindowHit"/> names.</summary>
    public enum ChildWindowPart
    {
        /// <summary>The title bar: a press drags the window.</summary>
        Title,
        /// <summary>An edge or corner handle; <see cref="ChildWindowHit.Edge"/> says which.</summary>
        Handle,
        /// <summary>The close button.</summary>
        Close,
        /// <summary>The body under the host's content: swallows a press so it never reaches what is behind the window.</summary>
        Body,
        /// <summary>A tab in the <see cref="ChildWindowArrangement.Tabs"/> strip.</summary>
        Tab,
        /// <summary>The empty part of the tab strip: swallows a press so it never reaches the host content behind it.</summary>
        Strip,
    }

    /// <summary>What a press on a child window's chrome hit: the part and the child's id.</summary>
    /// <param name="Part">Which part of the window.</param>
    /// <param name="Id">The <see cref="ChildWindow.Id"/>.</param>
    public sealed record ChildWindowHit(ChildWindowPart Part, long Id) : HitResult
    {
        /// <summary>For a <see cref="ChildWindowPart.Handle"/>, the edges it drags; otherwise <see cref="ResizeEdge.None"/>.</summary>
        public ResizeEdge Edge { get; init; }
    }

    /// <summary>Colours of the window chrome. Active and idle differ in more than colour: see <see cref="ChildWindowStyle.ActiveMarker"/>.</summary>
    public readonly record struct ChildWindowColors(
        RGBAColor32 ActiveTitleBg,
        RGBAColor32 ActiveTitleText,
        RGBAColor32 IdleTitleBg,
        RGBAColor32 IdleTitleText,
        RGBAColor32 ActiveBorder,
        RGBAColor32 IdleBorder,
        RGBAColor32 BodyBg,
        RGBAColor32 CloseHoverBg)
    {
        /// <summary>The tab strip's colours, for <see cref="ChildWindowArrangement.Tabs"/>.</summary>
        public TabBarColors Tabs { get; init; } = new();

        /// <summary>A dark default: every text colour is at least 4.5:1 on the title bar it is drawn on.</summary>
        public static ChildWindowColors Dark { get; } = new(
            ActiveTitleBg: new(0x2C, 0x4C, 0x8C, 0xFF),
            ActiveTitleText: new(0xFF, 0xFF, 0xFF, 0xFF),
            IdleTitleBg: new(0x2A, 0x2A, 0x36, 0xFF),
            IdleTitleText: new(0xB4, 0xB4, 0xC0, 0xFF),
            ActiveBorder: new(0x6C, 0x9C, 0xFF, 0xFF),
            IdleBorder: new(0x4A, 0x4A, 0x58, 0xFF),
            BodyBg: new(0x14, 0x14, 0x1C, 0xFF),
            CloseHoverBg: new(0xB0, 0x30, 0x30, 0xFF));
    }

    /// <summary>The sizes, in design units, the window chrome is built from; one place for each number.</summary>
    public sealed record ChildWindowStyle
    {
        /// <summary>Title-bar height; the model's, so the keep-reachable clamp and the drawn bar agree.</summary>
        public float TitleBarHeight { get; init; } = ChildWindowSet.TitleBarHeight;
        /// <summary>Thickness of an edge handle; a corner handle is twice it square.</summary>
        public float HandleWidth { get; init; } = 6f;
        /// <summary>Title and marker text size.</summary>
        public float FontSize { get; init; } = 12f;
        /// <summary>Width of the active marker's cell.</summary>
        public float MarkerWidth { get; init; } = 16f;
        /// <summary>Marker glyph of the active child.</summary>
        public string ActiveMarker { get; init; } = "●";
        /// <summary>Marker glyph of an idle child.</summary>
        public string IdleMarker { get; init; } = "○";
        /// <summary>Close button glyph.</summary>
        public string CloseGlyph { get; init; } = "✕";
        /// <summary>
        /// Estimated width of a tab label's character as a fraction of the font size, used when <see cref="MeasureLabel"/>
        /// is null (the layout has no surface to measure on). 0.75 is above the mean advance of a proportional UI face
        /// (DejaVu Sans averages about 0.65 on mixed-case words), so a short title is never ellipsised while the strip has room.
        /// </summary>
        public float TabCharEm { get; init; } = 0.75f;
        /// <summary>Measures a tab label's width in design units at <see cref="FontSize"/>, for a host that can; null estimates with <see cref="TabCharEm"/>.</summary>
        public Func<string, float>? MeasureLabel { get; init; }
        /// <summary>The word drawn in the active child's title bar, beside its marker, so the active state is said in text and not in colour alone.</summary>
        public string ActiveLabel { get; init; } = "active";
        /// <summary>
        /// Thickness of the tab strip's rule and, doubled, of the active tab's accent (<see cref="TabStripMetrics.Border"/>).
        /// A cell host passes 0: each is a fill, which a cell surface keeps at one cell at least, so in a one-row strip the
        /// rule would take the row the labels need.
        /// </summary>
        public float TabRule { get; init; } = 1f;
        /// <summary>The default style.</summary>
        public static ChildWindowStyle Default { get; } = new();
    }

    /// <summary>
    /// Declares a <see cref="ChildWindowSet"/> as one <see cref="Layout.Node"/> tree: the host's content, then each
    /// child as an <see cref="Node.Anchored"/> free node in its own <see cref="Node.Overlay"/> layer, back to front.
    /// Every gesture is declared, so <see cref="InputRouter"/> routes it and the host writes no pointer code.
    /// </summary>
    /// <remarks>
    /// <para>Arrange the tree into <see cref="ChildWindowSet.Host"/>'s rect (call <see cref="ChildWindowSet.NoteHost"/>
    /// with it): child positions are design-unit offsets from that rect's origin.</para>
    /// <para><b>Press to front.</b> A press on a content control reaches the control (the topmost region), so the window
    /// declares <see cref="Node.OnPressWithin"/>: the router tells the topmost window under the point before the control
    /// acts, and the control still gets its press.</para>
    /// <para><b>Minimised and maximised.</b> A <see cref="ChildWindow.Maximised"/> child is drawn over the whole host, with no
    /// resize handles and no drag; a <see cref="ChildWindow.Minimised"/> child is left out, and the host shows it where it can
    /// be restored (a task strip, a menu), since where that is depends on the host.</para>
    /// <para><b>Handles.</b> The edge and corner handles are laid out over the window by the engine (thin rows and columns,
    /// stretched along each edge), so they keep their place and extent on a cell surface. None covers the close button:
    /// the top edge stops short of it and the right edge starts below the title row, so the top-right has no corner handle.</para>
    /// <para><b>A window is a surface</b> (<see cref="Node.IsSurface"/>, 11.86): within its rect nothing painted before it,
    /// the host's content or a window behind it, lights, explains itself, sets the cursor, takes a press or the wheel.</para>
    /// <para><b>Free layout.</b> Each window is fixed-size at the model's bounds, so a change in one window's content
    /// moves no other window. Handles and the drag use totals from the press, not increments, so a clamped window
    /// follows the pointer back as soon as it returns.</para>
    /// </remarks>
    public static class ChildWindowsLayout
    {
        /// <summary>Builds the tree.</summary>
        /// <param name="content">The host's own content, drawn beneath the windows.</param>
        /// <param name="set">The windows; read, and written by the declared gestures.</param>
        /// <param name="windowContent">Per-child content, drawn in the window's body.</param>
        /// <param name="colors">Chrome colours.</param>
        /// <param name="style">Chrome sizes.</param>
        /// <param name="scale">The design-to-surface mapping the tree is arranged at, which a drag's pointer delta is divided by.</param>
        /// <param name="onChanged">Invoked when a gesture changed the set, so a host can ask for a frame.</param>
        public static Node Build(
            Node content,
            ChildWindowSet set,
            Func<ChildWindow, Node> windowContent,
            in ChildWindowColors colors,
            ChildWindowStyle? style,
            DesignScale scale,
            Action? onChanged = null)
        {
            ArgumentNullException.ThrowIfNull(content);
            ArgumentNullException.ThrowIfNull(set);
            ArgumentNullException.ThrowIfNull(windowContent);
            style ??= ChildWindowStyle.Default;
            var s = scale.OrOne();
            var colorsCopy = colors;

            var host = set.Host;
            var activeId = set.Active?.Id ?? 0;

            if (set.Arrangement == ChildWindowArrangement.Tabs)
            {
                return BuildTabs(content, set, windowContent, colorsCopy, style, onChanged);
            }

            var root = content;
            for (var i = set.Children.Count - 1; i >= 0; i--)   // back to front
            {
                var c = set.Children[i];
                if (c.Minimised) continue;                       // the host's to show; see the remarks
                var b = c.Maximised ? host : c.Bounds;
                var window = BuildWindow(set, c, b, windowContent, colorsCopy, style, s, onChanged, c.Id == activeId, framed: !c.Maximised);
                root = Layout.Builder.Overlay(root, new Node.Anchored(window, null, b.X - host.X, b.Y - host.Y, 0f, false)
                {
                    OffsetsPerAxis = true,                       // design units on both axes, so a cell host places it right
                });
            }

            return root;
        }

        private static Action<PointerPress> Activate(ChildWindowSet set, long id, Action? onChanged) => _ =>
        {
            var v = set.Version;
            set.BringToFront(id);
            if (set.Version != v) onChanged?.Invoke();
        };

        private static Node BuildWindow(ChildWindowSet set, ChildWindow c, RectF32 b, Func<ChildWindow, Node> windowContent,
            ChildWindowColors colors, ChildWindowStyle style, DesignScale s, Action? onChanged, bool active, bool framed)
        {
            var id = c.Id;
            var titleBg = active ? colors.ActiveTitleBg : colors.IdleTitleBg;
            var titleInk = active ? colors.ActiveTitleText : colors.IdleTitleText;

            var marker = Layout.Builder.Text(active ? style.ActiveMarker : style.IdleMarker, style.FontSize, titleInk,
                    TextAlign.Center, TextAlign.Center)
                .WFixed(style.MarkerWidth).HStar()
                .WithTooltip(active ? "Active window" : "Inactive window");

            var title = Layout.Builder.Text(c.Title, style.FontSize, titleInk, TextAlign.Near, TextAlign.Center)
                .WStar().HStar()
                .WithTooltip(c.Title);

            var close = Layout.Builder.Text(style.CloseGlyph, style.FontSize, titleInk, TextAlign.Center, TextAlign.Center)
                .WFixed(style.TitleBarHeight).HStar()
                .BgHover(colors.CloseHoverBg)
                .WithTooltip("Close")
                .Clickable(new ChildWindowHit(ChildWindowPart.Close, id), _ =>
                {
                    set.Close(id);
                    onChanged?.Invoke();
                }, CursorKind.Pointer);

            // The active state in words too: a marker shape and a label, so neither colour nor a tooltip carries it alone.
            var parts = active
                ? new[] { marker, title, Layout.Builder.Text(style.ActiveLabel, style.FontSize, titleInk, TextAlign.Near, TextAlign.Center).HStar(), close }
                : new[] { marker, title, close };
            var titleBar = Layout.Builder.HStack(parts)
                .WithGap(2f)
                .Bg(titleBg)
                .RowH(style.TitleBarHeight);
            titleBar = framed
                ? titleBar.Pressable(new ChildWindowHit(ChildWindowPart.Title, id), press => BeginMove(set, id, press, s, onChanged), CursorKind.Move)
                : titleBar.Clickable(new ChildWindowHit(ChildWindowPart.Title, id), cursor: CursorKind.Default);

            var body = Layout.Builder.VStack(windowContent(c))
                .Bg(colors.BodyBg)
                .WStar().HStar()
                .Clickable(new ChildWindowHit(ChildWindowPart.Body, id), cursor: CursorKind.Default);

            var column = Layout.Builder.VStack(titleBar, body)
                .WFixed(b.Width).HFixed(b.Height)
                .Border(active ? colors.ActiveBorder : colors.IdleBorder);

            var node = framed ? Layout.Builder.Overlay(column, Handles(set, id, style, s, onChanged)) : column;

            // A surface (Node.IsSurface): nothing painted before the window, the host's content or a window behind it,
            // gets the pointer within its rect, the border included, where no title, body or handle region lies.
            return node.WithPressWithin(Activate(set, id, onChanged)).AsSurface();
        }

        // ---- drag ----

        private static DragCapture? BeginMove(ChildWindowSet set, long id, PointerPress press, DesignScale s, Action? onChanged)
        {
            if (press.Button != MouseButton.Left || set.Find(id) is not { } start) return null;
            var origin = start.Bounds;
            void To(float x, float y)
            {
                if (set.Find(id) is not { } c) return;
                var tx = origin.X + s.ToDesignX(x - press.X);
                var ty = origin.Y + s.ToDesignY(y - press.Y);
                if (set.Move(id, tx - c.Bounds.X, ty - c.Bounds.Y)) onChanged?.Invoke();
            }
            return new DragCapture(m => To(m.X, m.Y), m => To(m.X, m.Y));
        }

        private static DragCapture? BeginResize(ChildWindowSet set, long id, ResizeEdge edge, PointerPress press, DesignScale s, Action? onChanged)
        {
            if (press.Button != MouseButton.Left || set.Find(id) is not { } start) return null;
            var origin = start.Bounds;
            void To(float x, float y)
            {
                if (set.Find(id) is not { } c) return;
                var cur = c.Bounds;
                float dx = 0f, dy = 0f;
                var tdx = s.ToDesignX(x - press.X);
                var tdy = s.ToDesignY(y - press.Y);
                if (edge.HasFlag(ResizeEdge.Left)) dx = origin.X + tdx - cur.X;
                else if (edge.HasFlag(ResizeEdge.Right)) dx = origin.Right + tdx - cur.Right;
                if (edge.HasFlag(ResizeEdge.Top)) dy = origin.Y + tdy - cur.Y;
                else if (edge.HasFlag(ResizeEdge.Bottom)) dy = origin.Bottom + tdy - cur.Bottom;
                if (set.Resize(id, edge, dx, dy)) onChanged?.Invoke();
            }
            return new DragCapture(m => To(m.X, m.Y), m => To(m.X, m.Y));
        }

        /// <summary>The <see cref="Content.Fill"/> key of a handle leaf. The painter draws no Fill by itself; a host's drawFill skips this key.</summary>
        public const string HandleFillKey = "DIR.Lib.ChildWindows.Handle";

        // The handles as one layer the size of the window: a top row, a middle row of a left and a right column, and a bottom
        // row, each thin side a fixed extent and each long side a star, so the engine places them on any surface. Each piece is a
        // Fill leaf: it paints nothing, but a cell context gives it at least one cell, where an empty spacer would round to none.
        // The top-right square of TitleBarHeight + HandleWidth is left to the close button, so no handle covers any of it.
        private static Node Handles(ChildWindowSet set, long id, ChildWindowStyle style, DesignScale s, Action? onChanged)
        {
            var e = style.HandleWidth;
            var k = e * 2f;                                  // corner reach along each edge
            var closeKeep = style.TitleBarHeight + e;        // the top-right square the close button keeps

            Node H(ResizeEdge edge, CursorKind cursor)
                => Layout.Builder.Fill(key: HandleFillKey)
                    .Pressable(new ChildWindowHit(ChildWindowPart.Handle, id) { Edge = edge },
                        press => BeginResize(set, id, edge, press, s, onChanged), cursor);
            Node Gap() => Layout.Builder.Fill(key: HandleFillKey);

            const ResizeEdge TL = ResizeEdge.Top | ResizeEdge.Left;
            const ResizeEdge BL = ResizeEdge.Bottom | ResizeEdge.Left;
            const ResizeEdge BR = ResizeEdge.Bottom | ResizeEdge.Right;

            var top = Layout.Builder.HStack(
                    H(TL, CursorKind.ResizeNWSE).WFixed(k).HStar(),
                    H(ResizeEdge.Top, CursorKind.ResizeNS).WStar().HStar(),
                    Gap().WFixed(closeKeep).HStar())
                .WStar().HFixed(e);
            var left = Layout.Builder.VStack(
                    H(TL, CursorKind.ResizeNWSE).WStar().HFixed(k - e),
                    H(ResizeEdge.Left, CursorKind.ResizeEW).WStar().HStar(),
                    H(BL, CursorKind.ResizeNESW).WStar().HFixed(k - e))
                .WFixed(e).HStar();
            var right = Layout.Builder.VStack(
                    Gap().WStar().HFixed(closeKeep - e),
                    H(ResizeEdge.Right, CursorKind.ResizeEW).WStar().HStar(),
                    H(BR, CursorKind.ResizeNWSE).WStar().HFixed(k - e))
                .WFixed(e).HStar();
            var middle = Layout.Builder.HStack(left, Layout.Builder.Spacer().WStar().HStar(), right).WStar().HStar();
            var bottom = Layout.Builder.HStack(
                    H(BL, CursorKind.ResizeNESW).WFixed(k).HStar(),
                    H(ResizeEdge.Bottom, CursorKind.ResizeNS).WStar().HStar(),
                    H(BR, CursorKind.ResizeNWSE).WFixed(k).HStar())
                .WStar().HFixed(e);

            return Layout.Builder.VStack(top, middle, bottom).WStar().HStar();
        }

        // ---- tabs ----

        private static Node BuildTabs(Node content, ChildWindowSet set, Func<ChildWindow, Node> windowContent,
            ChildWindowColors colors, ChildWindowStyle style, Action? onChanged)
        {
            var host = set.Host;
            var ordered = set.Children.OrderBy(c => c.Id).ToList();       // stable: tabs never reorder under the pointer
            if (ordered.Count == 0) return content;

            var items = new List<TabItem<long>>(ordered.Count);
            foreach (var c in ordered)
            {
                var id = c.Id;
                items.Add(new TabItem<long>(c.Title, id)
                {
                    Tooltip = c.Title,
                    OnSelect = v =>
                    {
                        var ver = set.Version;
                        set.BringToFront(v);
                        if (set.Version != ver) onChanged?.Invoke();
                    },
                    OnClose = v =>
                    {
                        set.Close(v);
                        onChanged?.Invoke();
                    },
                });
            }

            var active = set.Active ?? ordered[0];
            var activeIndex = ordered.FindIndex(c => c.Id == active.Id);
            var m = new TabStripMetrics(ChildWindowSet.TabStripHeight, style.FontSize, 8f, style.TabRule, 0f, 14f, 48f, 180f);
            var measure = style.MeasureLabel ?? (label => label.Length * style.FontSize * style.TabCharEm);
            var strip = TabStripTree.Build(items, activeIndex, null, null, host.Width, measure,
                new TabStripOptions { Metrics = m, Colors = colors.Tabs, Decoration = TabLabelDecoration.None }).Root;

            // The strip's empty part swallows a press: the tabs are regions over it, and right of the last tab a press
            // would otherwise reach the host content hidden behind the tabbed windows.
            var stripLayer = Layout.Builder.VStack(strip.WStar().HStar())
                .WStar().HFixed(ChildWindowSet.TabStripHeight)
                .Clickable(new ChildWindowHit(ChildWindowPart.Strip, active.Id), cursor: CursorKind.Default);

            var body = Layout.Builder.VStack(windowContent(active))
                .Bg(colors.BodyBg)
                .WStar().HStar()
                .Clickable(new ChildWindowHit(ChildWindowPart.Body, active.Id), cursor: CursorKind.Default);

            var box = Layout.Builder.VStack(stripLayer, body)
                .WFixed(host.Width).HFixed(host.Height)
                .WithPressWithin(Activate(set, active.Id, onChanged))
                .AsSurface();

            return Layout.Builder.Overlay(content, new Node.Anchored(box, null, 0f, 0f, 0f, false) { OffsetsPerAxis = true });
        }
    }
}
