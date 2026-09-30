using System;
using System.IO;
using System.Threading;

namespace Broiler.Media.Image.Managed.Compression;

/// <summary>How an LZW decode ended.</summary>
// Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=TBF
// Broiler-Human:        PENDING
public enum LzwOutcome
{
    Decoded,
    Malformed,
    TooLarge,
}

/// <summary>The result of decoding one LZW stream.</summary>
// Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=TBF
// Broiler-Human:        PENDING
public readonly record struct LzwResult(
    LzwOutcome Outcome,
    byte[]? Data,
    string? Failure);

/// <summary>
/// Variable-width Lempel-Ziv-Welch (LZW) decoder with configurable EarlyChange.
/// Used in TIFF, GIF, and PDF streams.
/// </summary>
// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=8; Fingerprint=TBF
// Broiler-Falsified-If: a stream of self-referencing table codes produces more than maxBytes of output before TooLarge is returned
// Broiler-Human:        PENDING
public static class LzwDecoder
{
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: ClearCode is not 256, so a PDF or TIFF clear code is decoded as a table reference instead of resetting the table
    // Broiler-Human:        PENDING
    private const int ClearCode = 256;
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: code 257 is decoded as a table reference instead of ending the stream
    // Broiler-Human:        PENDING
    private const int EndOfDataCode = 257;
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: the first entry added after a clear lands anywhere other than slot 258, so later codes resolve to the wrong strings
    // Broiler-Human:        PENDING
    private const int FirstAssignedCode = 258;
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: MaxCodes differs from 4096, so a full 12-bit table stops growing early or holds an entry no 12-bit code can reach
    // Broiler-Human:        PENDING
    private const int MaxCodes = 4096;
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: the first code after a clear is read with a width other than 9 bits
    // Broiler-Human:        PENDING
    private const int MinCodeWidth = 9;
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: codes wider than 12 bits are read once the table fills, misaligning every later code
    // Broiler-Human:        PENDING
    private const int MaxCodeWidth = 12;

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=8; Fingerprint=TBF
    // Broiler-Falsified-If: a code above the next free table slot resolves from an entry left before a clear instead of returning Malformed
    // Broiler-Human:        PENDING
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

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=3; Fingerprint=TBF
    // Broiler-Falsified-If: a prefix chain longer than scratch is followed past the 4096-byte buffer instead of returning false
    // Broiler-Human:        PENDING
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
