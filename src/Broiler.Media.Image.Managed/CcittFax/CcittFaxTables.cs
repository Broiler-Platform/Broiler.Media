using System.Collections.Generic;

namespace Broiler.Media.Image.Managed.CcittFax;

/// <summary>
/// The run-length and mode codes of ITU-T T.4 and T.6.
/// </summary>
// Broiler-AI:           Origin=Specification; IP=High; Security=High; Resources=1; Fingerprint=TBF
// Broiler-Falsified-If: a code-table row differs from the ITU-T T.4 run-length or mode code it transcribes, so a conforming fax stream decodes to different runs (transcribed ITU table, reproduction licence doubtful)
// Broiler-Human:        PENDING
public static class CcittFaxTables
{
    /// <summary>The longest run code, in bits.</summary>
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: MaxRunCodeLength is below 13, so the 13-bit black makeup codes for runs 512 to 1728 never resolve
    // Broiler-Human:        PENDING
    public const int MaxRunCodeLength = 14;

    /// <summary>Runs are coded in multiples of 64 above 63.</summary>
    // Broiler-AI:           Origin=AI; IP=None; Security=None; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: MakeupStep is not 64, so a run split by it into makeup and terminating codes does not match the T.4 makeup table
    // Broiler-Human:        PENDING
    public const int MakeupStep = 64;

    /// <summary>A code that resolves to a makeup run needs a terminating code after it.</summary>
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a makeup run of 64 is treated as terminating, ending the run before its terminating code is read
    // Broiler-Human:        PENDING
    public const int MaxTerminatingRun = 63;

    // ---- two-dimensional mode codes -------------------------------------------

    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: ModePass shares its value with another Mode constant, so the mode switch in DecodeTwoDimensionalLine takes a pass code for a different mode
    // Broiler-Human:        PENDING
    public const int ModePass = 1;
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: ModeHorizontal shares its value with another Mode constant, so the mode switch in DecodeTwoDimensionalLine takes a horizontal code for a different mode
    // Broiler-Human:        PENDING
    public const int ModeHorizontal = 2;
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: ModeVertical0 shares its value with another Mode constant, so the mode switch in DecodeTwoDimensionalLine takes a V0 code for a different mode
    // Broiler-Human:        PENDING
    public const int ModeVertical0 = 3;
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: ModeVerticalRight1 shares its value with another Mode constant, so the mode switch in DecodeTwoDimensionalLine takes a VR1 code for a different mode
    // Broiler-Human:        PENDING
    public const int ModeVerticalRight1 = 4;
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: ModeVerticalRight2 shares its value with another Mode constant, so the mode switch in DecodeTwoDimensionalLine takes a VR2 code for a different mode
    // Broiler-Human:        PENDING
    public const int ModeVerticalRight2 = 5;
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: ModeVerticalRight3 shares its value with another Mode constant, so the mode switch in DecodeTwoDimensionalLine takes a VR3 code for a different mode
    // Broiler-Human:        PENDING
    public const int ModeVerticalRight3 = 6;
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: ModeVerticalLeft1 shares its value with another Mode constant, so the mode switch in DecodeTwoDimensionalLine takes a VL1 code for a different mode
    // Broiler-Human:        PENDING
    public const int ModeVerticalLeft1 = 7;
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: ModeVerticalLeft2 shares its value with another Mode constant, so the mode switch in DecodeTwoDimensionalLine takes a VL2 code for a different mode
    // Broiler-Human:        PENDING
    public const int ModeVerticalLeft2 = 8;
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: ModeVerticalLeft3 shares its value with another Mode constant, so the mode switch in DecodeTwoDimensionalLine takes a VL3 code for a different mode
    // Broiler-Human:        PENDING
    public const int ModeVerticalLeft3 = 9;
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: ModeExtension shares its value with another Mode constant, so the mode switch in DecodeTwoDimensionalLine takes a extension code for a different mode
    // Broiler-Human:        PENDING
    public const int ModeExtension = 10;
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: ModeEndOfLine shares its value with another Mode constant, so the mode switch in DecodeTwoDimensionalLine takes a end-of-line code for a different mode
    // Broiler-Human:        PENDING
    public const int ModeEndOfLine = 11;

