using System;
using System.Numerics;

namespace DIR.Lib.Layout;

/// <summary>
/// What a scrolled stack's <see cref="ListScrollController"/> keeps between frames so a repeat arrange resolves only the
/// rows whose node changed (DIR.Lib #338): per child, the node it was resolved for, the main extent it takes, the main
/// and cross it contributes to the stack's own measure, all valid for one available size, one context key and one axis.
/// </summary>
/// <remarks>
/// Two passes are kept because a bar that shows re-resolves every row at a narrower cross extent. A child is kept only
/// when its subtree is pure (<see cref="MeasurePurity"/>), the same rule the measure memo keeps its answers by, and only
/// when nothing about its main depends on a sibling: the caller offers the kept path for a stack with no Star main, no
/// separator and no collapse threshold, and takes the ordinary resolve otherwise.
/// </remarks>
internal sealed class StackKeep<T> where T : INumber<T>
{
    internal sealed class Pass
    {
        public bool Valid;
        public Size<T> Available;
        public object? Key;
        public Axis Axis;
        public Node?[] Nodes = [];
        public T[] Mains = [];
        public T[] MeasureMain = [];
        public T[] MeasureCross = [];

        /// <summary>Makes the pass answer for this request, dropping every kept child when the request differs.</summary>
        public void Bind(int count, Size<T> available, object key, Axis axis)
        {
            if (!Valid || Nodes.Length != count || Axis != axis || Available.Width != available.Width
                || Available.Height != available.Height || !key.Equals(Key))
            {
                if (Nodes.Length != count)
                {
                    Nodes = new Node?[count];
                    Mains = new T[count];
                    MeasureMain = new T[count];
                    MeasureCross = new T[count];
                }
                else
                {
                    Array.Clear(Nodes);
                }

                Valid = true;
                Available = available;
                Key = key;
                Axis = axis;
            }
        }
    }

    public readonly Pass First = new();
    public readonly Pass Second = new();
    public bool[] Included = [];

    public bool[] AllIncluded(int count)
    {
        if (Included.Length != count)
        {
            Included = new bool[count];
            Array.Fill(Included, true);
        }

        return Included;
    }
}
