using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Media.Video.Windows;

namespace Broiler.Media.Video.MediaFoundation.Tests;

internal static class Program
{
    private static async Task<int> Main()
    {
        var tests = new List<(string Name, Func<ValueTask> Body)>
        {
            ("Media Foundation provider exposes one Windows direct-presentation codec", ProviderExposesCodec),
            ("Media Foundation codec probes MP4 signatures and hints", CodecProbesMp4),
            ("Borrowed HWND contract exposes observations, not owner-only operations", BorrowedTargetContractIsBorrowerShaped),
            ("Borrowed HWND target reports lifetime and output state to its borrower", BorrowedTargetLifecycleReachesTheBorrower),
            ("Session loads metadata and controls playback through the engine", SessionLifecycle),
            ("Session forwards target resize and visibility changes", TargetChangesReachSession),
            ("Target destruction fails the session and shuts down the engine", TargetDestructionFailsSession),
            ("Session disposal disconnects engine callbacks", DisposeDisconnectsCallbacks),
            ("Codec validates source policy before native startup", SourcePolicyValidation),
            ("Codec honors cancellation before native startup", CancellationBeforeNativeStartup),
            ("Video abstractions stay MediaFoundation and HWND free", AbstractionsStayBackendFree),
            ("MediaFoundation runtime names no Broiler.Graphics type at all", RuntimeDependencyBoundary),
        };

        int passed = 0;
        var failures = new List<string>();
        Console.WriteLine($"Running {tests.Count} Media Foundation video test(s)...\n");

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

    private static ValueTask ProviderExposesCodec()
    {
        IReadOnlyList<VideoCodec> codecs = MediaFoundationVideoCodecs.CreateCodecs();
        Assert.Equal(1, codecs.Count);
        Assert.True(codecs[0] is MediaFoundationVideoCodec);
        Assert.Equal(new MediaCodecId("broiler.video.mediafoundation.windows"), codecs[0].Id);
        Assert.True((codecs[0].Descriptor.Capabilities & MediaCodecCapabilities.Decode) != 0);
        Assert.True((codecs[0].Descriptor.Capabilities & MediaCodecCapabilities.Streaming) != 0);
        Assert.True((codecs[0].Descriptor.Capabilities & MediaCodecCapabilities.DirectPresentation) != 0);
        Assert.Equal("MPEG-4 video", codecs[0].Descriptor.Formats[0].Name);

        bool windowsOnly = typeof(MediaFoundationVideoCodecs)
            .GetCustomAttributes(typeof(SupportedOSPlatformAttribute), inherit: false)
            .Cast<SupportedOSPlatformAttribute>()
            .Any(attribute => string.Equals(attribute.PlatformName, "windows", StringComparison.OrdinalIgnoreCase));
        Assert.True(windowsOnly, "Expected provider to be marked Windows-only.");
        return ValueTask.CompletedTask;
    }

    private static async ValueTask CodecProbesMp4()
    {
        var codec = new MediaFoundationVideoCodec();
        byte[] prefix = [0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m'];

        MediaProbeResult signature = await codec.ProbeAsync(new MediaProbeRequest(prefix)).ConfigureAwait(false);
        Assert.True(signature.IsMatch);
        Assert.Equal("video/mp4", signature.MimeType);

        MediaProbeResult hint = await codec.ProbeAsync(new MediaProbeRequest(
            ReadOnlyMemory<byte>.Empty,
            new MediaSourceHints(fileExtension: "mp4"))).ConfigureAwait(false);
        Assert.True(hint.IsMatch);

        MediaProbeResult none = await codec.ProbeAsync(new MediaProbeRequest(new byte[] { 1, 2, 3, 4 })).ConfigureAwait(false);
        Assert.False(none.IsMatch);
    }

    private static ValueTask BorrowedTargetContractIsBorrowerShaped()
    {
        // ADR 0005 splits ownership: the windowing layer creates, resizes, shows/hides and
        // destroys the HWND; the video backend only borrows it. ADR 0006 makes that split a
        // compile-time guarantee by handing the backend an interface that simply has no
        // owner-only operation to call. Guard that shape — re-adding one of these members
        // would silently hand every borrower the ability to mutate a window it does not own.
        Type[] contract = [typeof(IHwndVideoOutput), .. typeof(IHwndVideoOutput).GetInterfaces()];
        string[] ownerOnly = ["Resize", "SetVisible", "NotifyDestroyed"];

        foreach (Type type in contract)
        {
            foreach (MethodInfo method in type.GetMethods())
            {
                Assert.False(
                    ownerOnly.Contains(method.Name, StringComparer.Ordinal),
                    $"{type.Name} exposes owner-only operation '{method.Name}' to borrowers.");
            }
        }

        // The observations a borrower legitimately needs must all be present.
        foreach (string member in new[] { "Hwnd", "Width", "Height", "IsVisible", "IsDestroyed" })
            Assert.True(typeof(IHwndVideoOutput).GetProperty(member) is not null, $"Expected {member} on the contract.");

        Assert.True(
            typeof(IHwndVideoOutput).GetMethod("ThrowIfUsableTargetRequired") is not null,
            "Expected the usability check on the contract.");
        Assert.True(
            typeof(IVideoOutput).IsAssignableFrom(typeof(IHwndVideoOutput)),
            "The borrowed HWND target must still be a platform-neutral video output.");

        return ValueTask.CompletedTask;
    }

    private static ValueTask BorrowedTargetLifecycleReachesTheBorrower()
    {
        FakeHwndVideoTarget target = CreateTarget();
        var changes = new List<HwndVideoTargetChangeKind>();
        target.TargetChanged += (_, e) => changes.Add(e.Kind);

        target.Resize(800, 450);
        target.SetVisible(false);
        target.NotifyDestroyed();

        Assert.Equal(800, target.Width);
        Assert.Equal(450, target.Height);
        Assert.False(target.IsVisible);
        Assert.True(target.IsDestroyed);
        Assert.SequenceEqual(
            new[]
            {
                HwndVideoTargetChangeKind.Resized,
                HwndVideoTargetChangeKind.VisibilityChanged,
                HwndVideoTargetChangeKind.Destroyed,
            },
            changes);
        Assert.Throws<ObjectDisposedException>(() => target.ThrowIfUsableTargetRequired());

        var error = new MediaError(MediaErrorCode.OutputFailed, "target failed");
        target.FailAsync(error).AsTask().GetAwaiter().GetResult();
        Assert.Equal(error, target.Failure);
        return ValueTask.CompletedTask;
    }

    private static async ValueTask SessionLifecycle()
    {
        FakeMediaEngine engine = new();
        FakeHwndVideoTarget target = CreateTarget();
        await using var session = new MediaFoundationVideoSession(engine, target);
        var events = new List<VideoSessionEventKind>();
        session.StateChanged += (_, e) => events.Add(e.Kind);

        VideoStreamInfo info = await session.LoadAsync("file:///C:/video.mp4", CancellationToken.None).ConfigureAwait(false);
        Assert.Equal(640, info.DisplayWidth);
        Assert.Equal(VideoSessionState.Ready, session.State);

        await session.PlayAsync().ConfigureAwait(false);
        Assert.Equal(VideoSessionState.Playing, session.State);
        await session.PauseAsync().ConfigureAwait(false);
        Assert.Equal(VideoSessionState.Paused, session.State);
        await session.SeekAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        Assert.Equal(TimeSpan.FromSeconds(2), session.Position);
        await session.StopAsync().ConfigureAwait(false);
        Assert.Equal(VideoSessionState.Ended, session.State);
        Assert.True(target.Completed);

        Assert.SequenceEqual(
            new[]
            {
                VideoSessionEventKind.Loading,
                VideoSessionEventKind.Ready,
                VideoSessionEventKind.Playing,
                VideoSessionEventKind.Paused,
                VideoSessionEventKind.Seeked,
                VideoSessionEventKind.Ended,
            },
            events);
        Assert.SequenceEqual(
            new[] { "SetSource:file:///C:/video.mp4", "Load", "Play", "Pause", "Seek:2.000", "Pause", "Seek:0.000" },
            engine.Calls);
    }

    private static async ValueTask TargetChangesReachSession()
    {
        FakeMediaEngine engine = new();
        FakeHwndVideoTarget target = CreateTarget();
        await using var session = new MediaFoundationVideoSession(engine, target);
        var events = new List<VideoSessionEventKind>();
        session.StateChanged += (_, e) => events.Add(e.Kind);
        await session.LoadAsync("file:///C:/video.mp4", CancellationToken.None).ConfigureAwait(false);

        target.Resize(1024, 576);
        target.SetVisible(false);

        Assert.Equal(2, engine.TargetChangeCount);
        Assert.Equal(1024, engine.LastTargetWidth);
        Assert.Equal(576, engine.LastTargetHeight);
        Assert.False(engine.LastTargetVisible);
        Assert.True(events.Count(kind => kind == VideoSessionEventKind.TargetChanged) == 2);
    }

    private static async ValueTask TargetDestructionFailsSession()
    {
        FakeMediaEngine engine = new();
        FakeHwndVideoTarget target = CreateTarget();
        await using var session = new MediaFoundationVideoSession(engine, target);
        var events = new List<VideoSessionEventKind>();
        session.StateChanged += (_, e) => events.Add(e.Kind);
        await session.LoadAsync("file:///C:/video.mp4", CancellationToken.None).ConfigureAwait(false);

        target.NotifyDestroyed();

        Assert.Equal(VideoSessionState.Failed, session.State);
        Assert.True(target.Failure is not null);
        Assert.Equal(MediaErrorCode.OutputFailed, target.Failure!.Code);
        Assert.Equal(1, engine.ShutdownCount);
        Assert.True(events.Contains(VideoSessionEventKind.Failed));
        await Assert.ThrowsAsync<MediaException>(async () => await session.PlayAsync().ConfigureAwait(false)).ConfigureAwait(false);
    }

    private static async ValueTask DisposeDisconnectsCallbacks()
    {
        FakeMediaEngine engine = new();
        FakeHwndVideoTarget target = CreateTarget();
        var session = new MediaFoundationVideoSession(engine, target);
        await session.LoadAsync("file:///C:/video.mp4", CancellationToken.None).ConfigureAwait(false);
        await session.DisposeAsync().ConfigureAwait(false);

        engine.Raise(MediaFoundationMediaEngineEventKind.Ended);

        Assert.Equal(VideoSessionState.Disposed, session.State);
        Assert.False(target.Completed, "Disposed sessions should ignore late engine callbacks.");
        Assert.Equal(1, engine.DisposeCount);
    }

    private static async ValueTask SourcePolicyValidation()
    {
        var codec = new MediaFoundationVideoCodec();
        using var empty = new MediaInput(new MemoryStream());
        await Assert.ThrowsAsync<MediaException>(async () =>
            await codec.GetInfoAsync(empty).ConfigureAwait(false)).ConfigureAwait(false);

        using var network = new MediaInput(
            new MemoryStream(),
            new MediaSourceHints(sourceUri: "https://example.test/video.mp4"));
        MediaException ex = await Assert.ThrowsAsync<MediaException>(async () =>
            await codec.GetInfoAsync(network).ConfigureAwait(false)).ConfigureAwait(false);
        Assert.Equal(MediaErrorCode.UnsupportedFormat, ex.Error.Code);
    }

    private static async ValueTask CancellationBeforeNativeStartup()
    {
        var codec = new MediaFoundationVideoCodec();
        using var input = new MediaInput(new MemoryStream());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(false);

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await codec.GetInfoAsync(input, cancellationToken: cts.Token).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await codec.OpenSessionAsync(input, new RecordingVideoOutput("unused"), cancellationToken: cts.Token).ConfigureAwait(false)).ConfigureAwait(false);
    }

    private static ValueTask AbstractionsStayBackendFree()
    {
        string root = FindMediaRoot();
        string runtimeRoot = Path.Combine(root, "Broiler.Media.Video");
        foreach (string file in Directory.EnumerateFiles(runtimeRoot, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            Assert.DoesNotContain("MediaFoundation", text, file);
            Assert.DoesNotContain("IMFMediaEngine", text, file);
            Assert.DoesNotContain("HWND", text, file);
        }

        return ValueTask.CompletedTask;
    }

    private static ValueTask RuntimeDependencyBoundary()
    {
        string root = FindMediaRoot();
        string runtimeRoot = Path.Combine(root, "Broiler.Media.Video.MediaFoundation");
        foreach (string file in Directory.EnumerateFiles(runtimeRoot, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);

            // There is no longer ANY approved Graphics edge. The backend borrows its HWND
            // presentation target through Broiler.Media.Video.Windows' IHwndVideoOutput, so
            // the graphics assembly that owns the window is invisible here (ADR 0006). This
            // used to permit "Broiler.Graphics.Windows" — the exception that closed the
            // Media → Graphics → Media cycle. Do not reintroduce it.
            Assert.DoesNotContain("Broiler.Graphics", text, file);

            Assert.DoesNotContain("Broiler.Html", text, file);
            Assert.DoesNotContain("SwapChain", text, file);
            Assert.DoesNotContain("Direct3D", text, file);

            // Borrow the HWND target only — never a rendering surface / GPU presentation backend.
            Assert.DoesNotContain("Direct2DSurface", text, file);
            Assert.DoesNotContain("Direct2DDevice", text, file);
            Assert.DoesNotContain("Direct2DImageStore", text, file);
        }

        return ValueTask.CompletedTask;
    }

    private static FakeHwndVideoTarget CreateTarget() =>
        new((nint)1234, "test hwnd", 640, 360);

    private static string FindMediaRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "Broiler.Media.Video.MediaFoundation", "Broiler.Media.Video.MediaFoundation.csproj");
            if (File.Exists(candidate))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the Broiler.Media root.");
    }

