using System;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Graphics.Windows;

namespace Broiler.Media.Video.MediaFoundation;

public sealed class MediaFoundationVideoSession : IVideoSession
{
    private readonly object _gate = new();
    private readonly IMediaFoundationMediaEngine _engine;
    private readonly HwndVideoOutput _target;
    private readonly IDisposable? _platformScope;
    private readonly TaskCompletionSource<VideoStreamInfo> _metadataReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _disposed;
    private VideoStreamInfo? _streamInfo;

    internal MediaFoundationVideoSession(
        IMediaFoundationMediaEngine engine,
        HwndVideoOutput target,
        IDisposable? platformScope = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _platformScope = platformScope;
        _target.ThrowIfUsableTargetRequired();
        _engine.EventReceived += OnEngineEventReceived;
        _target.TargetChanged += OnTargetChanged;
        State = VideoSessionState.Created;
    }

    public event EventHandler<VideoSessionEvent>? StateChanged;

    public VideoSessionState State { get; private set; }

    public VideoStreamInfo StreamInfo =>
        _streamInfo ?? throw new InvalidOperationException("Video stream metadata has not loaded yet.");

    public TimeSpan Position
    {
        get
        {
            if (_disposed)
                return TimeSpan.Zero;

            return _engine.Position;
        }
    }

    internal static async ValueTask<MediaFoundationVideoSession> OpenAsync(
        IMediaFoundationMediaEngine engine,
        HwndVideoOutput target,
        string sourceUri,
        VideoSessionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(sourceUri))
            throw new ArgumentException("A Media Foundation video session needs a source URI.", nameof(sourceUri));

        var session = new MediaFoundationVideoSession(engine, target);
        try
        {
            await session.LoadAsync(sourceUri, cancellationToken).ConfigureAwait(false);
            if (options.Autoplay)
                await session.PlayAsync(cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal async ValueTask<VideoStreamInfo> LoadAsync(string sourceUri, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        SetState(VideoSessionState.Loading, VideoSessionEventKind.Loading);
        _engine.SetSource(sourceUri);
        _engine.Load();
        return await _metadataReady.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask PlayAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        _target.ThrowIfUsableTargetRequired();
        _engine.Play();
        SetState(VideoSessionState.Playing, VideoSessionEventKind.Playing);
        return ValueTask.CompletedTask;
    }

    public ValueTask PauseAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        _engine.Pause();
        SetState(VideoSessionState.Paused, VideoSessionEventKind.Paused);
        return ValueTask.CompletedTask;
    }

    public ValueTask SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (position < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(position));

        cancellationToken.ThrowIfCancellationRequested();
        _engine.Seek(position);
        Raise(VideoSessionEventKind.Seeked);
        return ValueTask.CompletedTask;
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        _engine.Pause();
        _engine.Seek(TimeSpan.Zero);
        SetState(VideoSessionState.Ended, VideoSessionEventKind.Ended);
        await _target.CompleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _engine.EventReceived -= OnEngineEventReceived;
        _target.TargetChanged -= OnTargetChanged;
        _metadataReady.TrySetCanceled();
        _engine.Dispose();
        _platformScope?.Dispose();
        SetState(VideoSessionState.Disposed, VideoSessionEventKind.Disposed);
        await ValueTask.CompletedTask;
    }

    private void OnEngineEventReceived(object? sender, MediaFoundationMediaEngineEvent e)
    {
        if (_disposed)
            return;

        try
        {
            switch (e.Kind)
            {
                case MediaFoundationMediaEngineEventKind.LoadedMetadata:
                case MediaFoundationMediaEngineEventKind.FormatChange:
                    CompleteMetadataLoad();
                    break;
                case MediaFoundationMediaEngineEventKind.Play:
                case MediaFoundationMediaEngineEventKind.Playing:
                case MediaFoundationMediaEngineEventKind.FirstFrameReady:
                    SetState(VideoSessionState.Playing, VideoSessionEventKind.Playing);
                    break;
                case MediaFoundationMediaEngineEventKind.Pause:
                    SetState(VideoSessionState.Paused, VideoSessionEventKind.Paused);
                    break;
                case MediaFoundationMediaEngineEventKind.Seeked:
                    Raise(VideoSessionEventKind.Seeked);
                    break;
                case MediaFoundationMediaEngineEventKind.Ended:
                    SetState(VideoSessionState.Ended, VideoSessionEventKind.Ended);
                    _target.CompleteAsync().AsTask().GetAwaiter().GetResult();
                    break;
                case MediaFoundationMediaEngineEventKind.Error:
                case MediaFoundationMediaEngineEventKind.ResourceLost:
                case MediaFoundationMediaEngineEventKind.StreamRenderingError:
                    Fail(new MediaError(MediaErrorCode.NativeFailure, "Media Foundation media engine reported a playback error."));
                    break;
            }
        }
        catch (Exception ex)
        {
            Fail(new MediaError(MediaErrorCode.NativeFailure, "Media Foundation video event handling failed: " + ex.Message));
        }
    }

    private void OnTargetChanged(object? sender, HwndVideoTargetChangedEventArgs e)
    {
        if (_disposed)
            return;

        if (e.Kind == HwndVideoTargetChangeKind.Destroyed)
        {
            Fail(new MediaError(MediaErrorCode.OutputFailed, "The borrowed video HWND was destroyed by its owner."));
            _engine.Shutdown();
            return;
        }

        _engine.OnTargetChanged(_target);
        Raise(VideoSessionEventKind.TargetChanged);
    }

    private void CompleteMetadataLoad()
    {
        VideoStreamInfo info = _engine.GetStreamInfo();
        _streamInfo = info;
        _metadataReady.TrySetResult(info);
        if (State == VideoSessionState.Loading || State == VideoSessionState.Created)
            SetState(VideoSessionState.Ready, VideoSessionEventKind.Ready);
    }

    private void Fail(MediaError error)
    {
        _metadataReady.TrySetException(new MediaException(error));
        State = VideoSessionState.Failed;
        _target.FailAsync(error).AsTask().GetAwaiter().GetResult();
        Raise(VideoSessionEventKind.Failed, error);
    }

    private void SetState(VideoSessionState state, VideoSessionEventKind kind)
    {
        lock (_gate)
            State = state;
        Raise(kind);
    }

    private void Raise(VideoSessionEventKind kind, MediaError? error = null) =>
        StateChanged?.Invoke(this, new VideoSessionEvent(kind, State, Position, error));

    private void ThrowIfDisposed()
    {
        if (_disposed || State == VideoSessionState.Disposed)
            throw new ObjectDisposedException(nameof(MediaFoundationVideoSession));
        if (State == VideoSessionState.Failed)
            throw new MediaException(new MediaError(MediaErrorCode.OutputFailed, "The Media Foundation video session has failed."));
    }
}
