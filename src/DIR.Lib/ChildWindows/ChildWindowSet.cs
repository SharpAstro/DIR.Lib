using System;
using System.Collections.Generic;
using System.Linq;

namespace DIR.Lib.ChildWindows
{
    /// <summary>How the set lays its children out.</summary>
    public enum ChildWindowArrangement
    {
        /// <summary>Each child sits where the person put it.</summary>
        Free,
        /// <summary>Children overlap in a fixed down-and-right step.</summary>
        Cascaded,
        /// <summary>Children share the host in a grid with no overlap.</summary>
        Tiled,
        /// <summary>Every child fills the host below a tab strip; the active one is shown.</summary>
        Tabs,
    }

    /// <summary>The edges a resize drag holds; combine two for a corner.</summary>
    [Flags]
    public enum ResizeEdge
    {
        /// <summary>No edge.</summary>
        None = 0,
        /// <summary>Left edge.</summary>
        Left = 1,
        /// <summary>Top edge.</summary>
        Top = 2,
        /// <summary>Right edge.</summary>
        Right = 4,
        /// <summary>Bottom edge.</summary>
        Bottom = 8,
    }

    /// <summary>
    /// The surface-neutral model of floating child windows in one host: front-to-back order, the one active
    /// child, move, resize, close, the keep-reachable clamp and the Cascade / Tile / Tabs commands. Geometry
    /// is design units against a host rectangle the caller passes (<see cref="NoteHost"/>); nothing is read
    /// from a surface. Not thread-safe. <see cref="Version"/> and <see cref="Changed"/> work as on
    /// <c>NotificationFeed</c>.
    /// </summary>
    public sealed class ChildWindowSet
    {
        /// <summary>Height of a child's title bar, design units.</summary>
        public const float TitleBarHeight = 24f;
        /// <summary>Width of the title-bar strip that must stay inside the host on every edge, design units.</summary>
        public const float KeepReachableStrip = 48f;
        /// <summary>Smallest width a resize can leave a child.</summary>
        public const float MinWidth = 120f;
        /// <summary>Smallest height a resize can leave a child.</summary>
        public const float MinHeight = 64f;
        /// <summary>Down-and-right step between cascaded children, design units.</summary>
        public const float CascadeStep = 28f;
        /// <summary>The one gap between neighbouring tiles, design units.</summary>
        public const float TileGap = 4f;
        /// <summary>Height reserved at the top of the host for the tab strip in <see cref="ChildWindowArrangement.Tabs"/>.</summary>
        public const float TabStripHeight = 28f;

        private readonly List<ChildWindow> _children = [];   // front first
        private long _nextId = 1;
        private long _activeId;
        private RectF32 _host;
        private bool _hasHost;

        /// <summary>The children, front to back.</summary>
        public IReadOnlyList<ChildWindow> Children => _children;

        /// <summary>The active child, or null when there are none.</summary>
        public ChildWindow? Active => _children.FirstOrDefault(c => c.Id == _activeId);

        /// <summary>The current arrangement.</summary>
        public ChildWindowArrangement Arrangement { get; private set; } = ChildWindowArrangement.Free;

        /// <summary>The host rectangle last passed in, or an empty one before any call.</summary>
        public RectF32 Host => _host;

        /// <summary>Rises on every change a view could show; a host repaints when it moved.</summary>
        public long Version { get; private set; }

        /// <summary>Raised after <see cref="Version"/> rises.</summary>
        public event Action? Changed;

        private void Touch()
        {
            Version++;
            Changed?.Invoke();
        }

        /// <summary>Finds a child by id, or null.</summary>
        public ChildWindow? Find(long id) => _children.FirstOrDefault(c => c.Id == id);

        /// <summary>Adds a child in front and makes it active. Bounds are held to the minimum size and kept reachable.</summary>
        public ChildWindow Add(string title, RectF32 bounds)
        {
            var c = new ChildWindow { Id = _nextId++ };
            c.Title = title ?? "";
            var b = Clamp(EnforceMin(bounds));
            c.Bounds = b;
            c.FreeBounds = b;
            _children.Insert(0, c);
            _activeId = c.Id;
            if (Arrangement != ChildWindowArrangement.Free) ReapplyArrangement();
            Touch();
            return c;
        }

        /// <summary>Raises the child to the front and makes it active (what a press does).</summary>
        public bool BringToFront(long id)
        {
            var i = _children.FindIndex(c => c.Id == id);
            if (i < 0) return false;
            var c = _children[i];
            if (i == 0 && _activeId == id) return true;
            _children.RemoveAt(i);
            _children.Insert(0, c);
            _activeId = id;
            Touch();
            return true;
        }

