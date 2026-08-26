using System;
using System.Buffers.Binary;

namespace Broiler.Media.Image.Managed;

/// <summary>
/// Pure-managed decoder for uncompressed (BI_RGB) Windows BMP files using a
/// <c>BITMAPINFOHEADER</c>. Supports 24bpp (BGR) and 32bpp (BGRA) bitmaps in
/// both bottom-up and top-down row orders, expanding them to 8-bit RGBA.
/// </summary>
internal static class BmpDecoder
{
    /// <summary>True if <paramref name="data"/> starts with the 'BM' BMP signature.</summary>
    public static bool IsBmp(ReadOnlySpan<byte> data) =>
        data.Length >= 2 && data[0] == (byte)'B' && data[1] == (byte)'M';

    public static ImageBuffer Decode(ReadOnlySpan<byte> data)
    {
        if (!IsBmp(data))
            throw new FormatException("Data does not start with a BMP signature.");
        if (data.Length < 54)
            throw new FormatException("Truncated BMP: smaller than a BITMAPINFOHEADER.");

        uint pixelOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(10, 4));
        uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(14, 4));
        if (headerSize < 40)
            throw new NotSupportedException("Only BMPs with a BITMAPINFOHEADER (or newer) are supported.");

        int width = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(18, 4));
        int rawHeight = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(22, 4));
        ushort bitCount = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(28, 2));
        uint compression = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(30, 4));

        if (compression != 0 && compression != 3)
            throw new NotSupportedException($"BMP compression method {compression} is not supported.");
        if (bitCount != 24 && bitCount != 32)
            throw new NotSupportedException($"Only 24bpp and 32bpp BMPs are supported (got {bitCount}bpp).");
        if (width <= 0)
            throw new FormatException("BMP has a non-positive width.");

        bool topDown = rawHeight < 0;
        int height = Math.Abs(rawHeight);
        if (height == 0)
            throw new FormatException("BMP has zero height.");

        int bytesPerPixel = bitCount / 8;
        int rowStride = ((width * bitCount + 31) / 32) * 4; // rows padded to 4 bytes
        long needed = (long)pixelOffset + (long)rowStride * height;
        if (pixelOffset >= data.Length || needed > data.Length)
            throw new FormatException("BMP pixel data is truncated.");

        byte[] rgba = new byte[(long)width * height * 4];

        for (int y = 0; y < height; y++)
        {
            int srcRow = topDown ? y : height - 1 - y;
            int src = (int)pixelOffset + srcRow * rowStride;
            int dst = y * width * 4;

            for (int x = 0; x < width; x++)
            {
                int p = src + x * bytesPerPixel;
                byte b = data[p];
                byte g = data[p + 1];
                byte r = data[p + 2];
                byte a = bytesPerPixel == 4 ? data[p + 3] : (byte)255;

                rgba[dst] = r;
                rgba[dst + 1] = g;
                rgba[dst + 2] = b;
                rgba[dst + 3] = a;
                dst += 4;
            }
        }

        return new ImageBuffer(width, height, rgba);
    }
}
