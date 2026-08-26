using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Broiler.Media.Image.Managed;

internal static class WebpEncoder
{
    private const byte Vp8xAlphaFlag = 0x10;
    private const byte Vp8xAnimationFlag = 0x02;

    public static byte[] Encode(ImageBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return WriteRiff([new RiffChunk("VP8L", WebpLossless.Encode(buffer))]);
    }

    public static byte[] EncodeAnimation(ImageSequence sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        if (!sequence.IsAnimated)
            return Encode(sequence.FirstFrame);

        var chunks = new List<RiffChunk>
        {
            new("VP8X", BuildVp8x(
                sequence.Width,
                sequence.Height,
                (byte)(Vp8xAnimationFlag | (HasAlpha(sequence) ? Vp8xAlphaFlag : 0)))),
            new("ANIM", BuildAnim(sequence.LoopCount)),
        };

        for (int i = 0; i < sequence.Frames.Count; i++)
        {
            ImageFrame frame = sequence.Frames[i];
            if (frame.Pixels.Width != sequence.Width || frame.Pixels.Height != sequence.Height)
            {
                throw new ArgumentException(
                    $"WebP frame {i} is {frame.Pixels.Width}x{frame.Pixels.Height}, expected the canvas size {sequence.Width}x{sequence.Height}.",
                    nameof(sequence));
            }

            chunks.Add(new RiffChunk("ANMF", BuildAnmf(frame, sequence.Width, sequence.Height)));
        }

        return WriteRiff(chunks);
    }

    private static byte[] BuildVp8x(int width, int height, byte flags)
    {
        byte[] payload = new byte[10];
        payload[0] = flags;
        WriteUInt24(payload.AsSpan(4, 3), width - 1);
        WriteUInt24(payload.AsSpan(7, 3), height - 1);
        return payload;
    }

    private static byte[] BuildAnim(int loopCount)
    {
        byte[] payload = new byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), (ushort)Math.Clamp(loopCount, 0, ushort.MaxValue));
        return payload;
    }

    private static byte[] BuildAnmf(ImageFrame frame, int width, int height)
    {
        using var output = new MemoryStream();
        Span<byte> header = stackalloc byte[16];
        WriteUInt24(header.Slice(6, 3), width - 1);
        WriteUInt24(header.Slice(9, 3), height - 1);
        WriteUInt24(header.Slice(12, 3), DelayMilliseconds(frame));
        header[15] = 0x02; // do not blend; every encoded frame covers the whole canvas.
        output.Write(header);
        WriteChunk(output, "VP8L", WebpLossless.Encode(frame.Pixels));
        return output.ToArray();
    }

    private static byte[] WriteRiff(IReadOnlyList<RiffChunk> chunks)
    {
        using var body = new MemoryStream();
        WriteAscii(body, "WEBP");
        foreach (RiffChunk chunk in chunks)
            WriteChunk(body, chunk.FourCc, chunk.Payload);

        byte[] bodyBytes = body.ToArray();
        using var output = new MemoryStream();
        WriteAscii(output, "RIFF");
        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size, checked((uint)bodyBytes.Length));
        output.Write(size);
        output.Write(bodyBytes);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, string fourCc, ReadOnlySpan<byte> payload)
    {
        WriteAscii(output, fourCc);
        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size, checked((uint)payload.Length));
        output.Write(size);
        output.Write(payload);
        if ((payload.Length & 1) != 0)
            output.WriteByte(0);
    }

    private static void WriteAscii(Stream output, string value)
    {
        foreach (char ch in value)
            output.WriteByte((byte)ch);
    }

    private static void WriteUInt24(Span<byte> destination, int value)
    {
        if (value < 0 || value > 0xFFFFFF)
            throw new ArgumentOutOfRangeException(nameof(value));

        destination[0] = (byte)value;
        destination[1] = (byte)(value >> 8);
        destination[2] = (byte)(value >> 16);
    }

    private static int DelayMilliseconds(ImageFrame frame)
    {
        double milliseconds = Math.Round(frame.Duration.TotalMilliseconds, MidpointRounding.AwayFromZero);
        return (int)Math.Clamp(milliseconds, 0, 0xFFFFFF);
    }

    private static bool HasAlpha(ImageSequence sequence)
    {
        foreach (ImageFrame frame in sequence.Frames)
        {
            byte[] rgba = frame.Pixels.Rgba;
            for (int i = 3; i < rgba.Length; i += 4)
            {
                if (rgba[i] != 255)
                    return true;
            }
        }

        return false;
    }

    private readonly record struct RiffChunk(string FourCc, byte[] Payload);
}