        /// <summary>Closes the child. Closing the active one activates the next in order (the one behind it, else the new last).</summary>
        public bool Close(long id)
        {
            var i = _children.FindIndex(c => c.Id == id);
            if (i < 0) return false;
            bool wasActive = _activeId == id;
            _children.RemoveAt(i);
            if (wasActive)
                _activeId = _children.Count == 0 ? 0 : _children[Math.Min(i, _children.Count - 1)].Id;
            if (Arrangement is ChildWindowArrangement.Tiled or ChildWindowArrangement.Cascaded) ReapplyArrangement();
            Touch();
            return true;
        }

        /// <summary>Changes a child's title.</summary>
        public bool Rename(long id, string title)
        {
            var c = Find(id);
            if (c is null) return false;
            c.Title = title ?? "";
            Touch();
            return true;
        }

        /// <summary>Records that the host asked for the child to be minimised or restored.</summary>
        public bool SetMinimised(long id, bool value)
        {
            var c = Find(id);
            if (c is null) return false;
            c.Minimised = value;
            Touch();
            return true;
        }

        /// <summary>Records that the host asked for the child to be maximised or restored.</summary>
        public bool SetMaximised(long id, bool value)
        {
            var c = Find(id);
            if (c is null) return false;
            c.Maximised = value;
            Touch();
            return true;
        }

        /// <summary>
        /// Moves a child by a drag delta. A move in an arranged set frees every child where it stands first
        /// (the person took over). The result is clamped so the title-bar strip stays inside the host.
        /// </summary>
        public bool Move(long id, float dx, float dy)
        {
            var c = Find(id);
            if (c is null) return false;
            Free();
            var b = c.Bounds;
            Set(c, Clamp(new RectF32(b.X + dx, b.Y + dy, b.Width, b.Height)));
            Touch();
            return true;
        }

        /// <summary>
        /// Resizes a child by dragging <paramref name="edge"/> by (dx, dy). The opposite edge holds still,
        /// the size never drops below <see cref="MinWidth"/> x <see cref="MinHeight"/>, and the result is kept reachable.
        /// </summary>
        public bool Resize(long id, ResizeEdge edge, float dx, float dy)
        {
            var c = Find(id);
            if (c is null) return false;
            Free();
            var b = c.Bounds;
            float l = b.X, t = b.Y, r = b.Right, bt = b.Bottom;
            if (edge.HasFlag(ResizeEdge.Left)) l = Math.Min(l + dx, r - MinWidth);
            if (edge.HasFlag(ResizeEdge.Right)) r = Math.Max(r + dx, l + MinWidth);
            if (edge.HasFlag(ResizeEdge.Top)) t = Math.Min(t + dy, bt - MinHeight);
            if (edge.HasFlag(ResizeEdge.Bottom)) bt = Math.Max(bt + dy, t + MinHeight);
            Set(c, Clamp(new RectF32(l, t, r - l, bt - t)));
            Touch();
            return true;
        }

        /// <summary>Notes a new host rectangle: tiles and tabs refit it, everything else is re-clamped so each child stays reachable.</summary>
        public void NoteHost(RectF32 host)
        {
            if (_hasHost && host == _host) return;
            _host = host;
            _hasHost = true;
            if (Arrangement is ChildWindowArrangement.Tiled or ChildWindowArrangement.Tabs)
                ReapplyArrangement();
            foreach (var c in _children)
            {
                c.FreeBounds = Clamp(c.FreeBounds);
                if (Arrangement is ChildWindowArrangement.Free or ChildWindowArrangement.Cascaded)
                    c.Bounds = Clamp(c.Bounds);
            }
            Touch();
        }

        /// <summary>Returns to free positions: every child goes back to where it was before an arrangement.</summary>
        public void ShowFree()
        {
            if (Arrangement == ChildWindowArrangement.Free) return;
            foreach (var c in _children) c.Bounds = Clamp(c.FreeBounds);
            Arrangement = ChildWindowArrangement.Free;
            Touch();
        }

        /// <summary>
        /// Cascades the children: each a <see cref="CascadeStep"/> down and right of the last, back child first,
        /// wrapping to the host's top-left when the next would leave the host; the front child ends last.
        /// </summary>
        public void Cascade(RectF32 host)
        {
            _host = host; _hasHost = true;
            Arrangement = ChildWindowArrangement.Cascaded;
            LayCascade();
            Touch();
        }

