namespace DIR.Lib.ChildWindows
{
    /// <summary>
    /// One floating child window inside a host: an id, a title and bounds in DESIGN units. It owns no
    /// surface and draws nothing; <see cref="ChildWindowSet"/> mutates it and a layout draws it. Properties
    /// are init-only or set by the owning set, so a later field is additive.
    /// </summary>
    public sealed class ChildWindow
    {
        /// <summary>Stable id the set stamped when the child was added; never reused.</summary>
        public long Id { get; init; }

        /// <summary>Title shown in the title bar.</summary>
        public string Title { get; internal set; } = "";

        /// <summary>
        /// Where the child is NOW, in design units against the host: its free position, or the place the
        /// current <see cref="ChildWindowSet.Arrangement"/> gave it.
        /// </summary>
        public RectF32 Bounds { get; internal set; }

        /// <summary>The child's own free bounds, which an arrangement does not overwrite and Free restores.</summary>
        internal RectF32 FreeBounds { get; set; }

        /// <summary>True only when the host asked for it (<see cref="ChildWindowSet.SetMinimised"/>); the set never minimises by itself. <see cref="ChildWindowsLayout"/> leaves a minimised child to the host to show.</summary>
        public bool Minimised { get; internal set; }

        /// <summary>True only when the host asked for it (<see cref="ChildWindowSet.SetMaximised"/>); the set never maximises by itself. <see cref="ChildWindowsLayout"/> draws a maximised child over the whole host.</summary>
        public bool Maximised { get; internal set; }

        /// <summary>The title bar's rectangle: the top <see cref="ChildWindowSet.TitleBarHeight"/> of <see cref="Bounds"/>.</summary>
        public RectF32 TitleBar => new(Bounds.X, Bounds.Y, Bounds.Width, System.Math.Min(ChildWindowSet.TitleBarHeight, Bounds.Height));
    }
}
