using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media.Image.Tests;

internal static class Program
{
    private static async Task<int> Main()
    {
        var tests = new List<(string Name, Func<ValueTask> Body)>
        {
            ("ImageCodec rejects non-image descriptors", CodecKindGuard),
            ("ImageBuffer validates RGBA stride and length", ImageBufferShape),
            ("ImageSequence wraps still frames", StaticSequence),
            ("ImageCodec default encode path is explicit unsupported", DefaultEncodeUnsupported),
            ("Animation timeline selects the frame showing at a presentation time", FrameSelectionAtPresentationTime),
            ("Animation timeline clamps delays too short to honour", FrameSelectionClampsShortDelays),
            ("Animation timeline loops or holds by loop count", FrameSelectionLoopCount),
            ("The animation clock pins and restores a presentation time", AnimationClockPin),
            ("A per-image pin overrides the document time and nests", AnimationClockPinForImage),
            ("A per-image pin reaches a decode deferred to the thread pool", AnimationClockPinForImageFlowsToPool),
        };

        int passed = 0;
        var failures = new List<string>();
        Console.WriteLine($"Running {tests.Count} image contract test(s)...\n");

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
        Assert.Throws<ArgumentException>(() => _ = new FakeImageCodec(MediaKind.Audio));
        _ = new FakeImageCodec(MediaKind.Image);
        return ValueTask.CompletedTask;
    }

    private static ValueTask ImageBufferShape()
    {
        var buffer = new ImageBuffer(
            2,
            2,
            ImagePixelFormat.Rgba8,
            ImageAlphaMode.Straight,
            stride: 8,
            pixels: new byte[16]);

        Assert.Equal(2, buffer.Width);
        Assert.Equal(8, buffer.Stride);
        Assert.Throws<ArgumentException>(() => _ = new ImageBuffer(
            2,
            2,
            ImagePixelFormat.Rgba8,
            ImageAlphaMode.Straight,
            stride: 7,
            pixels: new byte[16]));
        Assert.Throws<ArgumentException>(() => _ = new ImageBuffer(
            2,
            2,
            ImagePixelFormat.Rgba8,
            ImageAlphaMode.Straight,
            stride: 8,
            pixels: new byte[15]));
        return ValueTask.CompletedTask;
    }

    private static ValueTask StaticSequence()
    {
        ImageBuffer buffer = Rgba1x1();
        ImageSequence sequence = ImageSequence.Static(buffer);

        Assert.False(sequence.IsAnimated);
        Assert.Equal(1, sequence.Frames.Count);
        Assert.Equal(buffer, sequence.FirstFrame);
        return ValueTask.CompletedTask;
    }

    private static async ValueTask DefaultEncodeUnsupported()
    {
        var codec = new FakeImageCodec(MediaKind.Image);
        ImageSequence sequence = ImageSequence.Static(Rgba1x1());

        await Assert.ThrowsAsync<NotSupportedException>(
            () => codec.EncodeAsync(sequence, Stream.Null).AsTask()).ConfigureAwait(false);
    }

    private static ValueTask FrameSelectionAtPresentationTime()
    {
        // Three 50 ms frames, looping forever.
        ImageSequence sequence = Animation(0, [50, 50, 50]);

        Assert.Equal(0, sequence.FrameIndexAt(TimeSpan.Zero));
        Assert.Equal(0, sequence.FrameIndexAt(TimeSpan.FromMilliseconds(49)));
        Assert.Equal(1, sequence.FrameIndexAt(TimeSpan.FromMilliseconds(50)));
        Assert.Equal(2, sequence.FrameIndexAt(TimeSpan.FromMilliseconds(120)));

        // Past the end of one pass it wraps, because loop count 0 means "forever".
        Assert.Equal(0, sequence.FrameIndexAt(TimeSpan.FromMilliseconds(150)));
        Assert.Equal(1, sequence.FrameIndexAt(TimeSpan.FromMilliseconds(200)));

        // A time before the start, and a still image, both resolve to the first frame.
        Assert.Equal(0, sequence.FrameIndexAt(TimeSpan.FromMilliseconds(-500)));
        Assert.Equal(0, ImageSequence.Static(Rgba1x1()).FrameIndexAt(TimeSpan.FromHours(1)));

        Assert.Equal(sequence.Frames[2], sequence.FrameAt(TimeSpan.FromMilliseconds(120)));
        return ValueTask.CompletedTask;
    }

