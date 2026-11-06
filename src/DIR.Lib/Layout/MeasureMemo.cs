using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace DIR.Lib.Layout;

/// <summary>
/// What decides whether a subtree's measure may be kept across calls: it must read nothing that changes behind the
/// node's back. Nodes and their content are immutable records, so a measure is a pure function of the node, the
/// available size and the context, with two exceptions: a <see cref="Content.TextEditor"/> measures its
/// <c>State.Text</c> and a <see cref="Content.TextInput"/> its <c>State.Placeholder</c>, and both live in mutable
/// caller-owned state. A subtree holding either is never kept, because the same node would measure differently
/// after a keystroke.
/// </summary>
internal static class MeasurePurity
{
    private sealed class Box(bool pure)
    {
        public readonly bool Pure = pure;
    }

    private static readonly ConditionalWeakTable<Node, Box> Cache = new();

    /// <summary>True when nothing under <paramref name="node"/> measures from mutable state. Computed once per node.</summary>
    public static bool IsPure(Node node)
    {
        if (Cache.TryGetValue(node, out var known))
        {
            return known.Pure;
        }

        var pure = Compute(node);
        return Cache.GetValue(node, _ => new Box(pure)).Pure;
    }

    private static bool Compute(Node node)
    {
        switch (node)
        {
            case Node.Leaf { Content: Content.TextEditor or Content.TextInput }:
                return false;
            case Node.Leaf:
                return true;
            case Node.Stack stack:
                return AllPure(stack.Children);
            case Node.Grid grid:
                return AllPure(grid.Cells);
            case Node.Wrap wrap:
                return AllPure(wrap.Children);
            case Node.Overlay overlay:
                return IsPure(overlay.Base) && IsPure(overlay.Top);
            case Node.Anchored anchored:
                return IsPure(anchored.Child);
            case Node.Dock dock:
                foreach (var docked in dock.Docked)
                {
                    if (!IsPure(docked.Child))
                    {
                        return false;
                    }
                }

                return IsPure(dock.Fill);
            case Node.Split split:
                return IsPure(split.First) && IsPure(split.Second);
            default:
                // A node kind this does not know may read anything: keep nothing of it.
                return false;
        }
    }

    private static bool AllPure(System.Collections.Immutable.ImmutableArray<Node> nodes)
    {
        foreach (var child in nodes)
        {
            if (!IsPure(child))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// The measure memo for one coordinate type. Two layers:
/// <list type="bullet">
/// <item>Within one public <see cref="Engine.Measure{T}"/> or <see cref="Engine.Arrange{T}"/> call, always: a
/// per-thread table keyed by node reference, available size and context reference, so no child is measured twice at
/// the same available size in one pass (the state cannot change during a pass).</item>
/// <item>Across calls, only when the context names a <see cref="IMeasureContext{T}.MeasureKey"/> and the subtree is
/// pure (<see cref="MeasurePurity"/>): a small ring of (available, key, result) slots per node in a
/// <see cref="ConditionalWeakTable{TKey, TValue}"/>, so an entry dies with its node.</item>
/// </list>
/// Nodes are keyed by REFERENCE, never by record equality, which would walk the whole subtree and equate two
/// distinct nodes that merely look alike.
/// </summary>
internal static class MeasureMemo<T> where T : INumber<T>
{
    private const int Slots = 4;

    private struct Slot
    {
        public Size<T> Available;
        public object Key;
        public Size<T> Result;
    }

    private sealed class Entry(bool pure)
    {
        public readonly bool Pure = pure;
        public readonly object Gate = new();
        public readonly Slot[] Slots = new Slot[MeasureMemo<T>.Slots];
        public int Count;
        public int Next;
    }

    private static readonly ConditionalWeakTable<Node, Entry> Kept = new();

    private readonly struct CallKey(Node node, IMeasureContext<T> ctx, Size<T> available) : IEquatable<CallKey>
    {
        private readonly Node _node = node;
        private readonly IMeasureContext<T> _ctx = ctx;
        private readonly Size<T> _available = available;

        public bool Equals(CallKey other) =>
            ReferenceEquals(_node, other._node) && ReferenceEquals(_ctx, other._ctx)
            && _available.Width == other._available.Width && _available.Height == other._available.Height;

        public override bool Equals(object? obj) => obj is CallKey other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(RuntimeHelpers.GetHashCode(_node), RuntimeHelpers.GetHashCode(_ctx), _available.Width, _available.Height);
    }

    [ThreadStatic]
    private static Dictionary<CallKey, Size<T>>? _call;

    [ThreadStatic]
    private static int _depth;

    /// <summary>Opens (or joins) this thread's pass. Pair with <see cref="Exit"/> in a finally.</summary>
    public static void Enter() => _depth++;

    /// <summary>Leaves the pass; the outermost exit drops the per-call table, so nothing outlives the call.</summary>
    public static void Exit()
    {
        if (--_depth == 0)
        {
            _call?.Clear();
        }
    }

    public static bool TryGet(Node node, Size<T> available, IMeasureContext<T> ctx, out Size<T> size, out object? key)
    {
        key = ctx.MeasureKey;
        if (_call is { } call && call.TryGetValue(new CallKey(node, ctx, available), out size))
        {
            return true;
        }

        if (key is not null && Kept.TryGetValue(node, out var entry) && entry.Pure)
        {
            lock (entry.Gate)
            {
                for (var i = 0; i < entry.Count; i++)
                {
                    ref var slot = ref entry.Slots[i];
                    if (slot.Available.Width == available.Width && slot.Available.Height == available.Height
                        && key.Equals(slot.Key))
                    {
                        size = slot.Result;
                        return true;
                    }
                }
            }
        }

        size = default;
        return false;
    }

    public static void Store(Node node, Size<T> available, IMeasureContext<T> ctx, object? key, Size<T> size)
    {
        (_call ??= [])[new CallKey(node, ctx, available)] = size;

        if (key is null)
        {
            return;
        }

        if (!Kept.TryGetValue(node, out var entry))
        {
            var created = new Entry(MeasurePurity.IsPure(node));
            entry = Kept.GetValue(node, _ => created);
        }

        if (!entry.Pure)
        {
            return;
        }

        lock (entry.Gate)
        {
            // Replace the same (available, key) in place, else the oldest slot.
            var at = -1;
            for (var i = 0; i < entry.Count; i++)
            {
                ref var slot = ref entry.Slots[i];
                if (slot.Available.Width == available.Width && slot.Available.Height == available.Height
                    && key.Equals(slot.Key))
                {
                    at = i;
                    break;
                }
            }

            if (at < 0)
            {
                if (entry.Count < Slots)
                {
                    at = entry.Count++;
                }
                else
                {
                    at = entry.Next;
                    entry.Next = (entry.Next + 1) % Slots;
                }
            }

            entry.Slots[at] = new Slot { Available = available, Key = key, Result = size };
        }
    }
}
