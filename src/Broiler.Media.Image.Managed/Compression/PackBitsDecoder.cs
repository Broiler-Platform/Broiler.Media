using System;
using System.IO;

namespace Broiler.Media.Image.Managed.Compression;

/// <summary>How a PackBits decode ended.</summary>
public enum PackBitsOutcome
{
    Decoded,
    Malformed,
    TooLarge,
}

/// <summary>The result of decoding one PackBits stream.</summary>
public readonly record struct PackBitsResult(
    PackBitsOutcome Outcome,
    byte[]? Data,
    string? Failure);

/// <summary>
/// Byte-run-1 / PackBits (Macintosh / TIFF Compression 32773 / PDF RunLengthDecode).
/// </summary>
public static class PackBitsDecoder
{
    public static PackBitsResult Decode(
        ReadOnlySpan<byte> input,
        long maxBytes = 64 * 1024 * 1024)
    {
        var output = new MemoryStream();
        int index = 0;

        while (index < input.Length)
        {
            byte length = input[index++];
            if (length == 128)
                break; // EOD

            if (length < 128)
            {
                int run = length + 1;
                if (index + run > input.Length)
                    return new PackBitsResult(PackBitsOutcome.Malformed, null, "A PackBits literal run ran past the end of the stream.");
                if (output.Length + run > maxBytes)
                    return new PackBitsResult(PackBitsOutcome.TooLarge, null, "A PackBits stream exceeded its decoded-byte ceiling.");

                output.Write(input.Slice(index, run));
                index += run;
                continue;
            }

            if (index >= input.Length)
                return new PackBitsResult(PackBitsOutcome.Malformed, null, "A PackBits repeat run had no byte to repeat.");

            int repeat = 257 - length;
            if (output.Length + repeat > maxBytes)
                return new PackBitsResult(PackBitsOutcome.TooLarge, null, "A PackBits stream exceeded its decoded-byte ceiling.");

            byte value = input[index++];
            for (int i = 0; i < repeat; i++)
                output.WriteByte(value);
        }

        return new PackBitsResult(PackBitsOutcome.Decoded, output.ToArray(), null);
    }
}
