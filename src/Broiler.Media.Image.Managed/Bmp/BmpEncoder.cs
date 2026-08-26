using System;
using System.Buffers.Binary;

namespace Broiler.Media.Image.Managed;

/// <summary>
/// Pure-managed encoder for uncompressed 32bpp BGRA Windows BMP files
/// (<c>BITMAPFILEHEADER</c> + <c>BITMAPINFOHEADER</c>, BI_RGB, bottom-up). The
/// alpha channel is preserved, so a BMP written here round-trips losslessly
/// through <see cref="BmpDecoder"/>.
/// </summary>
internal static class BmpEncoder
{
    private const int FileHeaderSize = 14;
    private const int InfoHeaderSize = 40;
    private const int PixelOffset = FileHeaderSize + InfoHeaderSize;

    public static byte[] Encode(ImageBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        int width = buffer.Width;
        int height = buffer.Height;
        byte[] rgba = buffer.Rgba;

        int rowStride = width * 4; // 32bpp rows are inherently 4-byte aligned
        int imageSize = rowStride * height;
        int fileSize = PixelOffset + imageSize;

        byte[] output = new byte[fileSize];
        var span = output.AsSpan();

        // BITMAPFILEHEADER
        span[0] = (byte)'B';
        span[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(2, 4), (uint)fileSize);
        // bytes 6..9 reserved = 0
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(10, 4), PixelOffset);

        // BITMAPINFOHEADER
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(14, 4), InfoHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(18, 4), width);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(22, 4), height); // positive => bottom-up
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(26, 2), 1);     // planes
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(28, 2), 32);    // bit count
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(30, 4), 0);     // BI_RGB
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(34, 4), (uint)imageSize);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(38, 4), 2835);   // ~72 DPI x
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(42, 4), 2835);   // ~72 DPI y
        // clrUsed (46..49) and clrImportant (50..53) = 0

        for (int y = 0; y < height; y++)
        {
            int srcRow = height - 1 - y;       // bottom-up
            int src = srcRow * width * 4;
            int dst = PixelOffset + y * rowStride;

            for (int x = 0; x < width; x++)
            {
                byte r = rgba[src];
                byte g = rgba[src + 1];
                byte b = rgba[src + 2];
                byte a = rgba[src + 3];
                src += 4;

                span[dst] = b;
                span[dst + 1] = g;
                span[dst + 2] = r;
                span[dst + 3] = a;
                dst += 4;
            }
        }

        return output;
    }
}
