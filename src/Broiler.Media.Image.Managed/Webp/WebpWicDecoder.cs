using System;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Broiler.Media.Image.Managed;

internal static class WebpWicDecoder
{
    private const uint ClsctxInprocServer = 0x1;
    private const uint CoInitMultithreaded = 0x0;
    private const int RpcEChangedMode = unchecked((int)0x80010106);
    private const int WinCodecErrComponentNotFound = unchecked((int)0x88982F50);

    private static readonly Guid ClsidWicImagingFactory = new("cacaf262-9370-4615-a13b-9f5539da4c0a");
    private static readonly Guid IidWicImagingFactory = new("ec5ec8a9-c395-4314-9c77-54d7a935ff70");
    private static readonly Guid PixelFormat32bppRgba = new("f5c7ad2d-6a8d-43dd-a7a8-a29935261ae9");
    private static readonly Guid PixelFormat32bppBgra = new("6fddc324-4e03-4bfe-b185-3d77768dc90f");

    public static ImageBuffer Decode(ReadOnlySpan<byte> webpData)
    {
        if (!OperatingSystem.IsWindows())
            throw new NotSupportedException("Lossy VP8 WebP decoding requires the Windows Imaging Component WebP decoder on Windows.");

        try
        {
            return DecodeWindows(webpData);
        }
        catch (COMException ex) when (ex.HResult == WinCodecErrComponentNotFound)
        {
            throw new NotSupportedException("Lossy VP8 WebP decoding requires the Windows Imaging Component WebP decoder.", ex);
        }
        catch (DllNotFoundException ex)
        {
            throw new NotSupportedException("Lossy VP8 WebP decoding requires Windows COM and WIC runtime support.", ex);
        }
        catch (EntryPointNotFoundException ex)
        {
            throw new NotSupportedException("Lossy VP8 WebP decoding requires Windows COM and WIC runtime support.", ex);
        }
        catch (COMException ex)
        {
            throw new FormatException("Windows Imaging Component could not decode the lossy VP8 WebP data.", ex);
        }
    }

    public static ImageBuffer DecodeVp8Payload(ReadOnlySpan<byte> vp8Payload) => Decode(WrapVp8Payload(vp8Payload));

    private static ImageBuffer DecodeWindows(ReadOnlySpan<byte> webpData)
    {
        bool uninitializeCom = InitializeComForCurrentThread();
        IStream? stream = null;
        IWICImagingFactory? factory = null;
        IWICBitmapDecoder? decoder = null;
        IWICBitmapFrameDecode? frame = null;
        IWICFormatConverter? converter = null;
        try
        {
            stream = CreateComStream(webpData);
            factory = CreateFactory();

            ThrowIfFailed(factory.CreateDecoderFromStream(stream, IntPtr.Zero, 0, out decoder));
            ThrowIfFailed(decoder.GetFrame(0, out frame));
            ThrowIfFailed(factory.CreateFormatConverter(out converter));

            Guid sourceFormat;
            ThrowIfFailed(frame.GetPixelFormat(out sourceFormat));
            ImageBuffer? image = TryConvertAndCopy(converter, frame, sourceFormat, PixelFormat32bppRgba, swizzleBgraToRgba: false);
            if (image is not null)
                return image;

            image = TryConvertAndCopy(converter, frame, sourceFormat, PixelFormat32bppBgra, swizzleBgraToRgba: true);
            if (image is not null)
                return image;

            throw new NotSupportedException("Windows Imaging Component could not convert lossy VP8 WebP pixels to RGBA.");
        }
        finally
        {
            Release(converter);
            Release(frame);
            Release(decoder);
            Release(factory);
            Release(stream);
            if (uninitializeCom)
                CoUninitialize();
        }
    }

    private static ImageBuffer? TryConvertAndCopy(
        IWICFormatConverter converter,
        IWICBitmapFrameDecode source,
        Guid sourceFormat,
        Guid destinationFormat,
        bool swizzleBgraToRgba)
    {
        Guid requestedFormat = destinationFormat;
        int hr = converter.CanConvert(ref sourceFormat, ref requestedFormat, out int canConvert);
        if (hr < 0 || canConvert == 0)
            return null;

        requestedFormat = destinationFormat;
        hr = converter.Initialize(source, ref requestedFormat, 0, IntPtr.Zero, 0, 0);
        if (hr < 0)
            return null;

        ThrowIfFailed(converter.GetSize(out uint widthValue, out uint heightValue));
        int width = checked((int)widthValue);
        int height = checked((int)heightValue);
        int stride = checked(width * 4);
        byte[] rgba = new byte[checked(stride * height)];
        CopyPixels(converter, stride, rgba);

        if (swizzleBgraToRgba)
            SwizzleBgraToRgba(rgba);

        return new ImageBuffer(width, height, rgba);
    }