    private sealed class FakeMediaEngine : IMediaFoundationMediaEngine
    {
        private readonly VideoStreamInfo _info = new(640, 360, 640, 360, TimeSpan.FromSeconds(5), 30);

        public event EventHandler<MediaFoundationMediaEngineEvent>? EventReceived;

        public List<string> Calls { get; } = [];

        public TimeSpan Position { get; private set; }

        public int TargetChangeCount { get; private set; }

        public int LastTargetWidth { get; private set; }

        public int LastTargetHeight { get; private set; }

        public bool LastTargetVisible { get; private set; }

        public int ShutdownCount { get; private set; }

        public int DisposeCount { get; private set; }

        public void SetSource(string sourceUri) => Calls.Add("SetSource:" + sourceUri);

        public void Load()
        {
            Calls.Add("Load");
            Raise(MediaFoundationMediaEngineEventKind.LoadedMetadata);
        }

        public void Play() => Calls.Add("Play");

        public void Pause() => Calls.Add("Pause");

        public void Seek(TimeSpan position)
        {
            Position = position;
            Calls.Add("Seek:" + position.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture));
        }

        public VideoStreamInfo GetStreamInfo() => _info;

        public void OnTargetChanged(IHwndVideoOutput target)
        {
            TargetChangeCount++;
            LastTargetWidth = target.Width;
            LastTargetHeight = target.Height;
            LastTargetVisible = target.IsVisible;
        }

