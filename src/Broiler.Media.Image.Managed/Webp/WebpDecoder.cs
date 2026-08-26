using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Broiler.Media.Image.Managed;

internal static class WebpDecoder
{
    public static bool IsWebp(ReadOnlySpan<byte> data) =>
        data.Length >= 12 &&
        FourCcIs(data[..4], "RIFF") &&
        FourCcIs(data.Slice(8, 4), "WEBP");

    public static ImageBuffer Decode(ReadOnlySpan<byte> data) => DecodeAnimation(data).FirstFrame;

    public static ImageSequence DecodeAnimation(ReadOnlySpan<byte> data)
    {
        WebpData webp = Parse(data);
        if (webp.Frames.Count == 0)
            throw new FormatException("WebP contains no image frames.");
        if (webp.Frames.Count == 1 && webp.Frames[0].X == 0 && webp.Frames[0].Y == 0)
            return ImageSequence.Static(webp.Frames[0].Pixels);

        byte[] canvas = new byte[checked(webp.Width * webp.Height * 4)];
        var frames = new List<ImageFrame>(webp.Frames.Count);
        foreach (WebpFrame frame in webp.Frames)
        {
            if (frame.X < 0 || frame.Y < 0 ||
                frame.X + frame.Pixels.Width > webp.Width ||
                frame.Y + frame.Pixels.Height > webp.Height)
            {
                throw new FormatException("WebP frame region lies outside the canvas.");
            }

            DrawFrame(canvas, webp.Width, frame);
            frames.Add(new ImageFrame(new ImageBuffer(webp.Width, webp.Height, (byte[])canvas.Clone()), frame.DurationMs, 1000));

            if (frame.DisposeToBackground)
                ClearRegion(canvas, webp.Width, frame.X, frame.Y, frame.Pixels.Width, frame.Pixels.Height);
        }

        return new ImageSequence(frames, webp.Width, webp.Height, webp.LoopCount);
    }

    private sealed class WebpData
    {
        public int Width;
        public int Height;
        public int LoopCount = 1;
        public readonly List<WebpFrame> Frames = [];
    }

    private sealed class WebpFrame
    {
        public required ImageBuffer Pixels;
        public int X;
        public int Y;
        public int DurationMs;
        public bool Blend;
        public bool DisposeToBackground;
    }

