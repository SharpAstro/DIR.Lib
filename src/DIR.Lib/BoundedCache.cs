using System.Collections.Concurrent;

namespace DIR.Lib;

/// <summary>
/// A thread-safe cache bounded both by entries and by bytes, which evicts by generation. Entries go into
/// the current generation; when the next would take it past half the entries or half the bytes allowed,
/// the previous generation is dropped whole and the current one takes its place. A hit in the previous
/// generation is copied into the current one, so an entry read at least once per generation stays, and
/// one not read since the last turnover goes at the next. That is least-recently-used eviction to within
/// a generation.
/// <para>A read takes no lock; an add (a miss, or a hit copied forward) takes one, which is also what
/// makes the bound exact: at most <c>maxEntries</c> entries and <c>maxBytes</c> bytes at any time, half in
/// each generation. An entry counts as what <c>sizeOf</c> says plus <see cref="EntryOverheadBytes"/>; a
/// value larger than a whole generation's bytes is not kept at all.</para>
/// </summary>
internal sealed class BoundedCache<TKey, TValue> where TKey : notnull
{
    /// <summary>What an entry costs beyond its value's own bytes: the dictionary node, the key and the
    /// value's headers, roughly.</summary>
    internal const long EntryOverheadBytes = 96;

    private sealed class Generation
    {
        public readonly ConcurrentDictionary<TKey, TValue> Entries = new();
        public int Count;
        public long Bytes;
    }

    private readonly int _generationEntries;
    private readonly long _generationBytes;
    private readonly Func<TValue, long> _sizeOf;
    private readonly Lock _gate = new();
    private volatile Generation _current = new();
    private volatile Generation _previous = new();

    /// <param name="maxEntries">The most entries kept, half in each generation; at least 2.</param>
    /// <param name="maxBytes">The most bytes kept, half in each generation.</param>
    /// <param name="sizeOf">The bytes a value holds.</param>
    public BoundedCache(int maxEntries, long maxBytes, Func<TValue, long> sizeOf)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEntries, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 2 * EntryOverheadBytes);
        _generationEntries = maxEntries / 2;
        _generationBytes = maxBytes / 2;
        _sizeOf = sizeOf;
    }

    /// <summary>The value kept for <paramref name="key"/>, if any. A hit in the previous generation is
    /// copied into the current one.</summary>
    public bool TryGet(TKey key, out TValue value)
    {
        if (_current.Entries.TryGetValue(key, out value!)) return true;
        if (!_previous.Entries.TryGetValue(key, out value!)) return false;
        Add(key, value);
        return true;
    }

    /// <summary>Keep <paramref name="value"/> for <paramref name="key"/>, unless the current generation
    /// already holds one (two threads that missed together keep the first; both computed the same).</summary>
    public void Add(TKey key, TValue value)
    {
        var bytes = _sizeOf(value) + EntryOverheadBytes;
        if (bytes > _generationBytes) return;
        lock (_gate)
        {
            var current = _current;
            if (current.Entries.ContainsKey(key)) return;
            if (current.Count + 1 > _generationEntries || current.Bytes + bytes > _generationBytes)
            {
                _previous = current;
                _current = current = new Generation();
            }
            current.Entries[key] = value;
            current.Count++;
            current.Bytes += bytes;
        }
    }

    /// <summary>Drop every entry.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _previous = new Generation();
            _current = new Generation();
        }
    }

    /// <summary>Entries held, both generations together (one copied forward counts in each).</summary>
    public int Count
    {
        get { lock (_gate) return _previous.Count + _current.Count; }
    }

    /// <summary>Bytes held as the entries counted them, both generations together.</summary>
    public long Bytes
    {
        get { lock (_gate) return _previous.Bytes + _current.Bytes; }
    }
}
