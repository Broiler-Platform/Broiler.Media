using Broiler.Native.Windows;
using Broiler.Native.Windows.MediaFoundation;
using System;

namespace Broiler.Media.Video.MediaFoundation;

internal sealed class MediaFoundationPlatformScope : IDisposable
{
    private readonly bool _shouldUninitializeCom;
    private bool _mediaFoundationStarted;
    private bool _disposed;

    public MediaFoundationPlatformScope()
    {
        int comResult = ComNative.CoInitializeEx(IntPtr.Zero, ComNative.COINIT_MULTITHREADED);
        if (comResult == ComNative.S_OK || comResult == ComNative.S_FALSE)
            _shouldUninitializeCom = true;
        else if (comResult != ComNative.RPC_E_CHANGED_MODE)
            MediaFoundationFaults.ThrowIfFailed(comResult, "COM initialization failed.", "COM");

        try
        {
            int result = MediaFoundationPlatformNative.MFStartup(MediaFoundationPlatformNative.MF_VERSION, MediaFoundationPlatformNative.MFSTARTUP_NOSOCKET);
            
            MediaFoundationFaults.ThrowIfFailed(result, "Media Foundation startup failed.");
            _mediaFoundationStarted = true;
        }
        catch
        {
            if (_shouldUninitializeCom)
                ComNative.CoUninitialize();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_mediaFoundationStarted)
            _ = MediaFoundationPlatformNative.MFShutdown();
        
        if (_shouldUninitializeCom)
            ComNative.CoUninitialize();
        
        _disposed = true;
    }
}

internal static class MediaFoundationFaults
{
    public static MediaException CreateException(int hresult, string message, string nativeFacility = "MediaFoundation") =>
        new(new MediaError(Map(hresult), FormatNativeFailureMessage(message, hresult, nativeFacility)));

    public static void ThrowIfFailed(int hresult, string message, string nativeFacility = "MediaFoundation")
    {
        if (hresult < 0)
            throw CreateException(hresult, message, nativeFacility);
    }

    private static MediaErrorCode Map(int hresult) => hresult switch
    {
        ComNative.E_ACCESSDENIED => MediaErrorCode.NativeFailure,
        MediaFoundationPlatformNative.MF_E_INVALIDMEDIATYPE => MediaErrorCode.UnsupportedFormat,
        MediaFoundationPlatformNative.MF_E_PLATFORM_NOT_INITIALIZED or
            MediaFoundationPlatformNative.MF_E_NOT_INITIALIZED or
            MediaFoundationPlatformNative.MF_E_NOT_AVAILABLE or
            MediaFoundationPlatformNative.MF_E_DISABLED_IN_SAFEMODE => MediaErrorCode.NativeFailure,
        _ => MediaErrorCode.NativeFailure,
    };

    private static string FormatNativeFailureMessage(string message, int hresult, string nativeFacility)
    {
        string formattedCode = "0x" + unchecked((uint)hresult).ToString("X8");
        string? name = GetNativeErrorName(hresult);
        string suffix = name is null
            ? nativeFacility + " HRESULT " + formattedCode
            : nativeFacility + " HRESULT " + formattedCode + " (" + name + ")";
        
        return message + " Native error: " + suffix + ".";
    }

    private static string? GetNativeErrorName(int hresult) => hresult switch
    {
        ComNative.E_ACCESSDENIED => "E_ACCESSDENIED",
        ComNative.RPC_E_CHANGED_MODE => "RPC_E_CHANGED_MODE",
        MediaFoundationPlatformNative.MF_E_PLATFORM_NOT_INITIALIZED => "MF_E_PLATFORM_NOT_INITIALIZED",
        MediaFoundationPlatformNative.MF_E_INVALIDMEDIATYPE => "MF_E_INVALIDMEDIATYPE",
        MediaFoundationPlatformNative.MF_E_NOT_INITIALIZED => "MF_E_NOT_INITIALIZED",
        MediaFoundationPlatformNative.MF_E_NOT_AVAILABLE => "MF_E_NOT_AVAILABLE",
        MediaFoundationPlatformNative.MF_E_DISABLED_IN_SAFEMODE => "MF_E_DISABLED_IN_SAFEMODE",
        MediaFoundationPlatformNative.MF_E_SHUTDOWN => "MF_E_SHUTDOWN",
        _ => null,
    };
}

