using System;
using System.Collections.Generic;

namespace DIR.Lib;

/// <summary>
/// The rows of a list a host builds every frame, each kept as the same <see cref="Layout.Node"/> instance for as long as
/// what the row was built from stands (DIR.Lib #338).
/// </summary>
/// <remarks>
/// <para>
/// Node identity is what the measure memo and a scrolled stack's kept extents key on (both by reference). A host that
/// builds its tree every frame makes every row a new node, so neither ever hits: 300 rebuilt Markdown rows measured at
/// 385 to 423 ms of arrange and 42,800 <c>MeasureText</c> calls a frame, against 3 to 4 ms when the rows were kept.
/// <see cref="KeptLayout{TKey}"/> keeps a whole widget's arranged tree and cannot do this per row.
/// </para>
/// <para>
/// <b>The key is everything the build reads</b>, as for <see cref="KeptLayout{TKey}"/>: a value the build reads that is
/// not in the key is a row that shows the old value until its key changes. The theme generation belongs in the key,
/// since a node carries its colours, so a theme switch rebuilds every row. A handler in a kept node runs frames after the
/// row was built, so it reads state when it runs and captures nothing computed at build time that is not in the key.
/// </para>
/// <para>
/// Call <see cref="Row"/> for each row of the frame, then <see cref="EndFrame"/> once: a row not asked for since the last
/// <see cref="EndFrame"/> is dropped, so the keep does not outlive the list. Not thread safe; it belongs to the render
/// thread, like the tree it feeds.
/// </para>
/// </remarks>
/// <typeparam name="TId">Names a row across frames (a message id, an index that is stable).</typeparam>
/// <typeparam name="TKey">What the row's build reads.</typeparam>
public sealed class KeptRows<TId, TKey> where TId : notnull where TKey : IEquatable<TKey>
{
    private sealed class Entry(TKey key, Layout.Node node)
    {
        public TKey Key = key;
        public Layout.Node Node = node;
        public bool Asked;
    }

    private readonly Dictionary<TId, Entry> _rows = [];
    private List<TId>? _dropped;

    /// <summary>How many rows were built. The observable for "an unchanged row builds nothing".</summary>
    public int Builds { get; private set; }

    /// <summary>How many rows are kept.</summary>
    public int Count => _rows.Count;

    /// <summary>
    /// The row <paramref name="id"/>: the node kept for an equal <paramref name="key"/>, else
    /// <paramref name="build"/> called with the key and its node kept.
    /// </summary>
    public Layout.Node Row(TId id, in TKey key, Func<TKey, Layout.Node> build)
    {
        if (_rows.TryGetValue(id, out var entry))
        {
            entry.Asked = true;
            if (entry.Key.Equals(key))
            {
                return entry.Node;
            }

            entry.Node = build(key);
            entry.Key = key;
            Builds++;
            return entry.Node;
        }

        var node = build(key);
        _rows[id] = new Entry(key, node) { Asked = true };
        Builds++;
        return node;
    }

    /// <summary>Drops the rows not asked for since the last call, and starts the next frame.</summary>
    public void EndFrame()
    {
        foreach (var (id, entry) in _rows)
        {
            if (entry.Asked)
            {
                entry.Asked = false;
            }
            else
            {
                (_dropped ??= []).Add(id);
            }
        }

        if (_dropped is { Count: > 0 })
        {
            foreach (var id in _dropped)
            {
                _rows.Remove(id);
            }

            _dropped.Clear();
        }
    }

    /// <summary>Drops every kept row: the next frame builds them all.</summary>
    public void Forget() => _rows.Clear();
}
