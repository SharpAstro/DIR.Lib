using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace DIR.Lib;

/// <summary>
/// The back and forward stacks of a window's moves, over any kind of place: the place being left is pushed, and going
/// back (or forward) trades the place the window is at for the one it returns to. A new move after going back drops the
/// places forward of it, as a browser's does. Bounded: the oldest place falls off the back stack at <see cref="Capacity"/>.
/// </summary>
/// <remarks>
/// What a place IS belongs to the host (a page, a selection, an open document); this only orders them. Deciding whether a
/// move led anywhere new, and not pushing when it did not, is the host's too. <see cref="NavigationInput"/> turns the keys
/// and mouse buttons into the question this answers, and <see cref="Layout.Builder.NavigationStrip"/> draws it.
/// </remarks>
/// <typeparam name="TPlace">Whatever the host needs to bring a window back to where it was.</typeparam>
public sealed class NavigationHistory<TPlace>(int capacity = NavigationHistory<TPlace>.DefaultCapacity)
{
    public const int DefaultCapacity = 100;

    private readonly List<TPlace> _back = [];
    private readonly List<TPlace> _forward = [];

    public int Capacity { get; } = Math.Max(1, capacity);

    public bool CanGoBack => _back.Count > 0;

    public bool CanGoForward => _forward.Count > 0;

    public int BackCount => _back.Count;

    public int ForwardCount => _forward.Count;

    /// <summary>Records <paramref name="from"/>, the place a move leaves, and forgets the places forward of it.</summary>
    public void Push(TPlace from)
    {
        _forward.Clear();
        _back.Add(from);
        if (_back.Count > Capacity)
        {
            _back.RemoveAt(0);
        }
    }

    /// <summary>The place before, with <paramref name="current"/> kept to come forward to; false with nothing to go back to.</summary>
    public bool Back(TPlace current, [MaybeNullWhen(false)] out TPlace place)
    {
        if (_back.Count == 0)
        {
            place = default;
            return false;
        }
        place = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        _forward.Add(current);
        return true;
    }

    /// <summary>The place after, with <paramref name="current"/> kept to go back to; false with nothing to go forward to.</summary>
    public bool Forward(TPlace current, [MaybeNullWhen(false)] out TPlace place)
    {
        if (_forward.Count == 0)
        {
            place = default;
            return false;
        }
        place = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
        _back.Add(current);
        return true;
    }
}
