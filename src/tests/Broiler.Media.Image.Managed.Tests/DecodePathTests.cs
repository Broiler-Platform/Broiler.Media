using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media.Image.Managed.Tests;

/// <summary>
/// The sync and async decode paths, and the property they exist to have: that
/// they differ in how bytes are fetched and in nothing else (PDF roadmap §6.5).
/// </summary>
internal static class DecodePathTests
{
    internal static void Register(List<(string Name, Func<ValueTask> Body)> tests)
    {
        tests.Add(("Both decode paths produce the same image", PathsAgree));
        tests.Add(("The sync path does no async I/O", SyncPathReadsSynchronously));
        tests.Add(("The async path does no sync I/O", AsyncPathReadsAsynchronously));
        tests.Add(("Both paths honour the encoded byte budget", PathsShareTheBudget));
        tests.Add(("Both paths observe cancellation", PathsObserveCancellation));
    }

    /// <summary>
    /// A stream that records which read API was used, so a test can tell a sync
    /// path that blocks on an async read from one that reads synchronously.
    /// </summary>
    private sealed class WatchedStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data, writable: false);

        public int SyncReads { get; private set; }

        public int AsyncReads { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            SyncReads++;
            return _inner.Read(buffer, offset, count);
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            AsyncReads++;
            return _inner.ReadAsync(buffer, cancellationToken);
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            AsyncReads++;
            return _inner.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override bool CanRead => true;

        public override bool CanSeek => _inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A one-pixel PNG, small enough to state inline and real enough to decode.</summary>
    private static byte[] Png() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static PngImageCodec Codec() => new();

    private static async ValueTask PathsAgree()
    {
        byte[] png = Png();
        PngImageCodec codec = Codec();

        using var syncInput = new MediaInput(new MemoryStream(png, writable: false));
        ImageSequence sync = codec.Decode(syncInput);

        using var asyncInput = new MediaInput(new MemoryStream(png, writable: false));
        ImageSequence async = await codec.DecodeAsync(asyncInput);

        Assert.Equal(sync.Frames.Count, async.Frames.Count);
        Assert.Equal(sync.Width, async.Width);
        Assert.Equal(sync.Height, async.Height);

        // Byte-identical, not merely the same shape: the two paths run the same
        // CPU decode, so anything else would mean one of them had its own.
        ReadOnlySpan<byte> left = sync.Frames[0].Pixels.Pixels.Span;
        ReadOnlySpan<byte> right = async.Frames[0].Pixels.Pixels.Span;
        Assert.True(left.SequenceEqual(right), "The two paths produced different pixels.");
    }

    private static ValueTask SyncPathReadsSynchronously()
    {
        // The point of having a sync path at all. One that blocked on an async
        // read would deadlock where continuations run on a single thread, and
        // this is what would catch that being reintroduced.
        var stream = new WatchedStream(Png());
        using var input = new MediaInput(stream);

        Codec().Decode(input);

        Assert.True(stream.SyncReads > 0, "The sync path read synchronously.");
        Assert.Equal(0, stream.AsyncReads);
        return default;
    }

    private static async ValueTask AsyncPathReadsAsynchronously()
    {
        // And the converse: an async path that read synchronously would occupy a
        // pool thread for the length of the I/O.
        var stream = new WatchedStream(Png());
        using var input = new MediaInput(stream);

        await Codec().DecodeAsync(input);

        Assert.True(stream.AsyncReads > 0, "The async path read asynchronously.");
        Assert.Equal(0, stream.SyncReads);
    }

    private static async ValueTask PathsShareTheBudget()
    {
        // The budget is the half the two paths do share, so it has to refuse on
        // both. A limit enforced on one path only is worse than none, because a
        // caller reads the number and believes it.
        byte[] png = Png();
        var options = new ImageDecodeOptions(new MediaLimits(maxEncodedBytes: 8));
        PngImageCodec codec = Codec();

        using var syncInput = new MediaInput(new MemoryStream(png, writable: false));
        Assert.Throws<MediaException>(() => codec.Decode(syncInput, options));

        using var asyncInput = new MediaInput(new MemoryStream(png, writable: false));
        try
        {
            await codec.DecodeAsync(asyncInput, options);
            throw new AssertException("The async path accepted input past the budget.");
        }
        catch (MediaException ex)
        {
            Assert.Equal(MediaErrorCode.LimitExceeded, ex.Error.Code);
        }
    }

    private static async ValueTask PathsObserveCancellation()
    {
        byte[] png = Png();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        PngImageCodec codec = Codec();

        using var syncInput = new MediaInput(new MemoryStream(png, writable: false));
        Assert.Throws<OperationCanceledException>(
            () => codec.Decode(syncInput, null, cancelled.Token));

        using var asyncInput = new MediaInput(new MemoryStream(png, writable: false));
        try
        {
            await codec.DecodeAsync(asyncInput, null, cancelled.Token);
            throw new AssertException("The async path ignored a cancelled token.");
        }
        catch (OperationCanceledException)
        {
        }
    }
}