    private static void CopyPixels(IWICFormatConverter converter, int stride, byte[] pixels)
    {
        IntPtr buffer = Marshal.AllocHGlobal(pixels.Length);
        try
        {
            ThrowIfFailed(converter.CopyPixels(IntPtr.Zero, (uint)stride, (uint)pixels.Length, buffer));
            Marshal.Copy(buffer, pixels, 0, pixels.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static byte[] WrapVp8Payload(ReadOnlySpan<byte> vp8Payload)
    {
        int paddedPayloadLength = checked(vp8Payload.Length + (vp8Payload.Length & 1));
        byte[] webp = new byte[checked(12 + 8 + paddedPayloadLength)];
        WriteFourCc(webp.AsSpan(0, 4), "RIFF");
        BinaryPrimitives.WriteUInt32LittleEndian(webp.AsSpan(4, 4), checked((uint)(webp.Length - 8)));
        WriteFourCc(webp.AsSpan(8, 4), "WEBP");
        WriteFourCc(webp.AsSpan(12, 4), "VP8 ");
        BinaryPrimitives.WriteUInt32LittleEndian(webp.AsSpan(16, 4), checked((uint)vp8Payload.Length));
        vp8Payload.CopyTo(webp.AsSpan(20));
        return webp;
    }

    private static bool InitializeComForCurrentThread()
    {
        int hr = CoInitializeEx(IntPtr.Zero, CoInitMultithreaded);
        if (hr == 0 || hr == 1)
            return true;
        if (hr == RpcEChangedMode)
            return false;

        Marshal.ThrowExceptionForHR(hr);
        return false;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2050", Justification = "The WIC COM interfaces used here are private, statically declared, and directly referenced by the lossy WebP decoder.")]
    private static IWICImagingFactory CreateFactory()
    {
        Guid clsid = ClsidWicImagingFactory;
        Guid iid = IidWicImagingFactory;
        ThrowIfFailed(CoCreateInstance(ref clsid, IntPtr.Zero, ClsctxInprocServer, ref iid, out IWICImagingFactory factory));
        return factory;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2050", Justification = "The COM IStream instance is only passed to private, statically declared WIC interfaces in this helper.")]
    private static IStream CreateComStream(ReadOnlySpan<byte> data)
    {
        IntPtr hglobal = Marshal.AllocHGlobal(data.Length);
        try
        {
            Marshal.Copy(data.ToArray(), 0, hglobal, data.Length);
            ThrowIfFailed(CreateStreamOnHGlobal(hglobal, fDeleteOnRelease: true, out IStream stream));
            hglobal = IntPtr.Zero;
            return stream;
        }
        finally
        {
            if (hglobal != IntPtr.Zero)
                Marshal.FreeHGlobal(hglobal);
        }
    }

    private static void SwizzleBgraToRgba(byte[] pixels)
    {
        for (int i = 0; i < pixels.Length; i += 4)
            (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
    }

    private static void WriteFourCc(Span<byte> destination, string fourCc)
    {
        destination[0] = (byte)fourCc[0];
        destination[1] = (byte)fourCc[1];
        destination[2] = (byte)fourCc[2];
        destination[3] = (byte)fourCc[3];
    }

    private static void ThrowIfFailed(int hr)
    {
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);
    }

    private static void Release(object? comObject)
    {
        if (comObject is not null && OperatingSystem.IsWindows())
            Marshal.FinalReleaseComObject(comObject);
    }

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
        ref Guid rclsid,
        IntPtr pUnkOuter,
        uint dwClsContext,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IWICImagingFactory ppv);

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    [DllImport("ole32.dll")]
    private static extern int CreateStreamOnHGlobal(IntPtr hGlobal, bool fDeleteOnRelease, out IStream ppstm);

    [ComImport]
    [Guid("3b16811b-6a43-4ec9-a813-3d930c13b940")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICBitmapFrameDecode
    {
        [PreserveSig]
        int GetSize(out uint puiWidth, out uint puiHeight);

        [PreserveSig]
        int GetPixelFormat(out Guid pPixelFormat);

        [PreserveSig]
        int GetResolution(out double pDpiX, out double pDpiY);

        [PreserveSig]
        int CopyPalette(IntPtr pIPalette);

        [PreserveSig]
        int CopyPixels(IntPtr prc, uint cbStride, uint cbBufferSize, IntPtr pbBuffer);

        [PreserveSig]
        int GetMetadataQueryReader(out IntPtr ppIMetadataQueryReader);

        [PreserveSig]
        int GetColorContexts(uint cCount, IntPtr ppIColorContexts, out uint pcActualCount);

        [PreserveSig]
        int GetThumbnail(out IntPtr ppIThumbnail);
    }

    [ComImport]
    [Guid("00000301-a8f2-4877-ba0a-fd2b6645fb94")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICFormatConverter
    {
        [PreserveSig]
        int GetSize(out uint puiWidth, out uint puiHeight);

        [PreserveSig]
        int GetPixelFormat(out Guid pPixelFormat);

        [PreserveSig]
        int GetResolution(out double pDpiX, out double pDpiY);

        [PreserveSig]
        int CopyPalette(IntPtr pIPalette);

        [PreserveSig]
        int CopyPixels(IntPtr prc, uint cbStride, uint cbBufferSize, IntPtr pbBuffer);

        [PreserveSig]
        int Initialize(
            IWICBitmapFrameDecode pISource,
            ref Guid dstFormat,
            int dither,
            IntPtr pIPalette,
            double alphaThresholdPercent,
            int paletteTranslate);

        [PreserveSig]
        int CanConvert(ref Guid srcPixelFormat, ref Guid dstPixelFormat, out int pfCanConvert);
    }

    [ComImport]
    [Guid("9edde9e7-8dee-47ea-99df-e6faf2ed44bf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICBitmapDecoder
    {
        [PreserveSig]
        int QueryCapability(IStream pIStream, out uint pdwCapability);

        [PreserveSig]
        int Initialize(IStream pIStream, int cacheOptions);

        [PreserveSig]
        int GetContainerFormat(out Guid pguidContainerFormat);

        [PreserveSig]
        int GetDecoderInfo(out IntPtr ppIDecoderInfo);

        [PreserveSig]
        int CopyPalette(IntPtr pIPalette);

        [PreserveSig]
        int GetMetadataQueryReader(out IntPtr ppIMetadataQueryReader);

        [PreserveSig]
        int GetPreview(out IntPtr ppIBitmapSource);

        [PreserveSig]
        int GetColorContexts(uint cCount, IntPtr ppIColorContexts, out uint pcActualCount);

        [PreserveSig]
        int GetThumbnail(out IntPtr ppIThumbnail);

        [PreserveSig]
        int GetFrameCount(out uint pCount);

        [PreserveSig]
        int GetFrame(uint index, out IWICBitmapFrameDecode ppIBitmapFrame);
    }

    [ComImport]
    [Guid("ec5ec8a9-c395-4314-9c77-54d7a935ff70")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICImagingFactory
    {
        [PreserveSig]
        int CreateDecoderFromFilename(
            [MarshalAs(UnmanagedType.LPWStr)] string wzFilename,
            IntPtr pguidVendor,
            uint dwDesiredAccess,
            int metadataOptions,
            out IWICBitmapDecoder ppIDecoder);

        [PreserveSig]
        int CreateDecoderFromStream(
            IStream pIStream,
            IntPtr pguidVendor,
            int metadataOptions,
            out IWICBitmapDecoder ppIDecoder);

        [PreserveSig]
        int CreateDecoderFromFileHandle(
            IntPtr hFile,
            IntPtr pguidVendor,
            int metadataOptions,
            out IWICBitmapDecoder ppIDecoder);

        [PreserveSig]
        int CreateComponentInfo(ref Guid clsidComponent, out IntPtr ppIInfo);

        [PreserveSig]
        int CreateDecoder(ref Guid guidContainerFormat, IntPtr pguidVendor, out IWICBitmapDecoder ppIDecoder);

        [PreserveSig]
        int CreateEncoder(ref Guid guidContainerFormat, IntPtr pguidVendor, out IntPtr ppIEncoder);

        [PreserveSig]
        int CreatePalette(out IntPtr ppIPalette);

        [PreserveSig]
        int CreateFormatConverter(out IWICFormatConverter ppIFormatConverter);
    }
}
