using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media.Audio.Managed.Tests;

internal static class Program
{
    private static async Task<int> Main()
    {
        var tests = new List<(string Name, Func<ValueTask> Body)>
        {
            ("Managed audio provider exposes WAVE without playback claims", ProviderExposesWave),
            ("Catalog selects WAVE by RIFF signature", CatalogSelectsWave),
            ("WAVE info reports PCM metadata and timing", WaveInfoReportsMetadata),
            ("16-bit PCM decodes in bounded chunks from a non-seekable stream", Decode16BitNonSeekable),
            ("8, 24 and 32-bit PCM decode to exact 16-bit samples", DecodeBitDepthsToS16),
            ("PCM decodes to float32 output", DecodeToFloat32),
            ("Null audio output accepts completed decode", NullOutputCompletes),
            ("Slow bounded output applies backpressure", SlowOutputBackpressure),
            ("Cancellation stops WAVE decode", CancellationStopsDecode),
            ("Truncated WAVE data fails the output", TruncatedDataFails),
            ("Declared WAVE data size is bounded", DeclaredDataLimitFails),
            ("Declared decoded sample count is bounded", DecodedSampleLimitFails),
            ("Audio.Managed runtime has no Graphics or HTML dependency", RuntimeHasNoGraphicsOrHtmlReference),
        };

        int passed = 0;
        var failures = new List<string>();
        Console.WriteLine($"Running {tests.Count} managed audio codec test(s)...\n");

        foreach ((string name, Func<ValueTask> body) in tests)
        {
            try
            {
                await body().ConfigureAwait(false);
                passed++;
                Console.WriteLine($"  [PASS] {name}");
            }
            catch (Exception ex)
            {
                failures.Add(name);
                Console.WriteLine($"  [FAIL] {name}");
                Console.WriteLine($"         {ex.GetType().Name}: {ex.Message}");
            }
        }

        Console.WriteLine($"\n{passed}/{tests.Count} passed, {failures.Count} failed.");
        return failures.Count;
    }

    private static ValueTask ProviderExposesWave()
    {
        IReadOnlyList<AudioCodec> codecs = ManagedAudioCodecs.CreateCodecs();
        Assert.Equal(1, codecs.Count);
        Assert.True(codecs[0] is WaveAudioCodec);
        Assert.Equal(new MediaCodecId("broiler.audio.wave.managed"), codecs[0].Id);
        Assert.True((codecs[0].Descriptor.Capabilities & MediaCodecCapabilities.Decode) != 0);
        Assert.True((codecs[0].Descriptor.Capabilities & MediaCodecCapabilities.Streaming) != 0);
        Assert.False((codecs[0].Descriptor.Capabilities & MediaCodecCapabilities.DirectPresentation) != 0);
        return ValueTask.CompletedTask;
    }

    private static async ValueTask CatalogSelectsWave()
    {
        byte[] wave = BuildWave(8_000, 1, 16, PcmBytes(16, [0, 1000]));
        var catalog = new MediaCodecCatalog(ManagedAudioCodecs.CreateCodecs());
        using var input = new MediaInput(new MemoryStream(wave));

        MediaCodecMatch? match = await catalog.SelectAsync(MediaKind.Audio, input).ConfigureAwait(false);

        Assert.True(match is not null);
        Assert.Equal(new MediaCodecId("broiler.audio.wave.managed"), match!.Codec.Id);
        Assert.Equal(MediaProbeConfidence.Certain, match.Result.Confidence);
        Assert.Equal(0L, input.Stream.Position);
    }

    private static async ValueTask WaveInfoReportsMetadata()
    {
        byte[] wave = BuildWave(
            48_000,
            2,
            16,
            PcmBytes(16, [0, 1000, -1000, 32767, -32768, 42]),
            includeUnknownChunks: true);

        AudioStreamInfo info = await DecodeInfoAsync(wave).ConfigureAwait(false);

        Assert.Equal(48_000, info.SampleRate);
        Assert.Equal(2, info.Channels);
        Assert.Equal(AudioSampleFormat.PcmS16Interleaved, info.SourceFormat);
        Assert.Equal(16, info.BitsPerSample);
        Assert.Equal(4, info.BlockAlign);
        Assert.Equal(192_000, info.ByteRate);
        Assert.Equal(3L, info.TotalFrames);
        Assert.Equal(TimeSpan.FromTicks(3L * TimeSpan.TicksPerSecond / 48_000), info.Duration);
    }

