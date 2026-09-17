using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Broiler.Media.Image.Managed.CcittFax;

namespace Broiler.Media.Image.Managed.Tests.CcittFax;

internal static class CcittFaxTests
{
    public static void Register(List<(string Name, Func<ValueTask> Body)> tests)
    {
        tests.Add(("CCITT Fax 1D round-trips pattern", () => RoundTripPattern(CcittCoding.OneDimensional, 0)));
        tests.Add(("CCITT Fax 2D round-trips pattern", () => RoundTripPattern(CcittCoding.TwoDimensional, -1)));
        tests.Add(("CCITT Fax Mixed round-trips pattern", () => RoundTripPattern(CcittCoding.Mixed, 4)));
        tests.Add(("CCITT Fax wide lines cross makeup codes", WideLinesCrossMakeupCodes));
        tests.Add(("CCITT Fax all-white bitmap survives", AllWhiteSurvives));
        tests.Add(("CCITT Fax all-black bitmap survives", AllBlackSurvives));
    }

    private static ValueTask RoundTripPattern(CcittCoding coding, int k)
    {
        bool[][] original = Pattern(64, 24);
        bool[][] decoded = RoundTrip(original, coding, k);
        AssertSame(original, decoded);
        return ValueTask.CompletedTask;
    }

    private static ValueTask WideLinesCrossMakeupCodes()
    {
        bool[][] original = Pattern(2000, 4);
        bool[][] decoded = RoundTrip(original, CcittCoding.TwoDimensional, -1);
        AssertSame(original, decoded);
        return ValueTask.CompletedTask;
    }

    private static ValueTask AllWhiteSurvives()
    {
        bool[][] original = Uniform(80, 6, black: false);
        bool[][] decoded = RoundTrip(original, CcittCoding.OneDimensional, 0);
        AssertSame(original, decoded);
        return ValueTask.CompletedTask;
    }

    private static ValueTask AllBlackSurvives()
    {
        bool[][] original = Uniform(80, 6, black: true);
        bool[][] decoded = RoundTrip(original, CcittCoding.TwoDimensional, -1);
        AssertSame(original, decoded);
        return ValueTask.CompletedTask;
    }

    private static bool[][] RoundTrip(bool[][] original, CcittCoding coding, int k)
    {
        int width = original[0].Length;
        int height = original.Length;

        byte[] encoded = CcittFaxEncoder.Encode(original, k);
        var options = new CcittFaxOptions(coding, width, height, BlackIs1: true, EncodedByteAlign: false, ExpectsEndOfLine: false);
        CcittFaxResult result = CcittFaxDecoder.Decode(encoded, options, 64 * 1024 * 1024);

        if (result.Outcome != CcittFaxOutcome.Decoded || result.Rows is null)
            throw new InvalidOperationException($"Decode failed: {result.Failure}");

        return Unpack(result.Rows, width, height);
    }

    private static bool[][] Pattern(int width, int height)
    {
        var image = new bool[height][];
        for (int y = 0; y < height; y++)
        {
            image[y] = new bool[width];
            for (int x = 0; x < width; x++)
                image[y][x] = ((x ^ y) & 4) != 0;
        }

        return image;
    }

    private static bool[][] Uniform(int width, int height, bool black)
    {
        var image = new bool[height][];
        for (int y = 0; y < height; y++)
        {
            image[y] = new bool[width];
            if (black)
                Array.Fill(image[y], true);
        }

        return image;
    }

    private static bool[][] Unpack(byte[] bytes, int width, int height)
    {
        int stride = (width + 7) / 8;
        var image = new bool[height][];

        for (int y = 0; y < height; y++)
        {
            image[y] = new bool[width];
            for (int x = 0; x < width; x++)
            {
                int index = (y * stride) + (x >> 3);
                int bit = 0x80 >> (x & 7);
                image[y][x] = (bytes[index] & bit) != 0;
            }
        }

        return image;
    }

    private static void AssertSame(bool[][] expected, bool[][] actual)
    {
        if (expected.Length != actual.Length)
            throw new Exception($"Height mismatch: expected {expected.Length}, was {actual.Length}");

        for (int y = 0; y < expected.Length; y++)
        {
            if (expected[y].Length != actual[y].Length)
                throw new Exception($"Width mismatch at row {y}");

            for (int x = 0; x < expected[y].Length; x++)
            {
                if (expected[y][x] != actual[y][x])
                    throw new Exception($"Pixel mismatch at ({x}, {y})");
            }
        }
    }
}
