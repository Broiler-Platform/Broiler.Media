using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Media.Video.Windows;

namespace Broiler.Media.Video.MediaFoundation;

// The native engine and its COM/MF scope never leave this thread. Native event
// subscribers must only enqueue work: shutdown may wait for a callback to return.
internal sealed class MediaFoundationEngineThread : IMediaFoundationMediaEngine
{
    private readonly BlockingCollection<Action> _commands = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();
    private readonly Thread _thread;
    private IMediaFoundationMediaEngine? _engine;
    private bool _disposing;

    internal MediaFoundationEngineThread(Func<IDisposable> createPlatform, Func<IMediaFoundationMediaEngine> createEngine)
    {
        _thread = new Thread(() => Run(createPlatform, createEngine))
        {
            IsBackground = true,
            Name = "Broiler Media Foundation"
        };
        _thread.Start();
        try { _ready.Task.GetAwaiter().GetResult(); }
        catch
        {
            // Await cleanup too, while preserving the creation failure.
            try { _stopped.Task.GetAwaiter().GetResult(); } catch { }
            throw;
        }
    }

    public event EventHandler<MediaFoundationMediaEngineEvent>? EventReceived;

    public TimeSpan Position => Invoke(engine => engine.Position);
    public void SetSource(string sourceUri) => Invoke(engine => engine.SetSource(sourceUri));
    public void Load() => Invoke(engine => engine.Load());
    public void Play() => Invoke(engine => engine.Play());
    public void Pause() => Invoke(engine => engine.Pause());
    public void Seek(TimeSpan position) => Invoke(engine => engine.Seek(position));
    public VideoStreamInfo GetStreamInfo() => Invoke(engine => engine.GetStreamInfo());
    public void OnTargetChanged(IHwndVideoOutput target) => Invoke(engine => engine.OnTargetChanged(target));
    public void Shutdown() => Invoke(engine => engine.Shutdown());

    public void Dispose()
    {
        lock (_gate)
        {
            if (!_disposing)
            {
                _disposing = true;
                _commands.CompleteAdding();
            }
        }
        _stopped.Task.GetAwaiter().GetResult();
    }

    private void Invoke(Action<IMediaFoundationMediaEngine> action) => Invoke(engine => { action(engine); return true; });

    private T Invoke<T>(Func<IMediaFoundationMediaEngine, T> action)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposing, this);
            _commands.Add(() =>
            {
                try { result.SetResult(action(_engine!)); }
                catch (Exception ex) { result.SetException(ex); }
            });
        }
        return result.Task.GetAwaiter().GetResult();
    }

    private void Run(Func<IDisposable> createPlatform, Func<IMediaFoundationMediaEngine> createEngine)
    {
        try
        {
            using IDisposable platform = createPlatform();
            using IMediaFoundationMediaEngine engine = createEngine();
            _engine = engine;
            engine.EventReceived += ForwardEvent;
            try
            {
                _ready.SetResult();
                foreach (Action command in _commands.GetConsumingEnumerable())
                    command();
            }
            finally { engine.EventReceived -= ForwardEvent; }
        }
        catch (Exception ex)
        {
            _ready.TrySetException(ex);
            _stopped.TrySetException(ex);
        }
        finally
        {
            _commands.Dispose();
            _stopped.TrySetResult();
        }
    }

    private void ForwardEvent(object? sender, MediaFoundationMediaEngineEvent e) => EventReceived?.Invoke(this, e);
}