    /// <summary>The longest mode code, in bits.</summary>
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: MaxModeCodeLength is below 12, so the 12-bit end-of-line code never resolves as a mode
    // Broiler-Human:        PENDING
    public const int MaxModeCodeLength = 12;

    /// <summary>The end-of-line code: eleven zeroes and a one.</summary>
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: EndOfLineCode is not eleven zero bits then a one, so an end-of-line inside a two-dimensional line is not recognised
    // Broiler-Human:        PENDING
    public const int EndOfLineCode = 0b000000000001;

    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: EndOfLineLength is below 12, so TryPeekEndOfLine takes fewer than eleven zero bits and a one for an end-of-line
    // Broiler-Human:        PENDING
    public const int EndOfLineLength = 12;

    // Broiler-AI:           Origin=Specification; IP=High; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a white row differs from its T.4 code, e.g. run 1728 not decoding from the 9-bit 010011011 (transcribed ITU table, reproduction licence doubtful)
    // Broiler-Human:        PENDING
    private static readonly (int Code, int Length, int Run)[] WhiteCodes =
    [
        (0b00110101, 8, 0), (0b000111, 6, 1), (0b0111, 4, 2), (0b1000, 4, 3),
        (0b1011, 4, 4), (0b1100, 4, 5), (0b1110, 4, 6), (0b1111, 4, 7),
        (0b10011, 5, 8), (0b10100, 5, 9), (0b00111, 5, 10), (0b01000, 5, 11),
        (0b001000, 6, 12), (0b000011, 6, 13), (0b110100, 6, 14), (0b110101, 6, 15),
        (0b101010, 6, 16), (0b101011, 6, 17), (0b0100111, 7, 18), (0b0001100, 7, 19),
        (0b0001000, 7, 20), (0b0010111, 7, 21), (0b0000011, 7, 22), (0b0000100, 7, 23),
        (0b0101000, 7, 24), (0b0101011, 7, 25), (0b0010011, 7, 26), (0b0100100, 7, 27),
        (0b0011000, 7, 28), (0b00000010, 8, 29), (0b00000011, 8, 30), (0b00011010, 8, 31),
        (0b00011011, 8, 32), (0b00010010, 8, 33), (0b00010011, 8, 34), (0b00010100, 8, 35),
        (0b00010101, 8, 36), (0b00010110, 8, 37), (0b00010111, 8, 38), (0b00101000, 8, 39),
        (0b00101001, 8, 40), (0b00101010, 8, 41), (0b00101011, 8, 42), (0b00101100, 8, 43),
        (0b00101101, 8, 44), (0b00000100, 8, 45), (0b00000101, 8, 46), (0b00001010, 8, 47),
        (0b00001011, 8, 48), (0b01010010, 8, 49), (0b01010011, 8, 50), (0b01010100, 8, 51),
        (0b01010101, 8, 52), (0b00100100, 8, 53), (0b00100101, 8, 54), (0b01011000, 8, 55),
        (0b01011001, 8, 56), (0b01011010, 8, 57), (0b01011011, 8, 58), (0b01001010, 8, 59),
        (0b01001011, 8, 60), (0b00110010, 8, 61), (0b00110011, 8, 62), (0b00110100, 8, 63),

        (0b11011, 5, 64), (0b10010, 5, 128), (0b010111, 6, 192), (0b0110111, 7, 256),
        (0b00110110, 8, 320), (0b00110111, 8, 384), (0b01100100, 8, 448), (0b01100101, 8, 512),
        (0b01101000, 8, 576), (0b01100111, 8, 640), (0b011001100, 9, 704), (0b011001101, 9, 768),
        (0b011010010, 9, 832), (0b011010011, 9, 896), (0b011010100, 9, 960), (0b011010101, 9, 1024),
        (0b011010110, 9, 1088), (0b011010111, 9, 1152), (0b011011000, 9, 1216), (0b011011001, 9, 1280),
        (0b011011010, 9, 1344), (0b011011011, 9, 1408), (0b010011000, 9, 1472), (0b010011001, 9, 1536),
        (0b010011010, 9, 1600), (0b011000, 6, 1664), (0b010011011, 9, 1728),
    ];

