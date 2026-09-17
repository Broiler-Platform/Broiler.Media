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
public sealed class Jbig2Bitmap
{
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

    public static Jbig2Bitmap Blank(int width, int height, byte value)
    {
        var pixels = new byte[width * height];
        if (value != 0)
            Array.Fill(pixels, value);

        return new Jbig2Bitmap(width, height, pixels);
    }

    public byte At(int x, int y) =>
        x >= 0 && x < Width && y >= 0 && y < Height ? Pixels[(y * Width) + x] : (byte)0;
}

/// <summary>How one segment's decode ended.</summary>
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
