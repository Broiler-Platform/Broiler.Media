using System;

namespace Broiler.Media.Image;

/// <summary>The base color space of raw image samples.</summary>
public enum SampleColorSpace
{
    /// <summary>Grayscale (1, 2, 4, or 8 bits per sample).</summary>
    Gray,

    /// <summary>RGB (8 bits per component).</summary>
    Rgb,

    /// <summary>Indexed (1, 2, 4, or 8 bits per pixel over an RGB palette).</summary>
    Indexed,
}

/// <summary>
/// Unpacks raw pixel samples into tightly packed 32-bit RGBA pixels.
/// </summary>
public static class ImageSampleConverter
{
    /// <summary>
    /// Converts raw samples into a 32-bit RGBA byte array.
    /// </summary>
    /// <param name="samples">Packed raw samples.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="bitsPerComponent">1, 2, 4, or 8 bits.</param>
    /// <param name="space">Color space: Gray, Rgb, or Indexed.</param>
    /// <param name="palette">For Indexed space: RGB triples (3 bytes per palette entry).</param>
    /// <param name="decode">Optional min/max linear mapping intervals per component.</param>
    public static byte[]? UnpackToRgba(
        ReadOnlySpan<byte> samples,
        int width,
        int height,
        int bitsPerComponent,
        SampleColorSpace space,
        byte[]? palette = null,
        double[]? decode = null)
    {
        if (width <= 0 || height <= 0)
            return null;

        int components = space == SampleColorSpace.Rgb ? 3 : 1;
        long expectedBytes = (((long)width * components * bitsPerComponent) + 7) / 8 * height;
        if (samples.Length != expectedBytes)
            return null;

        long pixels = (long)width * height;
        if (pixels > int.MaxValue / 4)
            return null;

        byte[] rgba = new byte[pixels * 4];
        int maximum = (1 << bitsPerComponent) - 1;
        long stride = (((long)width * components * bitsPerComponent) + 7) / 8;
        int output = 0;

        for (int y = 0; y < height; y++)
        {
            long row = y * stride;

            for (int x = 0; x < width; x++)
            {
                switch (space)
                {
                    case SampleColorSpace.Gray:
                    {
                        int raw = Sample(samples, row, x, bitsPerComponent);
                        byte level = Component(raw, maximum, decode, 0);
                        rgba[output] = level;
                        rgba[output + 1] = level;
                        rgba[output + 2] = level;
                        break;
                    }

                    case SampleColorSpace.Rgb:
                    {
                        long at = row + ((long)x * 3);
                        rgba[output] = Component(samples[(int)at], maximum, decode, 0);
                        rgba[output + 1] = Component(samples[(int)at + 1], maximum, decode, 1);
                        rgba[output + 2] = Component(samples[(int)at + 2], maximum, decode, 2);
                        break;
                    }

                    default:
                    {
                        int index = Sample(samples, row, x, bitsPerComponent);
                        if (palette is not null)
                        {
                            int at = index * 3;
                            if (at + 2 < palette.Length)
                            {
                                rgba[output] = palette[at];
                                rgba[output + 1] = palette[at + 1];
                                rgba[output + 2] = palette[at + 2];
                            }
                        }

                        break;
                    }
                }

                rgba[output + 3] = 255;
                output += 4;
            }
        }

        return rgba;
    }

    private static int Sample(ReadOnlySpan<byte> samples, long row, int x, int bits)
    {
        if (bits == 8)
            return samples[(int)(row + x)];

        int perByte = 8 / bits;
        byte packed = samples[(int)(row + (x / perByte))];
        int shift = 8 - bits - (x % perByte * bits);
        return (packed >> shift) & ((1 << bits) - 1);
    }

    private static byte Component(int raw, int maximum, double[]? decode, int component)
    {
        double value = (double)raw / maximum;

        if (decode is not null && (component * 2 + 1) < decode.Length)
        {
            double min = decode[component * 2];
            double max = decode[(component * 2) + 1];
            value = min + (value * (max - min));
        }

        double eight = value * 255;
        return eight <= 0 ? (byte)0 : eight >= 255 ? (byte)255 : (byte)Math.Round(eight, MidpointRounding.AwayFromZero);
    }
}
