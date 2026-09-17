using System;
using Broiler.Media.Image.Managed.Entropy;

namespace Broiler.Media.Image.Managed.Jpx;

/// <summary>The subband a code-block belongs to, which selects its context table.</summary>
public enum JpxSubband
{
    Ll,
    Hl,
    Lh,
    Hh,
}

/// <summary>
/// EBCOT tier-1: decodes one code-block's coefficients from its MQ-coded
/// bit-planes.
/// </summary>
public static class JpxBlockDecoder
{
    private const int RunLengthContext = 17;
    private const int UniformContext = 18;
    private const int ContextCount = 19;

    private const byte Significant = 1;
    private const byte VisitedThisPass = 2;
    private const byte Refined = 4;

    /// <summary>
    /// Decodes a code-block into signed coefficient magnitudes.
    /// </summary>
    public static int[]? Decode(
        ReadOnlyMemory<byte> data,
        int width,
        int height,
        int passes,
        int missingBitPlanes,
        int maxBitPlanes,
        JpxSubband subband)
    {
        if (width <= 0 || height <= 0 || passes <= 0)
            return null;

        long area = (long)width * height;
        if (area > int.MaxValue / 4)
            return null;

        var magnitudes = new int[area];
        var signs = new byte[area];
        var flags = new byte[area];

        var decoder = new MqDecoder(data);
        var contexts = new MqContexts(5);
        InitialiseContexts(contexts);

        int plane = maxBitPlanes - 1 - missingBitPlanes;
        if (plane < 0)
            return magnitudes;

        int pass = 0;
        int kind = 2;

        while (pass < passes && plane >= 0)
        {
            switch (kind)
            {
                case 0:
                    SignificancePropagation(decoder, contexts, magnitudes, signs, flags, width, height, plane, subband);
                    break;
                case 1:
                    MagnitudeRefinement(decoder, contexts, magnitudes, flags, width, height, plane);
                    break;
                default:
                    Cleanup(decoder, contexts, magnitudes, signs, flags, width, height, plane, subband);
                    break;
            }

            pass++;
            if (kind == 2)
            {
                ClearVisited(flags);
                plane--;
                kind = 0;
            }
            else
            {
                kind++;
            }
        }

        var result = new int[area];
        for (int i = 0; i < area; i++)
            result[i] = signs[i] != 0 ? -magnitudes[i] : magnitudes[i];

        return result;
    }

    private static void InitialiseContexts(MqContexts contexts)
    {
        for (int i = 0; i < ContextCount; i++)
        {
            contexts.State(i) = 0;
            contexts.Mps(i) = 0;
        }

        contexts.State(0) = 4;                  // the all-insignificant context
        contexts.State(RunLengthContext) = 3;
        contexts.State(UniformContext) = 46;
    }

    private static void ClearVisited(byte[] flags)
    {
        for (int i = 0; i < flags.Length; i++)
            flags[i] &= unchecked((byte)~VisitedThisPass);
    }

    private static void SignificancePropagation(
        MqDecoder decoder,
        MqContexts contexts,
        int[] magnitudes,
        byte[] signs,
        byte[] flags,
        int width,
        int height,
        int plane,
        JpxSubband subband)
    {
        foreach (int i in Stripes(width, height))
        {
            int x = i % width;
            int y = i / width;

            if ((flags[i] & Significant) != 0)
                continue;

            (int h, int v, int d) = Neighbours(flags, width, height, x, y);
            if (h + v + d == 0)
                continue;

            int context = SignificanceContext(h, v, d, subband);
            if (decoder.Decode(contexts, context) == 1)
            {
                signs[i] = (byte)DecodeSign(decoder, contexts, flags, signs, width, height, x, y);
                flags[i] |= Significant;
                magnitudes[i] |= 1 << plane;
            }

            flags[i] |= VisitedThisPass;
        }
    }

    private static void MagnitudeRefinement(
        MqDecoder decoder,
        MqContexts contexts,
        int[] magnitudes,
        byte[] flags,
        int width,
        int height,
        int plane)
    {
        foreach (int i in Stripes(width, height))
        {
            if ((flags[i] & Significant) == 0 || (flags[i] & VisitedThisPass) != 0)
                continue;

            int x = i % width;
            int y = i / width;

            int context;
            if ((flags[i] & Refined) != 0)
            {
                context = 16;
            }
            else
            {
                (int h, int v, int d) = Neighbours(flags, width, height, x, y);
                context = h + v + d > 0 ? 15 : 14;
            }

            if (decoder.Decode(contexts, context) == 1)
                magnitudes[i] |= 1 << plane;

            flags[i] |= Refined;
            flags[i] |= VisitedThisPass;
        }
    }

