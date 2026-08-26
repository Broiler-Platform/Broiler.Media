using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Broiler.Media.Image.Managed.Tests;

/// <summary>
/// Assembles minimal, valid PNG byte streams for arbitrary colour types and bit
/// depths so the decoder can be exercised against data its own encoder never
/// produces (grayscale, palette, 16-bit, tRNS, sub-byte bit depths). Scanlines
/// are supplied pre-packed and written with filter type 0 (None).
/// </summary>
internal static class PngFormatBuilder
{
    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    /// <param name="rows">Each entry is one packed scanline (no filter byte), MSB-first.</param>
    public static byte[] Build(
        int width, int height, byte bitDepth, byte colorType,
        IReadOnlyList<byte[]> rows, byte[]? palette = null, byte[]? trns = null,
        byte interlace = 0)
    {
        using var ms = new MemoryStream();
        ms.Write(Signature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[..4], (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.Slice(4, 4), (uint)height);
        ihdr[8] = bitDepth;
        ihdr[9] = colorType;
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = interlace;
        WriteChunk(ms, "IHDR", ihdr);

        if (palette is not null)
            WriteChunk(ms, "PLTE", palette);
        if (trns is not null)
            WriteChunk(ms, "tRNS", trns);

        // Filter byte (0) in front of every packed scanline, then zlib-compress.
        using var rawStream = new MemoryStream();
        foreach (byte[] row in rows)
        {
            rawStream.WriteByte(0);
            rawStream.Write(row, 0, row.Length);
        }
        rawStream.Position = 0;

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            rawStream.CopyTo(zlib);
        WriteChunk(ms, "IDAT", compressed.ToArray());

        WriteChunk(ms, "IEND", ReadOnlySpan<byte>.Empty);
        return ms.ToArray();
    }

    /// <summary>
    /// Builds an Adam7-<b>interlaced</b> 8-bit RGBA PNG (colour type 6) from a full
    /// RGBA buffer, splitting it into the seven reduced images. Used to exercise the
    /// decoder's de-interlacing.
    /// </summary>
    public static byte[] BuildInterlacedRgba(int width, int height, byte[] rgba)
    {
        ReadOnlySpan<int> xStart = [0, 4, 0, 2, 0, 1, 0];
        ReadOnlySpan<int> yStart = [0, 0, 4, 0, 2, 0, 1];
        ReadOnlySpan<int> xStep = [8, 8, 4, 4, 2, 2, 1];
        ReadOnlySpan<int> yStep = [8, 8, 8, 4, 4, 2, 2];

        using var rawStream = new MemoryStream();
        for (int p = 0; p < 7; p++)
        {
            int pw = (width - xStart[p] + xStep[p] - 1) / xStep[p];
            int ph = (height - yStart[p] + yStep[p] - 1) / yStep[p];
            if (pw <= 0 || ph <= 0)
                continue;
            for (int j = 0; j < ph; j++)
            {
                rawStream.WriteByte(0); // filter: none
                int y = yStart[p] + j * yStep[p];
                for (int i = 0; i < pw; i++)
                {
                    int x = xStart[p] + i * xStep[p];
                    rawStream.Write(rgba, (y * width + x) * 4, 4);
                }
            }
        }

        using var ms = new MemoryStream();
        ms.Write(Signature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[..4], (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.Slice(4, 4), (uint)height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // colour type: RGBA
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 1; // Adam7 interlace
        WriteChunk(ms, "IHDR", ihdr);

        rawStream.Position = 0;
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            rawStream.CopyTo(zlib);
        WriteChunk(ms, "IDAT", compressed.ToArray());

        WriteChunk(ms, "IEND", ReadOnlySpan<byte>.Empty);
        return ms.ToArray();
    }

    /// <summary>One frame for <see cref="BuildApng"/>: an 8-bit RGBA sub-image plus its fcTL fields.</summary>
    public sealed record ApngFrameSpec(
        int Width, int Height, int XOffset, int YOffset,
        int DelayNum, int DelayDen, byte DisposeOp, byte BlendOp, byte[] Rgba);

    /// <summary>
    /// Builds an APNG: a colour-type-6 PNG with an <c>acTL</c> and one
    /// <c>fcTL</c> per frame. The first frame's pixels are stored in <c>IDAT</c>
    /// (so it doubles as the default image); later frames use <c>fdAT</c>.
    /// </summary>
    public static byte[] BuildApng(int canvasWidth, int canvasHeight, int numPlays, IReadOnlyList<ApngFrameSpec> frames)
    {
        using var ms = new MemoryStream();
        ms.Write(Signature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[..4], (uint)canvasWidth);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.Slice(4, 4), (uint)canvasHeight);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // colour type: RGBA
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0; // not interlaced
        WriteChunk(ms, "IHDR", ihdr);

        Span<byte> actl = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(actl[..4], (uint)frames.Count);
        BinaryPrimitives.WriteUInt32BigEndian(actl.Slice(4, 4), (uint)numPlays);
        WriteChunk(ms, "acTL", actl);

        uint seq = 0;
        for (int i = 0; i < frames.Count; i++)
        {
            ApngFrameSpec f = frames[i];

            byte[] fctl = new byte[26];
            BinaryPrimitives.WriteUInt32BigEndian(fctl.AsSpan(0, 4), seq++);
            BinaryPrimitives.WriteUInt32BigEndian(fctl.AsSpan(4, 4), (uint)f.Width);
            BinaryPrimitives.WriteUInt32BigEndian(fctl.AsSpan(8, 4), (uint)f.Height);
            BinaryPrimitives.WriteUInt32BigEndian(fctl.AsSpan(12, 4), (uint)f.XOffset);
            BinaryPrimitives.WriteUInt32BigEndian(fctl.AsSpan(16, 4), (uint)f.YOffset);
            BinaryPrimitives.WriteUInt16BigEndian(fctl.AsSpan(20, 2), (ushort)f.DelayNum);
            BinaryPrimitives.WriteUInt16BigEndian(fctl.AsSpan(22, 2), (ushort)f.DelayDen);
            fctl[24] = f.DisposeOp;
            fctl[25] = f.BlendOp;
            WriteChunk(ms, "fcTL", fctl);

            byte[] imageData = CompressFrame(f.Rgba, f.Width, f.Height);
            if (i == 0)
            {
                WriteChunk(ms, "IDAT", imageData);
            }
            else
            {
                byte[] fdat = new byte[4 + imageData.Length];
                BinaryPrimitives.WriteUInt32BigEndian(fdat.AsSpan(0, 4), seq++);
                imageData.CopyTo(fdat.AsSpan(4));
                WriteChunk(ms, "fdAT", fdat);
            }
        }

        WriteChunk(ms, "IEND", ReadOnlySpan<byte>.Empty);
        return ms.ToArray();
    }

    private static byte[] CompressFrame(byte[] rgba, int width, int height)
    {
        using var raw = new MemoryStream();
        for (int y = 0; y < height; y++)
        {
            raw.WriteByte(0); // filter: none
            raw.Write(rgba, y * width * 4, width * 4);
        }
        raw.Position = 0;

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            raw.CopyTo(zlib);
        return compressed.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> lengthAndType = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(lengthAndType[..4], (uint)data.Length);
        for (int i = 0; i < 4; i++)
            lengthAndType[4 + i] = (byte)type[i];

        uint crc = Crc32.Update(0xFFFFFFFFu, lengthAndType[4..8]);
        crc = Crc32.Update(crc, data) ^ 0xFFFFFFFFu;

        stream.Write(lengthAndType);
        stream.Write(data);

        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }
}
