using System;
using System.Collections.Generic;
using System.Threading;

namespace DIR.Lib.Layout;

/// <summary>
/// Facts the layout engine keeps per node, by node reference (never by record equality, which would walk the subtree and equate
/// distinct nodes that merely look alike), for as long as the node goes on being asked about.
/// </summary>
/// <remarks>
/// <para>
/// Two generations of strong references, not a <see cref="System.Runtime.CompilerServices.ConditionalWeakTable{TKey, TValue}"/>.
/// Each entry of a weak table is a dependent GC handle, so a consumer that builds its tree anew every frame added and dropped
/// handles for every node of every frame, and paid for it in the collector and the finalizer: on pilot-gui's Ideas page (DE1's
/// data, 2026-11-28) the same window took 0.93 cores and 46 s of collection pause in 85 s with the weak tables, 0.36 cores and
/// 11 s with this (SharpAstro/DIR.Lib#341).
/// </para>
/// <para>
/// New facts go into the current generation. It becomes the previous one at <see cref="GenerationSize"/> entries, or once it is
/// <see cref="TurnAfter"/> old, at the next lookup; whatever was in the previous one before is let go. A lookup that finds a node in
/// the previous generation moves it to the current one. So a node read at least once per generation keeps its facts (a kept tree,
/// a kept row inside a rebuilt tree), and one nobody reads goes within two. The count lets go of the trees a host built once and
/// dropped; the age lets go of them in a host that still reads but no longer builds, so a node held only here (with what its
/// handlers capture, and a measure key's renderer) lives at most two generations past its last read while the host draws. A host
/// that draws nothing reads nothing, and its last facts stay until it draws again. A kept tree of more than
/// <see cref="GenerationSize"/> distinct nodes, read whole each frame, turns a generation every frame and measures part of itself
/// again; the windows these apps draw hold a few thousand.
/// </para>
/// <para>
/// Plain dictionaries swapped and cleared at a turn, under one lock held for a lookup or an insert and never while a fact is
/// computed. A lock-free <see cref="BoundedCache{TKey, TValue}"/> (concurrent dictionaries, a new one at every turn) was measured
/// in its place on the same window and was slower: 0.52 to 0.65 cores and 53 MB a frame against 0.28 to 0.37 and 44, the tree's
/// thousands of inserts a frame outweighing the lock on its reads.
/// </para>
/// </remarks>
internal sealed class NodeFacts<TValue>
    where TValue : class
{
    /// <summary>How many entries a generation holds before the next one begins.</summary>
    public const int GenerationSize = 32_768;

    /// <summary>How old a generation grows before the next one begins, whatever it holds.</summary>
    public static readonly TimeSpan TurnAfter = TimeSpan.FromSeconds(10);

    private readonly Lock _gate = new();
    private readonly TimeProvider _time;
    private readonly long _turnAfter;
    private Dictionary<Node, TValue> _current = new(ReferenceEqualityComparer.Instance);
    private Dictionary<Node, TValue> _previous = new(ReferenceEqualityComparer.Instance);
    private long _currentSince;

    public NodeFacts(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        _turnAfter = (long)(TurnAfter.TotalSeconds * _time.TimestampFrequency);
        _currentSince = _time.GetTimestamp();
    }

    /// <summary>Entries held, both generations together: what a test reads to see the bound hold.</summary>
    internal int Count
    {
        get
        {
            lock (_gate)
            {
                return _current.Count + _previous.Count;
            }
        }
    }

    /// <summary>The facts kept for <paramref name="node"/>, when there are any.</summary>
    public bool TryGetValue(Node node, out TValue value)
    {
        lock (_gate)
        {
            TurnIfOldLocked();
            if (_current.TryGetValue(node, out value!))
            {
                return true;
            }
            if (_previous.Remove(node, out value!))
            {
                AddLocked(node, value);
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// The facts kept for <paramref name="node"/>, or <paramref name="made"/> kept for it when there are none. As
    /// <see cref="System.Runtime.CompilerServices.ConditionalWeakTable{TKey, TValue}.GetValue"/>: when two threads make facts for
    /// the same node at once, the first kept is the answer to both.
    /// </summary>
    public TValue GetValue(Node node, TValue made)
    {
        lock (_gate)
        {
            TurnIfOldLocked();
            if (_current.TryGetValue(node, out var known))
            {
                return known;
            }
            if (_previous.Remove(node, out known))
            {
                AddLocked(node, known);
                return known;
            }
            AddLocked(node, made);
            return made;
        }
    }

    private void TurnIfOldLocked()
    {
        if (_time.GetTimestamp() - _currentSince >= _turnAfter)
        {
            TurnLocked();
        }
    }

    private void AddLocked(Node node, TValue value)
    {
        if (_current.Count >= GenerationSize)
        {
            TurnLocked();
        }
        _current[node] = value;
    }

    // The current generation becomes the previous one; the previous one's entries go, its table is reused.
    private void TurnLocked()
    {
        (_previous, _current) = (_current, _previous);
        _current.Clear();
        _currentSince = _time.GetTimestamp();
    }
}