    // Broiler-AI:           Origin=Specification; IP=High; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a black row differs from its T.4 code, e.g. run 0 not decoding from the 10-bit 0000110111 (transcribed ITU table, reproduction licence doubtful)
    // Broiler-Human:        PENDING
    private static readonly (int Code, int Length, int Run)[] BlackCodes =
    [
        (0b0000110111, 10, 0), (0b010, 3, 1), (0b11, 2, 2), (0b10, 2, 3),
        (0b011, 3, 4), (0b0011, 4, 5), (0b0010, 4, 6), (0b00011, 5, 7),
        (0b000101, 6, 8), (0b000100, 6, 9), (0b0000100, 7, 10), (0b0000101, 7, 11),
        (0b0000111, 7, 12), (0b00000100, 8, 13), (0b00000111, 8, 14), (0b000011000, 9, 15),
        (0b0000010111, 10, 16), (0b0000011000, 10, 17), (0b0000001000, 10, 18), (0b00001100111, 11, 19),
        (0b00001101000, 11, 20), (0b00001101100, 11, 21), (0b00000110111, 11, 22), (0b00000101000, 11, 23),
        (0b00000010111, 11, 24), (0b00000011000, 11, 25), (0b000011001010, 12, 26), (0b000011001011, 12, 27),
        (0b000011001100, 12, 28), (0b000011001101, 12, 29), (0b000001101000, 12, 30), (0b000001101001, 12, 31),
        (0b000001101010, 12, 32), (0b000001101011, 12, 33), (0b000011010010, 12, 34), (0b000011010011, 12, 35),
        (0b000011010100, 12, 36), (0b000011010101, 12, 37), (0b000011010110, 12, 38), (0b000011010111, 12, 39),
        (0b000001101100, 12, 40), (0b000001101101, 12, 41), (0b000011011010, 12, 42), (0b000011011011, 12, 43),
        (0b000001010100, 12, 44), (0b000001010101, 12, 45), (0b000001010110, 12, 46), (0b000001010111, 12, 47),
        (0b000001100100, 12, 48), (0b000001100101, 12, 49), (0b000001010010, 12, 50), (0b000001010011, 12, 51),
        (0b000000100100, 12, 52), (0b000000110111, 12, 53), (0b000000111000, 12, 54), (0b000000100111, 12, 55),
        (0b000000101000, 12, 56), (0b000001011000, 12, 57), (0b000001011001, 12, 58), (0b000000101011, 12, 59),
        (0b000000101100, 12, 60), (0b000001011010, 12, 61), (0b000001100110, 12, 62), (0b000001100111, 12, 63),

        (0b0000001111, 10, 64), (0b000011001000, 12, 128), (0b000011001001, 12, 192), (0b000001011011, 12, 256),
        (0b000000110011, 12, 320), (0b000000110100, 12, 384), (0b000000110101, 12, 448),
        (0b0000001101100, 13, 512), (0b0000001101101, 13, 576), (0b0000001001010, 13, 640),
        (0b0000001001011, 13, 704), (0b0000001001100, 13, 768), (0b0000001001101, 13, 832),
        (0b0000001110010, 13, 896), (0b0000001110011, 13, 960), (0b0000001110100, 13, 1024),
        (0b0000001110101, 13, 1088), (0b0000001110110, 13, 1152), (0b0000001110111, 13, 1216),
        (0b0000001010010, 13, 1280), (0b0000001010011, 13, 1344), (0b0000001010100, 13, 1408),
        (0b0000001010101, 13, 1472), (0b0000001011010, 13, 1536), (0b0000001011011, 13, 1600),
        (0b0000001100100, 13, 1664), (0b0000001100101, 13, 1728),
    ];

