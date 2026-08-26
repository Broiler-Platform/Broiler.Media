using System;
using System.Runtime.InteropServices;
using Broiler.Graphics.Windows;

namespace Broiler.Media.Video.MediaFoundation;

internal sealed class MediaFoundationMediaEngine : IMediaFoundationMediaEngine
{
    private readonly MediaEngineNotify _notify;
    private IMFMediaEngine? _engine;
    private object? _factoryObject;
    private object? _attributesObject;
    private bool _disposed;

    private MediaFoundationMediaEngine(
        IMFMediaEngine engine,
        object factoryObject,
        object attributesObject,
        MediaEngineNotify notify)
    {
        _engine = engine;
        _factoryObject = factoryObject;
        _attributesObject = attributesObject;
        _notify = notify;
        _notify.EventReceived += OnNotifyEventReceived;
    }

    public event EventHandler<MediaFoundationMediaEngineEvent>? EventReceived;

    public TimeSpan Position
    {
        get
        {
            IMFMediaEngine engine = GetEngine();
            double seconds = engine.GetCurrentTime();
            return double.IsFinite(seconds) && seconds > 0
                ? TimeSpan.FromSeconds(seconds)
                : TimeSpan.Zero;
        }
    }

    public static MediaFoundationMediaEngine Create(
        HwndVideoOutput? target,
        VideoSessionOptions options)
    {
        MediaFoundationFaults.ThrowIfFailed(
            MediaFoundationNative.MFCreateAttributes(out IntPtr attributesPointer, 4),
            "Media Foundation media engine attribute creation failed.");

        object attributesObject = Marshal.GetObjectForIUnknown(attributesPointer);
        MediaFoundationNative.ReleaseIUnknown(attributesPointer);
        var attributes = (IMFAttributes)attributesObject;
        object? factoryObject = null;
        IntPtr factoryPointer = IntPtr.Zero;
        var notify = new MediaEngineNotify();

        try
        {
            Guid callback = MediaFoundationNative.MF_MEDIA_ENGINE_CALLBACK;
            MediaFoundationFaults.ThrowIfFailed(
                attributes.SetUnknown(ref callback, notify),
                "Media Foundation media engine callback configuration failed.");

            Guid synchronousClose = MediaFoundationNative.MF_MEDIA_ENGINE_SYNCHRONOUS_CLOSE;
            MediaFoundationFaults.ThrowIfFailed(
                attributes.SetUINT32(ref synchronousClose, 1),
                "Media Foundation media engine synchronous close configuration failed.");

            Guid browserMode = MediaFoundationNative.MF_MEDIA_ENGINE_BROWSER_COMPATIBILITY_MODE;
            Guid edgeMode = MediaFoundationNative.MF_MEDIA_ENGINE_BROWSER_COMPATIBILITY_MODE_IE_EDGE;
            attributes.SetGUID(ref browserMode, ref edgeMode);

            if (target is not null)
            {
                target.ThrowIfUsableTargetRequired();
                Guid hwnd = MediaFoundationNative.MF_MEDIA_ENGINE_PLAYBACK_HWND;
                MediaFoundationFaults.ThrowIfFailed(
                    attributes.SetUINT64(ref hwnd, target.Hwnd),
                    "Media Foundation media engine HWND configuration failed.");
            }

            Guid clsid = MediaFoundationNative.CLSID_MFMediaEngineClassFactory;
            Guid iid = MediaFoundationNative.IID_IMFMediaEngineClassFactory;
            MediaFoundationFaults.ThrowIfFailed(
                MediaFoundationNative.CoCreateInstance(
                    ref clsid,
                    IntPtr.Zero,
                    MediaFoundationNative.CLSCTX_INPROC_SERVER,
                    ref iid,
                    out factoryPointer),
                "Media Foundation media engine factory creation failed.",
                "COM");

            factoryObject = Marshal.GetObjectForIUnknown(factoryPointer);
            MediaFoundationNative.ReleaseIUnknown(factoryPointer);
            factoryPointer = IntPtr.Zero;
            var factory = (IMFMediaEngineClassFactory)factoryObject!;
            uint createFlags = MediaFoundationNative.MF_MEDIA_ENGINE_DISABLE_LOCAL_PLUGINS;
            if (options.Muted)
                createFlags |= MediaFoundationNative.MF_MEDIA_ENGINE_FORCEMUTE;

            MediaFoundationFaults.ThrowIfFailed(
                factory.CreateInstance(createFlags, attributes, out IMFMediaEngine engine),
                "Media Foundation media engine creation failed.");

            MediaFoundationFaults.ThrowIfFailed(
                engine.SetMuted(options.Muted ? 1 : 0),
                "Media Foundation media engine mute configuration failed.");
            MediaFoundationFaults.ThrowIfFailed(
                engine.SetAutoPlay(options.Autoplay ? 1 : 0),
                "Media Foundation media engine autoplay configuration failed.");

            return new MediaFoundationMediaEngine(engine, factoryObject!, attributesObject, notify);
        }
        catch
        {
            notify.Disconnect();
            if (factoryPointer != IntPtr.Zero)
                MediaFoundationNative.ReleaseIUnknown(factoryPointer);
            MediaFoundationNative.ReleaseComObject(factoryObject);
            MediaFoundationNative.ReleaseComObject(attributesObject);
            throw;
        }
    }

