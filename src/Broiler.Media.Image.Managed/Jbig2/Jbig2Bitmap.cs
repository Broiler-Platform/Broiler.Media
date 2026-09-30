using System;

namespace Broiler.Media.Image.Managed.Jbig2;

/// <summary>
/// One decoded JBIG2 bitmap: a symbol, or a region, one byte per pixel with 1
/// meaning black.
/// </summary>
/// <remarks>
/// A byte per pixel rather than a packed bit, because every use here reads
/// individual pixels — the generic decoder forms a context from ten to sixteen
/// neighbours per pixel, and a text region composites symbols at arbitrary
/// offsets. Packing happens once, on the way out of the filter.
/// </remarks>
// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=7; Fingerprint=TBF
// Broiler-Falsified-If: Blank with a width and height whose product exceeds int.MaxValue returns a bitmap whose pixel array is shorter than Width x Height instead of throwing
// Broiler-Human:        PENDING
public sealed class Jbig2Bitmap
{
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a pixel array shorter than width x height is accepted, so At() inside the declared extent throws IndexOutOfRangeException instead of the mismatch being refused at construction
    // Broiler-Human:        PENDING
    public Jbig2Bitmap(int width, int height, byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Row-major, one byte per pixel.</summary>
    public byte[] Pixels { get; }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=7; Fingerprint=TBF
    // Broiler-Falsified-If: a width and height whose product exceeds int.MaxValue, such as 65536 x 65536, allocate a wrapped-around pixel array instead of raising OverflowException
    // Broiler-Human:        PENDING
    public static Jbig2Bitmap Blank(int width, int height, byte value)
    {
        var pixels = new byte[width * height];
        if (value != 0)
            Array.Fill(pixels, value);

        return new Jbig2Bitmap(width, height, pixels);
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a coordinate with x equal to Width, or y equal to -1, returns a pixel of the bitmap instead of 0
    // Broiler-Human:        PENDING
    public byte At(int x, int y) =>
        x >= 0 && x < Width && y >= 0 && y < Height ? Pixels[(y * Width) + x] : (byte)0;
}

/// <summary>How one segment's decode ended.</summary>
// Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=TBF
// Broiler-Human:        PENDING
public enum Jbig2DecodeOutcome
{
    /// <summary>A bitmap, or a set of them.</summary>
    Decoded,

    /// <summary>
    /// The segment is well formed and states something outside this build's
    /// subset. The message names the construct met, and the caller reports it as
    /// a refusal rather than a fault in the file.
    /// </summary>
    Unsupported,

    /// <summary>The segment contradicts itself or the data it points at.</summary>
    Malformed,

    /// <summary>The segment asks for more decoded bytes than the read may spend.</summary>
    TooLarge,
}
