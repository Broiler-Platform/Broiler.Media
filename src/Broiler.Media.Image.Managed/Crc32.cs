using System;

namespace Broiler.Media.Image.Managed;

/// <summary>
/// Standard CRC-32 (ISO 3309 / zlib polynomial <c>0xEDB88320</c>) used for PNG
/// chunk checksums. Implemented locally so the managed image codec carries no
/// dependency on any platform CRC facility.
/// </summary>
// Broiler-AI:           Origin=AI; Spec=PNG s5.5; IP=Low; Security=Medium; Resources=3; Fingerprint=TBF
// Broiler-Falsified-If: the CRC of the ASCII bytes 123456789 is not 0xCBF43926, so well-formed PNG chunks fail their CRC check
// Broiler-Human:        PENDING
internal static class Crc32
{
    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=1; Fingerprint=TBF
    // Broiler-Falsified-If: Table[1] is not 0x77073096
    // Broiler-Human:        PENDING
    private static readonly uint[] Table = BuildTable();

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=1; Fingerprint=TBF
    // Broiler-Falsified-If: the entry built for index 128 is not 0xEDB88320
    // Broiler-Human:        PENDING
    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    /// <summary>Computes the CRC-32 of <paramref name="data"/>.</summary>
    // Broiler-AI:           Origin=AI; Spec=PNG s5.5; IP=Low; Security=Medium; Resources=3; Fingerprint=TBF
    // Broiler-Falsified-If: Compute over an empty span returns anything other than 0
    // Broiler-Human:        PENDING
    public static uint Compute(ReadOnlySpan<byte> data) => Update(0xFFFFFFFFu, data) ^ 0xFFFFFFFFu;

    /// <summary>Feeds more bytes into a running CRC; pass <c>0xFFFFFFFF</c> as the seed.</summary>
    // Broiler-AI:           Origin=AI; Spec=PNG s5.5; IP=Low; Security=Medium; Resources=3; Fingerprint=TBF
    // Broiler-Falsified-If: feeding bytes to Update in two pieces gives a different CRC from one call over the same bytes
    // Broiler-Human:        PENDING
    public static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);

        return crc;
    }
}
