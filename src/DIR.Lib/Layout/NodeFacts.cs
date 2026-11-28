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
/// handles for every node of every frame, and paid for it in the collector and the finalizer: on pilot-gui's Ideas page
/// (DE1's data, 2026-11-28) 36 % of the sampled CPU was the table's container finalizers and 21 % the GC polls behind them,
/// with 2.5 to 3.5 s of collection pause in every 5 s (SharpAstro/DIR.Lib#341).
/// </para>
/// <para>
/// New facts go into the current generation. Once it holds <see cref="GenerationSize"/> entries it becomes the previous one and a
/// fresh one begins; whatever was in the previous one before is let go. A lookup that finds a node in the previous generation
/// moves it to the current one. So a node that is asked about again and again (a tree a consumer keeps, a kept row inside a
/// rebuilt tree) keeps its facts, and the facts of a tree that was built once and dropped go within two turns, held by nothing
/// but these two tables until then: at most twice <see cref="GenerationSize"/> nodes, and no handle for the collector to scan.
/// </para>
/// <para>Safe from any thread: one lock, held for a lookup or an insert, never while a fact is computed.</para>
/// </remarks>
internal sealed class NodeFacts<TValue>
    where TValue : class
{
    /// <summary>How many entries a generation holds before the next one begins.</summary>
    public const int GenerationSize = 16_384;

    private readonly Lock _gate = new();
    private Dictionary<Node, TValue> _current = new(ReferenceEqualityComparer.Instance);
    private Dictionary<Node, TValue> _previous = new(ReferenceEqualityComparer.Instance);

    /// <summary>How many times a generation turned over: what a test reads to see the bound hold.</summary>
    internal int Turns { get; private set; }

    /// <summary>The facts kept for <paramref name="node"/>, when there are any.</summary>
    public bool TryGetValue(Node node, out TValue value)
    {
        lock (_gate)
        {
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

    private void AddLocked(Node node, TValue value)
    {
        if (_current.Count >= GenerationSize)
        {
            (_previous, _current) = (_current, _previous);
            _current.Clear();
            Turns++;
        }
        _current[node] = value;
    }
}
