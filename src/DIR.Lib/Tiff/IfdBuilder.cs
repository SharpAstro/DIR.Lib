using System.Buffers.Binary;
using System.Text;

namespace DIR.Lib.Tiff;

/// <summary>
/// Accumulates IFD entries in tag-sorted order and writes a complete IFD.
/// Returns the file offset of the NextIFD field for chaining.
/// </summary>
internal sealed class IfdBuilder
{
    private readonly SortedDictionary<ushort, (TiffFieldType Type, uint Count, byte[] ValueBytes)> _entries = new();

    public void SetShort(ushort tag, ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        _entries[tag] = (TiffFieldType.Short, 1, bytes);
    }

    public void SetLong(ushort tag, uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        _entries[tag] = (TiffFieldType.Long, 1, bytes);
    }

    public void SetRational(ushort tag, uint numerator, uint denominator)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0), numerator);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), denominator);
        _entries[tag] = (TiffFieldType.Rational, 1, bytes);
    }

    public void SetAscii(ushort tag, string value)
    {
        var bytes = new byte[Encoding.ASCII.GetByteCount(value) + 1]; // null-terminated
        Encoding.ASCII.GetBytes(value, bytes);
        bytes[^1] = 0;
        _entries[tag] = (TiffFieldType.Ascii, (uint)bytes.Length, bytes);
    }

    public void SetShortArray(ushort tag, ushort[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (var i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), values[i]);
        _entries[tag] = (TiffFieldType.Short, (uint)values.Length, bytes);
    }

    public void SetLongArray(ushort tag, uint[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), values[i]);
        _entries[tag] = (TiffFieldType.Long, (uint)values.Length, bytes);
    }

    public void SetUndefined(ushort tag, byte[] data)
    {
        _entries[tag] = (TiffFieldType.Undefined, (uint)data.Length, data);
    }

    /// <summary>
    /// Writes the IFD to the target. Returns the file offset of the NextIFD pointer field.
    /// </summary>
    public async Task<long> WriteAsync(TiffFileTarget target, CancellationToken ct = default)
    {
        await target.AlignAsync(ct).ConfigureAwait(false);
        var ifdStart = target.Position;

        var entryCount = (ushort)_entries.Count;
        const int entrySize = 12; // tag(2) + type(2) + count(4) + value/offset(4)

        // Entry count
        await target.WriteUInt16Async(entryCount, ct).ConfigureAwait(false);

        // Compute where overflow data starts (after all entries + NextIFD pointer)
        var overflowOffset = (uint)(ifdStart + 2 + entryCount * entrySize + 4);

        // Collect overflow data to write after the directory
        var overflowData = new List<byte[]>();
        var entryBytes = new byte[entrySize];

        foreach (var (tag, (type, count, valueBytes)) in _entries)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(entryBytes.AsSpan(0), tag);
            BinaryPrimitives.WriteUInt16LittleEndian(entryBytes.AsSpan(2), (ushort)type);
            BinaryPrimitives.WriteUInt32LittleEndian(entryBytes.AsSpan(4), count);

            if (valueBytes.Length <= 4)
            {
                // Inline: pad to 4 bytes
                entryBytes.AsSpan(8, 4).Clear();
                valueBytes.CopyTo(entryBytes.AsSpan(8));
            }
            else
            {
                // Overflow: write offset to data area
                BinaryPrimitives.WriteUInt32LittleEndian(entryBytes.AsSpan(8), overflowOffset);
                overflowOffset += (uint)valueBytes.Length;
                overflowData.Add(valueBytes);
            }

            await target.WriteAsync(entryBytes, ct).ConfigureAwait(false);
        }

        // NextIFD pointer (0 = no next page; caller patches this)
        var nextIfdPatchOffset = target.Position;
        await target.WriteUInt32Async(0, ct).ConfigureAwait(false);

        // Write overflow value data
        foreach (var data in overflowData)
            await target.WriteAsync(data, ct).ConfigureAwait(false);

        return nextIfdPatchOffset;
    }
}
