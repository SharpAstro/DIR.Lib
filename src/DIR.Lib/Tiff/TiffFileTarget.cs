using System.Buffers.Binary;

namespace DIR.Lib.Tiff;

/// <summary>
/// Writable stream wrapper that tracks the current byte offset
/// and provides seek-and-patch helpers for TIFF back-patching.
/// </summary>
internal sealed class TiffFileTarget : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private long _position;

    private TiffFileTarget(Stream stream, bool ownsStream)
    {
        _stream = stream;
        _ownsStream = ownsStream;
        _position = 0;
    }

    public static TiffFileTarget FromFile(string path) =>
        new(new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 4096, true), true);

    public static TiffFileTarget FromStream(Stream stream) =>
        new(stream, false);

    public long Position => _position;

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        _stream.Seek(_position, SeekOrigin.Begin);
        await _stream.WriteAsync(data, ct).ConfigureAwait(false);
        _position += data.Length;
    }

    public ValueTask WriteUInt16Async(ushort value, CancellationToken ct = default)
    {
        Span<byte> buf = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buf, value);
        return WriteAsync(buf.ToArray(), ct);
    }

    public ValueTask WriteUInt32Async(uint value, CancellationToken ct = default)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, value);
        return WriteAsync(buf.ToArray(), ct);
    }

    /// <summary>
    /// Seek to patchOffset, write a uint32, then return to the current end position.
    /// </summary>
    public async Task PatchUInt32Async(long patchOffset, uint value, CancellationToken ct = default)
    {
        var saved = _position;
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, value);
        _stream.Seek(patchOffset, SeekOrigin.Begin);
        await _stream.WriteAsync(buf.ToArray(), ct).ConfigureAwait(false);
        _position = saved;
    }

    /// <summary>Align to 2-byte word boundary.</summary>
    public async ValueTask AlignAsync(CancellationToken ct = default)
    {
        if ((_position & 1) != 0)
            await WriteAsync(new byte[1], ct).ConfigureAwait(false);
    }

    public async Task FlushAsync(CancellationToken ct = default)
    {
        await _stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_ownsStream)
            await _stream.DisposeAsync().ConfigureAwait(false);
    }
}