    private static ValueTask FrameSelectionClampsShortDelays()
    {
        // What wpt/images/anim-gr.gif carries: a 10 ms green frame then a 100 s red one.
        // 10 ms is below the threshold every engine applies, so the green frame occupies
        // 100 ms — not 10 — and the 300 ms screenshot the reftest takes lands on red.
        ImageSequence sequence = Animation(0, [10, 100_000]);

        Assert.Equal(TimeSpan.FromMilliseconds(100), sequence.Frames[0].EffectiveDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(10), sequence.Frames[0].Duration);
        Assert.Equal(0, sequence.FrameIndexAt(TimeSpan.FromMilliseconds(99)));
        Assert.Equal(1, sequence.FrameIndexAt(TimeSpan.FromMilliseconds(100)));
        Assert.Equal(1, sequence.FrameIndexAt(TimeSpan.FromMilliseconds(300)));

        // A zero delay is the same case: "as fast as possible", not "no time at all".
        Assert.Equal(
            TimeSpan.FromMilliseconds(100),
            Animation(0, [0, 0]).Frames[0].EffectiveDuration);

        // A delay on the threshold is honoured as written.
        Assert.Equal(TimeSpan.FromMilliseconds(11), Animation(0, [11, 11]).Frames[0].EffectiveDuration);
        return ValueTask.CompletedTask;
    }

    private static ValueTask FrameSelectionLoopCount()
    {
        // Played twice, then held: 4 × 50 ms of timeline, and every later time is the last frame.
        ImageSequence sequence = Animation(2, [50, 50]);

        Assert.Equal(1, sequence.FrameIndexAt(TimeSpan.FromMilliseconds(50)));
        Assert.Equal(0, sequence.FrameIndexAt(TimeSpan.FromMilliseconds(100)));
        Assert.Equal(1, sequence.FrameIndexAt(TimeSpan.FromMilliseconds(150)));
        Assert.Equal(1, sequence.FrameIndexAt(TimeSpan.FromMilliseconds(200)));
        Assert.Equal(1, sequence.FrameIndexAt(TimeSpan.FromDays(1)));

        // The unbounded case never holds — it is still cycling a day later.
        Assert.Equal(0, Animation(0, [50, 50]).FrameIndexAt(TimeSpan.FromMilliseconds(200)));
        return ValueTask.CompletedTask;
    }

    private static ValueTask AnimationClockPin()
    {
        TimeSpan outer = ImageAnimationClock.PresentationTime;
        using (ImageAnimationClock.Pin(TimeSpan.FromMilliseconds(300)))
        {
            Assert.Equal(TimeSpan.FromMilliseconds(300), ImageAnimationClock.PresentationTime);

            using (ImageAnimationClock.Pin(TimeSpan.FromMilliseconds(50)))
                Assert.Equal(TimeSpan.FromMilliseconds(50), ImageAnimationClock.PresentationTime);

            // The inner scope restores the outer one's value, not the process default.
            Assert.Equal(TimeSpan.FromMilliseconds(300), ImageAnimationClock.PresentationTime);
        }

        Assert.Equal(outer, ImageAnimationClock.PresentationTime);

        ImageAnimationClock.PresentationTime = TimeSpan.FromMilliseconds(-10);
        Assert.Equal(TimeSpan.Zero, ImageAnimationClock.PresentationTime);
        ImageAnimationClock.PresentationTime = outer;
        return ValueTask.CompletedTask;
    }