    /// <summary>The makeup codes above 1728, shared by both colours.</summary>
    // Broiler-AI:           Origin=Specification; IP=High; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: an extended makeup row differs from its T.4 code, e.g. run 2560 not decoding from the 12-bit 000000011111 (transcribed ITU table, reproduction licence doubtful)
    // Broiler-Human:        PENDING
    private static readonly (int Code, int Length, int Run)[] ExtendedCodes =
    [
        (0b00000001000, 11, 1792), (0b00000001100, 11, 1856), (0b00000001101, 11, 1920),
        (0b000000010010, 12, 1984), (0b000000010011, 12, 2048), (0b000000010100, 12, 2112),
        (0b000000010101, 12, 2176), (0b000000010110, 12, 2240), (0b000000010111, 12, 2304),
        (0b000000011100, 12, 2368), (0b000000011101, 12, 2432), (0b000000011110, 12, 2496),
        (0b000000011111, 12, 2560),
    ];

    // Broiler-AI:           Origin=Specification; IP=High; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a mode row differs from the T.4 two-dimensional code table, e.g. pass not decoding from the 4-bit 0001 (transcribed ITU table, reproduction licence doubtful)
    // Broiler-Human:        PENDING
    private static readonly (int Code, int Length, int Mode)[] ModeCodes =
    [
        (0b1, 1, ModeVertical0),
        (0b011, 3, ModeVerticalRight1),
        (0b010, 3, ModeVerticalLeft1),
        (0b001, 3, ModeHorizontal),
        (0b0001, 4, ModePass),
        (0b000011, 6, ModeVerticalRight2),
        (0b000010, 6, ModeVerticalLeft2),
        (0b0000011, 7, ModeVerticalRight3),
        (0b0000010, 7, ModeVerticalLeft3),
        (0b0000001, 7, ModeExtension),
        (EndOfLineCode, EndOfLineLength, ModeEndOfLine),
    ];

    public static IReadOnlyDictionary<int, int> White { get; } = Build(WhiteCodes, ExtendedCodes);

    public static IReadOnlyDictionary<int, int> Black { get; } = Build(BlackCodes, ExtendedCodes);

    public static IReadOnlyDictionary<int, int> Modes { get; } = BuildModes();

    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: two distinct (length, code) pairs with codes under 16 bits map to the same key, so one table entry shadows another
    // Broiler-Human:        PENDING
    public static int Key(int length, int code) => (length << 16) | code;

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=1; Fingerprint=TBF
    // Broiler-Falsified-If: a key present in both the colour table and ExtendedCodes is silently overwritten, leaving one run length undecodable
    // Broiler-Human:        PENDING
    private static Dictionary<int, int> Build(
        (int Code, int Length, int Run)[] codes,
        (int Code, int Length, int Run)[] extended)
    {
        var table = new Dictionary<int, int>(codes.Length + extended.Length);
        foreach ((int code, int length, int run) in codes)
            table[Key(length, code)] = run;
        foreach ((int code, int length, int run) in extended)
            table[Key(length, code)] = run;
        return table;
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=1; Fingerprint=TBF
    // Broiler-Falsified-If: two ModeCodes rows share a key and the later silently replaces the earlier, leaving one mode undecodable
    // Broiler-Human:        PENDING
    private static Dictionary<int, int> BuildModes()
    {
        var table = new Dictionary<int, int>(ModeCodes.Length);
        foreach ((int code, int length, int mode) in ModeCodes)
            table[Key(length, code)] = mode;
        return table;
    }
}
