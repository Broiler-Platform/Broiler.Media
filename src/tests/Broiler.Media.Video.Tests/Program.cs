using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media.Video.Tests;

internal static class Program
{
    private static async Task<int> Main()
    {
        var tests = new List<(string Name, Func<ValueTask> Body)>
        {
            ("VideoCodec rejects non-video descriptors", CodecKindGuard),
            ("VideoStreamInfo validates dimensions and timing", StreamInfoShape),
            ("Video sessions expose deterministic lifecycle operations", SessionLifecycle),
        };

        int passed = 0;
        var failures = new List<string>();
        Console.WriteLine($"Running {tests.Count} video contract test(s)...\n");

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
        Assert.Throws<ArgumentException>(() => _ = new FakeVideoCodec(MediaKind.Image));
        _ = new FakeVideoCodec(MediaKind.Video);
        return ValueTask.CompletedTask;
    }

    private static ValueTask StreamInfoShape()
    {
        var info = new VideoStreamInfo(1920, 1080, 1920, 1080, TimeSpan.FromSeconds(1), 60);
        Assert.Equal(1920, info.DisplayWidth);
        Assert.Equal(60d, info.FrameRateHint);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new VideoStreamInfo(0, 1080, 1920, 1080));
        return ValueTask.CompletedTask;
    }

    private static async ValueTask SessionLifecycle()
    {
        var codec = new FakeVideoCodec(MediaKind.Video);
        using var input = new MediaInput(new MemoryStream([1, 2, 3]), leaveOpen: false);
        var output = new RecordingVideoOutput("test target");

        await using IVideoSession session = await codec.OpenSessionAsync(input, output).ConfigureAwait(false);
        var events = new List<VideoSessionEventKind>();
        session.StateChanged += (_, e) => events.Add(e.Kind);
        Assert.Equal(VideoSessionState.Ready, session.State);

        await session.PlayAsync().ConfigureAwait(false);
        Assert.Equal(VideoSessionState.Playing, session.State);

        await session.PauseAsync().ConfigureAwait(false);
        Assert.Equal(VideoSessionState.Paused, session.State);

        await session.SeekAsync(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
        Assert.Equal(TimeSpan.FromMilliseconds(250), ((FakeVideoSession)session).Position);

        await session.StopAsync().ConfigureAwait(false);
        Assert.Equal(VideoSessionState.Ended, session.State);
        Assert.SequenceEqual(
            new[]
            {
                VideoSessionEventKind.Playing,
                VideoSessionEventKind.Paused,
                VideoSessionEventKind.Seeked,
                VideoSessionEventKind.Ended,
            },
            events);
    }

    private sealed class FakeVideoCodec : VideoCodec
    {
        public FakeVideoCodec(MediaKind kind)
            : base(new MediaCodecDescriptor(
                new MediaCodecId($"fake.video.{kind}"),
                "Fake Video",
                kind,
                MediaCodecCapabilities.Decode | MediaCodecCapabilities.DirectPresentation))
        {
        }

        public override ValueTask<MediaProbeResult> ProbeAsync(
            MediaProbeRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(MediaProbeResult.NoMatch(MediaKind.Video));

        public override ValueTask<VideoStreamInfo> GetInfoAsync(
            MediaInput input,
            VideoDecodeOptions? options = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new VideoStreamInfo(640, 480, 640, 480));

        public override ValueTask<IVideoSession> OpenSessionAsync(
            MediaInput input,
            IVideoOutput output,
            VideoSessionOptions? options = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IVideoSession>(new FakeVideoSession(new VideoStreamInfo(640, 480, 640, 480)));
    }

    private sealed class FakeVideoSession(VideoStreamInfo streamInfo) : IVideoSession
    {
        public event EventHandler<VideoSessionEvent>? StateChanged;

        public VideoSessionState State { get; private set; } = VideoSessionState.Ready;

        public VideoStreamInfo StreamInfo { get; } = streamInfo;

        public TimeSpan Position { get; private set; }

        public ValueTask PlayAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            State = VideoSessionState.Playing;
            Raise(VideoSessionEventKind.Playing);
            return ValueTask.CompletedTask;
        }

        public ValueTask PauseAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            State = VideoSessionState.Paused;
            Raise(VideoSessionEventKind.Paused);
            return ValueTask.CompletedTask;
        }

        public ValueTask SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (position < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(position));

            Position = position;
            Raise(VideoSessionEventKind.Seeked);
            return ValueTask.CompletedTask;
        }

        public ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            State = VideoSessionState.Ended;
            Raise(VideoSessionEventKind.Ended);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            State = VideoSessionState.Disposed;
            Raise(VideoSessionEventKind.Disposed);
            return ValueTask.CompletedTask;
        }

        private void Raise(VideoSessionEventKind kind) =>
            StateChanged?.Invoke(this, new VideoSessionEvent(kind, State, Position));
    }

    private sealed class RecordingVideoOutput(string displayName) : IVideoOutput
    {
        public string DisplayName { get; } = displayName;

        public ValueTask CompleteAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask FailAsync(MediaError error, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(error);
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class AssertException(string message) : Exception(message);

internal static class Assert
{
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
}
