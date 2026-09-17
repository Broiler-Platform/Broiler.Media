using System;

namespace Broiler.Media.Image.Managed.Jpx;

/// <summary>
/// The inverse discrete wavelet transforms JPEG 2000 Part 1 defines: the
/// reversible 5/3 integer filter and the irreversible 9/7 float filter.
/// </summary>
public static class JpxWavelet
{
    private const float Alpha = -1.586134342059924f;
    private const float Beta = -0.052980118572961f;
    private const float Gamma = 0.882911075530934f;
    private const float Delta = 0.443506852043971f;
    private const float Kappa = 1.230174104914001f;

    /// <summary>
    /// One inverse decomposition level: combines a low-pass quadrant with the
    /// three high-pass quadrants into the next larger image.
    /// </summary>
    /// <param name="coefficients">Coefficient array.</param>
    /// <param name="width">Level width.</param>
    /// <param name="height">Level height.</param>
    /// <param name="lowWidth">Low-pass width.</param>
    /// <param name="lowHeight">Low-pass height.</param>
    /// <param name="reversible">True for the 5/3 filter, false for 9/7.</param>
    public static void InverseLevel(
        float[] coefficients,
        int width,
        int height,
        int lowWidth,
        int lowHeight,
        bool reversible)
    {
        var row = new float[width];
        for (int y = 0; y < height; y++)
        {
            Interleave(coefficients, row, y * width, 1, width, lowWidth);
            Inverse1D(row, width, reversible);
            Scatter(row, coefficients, y * width, 1, width);
        }

        var column = new float[height];
        for (int x = 0; x < width; x++)
        {
            Interleave(coefficients, column, x, width, height, lowHeight);
            Inverse1D(column, height, reversible);
            Scatter(column, coefficients, x, width, height);
        }
    }

    private static void Interleave(float[] source, float[] line, int offset, int stride, int length, int lowLength)
    {
        for (int i = 0; i < lowLength; i++)
            line[2 * i] = source[offset + (i * stride)];

        for (int i = 0; i + lowLength < length; i++)
            line[(2 * i) + 1] = source[offset + ((lowLength + i) * stride)];
    }

    private static void Scatter(float[] line, float[] destination, int offset, int stride, int length)
    {
        for (int i = 0; i < length; i++)
            destination[offset + (i * stride)] = line[i];
    }

    private static void Inverse1D(float[] line, int length, bool reversible)
    {
        if (length == 1)
        {
            if (!reversible)
                line[0] /= Kappa;
            return;
        }

        if (reversible)
        {
            for (int i = 0; i < length; i += 2)
                line[i] -= MathF.Floor((At(line, length, i - 1) + At(line, length, i + 1) + 2) / 4);

            for (int i = 1; i < length; i += 2)
                line[i] += MathF.Floor((At(line, length, i - 1) + At(line, length, i + 1)) / 2);

            return;
        }

        for (int i = 0; i < length; i += 2)
            line[i] *= Kappa;

        for (int i = 1; i < length; i += 2)
            line[i] /= Kappa;

        for (int i = 0; i < length; i += 2)
            line[i] -= Delta * (At(line, length, i - 1) + At(line, length, i + 1));

        for (int i = 1; i < length; i += 2)
            line[i] -= Gamma * (At(line, length, i - 1) + At(line, length, i + 1));

        for (int i = 0; i < length; i += 2)
            line[i] -= Beta * (At(line, length, i - 1) + At(line, length, i + 1));

        for (int i = 1; i < length; i += 2)
            line[i] -= Alpha * (At(line, length, i - 1) + At(line, length, i + 1));
    }

    private static float At(float[] line, int length, int index)
    {
        if (index < 0)
            index = -index;
        if (index >= length)
            index = (2 * length) - index - 2;

        return index >= 0 && index < length ? line[index] : 0;
    }
}

/// <summary>
/// The multiple component transforms: reversible (RCT) and irreversible (ICT).
/// </summary>
public static class JpxComponentTransform
{
    /// <summary>Undoes the reversible colour transform, T.800 G.2.</summary>
    public static void InverseReversible(float[] c0, float[] c1, float[] c2)
    {
        for (int i = 0; i < c0.Length; i++)
        {
            float y = c0[i];
            float u = c1[i];
            float v = c2[i];

            float g = y - MathF.Floor((u + v) / 4);
            c0[i] = v + g;
            c1[i] = g;
            c2[i] = u + g;
        }
    }

    /// <summary>Undoes the irreversible colour transform, T.800 G.3.</summary>
    public static void InverseIrreversible(float[] c0, float[] c1, float[] c2)
    {
        for (int i = 0; i < c0.Length; i++)
        {
            float y = c0[i];
            float cb = c1[i];
            float cr = c2[i];

            c0[i] = y + (1.402f * cr);
            c1[i] = y - (0.34413f * cb) - (0.71414f * cr);
            c2[i] = y + (1.772f * cb);
        }
    }
}
