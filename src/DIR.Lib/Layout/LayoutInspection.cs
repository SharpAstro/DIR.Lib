using System;

namespace DIR.Lib
{
    /// <summary>
    /// Inert. Layout capture is unconditional now, so there is nothing to switch on.
    /// </summary>
    /// <remarks>
    /// <para>It was a process-wide opt-in so production paints carried zero overhead, with the inspector
    /// wiring flipping it on once. Damage-based repaint changed what the capture is FOR: the arranged
    /// tree is diffed against the previous frame to decide which rects need painting at all, so it is
    /// load-bearing on every frame rather than a debug aid, and a flag recording whether we kept it
    /// would state a fact twice.</para>
    /// <para>The cost is one list of structs per widget per frame, accepted deliberately against the
    /// alternative: repainting the whole window to change one number in a status bar measured 8% GPU on
    /// an Adreno X1-85, and the only two states available without damage tracking were that and zero.
    /// </para>
    /// <para>Kept as an obsolete no-op rather than deleted because it is PUBLIC, and removing a public
    /// type is a breaking change -- which under this org's versioning means a major bump and a re-pin
    /// across Console.Lib, SdlVulkan.Renderer, WebGl.Renderer and TianWen, for a field nothing reads.
    /// Delete it at the next DIR.Lib major.</para>
    /// </remarks>
    [Obsolete("Layout capture is unconditional; this field is no longer read. Remove the assignment. " +
        "Scheduled for deletion at the next DIR.Lib major.")]
    public static class LayoutInspection
    {
        /// <summary>No longer read. Setting it has no effect.</summary>
        public static bool Enabled;
    }
}