    private static void Cleanup(
        MqDecoder decoder,
        MqContexts contexts,
        int[] magnitudes,
        byte[] signs,
        byte[] flags,
        int width,
        int height,
        int plane,
        JpxSubband subband)
    {
        for (int y0 = 0; y0 < height; y0 += 4)
        {
            for (int x = 0; x < width; x++)
            {
                int y = y0;
                while (y < Math.Min(y0 + 4, height))
                {
                    bool runLength = y == y0 && y0 + 3 < height && ColumnIsClean(flags, width, height, x, y0);

                    if (runLength)
                    {
                        if (decoder.Decode(contexts, RunLengthContext) == 0)
                        {
                            y = y0 + 4;
                            continue;
                        }

                        int which = (decoder.Decode(contexts, UniformContext) << 1) |
                                    decoder.Decode(contexts, UniformContext);
                        y = y0 + which;

                        int at = (y * width) + x;
                        signs[at] = (byte)DecodeSign(decoder, contexts, flags, signs, width, height, x, y);
                        flags[at] |= Significant;
                        magnitudes[at] |= 1 << plane;
                        y++;
                        continue;
                    }

                    int index = (y * width) + x;
                    if ((flags[index] & Significant) != 0 || (flags[index] & VisitedThisPass) != 0)
                    {
                        y++;
                        continue;
                    }

                    (int h, int v, int d) = Neighbours(flags, width, height, x, y);
                    if (decoder.Decode(contexts, SignificanceContext(h, v, d, subband)) == 1)
                    {
                        signs[index] = (byte)DecodeSign(decoder, contexts, flags, signs, width, height, x, y);
                        flags[index] |= Significant;
                        magnitudes[index] |= 1 << plane;
                    }

                    y++;
                }
            }
        }
    }

    private static bool ColumnIsClean(byte[] flags, int width, int height, int x, int y0)
    {
        for (int dy = 0; dy < 4; dy++)
        {
            int y = y0 + dy;
            int index = (y * width) + x;
            if ((flags[index] & (Significant | VisitedThisPass)) != 0)
                return false;

            (int h, int v, int d) = Neighbours(flags, width, height, x, y);
            if (h + v + d != 0)
                return false;
        }

        return true;
    }

    private static int SignificanceContext(int h, int v, int d, JpxSubband subband)
    {
        if (subband == JpxSubband.Hl)
            (h, v) = (v, h);

        if (subband == JpxSubband.Hh)
        {
            int hv = h + v;
            if (d >= 3)
                return 8;
            if (d == 2)
                return hv >= 1 ? 7 : 6;
            if (d == 1)
                return hv >= 2 ? 5 : hv == 1 ? 4 : 3;
            return hv >= 2 ? 2 : hv == 1 ? 1 : 0;
        }

        if (h == 2)
            return 8;
        if (h == 1)
            return v >= 1 ? 7 : d >= 1 ? 6 : 5;
        if (v == 2)
            return 4;
        if (v == 1)
            return 3;
        return d >= 2 ? 2 : d == 1 ? 1 : 0;
    }

    private static int DecodeSign(
        MqDecoder decoder,
        MqContexts contexts,
        byte[] flags,
        byte[] signs,
        int width,
        int height,
        int x,
        int y)
    {
        int h = SignContribution(flags, signs, width, height, x - 1, y) +
                SignContribution(flags, signs, width, height, x + 1, y);
        int v = SignContribution(flags, signs, width, height, x, y - 1) +
                SignContribution(flags, signs, width, height, x, y + 1);

        h = Math.Clamp(h, -1, 1);
        v = Math.Clamp(v, -1, 1);

        int context;
        int prediction;

        if (h == 1)
        {
            context = v == 1 ? 13 : v == 0 ? 12 : 11;
            prediction = 0;
        }
        else if (h == 0)
        {
            context = v == 0 ? 9 : 10;
            prediction = v == -1 ? 1 : 0;
        }
        else
        {
            context = v == 1 ? 11 : v == 0 ? 12 : 13;
            prediction = 1;
        }

        return decoder.Decode(contexts, context) ^ prediction;
    }

    private static int SignContribution(byte[] flags, byte[] signs, int width, int height, int x, int y)
    {
        if (x < 0 || x >= width || y < 0 || y >= height)
            return 0;

        int index = (y * width) + x;
        if ((flags[index] & Significant) == 0)
            return 0;

        return signs[index] != 0 ? -1 : 1;
    }

    private static (int H, int V, int D) Neighbours(byte[] flags, int width, int height, int x, int y)
    {
        int h = Sig(flags, width, height, x - 1, y) + Sig(flags, width, height, x + 1, y);
        int v = Sig(flags, width, height, x, y - 1) + Sig(flags, width, height, x, y + 1);
        int d = Sig(flags, width, height, x - 1, y - 1) + Sig(flags, width, height, x + 1, y - 1) +
                Sig(flags, width, height, x - 1, y + 1) + Sig(flags, width, height, x + 1, y + 1);

        return (h, v, d);
    }

    private static int Sig(byte[] flags, int width, int height, int x, int y) =>
        x >= 0 && x < width && y >= 0 && y < height && (flags[(y * width) + x] & Significant) != 0 ? 1 : 0;

    private static System.Collections.Generic.IEnumerable<int> Stripes(int width, int height)
    {
        for (int y0 = 0; y0 < height; y0 += 4)
        {
            for (int x = 0; x < width; x++)
            {
                for (int y = y0; y < Math.Min(y0 + 4, height); y++)
                    yield return (y * width) + x;
            }
        }
    }
}
