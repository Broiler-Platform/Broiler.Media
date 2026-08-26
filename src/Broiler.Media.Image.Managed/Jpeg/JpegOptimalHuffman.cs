using System;
using System.Collections.Generic;

namespace Broiler.Media.Image.Managed;

/// <summary>
/// Builds an optimal JPEG Huffman table specification (<c>BITS</c> / <c>HUFFVAL</c>,
/// Annex C) from a symbol-frequency histogram, following the standard algorithm
/// (ISO/IEC 10918-1 Annex K.2, as implemented by libjpeg's
/// <c>jpeg_gen_optimal_table</c>): build minimum-redundancy code lengths, clamp
/// them to 16 bits, and reserve one code point so no symbol is assigned the
/// all-ones codeword. The resulting spec is consumed by
/// <see cref="JpegHuffmanTable.Build"/>.
/// </summary>
internal static class JpegOptimalHuffman
{
    /// <param name="frequency">Per-symbol counts, indexed 0..255.</param>
    /// <returns>A 16-entry <c>BITS</c> array and the <c>HUFFVAL</c> symbol list.</returns>
    public static (byte[] Bits, byte[] Values) Generate(ReadOnlySpan<int> frequency)
    {
        if (frequency.Length != 256)
            throw new ArgumentException("Frequency histogram must have 256 entries.", nameof(frequency));

        // freq[256] is a reserved code point guaranteeing the all-ones code stays unused.
        var freq = new long[257];
        for (int i = 0; i < 256; i++)
            freq[i] = frequency[i];
        freq[256] = 1;

        var codeSize = new int[257];
        var others = new int[257];
        Array.Fill(others, -1);

        // Minimum-redundancy merge: repeatedly combine the two least-frequent groups.
        while (true)
        {
            int c1 = FindLeast(freq, -1);
            int c2 = FindLeast(freq, c1);
            if (c2 < 0)
                break; // only one group remains

            freq[c1] += freq[c2];
            freq[c2] = 0;

            codeSize[c1]++;
            while (others[c1] >= 0)
            {
                c1 = others[c1];
                codeSize[c1]++;
            }
            others[c1] = c2;

            codeSize[c2]++;
            while (others[c2] >= 0)
            {
                c2 = others[c2];
                codeSize[c2]++;
            }
        }

        // Count codes of each length (lengths can momentarily exceed 16).
        var bitsCount = new int[33];
        for (int i = 0; i <= 256; i++)
            if (codeSize[i] > 0)
                bitsCount[codeSize[i]]++;

        // Clamp the longest codes down to 16 bits (Annex K, figure K.3).
        for (int i = 32; i > 16; i--)
        {
            while (bitsCount[i] > 0)
            {
                int j = i - 2;
                while (bitsCount[j] == 0)
                    j--;

                bitsCount[i] -= 2;
                bitsCount[i - 1]++;
                bitsCount[j + 1] += 2;
                bitsCount[j]--;
            }
        }

        // Remove the reserved code point (the longest remaining code belongs to symbol 256).
        int longest = 16;
        while (longest > 0 && bitsCount[longest] == 0)
            longest--;
        if (longest > 0)
            bitsCount[longest]--;

        var bits = new byte[16];
        for (int l = 1; l <= 16; l++)
            bits[l - 1] = (byte)bitsCount[l];

        // HUFFVAL: real symbols (0..255) ordered by increasing code length.
        var values = new List<byte>();
        for (int l = 1; l <= 32; l++)
            for (int sym = 0; sym < 256; sym++)
                if (codeSize[sym] == l)
                    values.Add((byte)sym);

        return (bits, values.ToArray());
    }

    /// <summary>Finds the nonzero-frequency symbol with the least count (ties resolve to the highest index).</summary>
    private static int FindLeast(long[] freq, int exclude)
    {
        int found = -1;
        long best = long.MaxValue;
        for (int i = 0; i <= 256; i++)
        {
            if (i == exclude || freq[i] == 0)
                continue;
            if (freq[i] <= best)
            {
                best = freq[i];
                found = i;
            }
        }
        return found;
    }
}
