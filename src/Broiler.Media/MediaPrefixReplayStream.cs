using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media;

public sealed class MediaPrefixReplayStream : Stream
{
    private readonly byte[] _prefix;
    private readonly Stream _tail;
    private readonly bool _leaveOpen;
    private int _prefixOffset;
    private bool _disposed;

    public MediaPrefixReplayStream(ReadOnlyMemory<byte> prefix, Stream tail, bool leaveOpen = true)
    {
        _prefix = prefix.ToArray();
        _tail = tail ?? throw new ArgumentNullException(nameof(tail));
        if (!tail.CanRead)
            throw new ArgumentException("The replay tail stream must be readable.", nameof(tail));
        _leaveOpen = leaveOpen;
    }

    public override bool CanRead => !_disposed && _tail.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        ThrowIfDisposed();

        int copied = ReadPrefix(buffer);
        if (copied > 0)
            return copied;

        return _tail.Read(buffer);
    }

    public override int ReadByte()
    {
        ThrowIfDisposed();

        if (_prefixOffset < _prefix.Length)
            return _prefix[_prefixOffset++];

        return _tail.ReadByte();
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        int copied = ReadPrefix(buffer.Span);
        if (copied > 0)
            return ValueTask.FromResult(copied);

        return _tail.ReadAsync(buffer, cancellationToken);
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing && !_leaveOpen)
            _tail.Dispose();

        _disposed = true;
        base.Dispose(disposing);
    }

    private int ReadPrefix(Span<byte> destination)
    {
        int remaining = _prefix.Length - _prefixOffset;
        if (remaining <= 0 || destination.Length == 0)
            return 0;

        int count = Math.Min(destination.Length, remaining);
        _prefix.AsSpan(_prefixOffset, count).CopyTo(destination);
        _prefixOffset += count;
        return count;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