    public void SetSource(string sourceUri) =>
        MediaFoundationFaults.ThrowIfFailed(GetEngine().SetSource(sourceUri), "Media Foundation media engine source assignment failed.");

    public void Load() =>
        MediaFoundationFaults.ThrowIfFailed(GetEngine().Load(), "Media Foundation media engine load failed.");

    public void Play() =>
        MediaFoundationFaults.ThrowIfFailed(GetEngine().Play(), "Media Foundation media engine play failed.");

    public void Pause() =>
        MediaFoundationFaults.ThrowIfFailed(GetEngine().Pause(), "Media Foundation media engine pause failed.");

    public void Seek(TimeSpan position)
    {
        if (position < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(position));

        MediaFoundationFaults.ThrowIfFailed(
            GetEngine().SetCurrentTime(position.TotalSeconds),
            "Media Foundation media engine seek failed.");
    }

    public VideoStreamInfo GetStreamInfo()
    {
        IMFMediaEngine engine = GetEngine();
        MediaFoundationFaults.ThrowIfFailed(
            engine.GetNativeVideoSize(out uint width, out uint height),
            "Media Foundation media engine video size lookup failed.");

        if (width == 0 || height == 0 || width > int.MaxValue || height > int.MaxValue)
            throw new MediaException(new MediaError(MediaErrorCode.InvalidData, "Media Foundation did not expose a valid native video size."));

        TimeSpan? duration = null;
        double seconds = engine.GetDuration();
        if (double.IsFinite(seconds) && seconds >= 0)
            duration = TimeSpan.FromSeconds(seconds);

        return new VideoStreamInfo((int)width, (int)height, (int)width, (int)height, duration);
    }

    public void OnTargetChanged(HwndVideoOutput target)
    {
        if (target.IsDestroyed)
            Shutdown();
    }

    public void Shutdown()
    {
        IMFMediaEngine? engine = _engine;
        if (engine is null)
            return;

        _engine = null;
        try
        {
            engine.Shutdown();
        }
        finally
        {
            MediaFoundationNative.ReleaseComObject(engine);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _notify.EventReceived -= OnNotifyEventReceived;
        _notify.Disconnect();
        Shutdown();
        MediaFoundationNative.ReleaseComObject(_attributesObject);
        MediaFoundationNative.ReleaseComObject(_factoryObject);
        _attributesObject = null;
        _factoryObject = null;
        _disposed = true;
    }

    private IMFMediaEngine GetEngine() =>
        _engine ?? throw new ObjectDisposedException(nameof(MediaFoundationMediaEngine));

    private void OnNotifyEventReceived(object? sender, MediaFoundationMediaEngineEvent e) =>
        EventReceived?.Invoke(this, e);

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class MediaEngineNotify : IMFMediaEngineNotify
    {
        private bool _connected = true;

        public event EventHandler<MediaFoundationMediaEngineEvent>? EventReceived;

        public int EventNotify(uint @event, UIntPtr param1, uint param2)
        {
            if (_connected)
            {
                EventReceived?.Invoke(
                    this,
                    new MediaFoundationMediaEngineEvent((MediaFoundationMediaEngineEventKind)@event, param1, param2));
            }

            return MediaFoundationNative.S_OK;
        }

        public void Disconnect()
        {
            _connected = false;
            EventReceived = null;
        }
    }
}
