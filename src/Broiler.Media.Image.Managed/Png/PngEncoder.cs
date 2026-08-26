using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Broiler.Media.Image.Managed;

/// <summary>
/// Pure-managed PNG encoder. Writes a non-interlaced, 8-bit RGBA (colour type 6)
/// PNG, or an APNG when given a multi-frame sequence. Scanlines are filtered with
/// the standard minimum-sum-of-absolute-values heuristic before being
/// DEFLATE-compressed by the in-box <see cref="ZLibStream"/>.
/// </summary>
internal static class PngEncoder
{
    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    public static byte[] Encode(ImageBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        byte[] compressed = CompressScanlines(buffer.Rgba, buffer.Width, buffer.Height);

        using var ms = new MemoryStream();
        ms.Write(Signature);
        WriteIhdr(ms, buffer.Width, buffer.Height);
        WriteChunk(ms, "IDAT", compressed);
        WriteChunk(ms, "IEND", ReadOnlySpan<byte>.Empty);

        return ms.ToArray();
    }

    /// <summary>
    /// Encodes a frame sequence as an APNG. Every frame replaces the whole canvas
    /// (full-size, <c>SOURCE</c> blend, <c>NONE</c> dispose), so each frame is
    /// self-contained. The first frame's pixels are stored in <c>IDAT</c> and so
    /// double as the still image seen by non-APNG decoders.
    /// </summary>
    public static byte[] EncodeAnimation(ImageSequence sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        int width = sequence.Width;
        int height = sequence.Height;
        IReadOnlyList<ImageFrame> frames = sequence.Frames;

        using var ms = new MemoryStream();
        ms.Write(Signature);
        WriteIhdr(ms, width, height);

        Span<byte> actl = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(actl[..4], (uint)frames.Count);
        BinaryPrimitives.WriteUInt32BigEndian(actl.Slice(4, 4), (uint)sequence.LoopCount);
        WriteChunk(ms, "acTL", actl);

        uint seq = 0;
        for (int i = 0; i < frames.Count; i++)
        {
            ImageFrame frame = frames[i];
            ImageBuffer pixels = frame.Pixels;
            if (pixels.Width != width || pixels.Height != height)
                throw new ArgumentException(
                    $"APNG frame {i} is {pixels.Width}x{pixels.Height}, expected the canvas size {width}x{height}.",
                    nameof(sequence));

            WriteChunk(ms, "fcTL", BuildFctl(seq++, width, height, frame.DelayNumerator, frame.DelayDenominator));

            byte[] compressed = CompressScanlines(pixels.Rgba, width, height);
            if (i == 0)
            {
                WriteChunk(ms, "IDAT", compressed);
            }
            else
            {
                byte[] fdat = new byte[4 + compressed.Length];
                BinaryPrimitives.WriteUInt32BigEndian(fdat.AsSpan(0, 4), seq++);
                compressed.CopyTo(fdat.AsSpan(4));
                WriteChunk(ms, "fdAT", fdat);
            }
        }

        WriteChunk(ms, "IEND", ReadOnlySpan<byte>.Empty);
        return ms.ToArray();
    }

    private static void WriteIhdr(Stream s, int width, int height)
    {
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[..4], (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.Slice(4, 4), (uint)height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // colour type: RGBA
        ihdr[10] = 0; // compression
        ihdr[11] = 0; // filter method
        ihdr[12] = 0; // interlace: none
        WriteChunk(s, "IHDR", ihdr);
    }

    private static byte[] BuildFctl(uint sequenceNumber, int width, int height, int delayNum, int delayDen)
    {
        byte[] fctl = new byte[26];
        BinaryPrimitives.WriteUInt32BigEndian(fctl.AsSpan(0, 4), sequenceNumber);
        BinaryPrimitives.WriteUInt32BigEndian(fctl.AsSpan(4, 4), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(fctl.AsSpan(8, 4), (uint)height);
        // x_offset and y_offset (12, 16) stay 0 — frames cover the whole canvas.
        BinaryPrimitives.WriteUInt16BigEndian(fctl.AsSpan(20, 2), (ushort)Math.Clamp(delayNum, 0, ushort.MaxValue));
        BinaryPrimitives.WriteUInt16BigEndian(fctl.AsSpan(22, 2), (ushort)Math.Clamp(delayDen, 0, ushort.MaxValue));
        fctl[24] = 0; // dispose_op: NONE
        fctl[25] = 0; // blend_op: SOURCE
        return fctl;
    }

    private static byte[] CompressScanlines(byte[] rgba, int width, int height)
    {
        const int bpp = 4;
        int stride = width * bpp;

        byte[] prev = new byte[stride];
        byte[] cur = new byte[stride];
        // Scratch buffers, one per filter type (0..4); index 0 reuses cur directly.
        byte[][] candidates = new byte[5][];
        for (int f = 1; f < 5; f++)
            candidates[f] = new byte[stride];

        using var raw = new MemoryStream();

        for (int y = 0; y < height; y++)
        {
            Array.Copy(rgba, y * stride, cur, 0, stride);

            int bestFilter = 0;
            long bestScore = long.MaxValue;
            byte[] bestLine = cur;

            for (int filter = 0; filter < 5; filter++)
            {
                byte[] line = filter == 0 ? cur : candidates[filter];
                if (filter != 0)
                    ApplyFilter(filter, cur, prev, line, bpp, stride);

                long score = Score(line, stride);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestFilter = filter;
                    bestLine = line;
                }
            }

            raw.WriteByte((byte)bestFilter);
            raw.Write(bestLine, 0, stride);

            // Swap cur into prev for the next row without reallocating.
            (prev, cur) = (cur, prev);
        }

        raw.Position = 0;
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            raw.CopyTo(zlib);
        return output.ToArray();
    }

    private static void ApplyFilter(int filter, byte[] cur, byte[] prev, byte[] dst, int bpp, int stride)
    {
        for (int x = 0; x < stride; x++)
        {
            int a = x >= bpp ? cur[x - bpp] : 0; // left
            int b = prev[x];                     // up
            int c = x >= bpp ? prev[x - bpp] : 0; // upper-left
            int v = cur[x];

            int filtered = filter switch
            {
                1 => v - a,
                2 => v - b,
                3 => v - ((a + b) >> 1),
                4 => v - Paeth(a, b, c),
                _ => v,
            };
            dst[x] = (byte)filtered;
        }
    }

    /// <summary>Sum of absolute signed-byte values — the standard PNG filter-selection heuristic.</summary>
    private static long Score(byte[] line, int stride)
    {
        long sum = 0;
        for (int x = 0; x < stride; x++)
        {
            int v = line[x];
            sum += v < 128 ? v : 256 - v;
        }
        return sum;
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        return pb <= pc ? b : c;
    }

    private static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> lengthAndType = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(lengthAndType[..4], (uint)data.Length);
        lengthAndType[4] = (byte)type[0];
        lengthAndType[5] = (byte)type[1];
        lengthAndType[6] = (byte)type[2];
        lengthAndType[7] = (byte)type[3];

        // CRC covers the type bytes followed by the chunk data.
        uint crc = Crc32.Update(0xFFFFFFFFu, lengthAndType[4..8]);
        crc = Crc32.Update(crc, data) ^ 0xFFFFFFFFu;

        stream.Write(lengthAndType);
        stream.Write(data);

        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }
}