    private static WebpData Parse(ReadOnlySpan<byte> data)
    {
        if (!IsWebp(data))
            throw new FormatException("Data does not start with a WebP RIFF signature.");

        int declaredSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4, 4)));
        int riffEnd = Math.Min(data.Length, declaredSize + 8);
        var webp = new WebpData();
        int offset = 12;
        while (offset + 8 <= riffEnd)
        {
            ReadOnlySpan<byte> fourCc = data.Slice(offset, 4);
            int size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 4, 4)));
            int payloadOffset = offset + 8;
            int nextOffset = payloadOffset + size + (size & 1);
            if (size < 0 || payloadOffset + size > riffEnd || nextOffset > data.Length)
                throw new FormatException("Truncated WebP RIFF chunk.");

            ReadOnlySpan<byte> payload = data.Slice(payloadOffset, size);
            if (FourCcIs(fourCc, "VP8L"))
            {
                ImageBuffer pixels = WebpLossless.Decode(payload);
                webp.Width = pixels.Width;
                webp.Height = pixels.Height;
                webp.Frames.Add(new WebpFrame { Pixels = pixels, Blend = false });
            }
            else if (FourCcIs(fourCc, "VP8 "))
            {
                ImageBuffer pixels = WebpWicDecoder.Decode(data.Slice(0, riffEnd));
                webp.Width = pixels.Width;
                webp.Height = pixels.Height;
                webp.Frames.Add(new WebpFrame { Pixels = pixels, Blend = false });
            }
            else if (FourCcIs(fourCc, "VP8X"))
            {
                ParseVp8x(payload, webp);
            }
            else if (FourCcIs(fourCc, "ANIM"))
            {
                ParseAnim(payload, webp);
            }
            else if (FourCcIs(fourCc, "ANMF"))
            {
                webp.Frames.Add(ParseAnmf(payload));
            }

            offset = nextOffset;
        }

        return webp;
    }

    private static void ParseVp8x(ReadOnlySpan<byte> payload, WebpData webp)
    {
        if (payload.Length < 10)
            throw new FormatException("Truncated WebP VP8X chunk.");

        webp.Width = ReadUInt24(payload, 4) + 1;
        webp.Height = ReadUInt24(payload, 7) + 1;
    }

    private static void ParseAnim(ReadOnlySpan<byte> payload, WebpData webp)
    {
        if (payload.Length < 6)
            throw new FormatException("Truncated WebP ANIM chunk.");

        webp.LoopCount = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2));
    }

    private static WebpFrame ParseAnmf(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 16)
            throw new FormatException("Truncated WebP ANMF chunk.");

        int x = ReadUInt24(payload, 0) * 2;
        int y = ReadUInt24(payload, 3) * 2;
        int width = ReadUInt24(payload, 6) + 1;
        int height = ReadUInt24(payload, 9) + 1;
        int durationMs = ReadUInt24(payload, 12);
        byte flags = payload[15];

        ImageBuffer? pixels = null;
        int offset = 16;
        while (offset + 8 <= payload.Length)
        {
            ReadOnlySpan<byte> fourCc = payload.Slice(offset, 4);
            int size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(offset + 4, 4)));
            int payloadOffset = offset + 8;
            int nextOffset = payloadOffset + size + (size & 1);
            if (size < 0 || payloadOffset + size > payload.Length || nextOffset > payload.Length)
                throw new FormatException("Truncated WebP ANMF frame data.");

            ReadOnlySpan<byte> chunkPayload = payload.Slice(payloadOffset, size);
            if (FourCcIs(fourCc, "VP8L"))
            {
                pixels = WebpLossless.Decode(chunkPayload);
            }
            else if (FourCcIs(fourCc, "VP8 "))
            {
                pixels = WebpWicDecoder.DecodeVp8Payload(chunkPayload);
            }
            else if (FourCcIs(fourCc, "ALPH"))
            {
                throw new NotSupportedException("This managed WebP codec does not support separate ALPH chunks.");
            }

            offset = nextOffset;
        }

        if (pixels is null)
            throw new FormatException("WebP ANMF chunk is missing VP8L or VP8 frame data.");
        if (pixels.Width != width || pixels.Height != height)
            throw new FormatException("WebP ANMF dimensions do not match the frame dimensions.");

        return new WebpFrame
        {
            Pixels = pixels,
            X = x,
            Y = y,
            DurationMs = durationMs,
            Blend = (flags & 0x02) == 0,
            DisposeToBackground = (flags & 0x01) != 0,
        };
    }

    private static void DrawFrame(byte[] canvas, int canvasWidth, WebpFrame frame)
    {
        byte[] source = frame.Pixels.Rgba;
        for (int y = 0; y < frame.Pixels.Height; y++)
        for (int x = 0; x < frame.Pixels.Width; x++)
        {
            int src = (y * frame.Pixels.Width + x) * 4;
            int dst = ((frame.Y + y) * canvasWidth + frame.X + x) * 4;
            if (!frame.Blend)
            {
                canvas[dst] = source[src];
                canvas[dst + 1] = source[src + 1];
                canvas[dst + 2] = source[src + 2];
                canvas[dst + 3] = source[src + 3];
            }
            else
            {
                OverBlend(canvas, dst, source, src);
            }
        }
    }

    private static void OverBlend(byte[] dst, int d, byte[] src, int s)
    {
        int sa = src[s + 3];
        if (sa == 255)
        {
            dst[d] = src[s];
            dst[d + 1] = src[s + 1];
            dst[d + 2] = src[s + 2];
            dst[d + 3] = 255;
            return;
        }
        if (sa == 0)
            return;

        int da = dst[d + 3];
        int outScaled = (sa * 255) + (da * (255 - sa));
        if (outScaled == 0)
        {
            dst[d] = dst[d + 1] = dst[d + 2] = dst[d + 3] = 0;
            return;
        }

        for (int ch = 0; ch < 3; ch++)
        {
            int num = (src[s + ch] * sa * 255) + (dst[d + ch] * da * (255 - sa));
            dst[d + ch] = (byte)((num + (outScaled / 2)) / outScaled);
        }

        dst[d + 3] = (byte)((outScaled + 127) / 255);
    }

    private static void ClearRegion(byte[] canvas, int canvasWidth, int x, int y, int width, int height)
    {
        for (int row = 0; row < height; row++)
            Array.Clear(canvas, ((y + row) * canvasWidth + x) * 4, width * 4);
    }

    private static int ReadUInt24(ReadOnlySpan<byte> data, int offset)
    {
        if (offset + 3 > data.Length)
            throw new FormatException("Truncated WebP uint24 value.");

        return data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16);
    }

    private static bool FourCcIs(ReadOnlySpan<byte> value, string fourCc) =>
        value.Length >= 4 &&
        value[0] == (byte)fourCc[0] &&
        value[1] == (byte)fourCc[1] &&
        value[2] == (byte)fourCc[2] &&
        value[3] == (byte)fourCc[3];
}
