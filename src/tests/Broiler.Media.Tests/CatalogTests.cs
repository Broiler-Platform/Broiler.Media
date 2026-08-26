using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media.Tests;

internal static class CatalogTests
{
    public static void Register(ICollection<(string Name, Func<ValueTask> Body)> tests)
    {
        tests.Add(("Catalog rejects duplicate codec ids", DuplicateIdsRejected));
        tests.Add(("Catalog selects highest-confidence codec deterministically", HighestConfidenceWins));
        tests.Add(("Catalog preserves registration order on confidence ties", TieBreaksByRegistrationOrder));
        tests.Add(("Catalog honors cancellation before probing", CancellationBeforeProbe));
        tests.Add(("Catalog reads bounded prefixes and restores seekable inputs", PrefixReadIsBounded));
        tests.Add(("Prefix replay stream replays consumed bytes for non-seekable inputs", PrefixReplayStreamWorks));
        tests.Add(("Catalog returns null for unsupported inputs", UnsupportedReturnsNull));
    }

    private static ValueTask DuplicateIdsRejected()
    {
        var first = new FakeCodec("fake.same", MediaProbeConfidence.Low);
        var second = new FakeCodec("fake.same", MediaProbeConfidence.High);

        Assert.Throws<ArgumentException>(() => _ = new MediaCodecCatalog([first, second]));
        return ValueTask.CompletedTask;
    }

    private static async ValueTask HighestConfidenceWins()
    {
        var low = new FakeCodec("fake.low", MediaProbeConfidence.Low);
        var high = new FakeCodec("fake.high", MediaProbeConfidence.High);
        var catalog = new MediaCodecCatalog([low, high]);

        using var input = new MediaInput(new MemoryStream([1, 2, 3]), leaveOpen: false);
        MediaCodecMatch? match = await catalog.SelectAsync(MediaKind.Image, input).ConfigureAwait(false);

        Assert.True(match is not null, "Expected a selected codec.");
        Assert.Same(high, match!.Codec);
        Assert.Equal(MediaProbeConfidence.High, match.Result.Confidence);
    }

    private static async ValueTask TieBreaksByRegistrationOrder()
    {
        var first = new FakeCodec("fake.first", MediaProbeConfidence.Medium);
        var second = new FakeCodec("fake.second", MediaProbeConfidence.Medium);
        var catalog = new MediaCodecCatalog([first, second]);

        using var input = new MediaInput(new MemoryStream([1, 2, 3]), leaveOpen: false);
        MediaCodecMatch? match = await catalog.SelectAsync(MediaKind.Image, input).ConfigureAwait(false);

        Assert.True(match is not null, "Expected a selected codec.");
        Assert.Same(first, match!.Codec);
    }

    private static async ValueTask CancellationBeforeProbe()
    {
        var codec = new FakeCodec("fake.cancel", MediaProbeConfidence.Certain);
        var catalog = new MediaCodecCatalog([codec]);
        using var input = new MediaInput(new MemoryStream([1, 2, 3]), leaveOpen: false);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => catalog.SelectAsync(MediaKind.Image, input, cancellationToken: cts.Token).AsTask()).ConfigureAwait(false);
        Assert.Equal(0, codec.ProbeCount);
    }

    private static async ValueTask PrefixReadIsBounded()
    {
        var codec = new FakeCodec("fake.prefix", MediaProbeConfidence.Certain);
        var catalog = new MediaCodecCatalog([codec]);
        using var stream = new MemoryStream([1, 2, 3, 4, 5]);
        using var input = new MediaInput(stream);
        var options = new MediaProbeOptions(new MediaLimits(maxProbeBytes: 3));

        MediaCodecMatch? match = await catalog.SelectAsync(MediaKind.Image, input, options).ConfigureAwait(false);

        Assert.True(match is not null, "Expected a selected codec.");
        Assert.Equal(3, codec.LastPrefixLength);
        Assert.Equal(0L, stream.Position, "Seekable streams must be restored after probing.");
    }

    private static ValueTask PrefixReplayStreamWorks()
    {
        byte[] allBytes = [10, 11, 12, 13, 14];
        using var tail = new NonSeekableReadStream(allBytes);

        byte[] prefix = new byte[2];
        Assert.Equal(2, tail.Read(prefix, 0, prefix.Length));

        using var replay = new MediaPrefixReplayStream(prefix, tail);
        byte[] replayed = ReadAll(replay);
        Assert.SequenceEqual(allBytes, replayed);
        return ValueTask.CompletedTask;
    }

    private static async ValueTask UnsupportedReturnsNull()
    {
        var codec = new FakeCodec("fake.none", MediaProbeConfidence.None);
        var catalog = new MediaCodecCatalog([codec]);
        using var input = new MediaInput(new MemoryStream([1, 2, 3]), leaveOpen: false);

        MediaCodecMatch? match = await catalog.SelectAsync(MediaKind.Image, input).ConfigureAwait(false);

        Assert.True(match is null, "Unsupported input should not select a codec.");
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var output = new MemoryStream();
        byte[] buffer = new byte[2];
        while (true)
        {
            int read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
                break;
            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private sealed class FakeCodec : MediaCodec
    {
        private readonly MediaProbeConfidence _confidence;

        public FakeCodec(string id, MediaProbeConfidence confidence)
            : base(new MediaCodecDescriptor(
                new MediaCodecId(id),
                id,
                MediaKind.Image,
                MediaCodecCapabilities.Decode,
                [new MediaFormatDescriptor("fake")]))
        {
            _confidence = confidence;
        }

        public int ProbeCount { get; private set; }

        public int LastPrefixLength { get; private set; }

        public override ValueTask<MediaProbeResult> ProbeAsync(
            MediaProbeRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProbeCount++;
            LastPrefixLength = request.Prefix.Length;

            MediaProbeResult result = _confidence == MediaProbeConfidence.None
                ? MediaProbeResult.NoMatch(MediaKind.Image)
                : MediaProbeResult.Match(MediaKind.Image, _confidence, "fake");

            return ValueTask.FromResult(result);
        }
    }

    private sealed class NonSeekableReadStream(byte[] bytes) : Stream
    {
        private int _offset;

        public override bool CanRead => true;

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
            int remaining = bytes.Length - _offset;
            if (remaining <= 0)
                return 0;

            int read = Math.Min(count, remaining);
            Array.Copy(bytes, _offset, buffer, offset, read);
            _offset += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

