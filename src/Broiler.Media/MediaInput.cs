using System;
using System.IO;

namespace Broiler.Media;

public sealed class MediaInput : IDisposable
{
    private bool _disposed;

    public MediaInput(Stream stream, MediaSourceHints? hints = null, bool leaveOpen = true)
    {
        Stream = stream ?? throw new ArgumentNullException(nameof(stream));
        if (!stream.CanRead)
            throw new ArgumentException("Media input streams must be readable.", nameof(stream));

        Hints = hints ?? MediaSourceHints.Empty;
        LeaveOpen = leaveOpen;
    }

    public Stream Stream { get; }

    public MediaSourceHints Hints { get; }

    public bool LeaveOpen { get; }

    public MediaPrefixReplayStream CreateReplayStream(ReadOnlyMemory<byte> prefix, bool leaveOpen = true)
    {
        ThrowIfDisposed();
        return new MediaPrefixReplayStream(prefix, Stream, leaveOpen);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (!LeaveOpen)
            Stream.Dispose();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