        /// <summary>
        /// Tiles the children in a near-square grid filling the host, <see cref="TileGap"/> apart. A portrait host
        /// prefers rows. A short last row (column) stretches to fill. The front child takes the first cell.
        /// </summary>
        public void Tile(RectF32 host)
        {
            _host = host; _hasHost = true;
            Arrangement = ChildWindowArrangement.Tiled;
            LayTile();
            Touch();
        }

        /// <summary>
        /// Shows the children as tabs: every child fills the host below the tab strip, the active one in front. Takes the
        /// host as <see cref="Cascade"/> and <see cref="Tile"/> do, so all three arrangement commands read alike.
        /// </summary>
        public void ShowAsTabs(RectF32 host)
        {
            _host = host; _hasHost = true;
            Arrangement = ChildWindowArrangement.Tabs;
            LayTabs();
            Touch();
        }

        // ---- internals ----

        private static void Set(ChildWindow c, RectF32 b) { c.Bounds = b; c.FreeBounds = b; }

        /// <summary>Leaves an arrangement: each child keeps where it stands as its free position.</summary>
        private void Free()
        {
            if (Arrangement == ChildWindowArrangement.Free) return;
            foreach (var c in _children) c.FreeBounds = c.Bounds;
            Arrangement = ChildWindowArrangement.Free;
        }

        private void ReapplyArrangement()
        {
            switch (Arrangement)
            {
                case ChildWindowArrangement.Cascaded: LayCascade(); break;
                case ChildWindowArrangement.Tiled: LayTile(); break;
                case ChildWindowArrangement.Tabs: LayTabs(); break;
            }
        }

        private static RectF32 EnforceMin(RectF32 b)
            => new(b.X, b.Y, Math.Max(b.Width, MinWidth), Math.Max(b.Height, MinHeight));

        /// <summary>The keep-reachable rule: a strip of the title bar stays inside the host on every edge.</summary>
        private RectF32 Clamp(RectF32 b)
        {
            if (!_hasHost) return b;
            float strip = Math.Min(KeepReachableStrip, b.Width);
            float minX = _host.X + strip - b.Width;
            float maxX = Math.Max(minX, _host.Right - strip);
            float x = Math.Clamp(b.X, minX, maxX);
            float y = Math.Clamp(b.Y, _host.Y, Math.Max(_host.Y, _host.Bottom - TitleBarHeight));
            return new RectF32(x, y, b.Width, b.Height);
        }

        private void LayCascade()
        {
            int k = 0;
            for (int i = _children.Count - 1; i >= 0; i--)
            {
                var c = _children[i];
                var size = c.FreeBounds.Size;
                float w = Math.Min(size.X, _host.Width), h = Math.Min(size.Y, _host.Height);
                float x = _host.X + k * CascadeStep, y = _host.Y + k * CascadeStep;
                if (k > 0 && (x + w > _host.Right || y + h > _host.Bottom))
                {
                    k = 0; x = _host.X; y = _host.Y;
                }
                c.Bounds = new RectF32(x, y, w, h);
                k++;
            }
        }

        private void LayTile()
        {
            int n = _children.Count;
            if (n == 0) return;
            bool portrait = _host.Height > _host.Width;
            int major = (int)Math.Ceiling(Math.Sqrt(n));
            int minor = (int)Math.Ceiling(n / (double)major);
            int idx = 0;
            if (!portrait)
            {
                // columns = ceil(sqrt n), rows fill.
                int cols = major, rows = minor;
                float cellH = (_host.Height - TileGap * (rows - 1)) / rows;
                for (int r = 0; r < rows; r++)
                {
                    int inRow = Math.Min(cols, n - idx);
                    float cellW = (_host.Width - TileGap * (inRow - 1)) / inRow;
                    for (int q = 0; q < inRow; q++)
                        _children[idx++].Bounds = new RectF32(_host.X + q * (cellW + TileGap), _host.Y + r * (cellH + TileGap), cellW, cellH);
                }
            }
            else
            {
                // rows = ceil(sqrt n), columns fill.
                int rows = major, cols = minor;
                float cellW = (_host.Width - TileGap * (cols - 1)) / cols;
                for (int q = 0; q < cols; q++)
                {
                    int inCol = Math.Min(rows, n - idx);
                    float cellH = (_host.Height - TileGap * (inCol - 1)) / inCol;
                    for (int r = 0; r < inCol; r++)
                        _children[idx++].Bounds = new RectF32(_host.X + q * (cellW + TileGap), _host.Y + r * (cellH + TileGap), cellW, cellH);
                }
            }
        }

        private void LayTabs()
        {
            var area = new RectF32(_host.X, _host.Y + TabStripHeight, _host.Width, Math.Max(0, _host.Height - TabStripHeight));
            foreach (var c in _children) c.Bounds = area;
        }
    }
}
