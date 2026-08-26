using System;

namespace Broiler.Media.Image.Managed;

/// <summary>
/// Constants shared by the baseline JPEG decoder and encoder: marker bytes, the
/// zig-zag scan order, the standard (Annex K) quantization base tables with IJG
/// quality scaling, the standard Huffman table specifications, and the magnitude
/// helpers used by the entropy coder.
/// </summary>
internal static class JpegTables
{
    // Marker bytes (the byte following 0xFF).
    public const byte MarkerSoi = 0xD8;
    public const byte MarkerEoi = 0xD9;
    public const byte MarkerSof0 = 0xC0; // baseline sequential DCT
    public const byte MarkerSof2 = 0xC2; // progressive DCT
    public const byte MarkerDht = 0xC4;
    public const byte MarkerDqt = 0xDB;
    public const byte MarkerDri = 0xDD;
    public const byte MarkerSos = 0xDA;
    public const byte MarkerApp0 = 0xE0;
    public const byte MarkerRst0 = 0xD0;
    public const byte MarkerRst7 = 0xD7;

    /// <summary>Zig-zag scan order: maps a scan position 0..63 to a natural (row-major) index.</summary>
    public static ReadOnlySpan<byte> ZigZag =>
    [
         0,  1,  8, 16,  9,  2,  3, 10,
        17, 24, 32, 25, 18, 11,  4,  5,
        12, 19, 26, 33, 40, 48, 41, 34,
        27, 20, 13,  6,  7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36,
        29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46,
        53, 60, 61, 54, 47, 55, 62, 63,
    ];

    /// <summary>Standard luminance quantization base table (Annex K.1), natural order.</summary>
    public static ReadOnlySpan<byte> LuminanceQuant =>
    [
        16, 11, 10, 16, 24, 40, 51, 61,
        12, 12, 14, 19, 26, 58, 60, 55,
        14, 13, 16, 24, 40, 57, 69, 56,
        14, 17, 22, 29, 51, 87, 80, 62,
        18, 22, 37, 56, 68, 109, 103, 77,
        24, 35, 55, 64, 81, 104, 113, 92,
        49, 64, 78, 87, 103, 121, 120, 101,
        72, 92, 95, 98, 112, 100, 103, 99,
    ];

    /// <summary>Standard chrominance quantization base table (Annex K.2), natural order.</summary>
    public static ReadOnlySpan<byte> ChrominanceQuant =>
    [
        17, 18, 24, 47, 99, 99, 99, 99,
        18, 21, 26, 66, 99, 99, 99, 99,
        24, 26, 56, 99, 99, 99, 99, 99,
        47, 66, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
    ];

    // Standard Huffman tables (Annex K.3). Each "bits" array has 16 entries:
    // bits[i] is the number of codes of length (i+1).

    public static ReadOnlySpan<byte> DcLuminanceBits => [0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0];
    public static ReadOnlySpan<byte> DcLuminanceValues => [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];

    public static ReadOnlySpan<byte> DcChrominanceBits => [0, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0];
    public static ReadOnlySpan<byte> DcChrominanceValues => [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];

    public static ReadOnlySpan<byte> AcLuminanceBits => [0, 2, 1, 3, 3, 2, 4, 3, 5, 5, 4, 4, 0, 0, 1, 0x7D];

    public static ReadOnlySpan<byte> AcLuminanceValues =>
    [
        0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12,
        0x21, 0x31, 0x41, 0x06, 0x13, 0x51, 0x61, 0x07,
        0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xA1, 0x08,
        0x23, 0x42, 0xB1, 0xC1, 0x15, 0x52, 0xD1, 0xF0,
        0x24, 0x33, 0x62, 0x72, 0x82, 0x09, 0x0A, 0x16,
        0x17, 0x18, 0x19, 0x1A, 0x25, 0x26, 0x27, 0x28,
        0x29, 0x2A, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39,
        0x3A, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49,
        0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59,
        0x5A, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
        0x6A, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79,
        0x7A, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89,
        0x8A, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98,
        0x99, 0x9A, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7,
        0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6,
        0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3, 0xC4, 0xC5,
        0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xD2, 0xD3, 0xD4,
        0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA, 0xE1, 0xE2,
        0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9, 0xEA,
        0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8,
        0xF9, 0xFA,
    ];

    public static ReadOnlySpan<byte> AcChrominanceBits => [0, 2, 1, 2, 4, 4, 3, 4, 7, 5, 4, 4, 0, 1, 2, 0x77];

    public static ReadOnlySpan<byte> AcChrominanceValues =>
    [
        0x00, 0x01, 0x02, 0x03, 0x11, 0x04, 0x05, 0x21,
        0x31, 0x06, 0x12, 0x41, 0x51, 0x07, 0x61, 0x71,
        0x13, 0x22, 0x32, 0x81, 0x08, 0x14, 0x42, 0x91,
        0xA1, 0xB1, 0xC1, 0x09, 0x23, 0x33, 0x52, 0xF0,
        0x15, 0x62, 0x72, 0xD1, 0x0A, 0x16, 0x24, 0x34,
        0xE1, 0x25, 0xF1, 0x17, 0x18, 0x19, 0x1A, 0x26,
        0x27, 0x28, 0x29, 0x2A, 0x35, 0x36, 0x37, 0x38,
        0x39, 0x3A, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48,
        0x49, 0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58,
        0x59, 0x5A, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68,
        0x69, 0x6A, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78,
        0x79, 0x7A, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87,
        0x88, 0x89, 0x8A, 0x92, 0x93, 0x94, 0x95, 0x96,
        0x97, 0x98, 0x99, 0x9A, 0xA2, 0xA3, 0xA4, 0xA5,
        0xA6, 0xA7, 0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4,
        0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3,
        0xC4, 0xC5, 0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xD2,
        0xD3, 0xD4, 0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA,
        0xE2, 0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9,
        0xEA, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8,
        0xF9, 0xFA,
    ];

    /// <summary>Builds a quantization table scaled for <paramref name="quality"/> (1..100), natural order.</summary>
    public static int[] BuildQuantTable(ReadOnlySpan<byte> baseTable, int quality)
    {
        quality = Math.Clamp(quality, 1, 100);
        int scale = quality < 50 ? 5000 / quality : 200 - quality * 2;

        var q = new int[64];
        for (int i = 0; i < 64; i++)
            q[i] = Math.Clamp((baseTable[i] * scale + 50) / 100, 1, 255);
        return q;
    }

    /// <summary>Number of bits needed to represent <c>abs(value)</c> (the JPEG "magnitude category").</summary>
    public static int Magnitude(int value)
    {
        int a = Math.Abs(value);
        int s = 0;
        while (a > 0)
        {
            a >>= 1;
            s++;
        }
        return s;
    }

    /// <summary>Encodes a signed coefficient into its <paramref name="size"/>-bit JPEG representation.</summary>
    public static int ToBitPattern(int value, int size)
    {
        int code = value < 0 ? value + (1 << size) - 1 : value;
        return code & ((1 << size) - 1);
    }

    /// <summary>Sign-extends a received <paramref name="size"/>-bit value back into a signed coefficient.</summary>
    public static int Extend(int value, int size)
    {
        if (size == 0)
            return 0;
        return value < (1 << (size - 1)) ? value - (1 << size) + 1 : value;
    }
}
