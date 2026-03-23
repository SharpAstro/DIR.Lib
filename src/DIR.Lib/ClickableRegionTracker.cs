using System;
using System.Collections.Generic;

namespace DIR.Lib
{
    /// <summary>
    /// Manages a list of clickable regions registered during rendering.
    /// Used by both pixel widgets (GPU) and terminal widgets (TUI) for
    /// unified click dispatch via <see cref="HitTestAndDispatch"/>.
    /// </summary>
    public class ClickableRegionTracker
    {
        private readonly List<ClickableRegion> _regions = [];

        /// <summary>Clears all regions. Call at the start of each render pass.</summary>
        public void BeginFrame() => _regions.Clear();

        /// <summary>Registers a clickable region with an optional click handler.</summary>
        public void Register(float x, float y, float w, float h, HitResult result, Action<InputModifier>? onClick = null)
            => _regions.Add(new ClickableRegion(x, y, w, h, result, onClick));

        /// <summary>
        /// Hit-tests using regions registered during the last render pass.
        /// Returns the last (topmost) matching region's result, or null.
        /// </summary>
        public HitResult? HitTest(float x, float y)
        {
            for (var i = _regions.Count - 1; i >= 0; i--)
            {
                var r = _regions[i];
                if (x >= r.X && x < r.X + r.Width && y >= r.Y && y < r.Y + r.Height)
                {
                    return r.Result;
                }
            }
            return null;
        }

        /// <summary>
        /// Hit-tests and invokes the <see cref="ClickableRegion.OnClick"/> handler if present.
        /// Returns the hit result, or null if no region matched.
        /// </summary>
        public HitResult? HitTestAndDispatch(float x, float y, InputModifier modifiers = InputModifier.None)
        {
            for (var i = _regions.Count - 1; i >= 0; i--)
            {
                var r = _regions[i];
                if (x >= r.X && x < r.X + r.Width && y >= r.Y && y < r.Y + r.Height)
                {
                    r.OnClick?.Invoke(modifiers);
                    return r.Result;
                }
            }
            return null;
        }

        /// <summary>
        /// Returns all <see cref="TextInputState"/> instances registered during the last render pass,
        /// in registration order. Used for Tab/Shift+Tab cycling.
        /// </summary>
        public List<TextInputState> GetRegisteredTextInputs()
        {
            var result = new List<TextInputState>();
            foreach (var r in _regions)
            {
                if (r.Result is HitResult.TextInputHit { Input: { } input } && !result.Contains(input))
                {
                    result.Add(input);
                }
            }
            return result;
        }
    }
}
