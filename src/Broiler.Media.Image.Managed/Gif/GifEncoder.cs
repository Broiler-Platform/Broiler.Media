using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Broiler.Media.Image.Managed;

internal static class GifEncoder
{
    private const int TransparentIndex = 0;

    public static byte[] Encode(ImageBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return EncodeAnimation(ImageSequence.Static(buffer));
    }

    public static byte[] EncodeAnimation(ImageSequence sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        GifPalette palette = GifPalette.Build(sequence);
        int colorTableSize = palette.ColorTable.Length / 3;
        int tableSizeCode = Log2(colorTableSize) - 1;
        int lzwMinimumCodeSize = Math.Max(2, Log2(colorTableSize));

        using var output = new MemoryStream();
        WriteAscii(output, "GIF89a");
        WriteUInt16(output, sequence.Width);
        WriteUInt16(output, sequence.Height);
        output.WriteByte((byte)(0x80 | ((Math.Max(1, lzwMinimumCodeSize) - 1) << 4) | tableSizeCode));
        output.WriteByte(0);
        output.WriteByte(0);
        output.Write(palette.ColorTable);

        if (sequence.IsAnimated)
            WriteLoopExtension(output, sequence.LoopCount);

        for (int i = 0; i < sequence.Frames.Count; i++)
        {
            ImageFrame frame = sequence.Frames[i];
            if (frame.Pixels.Width != sequence.Width || frame.Pixels.Height != sequence.Height)
            {
                throw new ArgumentException(
                    $"GIF frame {i} is {frame.Pixels.Width}x{frame.Pixels.Height}, expected the canvas size {sequence.Width}x{sequence.Height}.",
                    nameof(sequence));
            }

            WriteGraphicControl(output, DelayHundredths(frame), palette.HasTransparency);
            WriteImageDescriptor(output, sequence.Width, sequence.Height);
            output.WriteByte((byte)lzwMinimumCodeSize);
            byte[] indices = palette.Map(frame.Pixels);
            byte[] lzw = EncodeLiteralLzw(indices, lzwMinimumCodeSize);
            WriteSubBlocks(output, lzw);
        }

        output.WriteByte(0x3B);
        return output.ToArray();
    }

    private sealed class GifPalette
    {
        private readonly Dictionary<int, byte>? _exactMap;
        private readonly bool _useCube;
        private readonly int _cubeOffset;

        private GifPalette(
            byte[] colorTable,
            bool hasTransparency,
            Dictionary<int, byte>? exactMap,
            bool useCube,
            int cubeOffset)
        {
            ColorTable = colorTable;
            HasTransparency = hasTransparency;
            _exactMap = exactMap;
            _useCube = useCube;
            _cubeOffset = cubeOffset;
        }

        public byte[] ColorTable { get; }

        public bool HasTransparency { get; }

        public static GifPalette Build(ImageSequence sequence)
        {
            bool hasTransparency = false;
            var colors = new Dictionary<int, byte>();
            foreach (ImageFrame frame in sequence.Frames)
            {
                byte[] rgba = frame.Pixels.Rgba;
                for (int i = 0; i < rgba.Length; i += 4)
                {
                    if (rgba[i + 3] < 128)
                    {
                        hasTransparency = true;
                        continue;
                    }

                    int color = (rgba[i] << 16) | (rgba[i + 1] << 8) | rgba[i + 2];
                    if (!colors.ContainsKey(color))
                        colors[color] = 0;
                }
            }

            int capacity = hasTransparency ? 255 : 256;
            if (colors.Count <= capacity)
                return BuildExact(colors.Keys, hasTransparency);

            return BuildCube(hasTransparency);
        }

        public byte[] Map(ImageBuffer buffer)
        {
            byte[] indices = new byte[checked(buffer.Width * buffer.Height)];
            int output = 0;
            for (int i = 0; i < buffer.Rgba.Length; i += 4)
            {
                byte a = buffer.Rgba[i + 3];
                if (HasTransparency && a < 128)
                {
                    indices[output++] = TransparentIndex;
                    continue;
                }

                int color = (buffer.Rgba[i] << 16) | (buffer.Rgba[i + 1] << 8) | buffer.Rgba[i + 2];
                indices[output++] = _useCube
                    ? (byte)(_cubeOffset + CubeIndex(buffer.Rgba[i], buffer.Rgba[i + 1], buffer.Rgba[i + 2]))
                    : _exactMap![color];
            }

            return indices;
        }

        private static GifPalette BuildExact(IEnumerable<int> colors, bool hasTransparency)
        {
            var palette = new List<byte>();
            var map = new Dictionary<int, byte>();
            int nextIndex = 0;
            if (hasTransparency)
            {
                palette.Add(0);
                palette.Add(0);
                palette.Add(0);
                nextIndex = 1;
            }

            foreach (int color in colors)
            {
                map[color] = checked((byte)nextIndex++);
                palette.Add((byte)(color >> 16));
                palette.Add((byte)(color >> 8));
                palette.Add((byte)color);
            }

            return new GifPalette(PadColorTable(palette), hasTransparency, map, useCube: false, cubeOffset: 0);
        }

