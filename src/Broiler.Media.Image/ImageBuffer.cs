using System;

namespace Broiler.Media.Image;

public sealed class ImageBuffer
{
    public ImageBuffer(int width, int height, byte[] rgba)
        : this(width, height, ImagePixelFormat.Rgba8, ImageAlphaMode.Straight, checked(width * BytesPerPixel(ImagePixelFormat.Rgba8)), rgba)
    {
    }

    public ImageBuffer(
        int width,
        int height,
        ImagePixelFormat pixelFormat,
        ImageAlphaMode alphaMode,
        int stride,
        ReadOnlyMemory<byte> pixels)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (stride <= 0)
            throw new ArgumentOutOfRangeException(nameof(stride));

        int minimumStride = checked(width * BytesPerPixel(pixelFormat));
        if (stride < minimumStride)
            throw new ArgumentException("Image stride is smaller than the minimum row size.", nameof(stride));

        int minimumLength = checked(((height - 1) * stride) + minimumStride);
        if (pixels.Length < minimumLength)
            throw new ArgumentException("The image pixel buffer is smaller than the described image dimensions.", nameof(pixels));

        Width = width;
        Height = height;
        PixelFormat = pixelFormat;
        AlphaMode = alphaMode;
        Stride = stride;
        Rgba = pixels.ToArray();
        Pixels = Rgba;
    }

    public int Width { get; }

    public int Height { get; }

    public ImagePixelFormat PixelFormat { get; }

    public ImageAlphaMode AlphaMode { get; }

    public int Stride { get; }

    public ReadOnlyMemory<byte> Pixels { get; }

    public byte[] Rgba { get; }

    public static int BytesPerPixel(ImagePixelFormat pixelFormat) => pixelFormat switch
    {
        ImagePixelFormat.Rgba8 => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(pixelFormat), pixelFormat, "Unknown image pixel format."),
    };
}