        public void Shutdown() => ShutdownCount++;

        public void Dispose()
        {
            DisposeCount++;
            EventReceived = null;
        }

        public void Raise(MediaFoundationMediaEngineEventKind kind) =>
            EventReceived?.Invoke(this, new MediaFoundationMediaEngineEvent(kind, 0, 0));
    }

    /// <summary>
    /// Stands in for the window owner (in production, Broiler.Graphics.Windows). The
    /// owner-only mutators live on the class, not on <see cref="IHwndVideoOutput"/>, exactly
    /// as they do in the real implementation — the test drives them as the owner would, while
    /// the session under test sees only the borrower contract.
    /// </summary>
    private sealed class FakeHwndVideoTarget : IHwndVideoOutput
    {
        public FakeHwndVideoTarget(nint hwnd, string displayName, int width, int height, bool isVisible = true)
        {
            Hwnd = hwnd;
            DisplayName = displayName;
            Width = width;
            Height = height;
            IsVisible = isVisible;
        }

        public event EventHandler<HwndVideoTargetChangedEventArgs>? TargetChanged;

        public nint Hwnd { get; }

        public string DisplayName { get; }

        public int Width { get; private set; }

        public int Height { get; private set; }

        public bool IsVisible { get; private set; }

        public bool IsDestroyed { get; private set; }

        public bool Completed { get; private set; }

        public MediaError? Failure { get; private set; }

        public void Resize(int width, int height)
        {
            ThrowIfUsableTargetRequired();
            Width = width;
            Height = height;
            Raise(HwndVideoTargetChangeKind.Resized);
        }

        public void SetVisible(bool isVisible)
        {
            ThrowIfUsableTargetRequired();
            IsVisible = isVisible;
            Raise(HwndVideoTargetChangeKind.VisibilityChanged);
        }

        public void NotifyDestroyed()
        {
            if (IsDestroyed)
                return;

            IsDestroyed = true;
            IsVisible = false;
            Raise(HwndVideoTargetChangeKind.Destroyed);
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

        public void ThrowIfUsableTargetRequired()
        {
            if (IsDestroyed)
                throw new ObjectDisposedException(nameof(FakeHwndVideoTarget), "The borrowed HWND has been destroyed by its owner.");
        }

        private void Raise(HwndVideoTargetChangeKind kind) =>
            TargetChanged?.Invoke(this, new HwndVideoTargetChangedEventArgs(kind, Width, Height, IsVisible));
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

    public static void DoesNotContain(string unexpected, string actual, string? message = null)
    {
        if (actual.Contains(unexpected, StringComparison.Ordinal))
            throw new AssertException(message ?? $"Expected text not to contain '{unexpected}'.");
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
