using Broiler.Media.Video.Windows;
using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media.Video.MediaFoundation;

/// <remarks>
/// Native notifications and output lifecycle calls are dispatched asynchronously.
/// Output implementations and event subscribers must marshal UI work to their own
/// dispatcher. Disposal releases the native engine without waiting for pending output
/// work; that work receives cancellation and any later exception is observed.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class MediaFoundationVideoSession : IVideoSession
{
    private readonly object _gate = new();
    private readonly IMediaFoundationMediaEngine _engine;
    private readonly IHwndVideoOutput _target;
    private readonly MediaFoundationCallbackQueue _callbacks;
    private readonly MediaFoundationCallbackQueue _outputs;
    private Task? _disposeTask;
    private volatile VideoSessionState _state;
    private readonly TaskCompletionSource<VideoStreamInfo> _metadataReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _disposed;
    private volatile VideoStreamInfo? _streamInfo;

    internal MediaFoundationVideoSession(IMediaFoundationMediaEngine engine, IHwndVideoOutput target)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _target = target ?? throw new ArgumentNullException(nameof(target));

        _target.ThrowIfUsableTargetRequired();
        _callbacks = new MediaFoundationCallbackQueue(ex => FailAsync(new MediaError(
            MediaErrorCode.OutputFailed, "Media Foundation video event handling failed: " + ex.Message)));
        _outputs = new MediaFoundationCallbackQueue(ex => FailAsync(new MediaError(
            MediaErrorCode.OutputFailed, "Video output operation failed: " + ex.Message), notifyInline: true));
        _engine.EventReceived += OnEngineEventReceived;
        _target.TargetChanged += OnTargetChanged;

        _state = VideoSessionState.Created;
    }

    public event EventHandler<VideoSessionEvent>? StateChanged;

    public VideoSessionState State => _state;

    internal async Task FlushEventsAsync()
    {
        await _callbacks.FlushAsync().ConfigureAwait(false);
        await _outputs.FlushAsync().ConfigureAwait(false);
    }

    internal Task FlushNativeEventsAsync() => _callbacks.FlushAsync();

    public VideoStreamInfo StreamInfo => _streamInfo ?? throw new InvalidOperationException("Video stream metadata has not loaded yet.");

    public TimeSpan Position
    {
        get
        {
            lock (_gate)
                return _disposed || State == VideoSessionState.Failed ? TimeSpan.Zero : _engine.Position;
        }
    }

    internal async ValueTask<VideoStreamInfo> LoadAsync(string sourceUri, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        
        cancellationToken.ThrowIfCancellationRequested();
        SetState(VideoSessionState.Loading, VideoSessionEventKind.Loading);
        
        lock (_gate)
        {
            ThrowIfDisposed();
            _engine.SetSource(sourceUri);
            _engine.Load();
        }

        return await _metadataReady.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask PlayAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        cancellationToken.ThrowIfCancellationRequested();
        _target.ThrowIfUsableTargetRequired();
        lock (_gate) { ThrowIfDisposed(); _engine.Play(); }
        
        SetState(VideoSessionState.Playing, VideoSessionEventKind.Playing);
        return ValueTask.CompletedTask;
    }

    public ValueTask PauseAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) { ThrowIfDisposed(); _engine.Pause(); }
        
        SetState(VideoSessionState.Paused, VideoSessionEventKind.Paused);
        return ValueTask.CompletedTask;
    }

    public ValueTask SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfLessThan(position, TimeSpan.Zero);

        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) { ThrowIfDisposed(); _engine.Seek(position); }

        Raise(VideoSessionEventKind.Seeked);
        return ValueTask.CompletedTask;
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        
        lock (_gate)
        {
            ThrowIfDisposed();
            _engine.Pause();
            _engine.Seek(TimeSpan.Zero);
        }
        
        SetState(VideoSessionState.Ended, VideoSessionEventKind.Ended);
        await _target.CompleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposeTask is not null)
                return new ValueTask(_disposeTask);
            _disposed = true;
            _metadataReady.TrySetCanceled();
            _engine.EventReceived -= OnEngineEventReceived;
            _target.TargetChanged -= OnTargetChanged;
            // Do not join the callback queue: output code may be awaiting disposal itself.
            var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTask = disposed.Task;
            _ = Task.Run(() =>
            {
                _callbacks.Stop();
                _outputs.Stop();
                Exception? failure = null;
                try { _engine.Dispose(); }
                catch (Exception ex) { failure = ex; }
                finally
                {
                    _state = VideoSessionState.Disposed;
                    // Complete before notifying subscribers, which may themselves dispose.
                    if (failure is null) disposed.SetResult();
                    else disposed.SetException(failure);
                    try { Raise(VideoSessionEventKind.Disposed); } catch { }
                }
            });
            return new ValueTask(_disposeTask);
        }
    }

    private void OnEngineEventReceived(object? sender, MediaFoundationMediaEngineEvent e) =>
        _callbacks.Post(() => HandleEngineEventAsync(e));

    private async ValueTask HandleEngineEventAsync(MediaFoundationMediaEngineEvent e)
    {
        if (_disposed || State == VideoSessionState.Failed)
            return;

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
                _outputs.Post(() => _disposed ? ValueTask.CompletedTask : _target.CompleteAsync(_outputs.CancellationToken));
                break;
            case MediaFoundationMediaEngineEventKind.Error:
            case MediaFoundationMediaEngineEventKind.ResourceLost:
            case MediaFoundationMediaEngineEventKind.StreamRenderingError:
                await FailAsync(new MediaError(MediaErrorCode.NativeFailure,
                    "Media Foundation media engine reported a playback error.")).ConfigureAwait(false);
                break;
        }
    }

    private void OnTargetChanged(object? sender, HwndVideoTargetChangedEventArgs e) =>
        _callbacks.Post(() => HandleTargetChangedAsync(e));

    private async ValueTask HandleTargetChangedAsync(HwndVideoTargetChangedEventArgs e)
    {
        if (e.Kind == HwndVideoTargetChangeKind.Destroyed)
        {
            await FailAsync(new MediaError(MediaErrorCode.OutputFailed,
                "The borrowed video HWND was destroyed by its owner."), shutdownEngine: true).ConfigureAwait(false);
            return;
        }
        lock (_gate)
        {
            if (_disposed || State == VideoSessionState.Failed)
                return;
            _engine.OnTargetChanged(_target);
        }
        Raise(VideoSessionEventKind.TargetChanged);
    }

    private void CompleteMetadataLoad()
    {
        VideoStreamInfo info;
        bool ready;
        lock (_gate)
        {
            if (_disposed || State == VideoSessionState.Failed)
                return;
            info = _engine.GetStreamInfo();
            _streamInfo = info;
            ready = State is VideoSessionState.Loading or VideoSessionState.Created;
            if (ready)
                _state = VideoSessionState.Ready;
        }
        if (ready)
            Raise(VideoSessionEventKind.Ready);
        _metadataReady.TrySetResult(info);
    }

    private ValueTask FailAsync(MediaError error, bool notifyInline = false, bool shutdownEngine = false)
    {
        lock (_gate)
        {
            if (_disposed || State == VideoSessionState.Failed)
                return ValueTask.CompletedTask;
            _state = VideoSessionState.Failed;
            _metadataReady.TrySetException(new MediaException(error));
            if (shutdownEngine)
            {
                try { _engine.Shutdown(); }
                catch (Exception ex)
                {
                    error = new MediaError(MediaErrorCode.NativeFailure, error.Message + " Shutdown failed: " + ex.Message);
                }
            }
        }
        if (notifyInline)
            return NotifyFailureAsync(error);
        _outputs.Post(() => NotifyFailureAsync(error));
        return ValueTask.CompletedTask;
    }

    private async ValueTask NotifyFailureAsync(MediaError error)
    {
        try
        {
            if (!_disposed)
                await _target.FailAsync(error, _outputs.CancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (!_disposed)
                Raise(VideoSessionEventKind.Failed, error);
        }
    }

    private void SetState(VideoSessionState state, VideoSessionEventKind kind)
    {
        lock (_gate)
        {
            if (state != VideoSessionState.Disposed && (_disposed || State == VideoSessionState.Failed))
                return;
            _state = state;
        }
        Raise(kind);
    }

    private void Raise(VideoSessionEventKind kind, MediaError? error = null)
    {
        VideoSessionEvent notification;
        lock (_gate)
        {
            if (_disposed && kind != VideoSessionEventKind.Disposed)
                return;
            notification = new VideoSessionEvent(kind, State, Position, error);
        }
        StateChanged?.Invoke(this, notification);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed || State == VideoSessionState.Disposed)
            throw new ObjectDisposedException(nameof(MediaFoundationVideoSession));
        
        if (State == VideoSessionState.Failed)
            throw new MediaException(new MediaError(MediaErrorCode.OutputFailed, "The Media Foundation video session has failed."));
    }
}
