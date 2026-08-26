using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media.Audio.Tests;

internal static class Program
{
    private static async Task<int> Main()
    {
        var tests = new List<(string Name, Func<ValueTask> Body)>
        {
            ("AudioCodec rejects non-audio descriptors", CodecKindGuard),
            ("AudioBuffer validates shape and exposes timing", AudioBufferShape),
            ("Recording audio output applies backpressure", RecordingOutputBackpressure),
            ("Recording audio output records completion and failure", RecordingOutputLifecycle),
        };

        int passed = 0;
        var failures = new List<string>();
        Console.WriteLine($"Running {tests.Count} audio contract test(s)...\n");

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

    private static ValueTask CodecKindGuard()
    {
        Assert.Throws<ArgumentException>(() => _ = new FakeAudioCodec(MediaKind.Image));
        _ = new FakeAudioCodec(MediaKind.Audio);
        return ValueTask.CompletedTask;
    }

    private static ValueTask AudioBufferShape()
    {
        byte[] samples = new byte[4 * 2 * AudioBuffer.BytesPerSample(AudioSampleFormat.PcmS16Interleaved)];
        var buffer = new AudioBuffer(
            samples,
            AudioSampleFormat.PcmS16Interleaved,
            sampleRate: 48_000,
            channels: 2,
            frameCount: 4,
            timestamp: TimeSpan.FromMilliseconds(5),
            duration: TimeSpan.FromMilliseconds(10));

        Assert.Equal(4, buffer.FrameCount);
        Assert.Equal(2, buffer.Channels);
        Assert.Equal(TimeSpan.FromMilliseconds(10), buffer.Duration);
        Assert.Throws<ArgumentException>(() => _ = new AudioBuffer(
            ReadOnlyMemory<byte>.Empty,
            AudioSampleFormat.PcmS16Interleaved,
            48_000,
            2,
            4,
            TimeSpan.Zero,
            TimeSpan.Zero));
        return ValueTask.CompletedTask;
    }

    private static async ValueTask RecordingOutputBackpressure()
    {
        var output = new RecordingAudioOutput(capacity: 1);
        AudioBuffer buffer = SilentBuffer();

        await output.WriteAsync(buffer).ConfigureAwait(false);
        ValueTask pending = output.WriteAsync(buffer);
        Assert.False(pending.IsCompleted, "Second write should wait for capacity.");

        output.ReleaseOne();
        await pending.AsTask().WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        Assert.Equal(1, output.QueuedCount);
    }

    private static async ValueTask RecordingOutputLifecycle()
    {
        var output = new RecordingAudioOutput(capacity: 2);
        await output.CompleteAsync().ConfigureAwait(false);
        Assert.True(output.Completed);

        var failed = new RecordingAudioOutput(capacity: 2);
        var error = new MediaError(MediaErrorCode.OutputFailed, "audio sink failed");
        await failed.FailAsync(error).ConfigureAwait(false);
        Assert.Equal(error, failed.Failure);
    }

    private static AudioBuffer SilentBuffer() =>
        new(
            new byte[2 * AudioBuffer.BytesPerSample(AudioSampleFormat.PcmS16Interleaved)],
            AudioSampleFormat.PcmS16Interleaved,
            sampleRate: 48_000,
            channels: 2,
            frameCount: 1,
            timestamp: TimeSpan.Zero,
            duration: TimeSpan.FromSeconds(1.0 / 48_000));

    private sealed class FakeAudioCodec : AudioCodec
    {
        public FakeAudioCodec(MediaKind kind)
            : base(new MediaCodecDescriptor(
                new MediaCodecId($"fake.audio.{kind}"),
                "Fake Audio",
                kind,
                MediaCodecCapabilities.Decode))
        {
        }

        public override ValueTask<MediaProbeResult> ProbeAsync(
            MediaProbeRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(MediaProbeResult.NoMatch(MediaKind.Audio));

        public override ValueTask<AudioStreamInfo> GetInfoAsync(
            MediaInput input,
            AudioDecodeOptions? options = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AudioStreamInfo(48_000, 2, AudioSampleFormat.PcmS16Interleaved));

        public override ValueTask DecodeAsync(
            MediaInput input,
            IAudioOutput output,
            AudioDecodeOptions? options = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class RecordingAudioOutput(int capacity) : IAudioOutput
    {
        private readonly Queue<AudioBuffer> _queued = [];
        private TaskCompletionSource? _spaceAvailable;

        public int QueuedCount => _queued.Count;

        public bool Completed { get; private set; }

        public MediaError? Failure { get; private set; }

        public ValueTask WriteAsync(AudioBuffer buffer, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            cancellationToken.ThrowIfCancellationRequested();

            if (_queued.Count < capacity)
            {
                _queued.Enqueue(buffer);
                return ValueTask.CompletedTask;
            }

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
            Failure = error ?? throw new ArgumentNullException(nameof(error));
            return ValueTask.CompletedTask;
        }

        public void ReleaseOne()
        {
            if (_queued.Count > 0)
                _queued.Dequeue();

            TaskCompletionSource? waiter = _spaceAvailable;
            _spaceAvailable = null;
            waiter?.SetResult();
        }

        private async Task WaitForCapacityAsync(AudioBuffer buffer, CancellationToken cancellationToken)
        {
            while (_queued.Count >= capacity)
            {
                _spaceAvailable ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await _spaceAvailable.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            _queued.Enqueue(buffer);
        }
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

    public static void Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        catch (Exception ex)
        {
            throw new AssertException($"Expected {typeof(TException).Name}, got {ex.GetType().Name}.");
        }

        throw new AssertException($"Expected {typeof(TException).Name}, but no exception was thrown.");
    }
}

