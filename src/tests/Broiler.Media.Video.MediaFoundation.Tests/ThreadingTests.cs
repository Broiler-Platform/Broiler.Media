using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media.Video.MediaFoundation.Tests;

internal static partial class Program
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private static void RegisterThreadingTests(List<(string Name, Func<ValueTask> Body)> tests)
    {
        tests.Add(("Engine creation, operations and cleanup share one owner thread", EngineThreadOwnership));
        tests.Add(("Metadata await retains COM ownership and cleans up on its owner", MetadataThreadOwnership));
        tests.Add(("Cancellation cleans up pending metadata and session opens", CancellationCleansUpOwner));
        tests.Add(("Engine creation failure releases the platform on its owner", CreationFailureCleansUpOwner));
        tests.Add(("Real COM and Media Foundation scope survives asynchronous metadata", NativePlatformThreadOwnership));
        tests.Add(("Native media engine can be created and disposed across caller threads", NativeEngineLifetime));
        tests.Add(("Native media errors cross the asynchronous callback queue", NativeEngineError));
        tests.Add(("Native callbacks return while output completion is pending", CallbackCompletionIsAsynchronous));
        tests.Add(("Disposal does not wait for an output that ignores cancellation", DisposalWithPendingOutput));
        tests.Add(("Output failures and throwing failure handlers are observed", CallbackFailuresAreObserved));
        tests.Add(("A state subscriber can dispose the session from a callback", CallbackCanDisposeSession));
        tests.Add(("Concurrent disposal shares one cleanup operation", ConcurrentDisposal));
        tests.Add(("Disposed subscribers can reenter disposal without waiting on themselves", DisposedSubscriberCanReenter));
        tests.Add(("Output cancellation callbacks can await disposal", OutputCancellationCanReenter));
    }

    private sealed class ThreadPlatformScope : IDisposable
    {
        private readonly IDisposable? _native;
        internal int CreatedThread { get; } = Environment.CurrentManagedThreadId;
        internal int DisposedThread { get; private set; }
        internal int DisposeCount { get; private set; }

        internal ThreadPlatformScope(bool native = false)
        {
            if (native)
                _native = new MediaFoundationPlatformScope();
        }

        public void Dispose()
        {
            DisposedThread = Environment.CurrentManagedThreadId;
            DisposeCount++;
            _native?.Dispose();
        }
    }

    private static MediaInput FileInput() => new(new MemoryStream(), new MediaSourceHints(sourceUri: "file:///C:/video.mp4"));

    private static void AssertOwner(ThreadPlatformScope platform, FakeMediaEngine engine)
    {
        Assert.Equal(platform.CreatedThread, platform.DisposedThread);
        Assert.Equal(1, platform.DisposeCount);
        Assert.Equal(1, engine.DisposeCount);
        Assert.True(engine.ThreadIds.Count >= 3);
        Assert.True(engine.ThreadIds.All(id => id == platform.CreatedThread), "Every engine call must run on its COM owner.");
    }

    private static async ValueTask EngineThreadOwnership()
    {
        ThreadPlatformScope? platform = null;
        int creationThread = 0;
        var fake = new FakeMediaEngine { AutoMetadata = false };
        var engine = new MediaFoundationEngineThread(() => platform = new ThreadPlatformScope(), () =>
        {
            creationThread = Environment.CurrentManagedThreadId;
            return fake;
        });
        var session = new MediaFoundationVideoSession(engine, CreateTarget());
        Task<VideoStreamInfo> loading = session.LoadAsync("file:///C:/video.mp4", CancellationToken.None).AsTask();
        await fake.Loaded.Task.WaitAsync(TestTimeout);
        Assert.False(loading.IsCompleted);
        await Task.Run(() => fake.Raise(MediaFoundationMediaEngineEventKind.LoadedMetadata)).WaitAsync(TestTimeout);
        await loading.WaitAsync(TestTimeout);
        await Task.Run(async () =>
        {
            await session.PlayAsync();
            await session.PauseAsync();
            await session.SeekAsync(TimeSpan.FromSeconds(1));
            await session.DisposeAsync();
        }).WaitAsync(TestTimeout);
        Assert.Equal(platform!.CreatedThread, creationThread);
        AssertOwner(platform, fake);
    }

    private static ValueTask MetadataThreadOwnership() => CheckMetadataOwner(native: false);
    private static ValueTask NativePlatformThreadOwnership() => CheckMetadataOwner(native: true);

    private static async ValueTask NativeEngineLifetime()
    {
        var engine = new MediaFoundationEngineThread(() => new MediaFoundationPlatformScope(),
            () => MediaFoundationMediaEngine.Create(null, new VideoSessionOptions(autoplay: false, muted: true)));
        await Task.Run(engine.Dispose).WaitAsync(TestTimeout);
    }

    private static async ValueTask NativeEngineError()
    {
        using var cancellation = new CancellationTokenSource(TestTimeout);
        using var input = new MediaInput(new MemoryStream(), new MediaSourceHints(
            sourceUri: new Uri(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp4")).AbsoluteUri));
        MediaException error = await Assert.ThrowsAsync<MediaException>(async () =>
            await new MediaFoundationVideoCodec().GetInfoAsync(input, cancellationToken: cancellation.Token));
        Assert.Equal(MediaErrorCode.NativeFailure, error.Error.Code);
        Assert.True(error.Error.Message.Contains("loading video metadata", StringComparison.Ordinal));
    }

    private static async ValueTask CheckMetadataOwner(bool native)
    {
        ThreadPlatformScope? platform = null;
        var fake = new FakeMediaEngine { AutoMetadata = false };
        var codec = new MediaFoundationVideoCodec((_, _) => new MediaFoundationEngineThread(
            () => platform = new ThreadPlatformScope(native), () => fake));
        using MediaInput input = FileInput();
        Task<VideoStreamInfo> loading = codec.GetInfoAsync(input).AsTask();
        await fake.Loaded.Task.WaitAsync(TestTimeout);
        Assert.False(loading.IsCompleted);
        await Task.Run(() => fake.Raise(MediaFoundationMediaEngineEventKind.LoadedMetadata)).WaitAsync(TestTimeout);
        VideoStreamInfo info = await loading.WaitAsync(TestTimeout);
        Assert.Equal(640, info.DisplayWidth);
        AssertOwner(platform!, fake);
    }

    private static async ValueTask CancellationCleansUpOwner()
    {
        foreach (bool session in new[] { false, true })
        {
            ThreadPlatformScope? platform = null;
            var fake = new FakeMediaEngine { AutoMetadata = false };
            var codec = new MediaFoundationVideoCodec((_, _) => new MediaFoundationEngineThread(
                () => platform = new ThreadPlatformScope(), () => fake));
            using MediaInput input = FileInput();
            using var cancellation = new CancellationTokenSource();
            Task loading = session
                ? codec.OpenSessionAsync(input, CreateTarget(), cancellationToken: cancellation.Token).AsTask()
                : codec.GetInfoAsync(input, cancellationToken: cancellation.Token).AsTask();
            await fake.Loaded.Task.WaitAsync(TestTimeout);
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => loading.WaitAsync(TestTimeout));
            AssertOwner(platform!, fake);
        }
    }

    private static ValueTask CreationFailureCleansUpOwner()
    {
        ThreadPlatformScope? platform = null;
        Assert.Throws<InvalidOperationException>(() => new MediaFoundationEngineThread(
            () => platform = new ThreadPlatformScope(),
            () => throw new InvalidOperationException("Simulated native creation failure.")));
        Assert.Equal(1, platform!.DisposeCount);
        Assert.Equal(platform.CreatedThread, platform.DisposedThread);
        return ValueTask.CompletedTask;
    }

    private static async ValueTask CallbackCompletionIsAsynchronous()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fake = new FakeMediaEngine();
        int completions = 0;
        var target = new FakeHwndVideoTarget(1234, "delayed", 640, 360)
        {
            OnComplete = _ =>
            {
                if (++completions != 1)
                    return ValueTask.CompletedTask;
                entered.SetResult();
                return new ValueTask(finish.Task);
            }
        };
        await using var session = new MediaFoundationVideoSession(fake, target);
        await session.LoadAsync("file:///C:/video.mp4", CancellationToken.None);
        await Task.Run(() => fake.Raise(MediaFoundationMediaEngineEventKind.Ended)).WaitAsync(TestTimeout);
        await entered.Task.WaitAsync(TestTimeout);
        fake.Raise(MediaFoundationMediaEngineEventKind.Pause);
        await session.FlushNativeEventsAsync().WaitAsync(TestTimeout);
        Assert.Equal(VideoSessionState.Paused, session.State);
        fake.Raise(MediaFoundationMediaEngineEventKind.Ended);
        await session.FlushNativeEventsAsync().WaitAsync(TestTimeout);
        Assert.Equal(1, completions, "Output calls must remain serialized while completion is pending.");
        finish.SetResult();
        await session.FlushEventsAsync().WaitAsync(TestTimeout);
        Assert.Equal(2, completions);
    }

    private static async ValueTask DisposalWithPendingOutput()
    {
        foreach (bool failure in new[] { false, true })
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken outputToken = default;
            ValueTask Delay(CancellationToken token)
            {
                outputToken = token;
                entered.SetResult();
                return new ValueTask(finish.Task); // Deliberately ignores cancellation.
            }
            var fake = new FakeMediaEngine();
            var target = new FakeHwndVideoTarget(1234, "delayed", 640, 360)
            {
                OnComplete = Delay,
                OnFail = (_, token) => Delay(token)
            };
            var session = new MediaFoundationVideoSession(fake, target);
            await session.LoadAsync("file:///C:/video.mp4", CancellationToken.None);
            await Task.Run(() => fake.Raise(failure ? MediaFoundationMediaEngineEventKind.Error :
                MediaFoundationMediaEngineEventKind.Ended)).WaitAsync(TestTimeout);
            await entered.Task.WaitAsync(TestTimeout);
            if (!failure)
            {
                target.NotifyDestroyed();
                await session.FlushNativeEventsAsync().WaitAsync(TestTimeout);
                Assert.Equal(1, fake.ShutdownCount);
                Assert.Equal(VideoSessionState.Failed, session.State);
            }
            await session.DisposeAsync().AsTask().WaitAsync(TestTimeout);
            Assert.True(outputToken.IsCancellationRequested);
            Assert.Equal(1, fake.DisposeCount);
            finish.SetException(new InvalidOperationException("Late output failure."));
            await session.FlushEventsAsync().WaitAsync(TestTimeout);
            Assert.Equal(VideoSessionState.Disposed, session.State);
        }
    }

    private static async ValueTask CallbackFailuresAreObserved()
    {
        var fake = new FakeMediaEngine();
        var target = new FakeHwndVideoTarget(1234, "throwing", 640, 360)
        {
            OnComplete = _ => ValueTask.FromException(new InvalidOperationException("Completion failed.")),
            OnFail = (_, _) => ValueTask.FromException(new InvalidOperationException("Failure reporting failed."))
        };
        await using var session = new MediaFoundationVideoSession(fake, target);
        await session.LoadAsync("file:///C:/video.mp4", CancellationToken.None);
        fake.Raise(MediaFoundationMediaEngineEventKind.Ended);
        await session.FlushEventsAsync().WaitAsync(TestTimeout);
        Assert.Equal(VideoSessionState.Failed, session.State);
        Assert.Equal(MediaErrorCode.OutputFailed, target.Failure!.Code);
    }

    private static async ValueTask CallbackCanDisposeSession()
    {
        var fake = new FakeMediaEngine();
        using var engine = new MediaFoundationEngineThread(() => new ThreadPlatformScope(), () => fake);
        var session = new MediaFoundationVideoSession(engine, CreateTarget());
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.StateChanged += (_, e) =>
        {
            if (e.Kind != VideoSessionEventKind.Ended)
                return;
            session.DisposeAsync().AsTask().GetAwaiter().GetResult();
            disposed.SetResult();
        };
        await session.LoadAsync("file:///C:/video.mp4", CancellationToken.None);
        await Task.Run(() => fake.Raise(MediaFoundationMediaEngineEventKind.Ended)).WaitAsync(TestTimeout);
        await disposed.Task.WaitAsync(TestTimeout);
        Assert.Equal(VideoSessionState.Disposed, session.State);
    }

    private static async ValueTask ConcurrentDisposal()
    {
        var fake = new FakeMediaEngine();
        var session = new MediaFoundationVideoSession(fake, CreateTarget());
        await session.LoadAsync("file:///C:/video.mp4", CancellationToken.None);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () => await session.DisposeAsync())))
            .WaitAsync(TestTimeout);
        Assert.Equal(1, fake.DisposeCount);
    }

    private static async ValueTask DisposedSubscriberCanReenter()
    {
        var session = new MediaFoundationVideoSession(new FakeMediaEngine(), CreateTarget());
        var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.StateChanged += (_, e) =>
        {
            if (e.Kind == VideoSessionEventKind.Disposed)
            {
                session.DisposeAsync().AsTask().GetAwaiter().GetResult();
                notified.SetResult();
            }
        };
        await session.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        await notified.Task.WaitAsync(TestTimeout);
    }

    private static async ValueTask OutputCancellationCanReenter()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        MediaFoundationVideoSession? session = null;
        CancellationTokenRegistration registration = default;
        var target = new FakeHwndVideoTarget(1234, "cancel callback", 640, 360)
        {
            OnComplete = token =>
            {
                registration = token.Register(() =>
                {
                    session!.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    canceled.SetResult();
                });
                entered.SetResult();
                return new ValueTask(finish.Task);
            }
        };
        var fake = new FakeMediaEngine();
        session = new MediaFoundationVideoSession(fake, target);
        await session.LoadAsync("file:///C:/video.mp4", CancellationToken.None);
        fake.Raise(MediaFoundationMediaEngineEventKind.Ended);
        await entered.Task.WaitAsync(TestTimeout);
        await session.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        await canceled.Task.WaitAsync(TestTimeout);
        finish.SetResult();
        registration.Dispose();
    }
}
