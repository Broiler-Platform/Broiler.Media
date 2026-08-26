using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Broiler.Media.Image.Managed;

internal static class GifDecoder
{
    public static bool IsGif(ReadOnlySpan<byte> data) =>
        data.Length >= 6 &&
        data[0] == (byte)'G' &&
        data[1] == (byte)'I' &&
        data[2] == (byte)'F' &&
        data[3] == (byte)'8' &&
        (data[4] == (byte)'7' || data[4] == (byte)'9') &&
        data[5] == (byte)'a';

    public static ImageBuffer Decode(ReadOnlySpan<byte> data) => DecodeAnimation(data).FirstFrame;

    public static ImageSequence DecodeAnimation(ReadOnlySpan<byte> data)
    {
        GifData gif = Parse(data);
        return Composite(gif);
    }

    private sealed class GifData
    {
        public int Width;
        public int Height;
        public int LoopCount = 1;
        public byte[]? GlobalPalette;
        public readonly List<GifFrame> Frames = [];
    }

    private sealed class GifFrame
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public bool Interlaced;
        public byte[] Palette = [];
        public byte[] Indices = [];
        public int DelayHundredths;
        public int Disposal;
        public bool HasTransparency;
        public byte TransparentIndex;
    }

    private readonly struct GraphicControl(
        int delayHundredths,
        int disposal,
        bool hasTransparency,
        byte transparentIndex)
    {
        public readonly int DelayHundredths = delayHundredths;
        public readonly int Disposal = disposal;
        public readonly bool HasTransparency = hasTransparency;
        public readonly byte TransparentIndex = transparentIndex;

        public static GraphicControl Default => new(0, 0, false, 0);
    }

    private static GifData Parse(ReadOnlySpan<byte> data)
    {
        if (!IsGif(data))
            throw new FormatException("Data does not start with a GIF signature.");
        if (data.Length < 13)
            throw new FormatException("Truncated GIF logical screen descriptor.");

        var gif = new GifData
        {
            Width = ReadUInt16(data, 6),
            Height = ReadUInt16(data, 8),
        };

        if (gif.Width <= 0 || gif.Height <= 0)
            throw new FormatException("GIF image has non-positive dimensions.");

        byte packed = data[10];
        bool hasGlobalPalette = (packed & 0x80) != 0;
        int offset = 13;
        if (hasGlobalPalette)
            gif.GlobalPalette = ReadPalette(data, ref offset, 1 << ((packed & 0x07) + 1));

        GraphicControl control = GraphicControl.Default;
        while (offset < data.Length)
        {
            byte introducer = data[offset++];
            if (introducer == 0x3B)
                break;

            if (introducer == 0x2C)
            {
                gif.Frames.Add(ReadImage(data, ref offset, gif.GlobalPalette, control));
                control = GraphicControl.Default;
                continue;
            }

            if (introducer != 0x21)
                throw new FormatException($"Unknown GIF block introducer 0x{introducer:X2}.");

            if (offset >= data.Length)
                throw new FormatException("Truncated GIF extension block.");

            byte label = data[offset++];
            switch (label)
            {
                case 0xF9:
                    control = ReadGraphicControl(data, ref offset);
                    break;
                case 0xFF:
                    ReadApplicationExtension(data, ref offset, gif);
                    break;
                case 0xFE:
                    SkipSubBlocks(data, ref offset);
                    break;
                case 0x01:
                    SkipFixedExtensionAndSubBlocks(data, ref offset);
                    break;
                default:
                    SkipSubBlocks(data, ref offset);
                    break;
            }
        }

        if (gif.Frames.Count == 0)
            throw new FormatException("GIF contains no image frames.");

        return gif;
    }

    private static GifFrame ReadImage(
        ReadOnlySpan<byte> data,
        ref int offset,
        byte[]? globalPalette,
        GraphicControl control)
    {
        if (offset + 9 > data.Length)
            throw new FormatException("Truncated GIF image descriptor.");

        int x = ReadUInt16(data, offset);
        int y = ReadUInt16(data, offset + 2);
        int width = ReadUInt16(data, offset + 4);
        int height = ReadUInt16(data, offset + 6);
        byte packed = data[offset + 8];
        offset += 9;

        if (width <= 0 || height <= 0)
            throw new FormatException("GIF frame has non-positive dimensions.");

        byte[]? palette = (packed & 0x80) != 0
            ? ReadPalette(data, ref offset, 1 << ((packed & 0x07) + 1))
            : globalPalette;
        if (palette is null)
            throw new FormatException("GIF frame is missing a color table.");

        if (offset >= data.Length)
            throw new FormatException("Truncated GIF image data.");

        int lzwMinimumCodeSize = data[offset++];
        byte[] compressed = ReadSubBlocks(data, ref offset);
        byte[] indices = DecodeLzw(compressed, lzwMinimumCodeSize, checked(width * height));
        if ((packed & 0x40) != 0)
            indices = Deinterlace(indices, width, height);

        return new GifFrame
        {
            X = x,
            Y = y,
            Width = width,
            Height = height,
            Interlaced = (packed & 0x40) != 0,
            Palette = palette,
            Indices = indices,
            DelayHundredths = control.DelayHundredths,
            Disposal = control.Disposal,
            HasTransparency = control.HasTransparency,
            TransparentIndex = control.TransparentIndex,
        };
    }

    private static GraphicControl ReadGraphicControl(ReadOnlySpan<byte> data, ref int offset)
    {
        if (offset >= data.Length)
            throw new FormatException("Truncated GIF graphic control extension.");

        int blockSize = data[offset++];
        if (blockSize != 4 || offset + blockSize >= data.Length)
            throw new FormatException("Invalid GIF graphic control extension.");

        byte packed = data[offset];
        int delay = ReadUInt16(data, offset + 1);
        byte transparentIndex = data[offset + 3];
        offset += blockSize;

        if (data[offset++] != 0)
            throw new FormatException("GIF graphic control extension is missing its terminator.");

        return new GraphicControl(delay, (packed >> 2) & 0x07, (packed & 0x01) != 0, transparentIndex);
    }

    private static void ReadApplicationExtension(ReadOnlySpan<byte> data, ref int offset, GifData gif)
    {
        if (offset >= data.Length)
            throw new FormatException("Truncated GIF application extension.");

        int blockSize = data[offset++];
        if (offset + blockSize > data.Length)
            throw new FormatException("Truncated GIF application identifier.");

        ReadOnlySpan<byte> app = data.Slice(offset, blockSize);
        offset += blockSize;
        byte[] subBlocks = ReadSubBlocks(data, ref offset);

        if (blockSize == 11 &&
            (AsciiEquals(app, "NETSCAPE2.0") || AsciiEquals(app, "ANIMEXTS1.0")) &&
            subBlocks.Length >= 3 &&
            subBlocks[0] == 1)
        {
            gif.LoopCount = BinaryPrimitives.ReadUInt16LittleEndian(subBlocks.AsSpan(1, 2));
        }
    }

    private static ImageSequence Composite(GifData gif)
    {
        byte[] canvas = new byte[checked(gif.Width * gif.Height * 4)];
        var frames = new List<ImageFrame>(gif.Frames.Count);

        foreach (GifFrame frame in gif.Frames)
        {
            if (frame.X < 0 || frame.Y < 0 ||
                frame.X + frame.Width > gif.Width ||
                frame.Y + frame.Height > gif.Height)
            {
                throw new FormatException("GIF frame region lies outside the logical screen.");
            }

            byte[]? saved = frame.Disposal == 3
                ? SnapshotRegion(canvas, gif.Width, frame.X, frame.Y, frame.Width, frame.Height)
                : null;

            DrawFrame(canvas, gif.Width, frame);
            frames.Add(new ImageFrame(
                new ImageBuffer(gif.Width, gif.Height, (byte[])canvas.Clone()),
                frame.DelayHundredths,
                100));

            if (frame.Disposal == 2)
                ClearRegion(canvas, gif.Width, frame.X, frame.Y, frame.Width, frame.Height);
            else if (frame.Disposal == 3 && saved is not null)
                RestoreRegion(canvas, gif.Width, saved, frame.X, frame.Y, frame.Width, frame.Height);
        }

        return new ImageSequence(frames, gif.Width, gif.Height, gif.LoopCount);
    }

    private static void DrawFrame(byte[] canvas, int canvasWidth, GifFrame frame)
    {
        for (int y = 0; y < frame.Height; y++)
        for (int x = 0; x < frame.Width; x++)
        {
            byte index = frame.Indices[y * frame.Width + x];
            if (frame.HasTransparency && index == frame.TransparentIndex)
                continue;

            int paletteOffset = index * 3;
            if (paletteOffset + 2 >= frame.Palette.Length)
                throw new FormatException("GIF palette index out of range.");

            int dst = ((frame.Y + y) * canvasWidth + frame.X + x) * 4;
            canvas[dst] = frame.Palette[paletteOffset];
            canvas[dst + 1] = frame.Palette[paletteOffset + 1];
            canvas[dst + 2] = frame.Palette[paletteOffset + 2];
            canvas[dst + 3] = 255;
        }
    }

    private static byte[] DecodeLzw(ReadOnlySpan<byte> data, int minimumCodeSize, int expectedPixels)
    {
        if (minimumCodeSize is < 1 or > 8)
            throw new FormatException($"GIF LZW minimum code size {minimumCodeSize} is invalid.");

        int clearCode = 1 << minimumCodeSize;
        int endCode = clearCode + 1;
        int codeSize = minimumCodeSize + 1;
        int nextCode = endCode + 1;
        int oldCode = -1;
        byte first = 0;

        Span<ushort> prefix = stackalloc ushort[4096];
        Span<byte> suffix = stackalloc byte[4096];
        Span<byte> stack = stackalloc byte[4097];
        for (int i = 0; i < clearCode; i++)
            suffix[i] = (byte)i;

        byte[] output = new byte[expectedPixels];
        int outputOffset = 0;
        var reader = new BitReader(data);

        while (outputOffset < expectedPixels)
        {
            int code = reader.Read(codeSize);
            if (code < 0)
                break;

            if (code == clearCode)
            {
                codeSize = minimumCodeSize + 1;
                nextCode = endCode + 1;
                oldCode = -1;
                continue;
            }

            if (code == endCode)
                break;

            if (oldCode < 0)
            {
                if (code >= clearCode)
                    throw new FormatException("GIF LZW stream starts with an invalid code.");

                output[outputOffset++] = (byte)code;
                first = (byte)code;
                oldCode = code;
                continue;
            }

            int inCode = code;
            int stackOffset = 0;
            if (code == nextCode)
            {
                stack[stackOffset++] = first;
                code = oldCode;
            }
            else if (code > nextCode)
            {
                throw new FormatException("GIF LZW stream contains an invalid code.");
            }

            while (code >= clearCode)
            {
                if (code >= nextCode || stackOffset >= stack.Length)
                    throw new FormatException("GIF LZW stream contains an invalid dictionary reference.");

                stack[stackOffset++] = suffix[code];
                code = prefix[code];
            }

            first = suffix[code];
            stack[stackOffset++] = first;

            while (stackOffset > 0 && outputOffset < expectedPixels)
                output[outputOffset++] = stack[--stackOffset];

            if (nextCode < 4096)
            {
                prefix[nextCode] = (ushort)oldCode;
                suffix[nextCode] = first;
                nextCode++;
                if (nextCode == (1 << codeSize) && codeSize < 12)
                    codeSize++;
            }

            oldCode = inCode;
        }

        if (outputOffset != expectedPixels)
            throw new FormatException("GIF image data ended before all pixels were decoded.");

        return output;
    }

    private static byte[] Deinterlace(byte[] source, int width, int height)
    {
        byte[] dest = new byte[source.Length];
        int sourceRow = 0;

        ReadOnlySpan<int> starts = [0, 4, 2, 1];
        ReadOnlySpan<int> steps = [8, 8, 4, 2];
        for (int pass = 0; pass < 4; pass++)
        {
            for (int y = starts[pass]; y < height; y += steps[pass])
            {
                Array.Copy(source, sourceRow * width, dest, y * width, width);
                sourceRow++;
            }
        }

        return dest;
    }

    private static byte[] ReadPalette(ReadOnlySpan<byte> data, ref int offset, int entries)
    {
        int byteCount = checked(entries * 3);
        if (offset + byteCount > data.Length)
            throw new FormatException("Truncated GIF color table.");

        byte[] palette = data.Slice(offset, byteCount).ToArray();
        offset += byteCount;
        return palette;
    }

    private static byte[] ReadSubBlocks(ReadOnlySpan<byte> data, ref int offset)
    {
        using var output = new MemoryStream();
        while (true)
        {
            if (offset >= data.Length)
                throw new FormatException("Truncated GIF data sub-blocks.");

            int size = data[offset++];
            if (size == 0)
                break;
            if (offset + size > data.Length)
                throw new FormatException("Truncated GIF data sub-block.");

            output.Write(data.Slice(offset, size));
            offset += size;
        }

        return output.ToArray();
    }

    private static void SkipSubBlocks(ReadOnlySpan<byte> data, ref int offset) =>
        _ = ReadSubBlocks(data, ref offset);

    private static void SkipFixedExtensionAndSubBlocks(ReadOnlySpan<byte> data, ref int offset)
    {
        if (offset >= data.Length)
            throw new FormatException("Truncated GIF extension block.");

        int blockSize = data[offset++];
        if (offset + blockSize > data.Length)
            throw new FormatException("Truncated GIF extension block.");

        offset += blockSize;
        SkipSubBlocks(data, ref offset);
    }

    private static int ReadUInt16(ReadOnlySpan<byte> data, int offset)
    {
        if (offset + 2 > data.Length)
            throw new FormatException("Truncated GIF data.");

        return BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
    }

    private static bool AsciiEquals(ReadOnlySpan<byte> data, string value)
    {
        if (data.Length != value.Length)
            return false;

        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] != (byte)value[i])
                return false;
        }

        return true;
    }

    private static byte[] SnapshotRegion(byte[] canvas, int canvasWidth, int x, int y, int width, int height)
    {
        byte[] snapshot = new byte[checked(width * height * 4)];
        for (int row = 0; row < height; row++)
            Array.Copy(canvas, ((y + row) * canvasWidth + x) * 4, snapshot, row * width * 4, width * 4);
        return snapshot;
    }

    private static void RestoreRegion(byte[] canvas, int canvasWidth, byte[] snapshot, int x, int y, int width, int height)
    {
        for (int row = 0; row < height; row++)
            Array.Copy(snapshot, row * width * 4, canvas, ((y + row) * canvasWidth + x) * 4, width * 4);
    }

    private static void ClearRegion(byte[] canvas, int canvasWidth, int x, int y, int width, int height)
    {
        for (int row = 0; row < height; row++)
            Array.Clear(canvas, ((y + row) * canvasWidth + x) * 4, width * 4);
    }

    private ref struct BitReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _bitOffset;

        public BitReader(ReadOnlySpan<byte> data) => _data = data;

        public int Read(int bitCount)
        {
            if (_bitOffset + bitCount > _data.Length * 8)
                return -1;

            int value = 0;
            for (int i = 0; i < bitCount; i++)
            {
                int bit = (_data[(_bitOffset + i) >> 3] >> ((_bitOffset + i) & 7)) & 1;
                value |= bit << i;
            }

            _bitOffset += bitCount;
            return value;
        }
    }
}

