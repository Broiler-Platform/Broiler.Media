using System;

namespace Broiler.Media.Image.Managed;

/// <summary>
/// Separable 8x8 type-II DCT used by JPEG. Implemented with the orthonormal 1D
/// basis matrix <c>M</c> so that the forward transform is <c>F = M · S · Mᵀ</c>
/// and the inverse is <c>S = Mᵀ · F · M</c>. Blocks are stored row-major as
/// <c>block[row * 8 + col]</c>; the vertical frequency is the row, horizontal the
/// column — matching the natural order of the quantization and zig-zag tables.
/// </summary>
internal static class JpegDct
{
    private const int N = 8;

    /// <summary>Orthonormal 1D DCT basis: <c>M[k, n] = α(k)·cos((2n+1)kπ/16)</c>.</summary>
    private static readonly double[] M = BuildBasis();

    private static double[] BuildBasis()
    {
        var m = new double[N * N];
        for (int k = 0; k < N; k++)
        {
            double alpha = k == 0 ? Math.Sqrt(1.0 / N) : Math.Sqrt(2.0 / N);
            for (int n = 0; n < N; n++)
                m[k * N + n] = alpha * Math.Cos((2 * n + 1) * k * Math.PI / (2 * N));
        }
        return m;
    }

    /// <summary>Forward DCT of a spatial block (in place): <c>F = M · S · Mᵀ</c>.</summary>
    public static void Forward(double[] block)
    {
        var tmp = new double[N * N];

        // tmp = M · S  (rows of tmp indexed by frequency k1, columns by spatial n2)
        for (int k1 = 0; k1 < N; k1++)
            for (int n2 = 0; n2 < N; n2++)
            {
                double sum = 0;
                for (int n1 = 0; n1 < N; n1++)
                    sum += M[k1 * N + n1] * block[n1 * N + n2];
                tmp[k1 * N + n2] = sum;
            }

        // F = tmp · Mᵀ  (F[k1, k2] = Σ tmp[k1, n2] · M[k2, n2])
        for (int k1 = 0; k1 < N; k1++)
            for (int k2 = 0; k2 < N; k2++)
            {
                double sum = 0;
                for (int n2 = 0; n2 < N; n2++)
                    sum += tmp[k1 * N + n2] * M[k2 * N + n2];
                block[k1 * N + k2] = sum;
            }
    }

    /// <summary>Inverse DCT of a coefficient block (in place): <c>S = Mᵀ · F · M</c>.</summary>
    public static void Inverse(double[] block) => Inverse(block, new double[N * N]);

    /// <summary>
    /// Inverse DCT of a coefficient block (in place), reusing a caller-owned intermediate.
    /// </summary>
    /// <param name="scratch">
    /// A <c>64</c>-element buffer for the half-transformed block. The decoder reconstructs
    /// thousands of blocks per image and each one needed its own allocation before; a caller that
    /// hands the same buffer back every time — one per band, so it stays thread-confined — removes
    /// that garbage entirely. Contents on entry are irrelevant and on exit meaningless.
    /// </param>
    public static void Inverse(double[] block, double[] scratch)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentOutOfRangeException.ThrowIfLessThan(scratch?.Length ?? 0, N * N, nameof(scratch));
        double[] tmp = scratch!;

        // tmp = Mᵀ · F  (tmp[n1, k2] = Σ M[k1, n1] · F[k1, k2])
        for (int n1 = 0; n1 < N; n1++)
            for (int k2 = 0; k2 < N; k2++)
            {
                double sum = 0;
                for (int k1 = 0; k1 < N; k1++)
                    sum += M[k1 * N + n1] * block[k1 * N + k2];
                tmp[n1 * N + k2] = sum;
            }

        // S = tmp · M  (S[n1, n2] = Σ tmp[n1, k2] · M[k2, n2])
        for (int n1 = 0; n1 < N; n1++)
            for (int n2 = 0; n2 < N; n2++)
            {
                double sum = 0;
                for (int k2 = 0; k2 < N; k2++)
                    sum += tmp[n1 * N + k2] * M[k2 * N + n2];
                block[n1 * N + n2] = sum;
            }
    }
}
