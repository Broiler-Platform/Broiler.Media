using System;
using System.IO;
using System.Threading;

namespace Broiler.Media.Image.Managed.Compression;

/// <summary>How an LZW decode ended.</summary>
public enum LzwOutcome
{
    Decoded,
    Malformed,
    TooLarge,
}

/// <summary>The result of decoding one LZW stream.</summary>
public readonly record struct LzwResult(
    LzwOutcome Outcome,
    byte[]? Data,
    string? Failure);

/// <summary>
/// Variable-width Lempel-Ziv-Welch (LZW) decoder with configurable EarlyChange.
/// Used in TIFF, GIF, and PDF streams.
/// </summary>
public static class LzwDecoder
{
    private const int ClearCode = 256;
    private const int EndOfDataCode = 257;
    private const int FirstAssignedCode = 258;
    private const int MaxCodes = 4096;
    private const int MinCodeWidth = 9;
    private const int MaxCodeWidth = 12;

    public static LzwResult Decode(
        ReadOnlySpan<byte> input,
        int earlyChange = 1,
        long maxBytes = 64 * 1024 * 1024,
        CancellationToken cancellationToken = default)
    {
        if (input.Length == 0)
            return new LzwResult(LzwOutcome.Decoded, [], null);

        int effectiveEarlyChange = earlyChange == 0 ? 0 : 1;

        var prefix = new int[MaxCodes];
        var suffix = new byte[MaxCodes];
        for (int code = 0; code < 256; code++)
        {
            prefix[code] = -1;
            suffix[code] = (byte)code;
        }

        byte[] scratch = new byte[MaxCodes];
        var output = new MemoryStream();
        int next = FirstAssignedCode;
        int codeWidth = MinCodeWidth;
        int previous = -1;

        int bitBuffer = 0;
        int bitCount = 0;
        int position = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            while (bitCount < codeWidth)
            {
                if (position >= input.Length)
                    return new LzwResult(LzwOutcome.Decoded, output.ToArray(), null);

                bitBuffer = (bitBuffer << 8) | input[position++];
                bitCount += 8;
            }

            bitCount -= codeWidth;
            int code = (bitBuffer >> bitCount) & ((1 << codeWidth) - 1);
            bitBuffer &= (1 << bitCount) - 1;

            if (code == EndOfDataCode)
                break;

            if (code == ClearCode)
            {
                next = FirstAssignedCode;
                codeWidth = MinCodeWidth;
                previous = -1;
                continue;
            }

            byte firstByte;
            if (previous < 0)
            {
                if (code >= FirstAssignedCode)
                    return new LzwResult(LzwOutcome.Malformed, null, "An LZW stream began with a code that was not in its table.");

                firstByte = suffix[code];
                if (output.Length + 1 > maxBytes)
                    return new LzwResult(LzwOutcome.TooLarge, null, "An LZW stream exceeded its decoded-byte ceiling.");

                output.WriteByte(firstByte);
                previous = code;
                continue;
            }

            if (code < next)
            {
                if (!TryWrite(output, maxBytes, prefix, suffix, scratch, code, out firstByte, out bool over))
                {
                    return over
                        ? new LzwResult(LzwOutcome.TooLarge, null, "An LZW stream exceeded its decoded-byte ceiling.")
                        : new LzwResult(LzwOutcome.Malformed, null, "An LZW table entry did not resolve to a string.");
                }
            }
            else if (code == next)
            {
                if (!TryWrite(output, maxBytes, prefix, suffix, scratch, previous, out firstByte, out bool over))
                {
                    return over
                        ? new LzwResult(LzwOutcome.TooLarge, null, "An LZW stream exceeded its decoded-byte ceiling.")
                        : new LzwResult(LzwOutcome.Malformed, null, "An LZW table entry did not resolve to a string.");
                }

                if (output.Length + 1 > maxBytes)
                    return new LzwResult(LzwOutcome.TooLarge, null, "An LZW stream exceeded its decoded-byte ceiling.");

                output.WriteByte(firstByte);
            }
            else
            {
                return new LzwResult(LzwOutcome.Malformed, null, "An LZW stream used a code that was not in its table.");
            }

            if (next < MaxCodes)
            {
                prefix[next] = previous;
                suffix[next] = firstByte;
                next++;
            }

            previous = code;

            if (codeWidth < MaxCodeWidth && next + effectiveEarlyChange >= 1 << codeWidth)
                codeWidth++;
        }

        return new LzwResult(LzwOutcome.Decoded, output.ToArray(), null);
    }

    private static bool TryWrite(
        MemoryStream output,
        long ceiling,
        int[] prefix,
        byte[] suffix,
        byte[] scratch,
        int code,
        out byte firstByte,
        out bool overCeiling)
    {
        firstByte = 0;
        overCeiling = false;

        int count = 0;
        for (int current = code; current >= 0; current = prefix[current])
        {
            if (count >= scratch.Length)
                return false;
            scratch[count++] = suffix[current];
        }

        if (count == 0)
            return false;

        if (output.Length + count > ceiling)
        {
            overCeiling = true;
            return false;
        }

        firstByte = scratch[count - 1];
        for (int i = count - 1; i >= 0; i--)
            output.WriteByte(scratch[i]);

        return true;
    }
}