    // CSS Image Animation 1: an image whose element paused it holds its frame while the rest of
    // the document advances, so the per-image override has to win over the document pin — and give
    // it back untouched, since only one image at a time is inside a scope.
    private static ValueTask AnimationClockPinForImage()
    {
        using (ImageAnimationClock.Pin(TimeSpan.FromMilliseconds(300)))
        {
            Assert.Equal(TimeSpan.FromMilliseconds(300), ImageAnimationClock.PresentationTime);

            using (ImageAnimationClock.PinForImage(TimeSpan.Zero))
            {
                Assert.Equal(TimeSpan.Zero, ImageAnimationClock.PresentationTime);

                using (ImageAnimationClock.PinForImage(TimeSpan.FromMilliseconds(120)))
                    Assert.Equal(TimeSpan.FromMilliseconds(120), ImageAnimationClock.PresentationTime);

                // The inner image scope restores the outer image's override, not the document time.
                Assert.Equal(TimeSpan.Zero, ImageAnimationClock.PresentationTime);
            }

            // Leaving every image scope hands the document time back.
            Assert.Equal(TimeSpan.FromMilliseconds(300), ImageAnimationClock.PresentationTime);

            ImageAnimationClock.PinForImage(TimeSpan.FromMilliseconds(-5)).Dispose();
        }

        return ValueTask.CompletedTask;
    }

    // The reason the override is an AsyncLocal and not a [ThreadStatic]: an async host queues the
    // decode to the pool from inside the scope, and only an AsyncLocal rides the captured
    // ExecutionContext to the thread that runs it. A thread-local would read the document time
    // there and the image would animate anyway — silently, and only in the async configuration.
    private static async ValueTask AnimationClockPinForImageFlowsToPool()
    {
        using (ImageAnimationClock.Pin(TimeSpan.FromMilliseconds(300)))
        {
            var observed = new TaskCompletionSource<TimeSpan>();
            using (ImageAnimationClock.PinForImage(TimeSpan.Zero))
                ThreadPool.QueueUserWorkItem(_ => observed.SetResult(ImageAnimationClock.PresentationTime));

            Assert.Equal(TimeSpan.Zero, await observed.Task);

            // The control: queued outside any image scope, the pool thread sees the document time.
            var unscoped = new TaskCompletionSource<TimeSpan>();
            ThreadPool.QueueUserWorkItem(_ => unscoped.SetResult(ImageAnimationClock.PresentationTime));
            Assert.Equal(TimeSpan.FromMilliseconds(300), await unscoped.Task);
        }
    }

    private static ImageSequence Animation(int loopCount, int[] delaysMilliseconds)
    {
        var frames = new List<ImageFrame>(delaysMilliseconds.Length);
        foreach (int delay in delaysMilliseconds)
            frames.Add(new ImageFrame(Rgba1x1(), TimeSpan.FromMilliseconds(delay)));

        return new ImageSequence(frames, 1, 1, loopCount);
    }

    private static ImageBuffer Rgba1x1() =>
        new(1, 1, ImagePixelFormat.Rgba8, ImageAlphaMode.Straight, 4, new byte[4]);

    private sealed class FakeImageCodec : ImageCodec
    {
        public FakeImageCodec(MediaKind kind)
            : base(new MediaCodecDescriptor(
                new MediaCodecId($"fake.image.{kind}"),
                "Fake Image",
                kind,
                MediaCodecCapabilities.Decode))
        {
        }

        public override ValueTask<MediaProbeResult> ProbeAsync(
            MediaProbeRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(MediaProbeResult.NoMatch(MediaKind.Image));

        public override ValueTask<ImageSequence> DecodeAsync(
            MediaInput input,
            ImageDecodeOptions? options = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ImageSequence.Static(Rgba1x1()));
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

    public static async ValueTask ThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try
        {
            await action().ConfigureAwait(false);
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

