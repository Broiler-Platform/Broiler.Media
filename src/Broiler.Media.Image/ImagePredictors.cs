using System;

namespace Broiler.Media.Image;

/// <summary>
/// Raster predictors and differential filtering (TIFF Predictor 2 and PNG predictors).
/// </summary>
public static class ImagePredictors
{
    public const int None = 1;
    public const int Tiff = 2;
    public const int PngNone = 10;

    /// <summary>
    /// Reverses the declared prediction algorithm.
    /// </summary>
    public static bool TryReverse(
        byte[] data,
        int predictor,
        int colors,
        int bitsPerComponent,
        int columns,
        out byte[] result,
        out string? error)
    {
        result = data;
        error = null;

        if (predictor <= None)
            return true;

        if (colors is < 1 or > 32)
        {
            error = "A stream predictor declared an out-of-range colors value.";
            return false;
        }

        if (bitsPerComponent is not (1 or 2 or 4 or 8 or 16))
        {
            error = "A stream predictor declared an unsupported bitsPerComponent value.";
            return false;
        }

        if (columns < 1)
        {
            error = "A stream predictor declared an out-of-range columns value.";
            return false;
        }

        long bitsPerPixel = (long)colors * bitsPerComponent;
        long rowBits = bitsPerPixel * columns;
        long rowBytes = (rowBits + 7) / 8;
        if (rowBytes is <= 0 or > int.MaxValue)
        {
            error = "A stream predictor described a row larger than the addressable limit.";
            return false;
        }

        if (predictor == Tiff)
            return TryReverseTiff(data, colors, bitsPerComponent, columns, (int)rowBytes, out result, out error);

        if (predictor < PngNone)
        {
            error = "A stream declared an unknown predictor.";
            return false;
        }

        return TryReversePng(data, (int)rowBytes, Math.Max(1, (int)(bitsPerPixel / 8)), out result, out error);
    }

    /// <summary>
    /// Undoes TIFF predictor 2, which differences each component against the same
    /// component of the pixel to its left.
    /// </summary>
    public static bool TryReverseTiff(
        byte[] data,
        int colors,
        int bitsPerComponent,
        int columns,
        int rowBytes,
        out byte[] result,
        out string? error)
    {
        result = data;
        error = null;

        int rows = data.Length / rowBytes;
        var output = (byte[])data.Clone();
        int componentsPerRow = columns * colors;

        for (int row = 0; row < rows; row++)
        {
            int rowStart = row * rowBytes;

            if (bitsPerComponent == 16)
            {
                int step = colors * 2;
                for (int i = step; i + 1 < rowBytes; i += 2)
                {
                    int previous = (output[rowStart + i - step] << 8) | output[rowStart + i - step + 1];
                    int current = (output[rowStart + i] << 8) | output[rowStart + i + 1];
                    int sum = (current + previous) & 0xFFFF;
                    output[rowStart + i] = (byte)(sum >> 8);
                    output[rowStart + i + 1] = (byte)sum;
                }

                continue;
            }

            int mask = (1 << bitsPerComponent) - 1;
            for (int component = colors; component < componentsPerRow; component++)
            {
                int previous = ReadComponent(output, rowStart, component - colors, bitsPerComponent, rowBytes);
                int current = ReadComponent(output, rowStart, component, bitsPerComponent, rowBytes);
                WriteComponent(output, rowStart, component, bitsPerComponent, (current + previous) & mask, rowBytes);
            }
        }

        result = output;
        return true;
    }

    /// <summary>
    /// Undoes PNG row prediction (0: None, 1: Sub, 2: Up, 3: Average, 4: Paeth).
    /// </summary>
    public static bool TryReversePng(
        byte[] data,
        int rowBytes,
        int bytesPerPixel,
        out byte[] result,
        out string? error)
    {
        result = data;
        error = null;

        int stride = rowBytes + 1;
        int rows = data.Length / stride;
        if (rows == 0)
        {
            result = [];
            return true;
        }

        var output = new byte[(long)rows * rowBytes <= int.MaxValue ? rows * rowBytes : 0];
        if (output.Length == 0 && rows > 0)
        {
            error = "A PNG predictor described more output than the addressable limit.";
            return false;
        }

        Span<byte> previous = new byte[rowBytes];

        for (int row = 0; row < rows; row++)
        {
            int inputStart = row * stride;
            byte tag = data[inputStart];
            int outputStart = row * rowBytes;
            Span<byte> current = output.AsSpan(outputStart, rowBytes);
            data.AsSpan(inputStart + 1, rowBytes).CopyTo(current);

            switch (tag)
            {
                case 0: // None
                    break;
                case 1: // Sub
                    for (int i = bytesPerPixel; i < rowBytes; i++)
                        current[i] += current[i - bytesPerPixel];
                    break;
                case 2: // Up
                    for (int i = 0; i < rowBytes; i++)
                        current[i] += previous[i];
                    break;
                case 3: // Average
                    for (int i = 0; i < rowBytes; i++)
                    {
                        int left = i >= bytesPerPixel ? current[i - bytesPerPixel] : 0;
                        current[i] += (byte)((left + previous[i]) / 2);
                    }

                    break;
                case 4: // Paeth
                    for (int i = 0; i < rowBytes; i++)
                    {
                        int left = i >= bytesPerPixel ? current[i - bytesPerPixel] : 0;
                        int up = previous[i];
                        int upperLeft = i >= bytesPerPixel ? previous[i - bytesPerPixel] : 0;
                        current[i] += (byte)Paeth(left, up, upperLeft);
                    }

                    break;
                default:
                    error = "A PNG predictor row declared an unknown filter tag.";
                    return false;
            }

            current.CopyTo(previous);
        }

        result = output;
        return true;
    }

    private static int ReadComponent(byte[] data, int rowStart, int component, int bits, int rowBytes)
    {
        int bitOffset = component * bits;
        int index = rowStart + (bitOffset >> 3);
        if (index >= data.Length || (bitOffset >> 3) >= rowBytes)
            return 0;

        int shift = 8 - bits - (bitOffset & 7);
        return (data[index] >> shift) & ((1 << bits) - 1);
    }

    private static void WriteComponent(byte[] data, int rowStart, int component, int bits, int value, int rowBytes)
    {
        int bitOffset = component * bits;
        int index = rowStart + (bitOffset >> 3);
        if (index >= data.Length || (bitOffset >> 3) >= rowBytes)
            return;

        int shift = 8 - bits - (bitOffset & 7);
        int mask = ((1 << bits) - 1) << shift;
        data[index] = (byte)((data[index] & ~mask) | ((value << shift) & mask));
    }

    private static int Paeth(int left, int up, int upperLeft)
    {
        int estimate = left + up - upperLeft;
        int distanceLeft = Math.Abs(estimate - left);
        int distanceUp = Math.Abs(estimate - up);
        int distanceUpperLeft = Math.Abs(estimate - upperLeft);

        if (distanceLeft <= distanceUp && distanceLeft <= distanceUpperLeft)
            return left;
        return distanceUp <= distanceUpperLeft ? up : upperLeft;
    }
}