    private static async ValueTask Decode16BitNonSeekable()
    {
        int[] samples = [0, 1000, -1000, 32767, -32768, 42, 99, -99, 1234, -1234];
        byte[] wave = BuildWave(10_000, 2, 16, PcmBytes(16, samples), includeUnknownChunks: true);
        using var input = new MediaInput(new NonSeekableReadStream(wave));
        var output = new RecordingAudioOutput();
        var codec = new WaveAudioCodec();

        await codec.DecodeAsync(
            input,
            output,
            new AudioDecodeOptions(maxFramesPerBuffer: 2)).ConfigureAwait(false);

        Assert.True(output.Completed);
        Assert.Equal(3, output.Buffers.Count);
        Assert.Equal(2, output.Buffers[0].FrameCount);
        Assert.Equal(2, output.Buffers[1].FrameCount);
        Assert.Equal(1, output.Buffers[2].FrameCount);
        Assert.Equal(TimeSpan.Zero, output.Buffers[0].Timestamp);
        Assert.Equal(TimeSpan.FromTicks(2L * TimeSpan.TicksPerSecond / 10_000), output.Buffers[1].Timestamp);
        Assert.Equal(TimeSpan.FromTicks(4L * TimeSpan.TicksPerSecond / 10_000), output.Buffers[2].Timestamp);
        Assert.BytesEqual(PcmBytes(16, samples), ConcatSamples(output.Buffers));
    }

    private static async ValueTask DecodeBitDepthsToS16()
    {
        await AssertDecodedS16(
            BuildWave(8_000, 1, 8, PcmBytes(8, [0, 128, 255])),
            [-32768, 0, 32512]).ConfigureAwait(false);

        await AssertDecodedS16(
            BuildWave(8_000, 1, 24, PcmBytes(24, [-8388608, 0, 8388607])),
            [-32768, 0, 32767]).ConfigureAwait(false);

        await AssertDecodedS16(
            BuildWave(8_000, 1, 32, PcmBytes(32, [int.MinValue, 0, int.MaxValue])),
            [-32768, 0, 32767]).ConfigureAwait(false);
    }

    private static async ValueTask DecodeToFloat32()
    {
        byte[] wave = BuildWave(8_000, 1, 16, PcmBytes(16, [-32768, 0, 32767]));
        var output = await DecodeAsync(
            wave,
            new AudioDecodeOptions(AudioSampleFormat.Float32Interleaved, maxFramesPerBuffer: 16)).ConfigureAwait(false);

        byte[] samples = ConcatSamples(output.Buffers);
        Assert.Near(-1f, BinaryPrimitives.ReadSingleLittleEndian(samples.AsSpan(0, 4)));
        Assert.Near(0f, BinaryPrimitives.ReadSingleLittleEndian(samples.AsSpan(4, 4)));
        Assert.Near(32767f / 32768f, BinaryPrimitives.ReadSingleLittleEndian(samples.AsSpan(8, 4)));
    }

    private static async ValueTask NullOutputCompletes()
    {
        byte[] wave = BuildWave(8_000, 1, 16, PcmBytes(16, [0, 1, -1, 2]));
        using var input = new MediaInput(new MemoryStream(wave));
        var output = new NullAudioOutput();

        await new WaveAudioCodec().DecodeAsync(input, output).ConfigureAwait(false);

        Assert.True(output.Completed);
        Assert.Equal(4, output.FrameCount);
    }

    private static async ValueTask SlowOutputBackpressure()
    {
        byte[] wave = BuildWave(8_000, 1, 16, PcmBytes(16, [1, 2, 3, 4]));
        using var stream = new NonSeekableReadStream(wave);
        using var input = new MediaInput(stream);
        var output = new BoundedAudioOutput(capacity: 1);
        var codec = new WaveAudioCodec();

        Task decode = codec.DecodeAsync(
            input,
            output,
            new AudioDecodeOptions(maxFramesPerBuffer: 1)).AsTask();

        await output.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        long bytesReadWhileBlocked = stream.BytesRead;
        await Task.Delay(50).ConfigureAwait(false);

        Assert.False(decode.IsCompleted, "Decode should wait for bounded output capacity.");
        Assert.Equal(bytesReadWhileBlocked, stream.BytesRead, "Decoder read ahead while output was backpressured.");

        while (!decode.IsCompleted)
        {
            output.ReleaseOne();
            await Task.Delay(1).ConfigureAwait(false);
        }

        await decode.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        Assert.True(output.Completed);
        Assert.Equal(4, output.TotalFramesWritten);
    }

