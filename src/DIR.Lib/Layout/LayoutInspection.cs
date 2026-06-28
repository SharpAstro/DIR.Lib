namespace DIR.Lib
{
    /// <summary>
    /// Process-wide opt-in switch for the DEBUG UI inspector's layout capture. Off by default so
    /// production paints carry zero overhead; the inspector wiring flips it on once (when a
    /// describe-layout callback is supplied -- see <c>DebugInspector.Attach</c>) so every
    /// <see cref="PixelWidgetBase{TSurface}"/> retains the arranged <see cref="Layout.ArrangedNode{T}"/>
    /// tree it paints for the inspector to serialize. Set once, never in a hot loop.
    /// </summary>
    public static class LayoutInspection
    {
        /// <summary>When true, <see cref="PixelWidgetBase{TSurface}.PaintLayout"/> retains the arranged
        /// nodes it paints (cleared each <c>BeginFrame</c>) so
        /// <see cref="PixelWidgetBase{TSurface}.GetCapturedLayout"/> can hand them to the debug inspector.
        /// Written once on inspector attach, read on the render thread during paint; a torn read only
        /// costs a one-frame delay in starting capture, so no synchronization is needed.</summary>
        public static bool Enabled;
    }
}
