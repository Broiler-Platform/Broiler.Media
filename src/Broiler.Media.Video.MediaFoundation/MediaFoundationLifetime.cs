using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Broiler.Media.Video.MediaFoundation;

internal sealed class MediaFoundationPlatformScope : IDisposable
{
    private readonly bool _shouldUninitializeCom;
    private bool _mediaFoundationStarted;
    private bool _disposed;

    public MediaFoundationPlatformScope()
    {
        int comResult = MediaFoundationNative.CoInitializeEx(IntPtr.Zero, MediaFoundationNative.COINIT_MULTITHREADED);
        if (comResult == MediaFoundationNative.S_OK || comResult == MediaFoundationNative.S_FALSE)
            _shouldUninitializeCom = true;
        else if (comResult != MediaFoundationNative.RPC_E_CHANGED_MODE)
            MediaFoundationFaults.ThrowIfFailed(comResult, "COM initialization failed.", "COM");

        try
        {
            int result = MediaFoundationNative.MFStartup(MediaFoundationNative.MF_VERSION, MediaFoundationNative.MFSTARTUP_NOSOCKET);
            
            MediaFoundationFaults.ThrowIfFailed(result, "Media Foundation startup failed.");
            _mediaFoundationStarted = true;
        }
        catch
        {
            if (_shouldUninitializeCom)
                MediaFoundationNative.CoUninitialize();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_mediaFoundationStarted)
            _ = MediaFoundationNative.MFShutdown();
        
        if (_shouldUninitializeCom)
            MediaFoundationNative.CoUninitialize();
        
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
        MediaFoundationNative.E_ACCESSDENIED => MediaErrorCode.NativeFailure,
        MediaFoundationNative.MF_E_INVALIDMEDIATYPE => MediaErrorCode.UnsupportedFormat,
        MediaFoundationNative.MF_E_PLATFORM_NOT_INITIALIZED or
            MediaFoundationNative.MF_E_NOT_INITIALIZED or
            MediaFoundationNative.MF_E_NOT_AVAILABLE or
            MediaFoundationNative.MF_E_DISABLED_IN_SAFEMODE => MediaErrorCode.NativeFailure,
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
        MediaFoundationNative.E_ACCESSDENIED => "E_ACCESSDENIED",
        MediaFoundationNative.RPC_E_CHANGED_MODE => "RPC_E_CHANGED_MODE",
        MediaFoundationNative.MF_E_PLATFORM_NOT_INITIALIZED => "MF_E_PLATFORM_NOT_INITIALIZED",
        MediaFoundationNative.MF_E_INVALIDMEDIATYPE => "MF_E_INVALIDMEDIATYPE",
        MediaFoundationNative.MF_E_NOT_INITIALIZED => "MF_E_NOT_INITIALIZED",
        MediaFoundationNative.MF_E_NOT_AVAILABLE => "MF_E_NOT_AVAILABLE",
        MediaFoundationNative.MF_E_DISABLED_IN_SAFEMODE => "MF_E_DISABLED_IN_SAFEMODE",
        MediaFoundationNative.MF_E_SHUTDOWN => "MF_E_SHUTDOWN",
        _ => null,
    };
}