    private static async ValueTask CancellationStopsDecode()
    {
        byte[] wave = BuildWave(8_000, 1, 16, PcmBytes(16, [1, 2, 3, 4]));
        using var input = new MediaInput(new MemoryStream(wave));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(false);

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await new WaveAudioCodec().DecodeAsync(input, new RecordingAudioOutput(), cancellationToken: cts.Token)
                .ConfigureAwait(false)).ConfigureAwait(false);
    }

    private static async ValueTask TruncatedDataFails()
    {
        byte[] wave = BuildWave(8_000, 1, 16, PcmBytes(16, [1]), declaredDataBytes: 4);
        var output = new RecordingAudioOutput();

        await Assert.ThrowsAsync<MediaException>(async () =>
            await DecodeWithOutputAsync(wave, output).ConfigureAwait(false)).ConfigureAwait(false);

        Assert.True(output.Failure is not null);
        Assert.Equal(MediaErrorCode.InvalidData, output.Failure!.Code);
    }

    private static async ValueTask DeclaredDataLimitFails()
    {
        byte[] wave = BuildWave(8_000, 1, 16, ReadOnlySpan<byte>.Empty, declaredDataBytes: 64);
        var output = new RecordingAudioOutput();
        var options = new AudioDecodeOptions(limits: new MediaLimits(maxDecodedBytes: 16));

        MediaException ex = await Assert.ThrowsAsync<MediaException>(async () =>
            await DecodeWithOutputAsync(wave, output, options).ConfigureAwait(false)).ConfigureAwait(false);

        Assert.Equal(MediaErrorCode.LimitExceeded, ex.Error.Code);
        Assert.Equal(MediaErrorCode.LimitExceeded, output.Failure!.Code);
    }

    private static async ValueTask DecodedSampleLimitFails()
    {
        byte[] wave = BuildWave(8_000, 2, 16, ReadOnlySpan<byte>.Empty, declaredDataBytes: 24);
        var output = new RecordingAudioOutput();
        var options = new AudioDecodeOptions(limits: new MediaLimits(maxDecodedSamples: 8));

        MediaException ex = await Assert.ThrowsAsync<MediaException>(async () =>
            await DecodeWithOutputAsync(wave, output, options).ConfigureAwait(false)).ConfigureAwait(false);

        Assert.Equal(MediaErrorCode.LimitExceeded, ex.Error.Code);
        Assert.Equal(MediaErrorCode.LimitExceeded, output.Failure!.Code);
    }

    private static ValueTask RuntimeHasNoGraphicsOrHtmlReference()
    {
        string root = FindMediaRoot();
        string runtimeRoot = Path.Combine(root, "Broiler.Media.Audio.Managed");
        foreach (string file in Directory.EnumerateFiles(runtimeRoot, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            Assert.DoesNotContain("Broiler.Graphics", text, file);
            Assert.DoesNotContain("Broiler.Html", text, file);
        }

        return ValueTask.CompletedTask;
    }

    private static async ValueTask<AudioStreamInfo> DecodeInfoAsync(byte[] wave)
    {
        using var input = new MediaInput(new MemoryStream(wave));
        return await new WaveAudioCodec().GetInfoAsync(input).ConfigureAwait(false);
    }

    private static async ValueTask<RecordingAudioOutput> DecodeAsync(byte[] wave, AudioDecodeOptions? options = null)
    {
        var output = new RecordingAudioOutput();
        await DecodeWithOutputAsync(wave, output, options).ConfigureAwait(false);
        return output;
    }

    private static async ValueTask DecodeWithOutputAsync(
        byte[] wave,
        IAudioOutput output,
        AudioDecodeOptions? options = null)
    {
        using var input = new MediaInput(new MemoryStream(wave));
        await new WaveAudioCodec().DecodeAsync(input, output, options).ConfigureAwait(false);
    }

    private static async ValueTask AssertDecodedS16(byte[] wave, IReadOnlyList<short> expected)
    {
        RecordingAudioOutput output = await DecodeAsync(wave).ConfigureAwait(false);
        byte[] actualBytes = ConcatSamples(output.Buffers);
        var actual = new short[expected.Count];
        for (int i = 0; i < actual.Length; i++)
            actual[i] = BinaryPrimitives.ReadInt16LittleEndian(actualBytes.AsSpan(i * 2, 2));

        Assert.SequenceEqual(expected, actual);
    }

    private static byte[] BuildWave(
        int sampleRate,
        int channels,
        int bitsPerSample,
        ReadOnlySpan<byte> data,
        bool includeUnknownChunks = false,
        uint? declaredDataBytes = null)
    {
        using var stream = new MemoryStream();
        WriteFourCc(stream, "RIFF");
        WriteUInt32(stream, 0);
        WriteFourCc(stream, "WAVE");

        long declaredChunkBytes = 4;
        if (includeUnknownChunks)
        {
            WriteFourCc(stream, "JUNK");
            WriteUInt32(stream, 3);
            stream.WriteByte(0x11);
            stream.WriteByte(0x22);
            stream.WriteByte(0x33);
            stream.WriteByte(0);
            declaredChunkBytes += 8 + 4;
        }

        int bytesPerSample = (bitsPerSample + 7) / 8;
        int blockAlign = checked(channels * bytesPerSample);
        int byteRate = checked(sampleRate * blockAlign);

        WriteFourCc(stream, "fmt ");
        WriteUInt32(stream, 16);
        WriteUInt16(stream, 1);
        WriteUInt16(stream, (ushort)channels);
        WriteUInt32(stream, (uint)sampleRate);
        WriteUInt32(stream, (uint)byteRate);
        WriteUInt16(stream, (ushort)blockAlign);
        WriteUInt16(stream, (ushort)bitsPerSample);
        declaredChunkBytes += 8 + 16;

        if (includeUnknownChunks)
        {
            WriteFourCc(stream, "LIST");
            WriteUInt32(stream, 4);
            stream.WriteByte((byte)'I');
            stream.WriteByte((byte)'N');
            stream.WriteByte((byte)'F');
            stream.WriteByte((byte)'O');
            declaredChunkBytes += 8 + 4;
        }

        uint dataSize = declaredDataBytes ?? checked((uint)data.Length);
        WriteFourCc(stream, "data");
        WriteUInt32(stream, dataSize);
        stream.Write(data);
        if ((data.Length & 1) != 0 && declaredDataBytes is null)
            stream.WriteByte(0);

        declaredChunkBytes += 8 + dataSize + (dataSize & 1u);
        stream.Position = 4;
        WriteUInt32(stream, checked((uint)declaredChunkBytes));
        return stream.ToArray();
    }

    private static byte[] PcmBytes(int bitsPerSample, IReadOnlyList<int> samples)
    {
        using var stream = new MemoryStream();
        Span<byte> scratch = stackalloc byte[4];
        foreach (int sample in samples)
        {
            switch (bitsPerSample)
            {
                case 8:
                    stream.WriteByte(checked((byte)sample));
                    break;
                case 16:
                    BinaryPrimitives.WriteInt16LittleEndian(scratch[..2], checked((short)sample));
                    stream.Write(scratch[..2]);
                    break;
                case 24:
                    scratch[0] = (byte)sample;
                    scratch[1] = (byte)(sample >> 8);
                    scratch[2] = (byte)(sample >> 16);
                    stream.Write(scratch[..3]);
                    break;
                case 32:
                    BinaryPrimitives.WriteInt32LittleEndian(scratch, sample);
                    stream.Write(scratch);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(bitsPerSample));
            }
        }

        return stream.ToArray();
    }

    private static byte[] ConcatSamples(IReadOnlyList<AudioBuffer> buffers)
    {
        using var stream = new MemoryStream();
        foreach (AudioBuffer buffer in buffers)
            stream.Write(buffer.Samples.Span);
        return stream.ToArray();
    }

    private static void WriteFourCc(Stream stream, string value)
    {
        if (value.Length != 4)
            throw new ArgumentException("FOURCC values must be exactly four characters.", nameof(value));

        stream.WriteByte((byte)value[0]);
        stream.WriteByte((byte)value[1]);
        stream.WriteByte((byte)value[2]);
        stream.WriteByte((byte)value[3]);
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static string FindMediaRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "Broiler.Media.Audio.Managed", "Broiler.Media.Audio.Managed.csproj");
            if (File.Exists(candidate))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the Broiler.Media root.");
    }

    private sealed class RecordingAudioOutput : IAudioOutput
    {
        public List<AudioBuffer> Buffers { get; } = [];

        public bool Completed { get; private set; }

        public MediaError? Failure { get; private set; }

        public ValueTask WriteAsync(AudioBuffer buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Buffers.Add(buffer);
            return ValueTask.CompletedTask;
        }

        public ValueTask CompleteAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Completed = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask FailAsync(MediaError error, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Failure = error;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NullAudioOutput : IAudioOutput
    {
        public bool Completed { get; private set; }

        public long FrameCount { get; private set; }

        public ValueTask WriteAsync(AudioBuffer buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FrameCount += buffer.FrameCount;
            return ValueTask.CompletedTask;
        }

        public ValueTask CompleteAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Completed = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask FailAsync(MediaError error, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BoundedAudioOutput(int capacity) : IAudioOutput
    {
        private readonly Queue<AudioBuffer> _queue = [];
        private TaskCompletionSource? _spaceAvailable;

        public TaskCompletionSource Blocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Completed { get; private set; }

        public long TotalFramesWritten { get; private set; }

        public ValueTask WriteAsync(AudioBuffer buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_queue.Count < capacity)
            {
                _queue.Enqueue(buffer);
                TotalFramesWritten += buffer.FrameCount;
                return ValueTask.CompletedTask;
            }

            Blocked.TrySetResult();
            return new ValueTask(WaitForCapacityAsync(buffer, cancellationToken));
        }

        public ValueTask CompleteAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Completed = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask FailAsync(MediaError error, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public void ReleaseOne()
        {
            if (_queue.Count > 0)
                _queue.Dequeue();

            TaskCompletionSource? waiter = _spaceAvailable;
            _spaceAvailable = null;
            waiter?.SetResult();
        }

        private async Task WaitForCapacityAsync(AudioBuffer buffer, CancellationToken cancellationToken)
        {
            while (_queue.Count >= capacity)
            {
                _spaceAvailable ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await _spaceAvailable.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            _queue.Enqueue(buffer);
            TotalFramesWritten += buffer.FrameCount;
        }
    }

    private sealed class NonSeekableReadStream : Stream
    {
        private readonly MemoryStream _inner;

        public NonSeekableReadStream(byte[] data)
        {
            _inner = new MemoryStream(data);
        }

        public long BytesRead { get; private set; }

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
            int read = _inner.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            int read = _inner.Read(buffer);
            BytesRead += read;
            return read;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = _inner.Read(buffer.Span);
            BytesRead += read;
            return ValueTask.FromResult(read);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

internal sealed class AssertException(string message) : Exception(message);

internal static class Assert
{
    public static void True(bool condition, string message = "Expected true.")
    {
        if (!condition)
            throw new AssertException(message);
    }

    public static void False(bool condition, string message = "Expected false.") => True(!condition, message);

    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new AssertException(message ?? $"Expected <{expected}>, but was <{actual}>.");
    }

    public static void SequenceEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, string? message = null)
    {
        if (expected.Count != actual.Count)
            throw new AssertException(message ?? $"Expected {expected.Count} item(s), got {actual.Count}.");

        for (int i = 0; i < expected.Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(expected[i], actual[i]))
                throw new AssertException(message ?? $"Sequence differs at index {i}: expected <{expected[i]}>, got <{actual[i]}>.");
        }
    }

    public static void BytesEqual(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual, string? message = null)
    {
        if (!expected.SequenceEqual(actual))
            throw new AssertException(message ?? $"Byte sequence differs. Expected {expected.Length} byte(s), got {actual.Length}.");
    }

    public static void Near(float expected, float actual, float tolerance = 0.00001f)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new AssertException($"Expected <{expected}>, but was <{actual}>.");
    }

    public static void DoesNotContain(string unexpected, string actual, string? message = null)
    {
        if (actual.Contains(unexpected, StringComparison.Ordinal))
            throw new AssertException(message ?? $"Expected text not to contain '{unexpected}'.");
    }

    public static async ValueTask<TException> ThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (TException ex)
        {
            return ex;
        }
        catch (Exception ex)
        {
            throw new AssertException($"Expected {typeof(TException).Name}, got {ex.GetType().Name}.");
        }

        throw new AssertException($"Expected {typeof(TException).Name}, but no exception was thrown.");
    }
}