        private static GifPalette BuildCube(bool hasTransparency)
        {
            var palette = new List<byte>();
            int offset = hasTransparency ? 1 : 0;
            if (hasTransparency)
            {
                palette.Add(0);
                palette.Add(0);
                palette.Add(0);
            }

            for (int r = 0; r < 6; r++)
            for (int g = 0; g < 6; g++)
            for (int b = 0; b < 6; b++)
            {
                palette.Add((byte)(r * 255 / 5));
                palette.Add((byte)(g * 255 / 5));
                palette.Add((byte)(b * 255 / 5));
            }

            return new GifPalette(PadColorTable(palette), hasTransparency, exactMap: null, useCube: true, offset);
        }

        private static byte[] PadColorTable(List<byte> palette)
        {
            int entries = Math.Max(2, (palette.Count + 2) / 3);
            int paddedEntries = 1;
            while (paddedEntries < entries)
                paddedEntries <<= 1;

            byte[] table = new byte[checked(paddedEntries * 3)];
            palette.CopyTo(table);
            return table;
        }

        private static int CubeIndex(byte r, byte g, byte b) =>
            Quantize6(r) * 36 + Quantize6(g) * 6 + Quantize6(b);

        private static int Quantize6(byte value) => (value * 5 + 127) / 255;
    }

    private static byte[] EncodeLiteralLzw(ReadOnlySpan<byte> indices, int minimumCodeSize)
    {
        int clearCode = 1 << minimumCodeSize;
        int endCode = clearCode + 1;
        int codeSize = minimumCodeSize + 1;

        var writer = new BitWriter();
        writer.Write(clearCode, codeSize);
        foreach (byte index in indices)
        {
            writer.Write(index, codeSize);
            writer.Write(clearCode, codeSize);
        }

        writer.Write(endCode, codeSize);
        return writer.ToArray();
    }

    private static int DelayHundredths(ImageFrame frame)
    {
        double hundredths = Math.Round(frame.Duration.TotalSeconds * 100, MidpointRounding.AwayFromZero);
        return (int)Math.Clamp(hundredths, 0, ushort.MaxValue);
    }

    private static void WriteGraphicControl(Stream output, int delayHundredths, bool hasTransparency)
    {
        output.WriteByte(0x21);
        output.WriteByte(0xF9);
        output.WriteByte(4);
        output.WriteByte((byte)(hasTransparency ? 0x01 : 0x00));
        WriteUInt16(output, delayHundredths);
        output.WriteByte(hasTransparency ? (byte)TransparentIndex : (byte)0);
        output.WriteByte(0);
    }

    private static void WriteImageDescriptor(Stream output, int width, int height)
    {
        output.WriteByte(0x2C);
        WriteUInt16(output, 0);
        WriteUInt16(output, 0);
        WriteUInt16(output, width);
        WriteUInt16(output, height);
        output.WriteByte(0);
    }

    private static void WriteLoopExtension(Stream output, int loopCount)
    {
        output.WriteByte(0x21);
        output.WriteByte(0xFF);
        output.WriteByte(11);
        WriteAscii(output, "NETSCAPE2.0");
        output.WriteByte(3);
        output.WriteByte(1);
        WriteUInt16(output, Math.Clamp(loopCount, 0, ushort.MaxValue));
        output.WriteByte(0);
    }

    private static void WriteSubBlocks(Stream output, ReadOnlySpan<byte> data)
    {
        int offset = 0;
        while (offset < data.Length)
        {
            int size = Math.Min(255, data.Length - offset);
            output.WriteByte((byte)size);
            output.Write(data.Slice(offset, size));
            offset += size;
        }

        output.WriteByte(0);
    }

    private static void WriteAscii(Stream output, string value)
    {
        foreach (char ch in value)
            output.WriteByte((byte)ch);
    }

    private static void WriteUInt16(Stream output, int value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, checked((ushort)value));
        output.Write(buffer);
    }

    private static int Log2(int value)
    {
        int log = 0;
        while ((1 << log) < value)
            log++;
        return log;
    }

    private sealed class BitWriter
    {
        private readonly MemoryStream _stream = new();
        private int _bitBuffer;
        private int _bitCount;

        public void Write(int code, int bitCount)
        {
            _bitBuffer |= code << _bitCount;
            _bitCount += bitCount;
            while (_bitCount >= 8)
            {
                _stream.WriteByte((byte)_bitBuffer);
                _bitBuffer >>= 8;
                _bitCount -= 8;
            }
        }

        public byte[] ToArray()
        {
            if (_bitCount > 0)
            {
                _stream.WriteByte((byte)_bitBuffer);
                _bitBuffer = 0;
                _bitCount = 0;
            }

            return _stream.ToArray();
        }
    }
}
