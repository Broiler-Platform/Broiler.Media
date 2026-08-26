using System;
using System.Collections.Generic;

namespace Broiler.Media.Image.Managed;

internal static class WebpLossless
{
    private const int LiteralAlphabetSize = 256;
    private const int LengthCodeCount = 24;
    private const int DistanceAlphabetSize = 40;

    public static ImageBuffer Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length == 0 || payload[0] != 0x2F)
            throw new FormatException("WebP lossless payload is missing its VP8L signature.");

        var reader = new WebpBitReader(payload[1..]);
        int width = reader.ReadBits(14) + 1;
        int height = reader.ReadBits(14) + 1;
        _ = reader.ReadBits(1); // alpha_is_used is only a hint.
        int version = reader.ReadBits(3);
        if (version != 0)
            throw new FormatException($"Unsupported WebP lossless version {version}.");

        int codedWidth = width;
        int codedHeight = height;
        var transforms = new List<WebpTransform>();
        while (reader.ReadBits(1) != 0)
        {
            int transformType = reader.ReadBits(2);
            switch (transformType)
            {
                case 0:
                {
                    int sizeBits = reader.ReadBits(3) + 2;
                    int transformWidth = DivRoundUp(codedWidth, 1 << sizeBits);
                    int transformHeight = DivRoundUp(codedHeight, 1 << sizeBits);
                    transforms.Add(new PredictorTransform(
                        sizeBits,
                        transformWidth,
                        DecodeImageData(ref reader, transformWidth, transformHeight, allowMetaPrefix: false)));
                    break;
                }
                case 1:
                {
                    int sizeBits = reader.ReadBits(3) + 2;
                    int transformWidth = DivRoundUp(codedWidth, 1 << sizeBits);
                    int transformHeight = DivRoundUp(codedHeight, 1 << sizeBits);
                    transforms.Add(new ColorTransform(
                        sizeBits,
                        transformWidth,
                        DecodeImageData(ref reader, transformWidth, transformHeight, allowMetaPrefix: false)));
                    break;
                }
                case 2:
                    transforms.Add(SubtractGreenTransform.Instance);
                    break;
                case 3:
                {
                    int colorTableSize = reader.ReadBits(8) + 1;
                    uint[] colorTable = DecodeImageData(ref reader, colorTableSize, 1, allowMetaPrefix: false);
                    int widthBits = colorTableSize <= 2 ? 3 : colorTableSize <= 4 ? 2 : colorTableSize <= 16 ? 1 : 0;
                    transforms.Add(new ColorIndexingTransform(colorTable, widthBits, codedWidth));
                    codedWidth = DivRoundUp(codedWidth, 1 << widthBits);
                    break;
                }
                default:
                    throw new FormatException("Unknown WebP lossless transform.");
            }
        }

        uint[] argb = DecodeImageData(ref reader, codedWidth, codedHeight, allowMetaPrefix: true);
        for (int i = transforms.Count - 1; i >= 0; i--)
            argb = transforms[i].Apply(argb, ref codedWidth, codedHeight);

        if (codedWidth != width || codedHeight != height)
            throw new FormatException("WebP lossless transforms did not restore the expected image dimensions.");

        byte[] rgba = new byte[checked(width * height * 4)];
        for (int i = 0; i < argb.Length; i++)
        {
            uint pixel = argb[i];
            int dst = i * 4;
            rgba[dst] = (byte)(pixel >> 16);
            rgba[dst + 1] = (byte)(pixel >> 8);
            rgba[dst + 2] = (byte)pixel;
            rgba[dst + 3] = (byte)(pixel >> 24);
        }

        return new ImageBuffer(width, height, rgba);
    }

    public static byte[] Encode(ImageBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Width > 16384 || buffer.Height > 16384)
            throw new NotSupportedException("WebP lossless images are limited to 16384x16384 pixels.");

        using var output = new System.IO.MemoryStream();
        output.WriteByte(0x2F);
        var writer = new WebpBitWriter(output);
        writer.WriteBits(buffer.Width - 1, 14);
        writer.WriteBits(buffer.Height - 1, 14);
        writer.WriteBits(HasAlpha(buffer) ? 1 : 0, 1);
        writer.WriteBits(0, 3);
        writer.WriteBits(0, 1); // no transforms
        writer.WriteBits(0, 1); // no color cache
        writer.WriteBits(0, 1); // single meta-prefix group

        WriteFullLiteralCode(writer, LiteralAlphabetSize + LengthCodeCount);
        WriteFullLiteralCode(writer, LiteralAlphabetSize);
        WriteFullLiteralCode(writer, LiteralAlphabetSize);
        WriteFullLiteralCode(writer, LiteralAlphabetSize);
        WriteSimpleSingleSymbolCode(writer, 0);

        byte[] rgba = buffer.Rgba;
        for (int i = 0; i < rgba.Length; i += 4)
        {
            WriteEightBitLiteral(writer, rgba[i + 1]); // green
            WriteEightBitLiteral(writer, rgba[i]);     // red
            WriteEightBitLiteral(writer, rgba[i + 2]); // blue
            WriteEightBitLiteral(writer, rgba[i + 3]); // alpha
        }

        writer.Flush();
        return output.ToArray();
    }

    private static uint[] DecodeImageData(
        ref WebpBitReader reader,
        int width,
        int height,
        bool allowMetaPrefix)
    {
        int colorCacheBits = 0;
        int colorCacheSize = 0;
        if (reader.ReadBits(1) != 0)
        {
            colorCacheBits = reader.ReadBits(4);
            if (colorCacheBits is < 1 or > 11)
                throw new FormatException("Invalid WebP color cache size.");

            colorCacheSize = 1 << colorCacheBits;
        }

        if (allowMetaPrefix && reader.ReadBits(1) != 0)
            throw new NotSupportedException("This managed WebP codec supports a single VP8L meta-prefix group.");

        var group = new PrefixCodeGroup(
            WebpHuffmanCode.Read(ref reader, LiteralAlphabetSize + LengthCodeCount + colorCacheSize),
            WebpHuffmanCode.Read(ref reader, LiteralAlphabetSize),
            WebpHuffmanCode.Read(ref reader, LiteralAlphabetSize),
            WebpHuffmanCode.Read(ref reader, LiteralAlphabetSize),
            WebpHuffmanCode.Read(ref reader, DistanceAlphabetSize));

        uint[] pixels = new uint[checked(width * height)];
        uint[]? colorCache = colorCacheSize > 0 ? new uint[colorCacheSize] : null;
        int offset = 0;
        while (offset < pixels.Length)
        {
            int symbol = group.Green.ReadSymbol(ref reader);
            if (symbol < 256)
            {
                byte green = (byte)symbol;
                byte red = (byte)group.Red.ReadSymbol(ref reader);
                byte blue = (byte)group.Blue.ReadSymbol(ref reader);
                byte alpha = (byte)group.Alpha.ReadSymbol(ref reader);
                uint pixel = Pack(alpha, red, green, blue);
                pixels[offset++] = pixel;
                InsertColorCache(colorCache, colorCacheBits, pixel);
                continue;
            }

            if (symbol < 256 + LengthCodeCount)
            {
                int length = PrefixCodeToValue(symbol - 256, ref reader);
                int distanceSymbol = group.Distance.ReadSymbol(ref reader);
                int distance = PrefixCodeToValue(distanceSymbol, ref reader);
                distance = DistanceToPlaneCode(distance, width);
                if (distance > offset)
                    throw new FormatException("WebP backward reference points before the image data.");
                if (offset + length > pixels.Length)
                    throw new FormatException("WebP backward reference exceeds the image data.");

                for (int i = 0; i < length; i++)
                {
                    uint pixel = pixels[offset - distance];
                    pixels[offset++] = pixel;
                    InsertColorCache(colorCache, colorCacheBits, pixel);
                }
                continue;
            }

            if (colorCache is null)
                throw new FormatException("WebP stream used a color cache code without a color cache.");

            int cacheIndex = symbol - 256 - LengthCodeCount;
            if ((uint)cacheIndex >= (uint)colorCache.Length)
                throw new FormatException("WebP color cache index is out of range.");

            uint cached = colorCache[cacheIndex];
            pixels[offset++] = cached;
            InsertColorCache(colorCache, colorCacheBits, cached);
        }

        return pixels;
    }

    private static int PrefixCodeToValue(int prefixCode, ref WebpBitReader reader)
    {
        if (prefixCode < 4)
            return prefixCode + 1;

        int extraBits = (prefixCode - 2) >> 1;
        int offset = (2 + (prefixCode & 1)) << extraBits;
        return offset + reader.ReadBits(extraBits) + 1;
    }

    private static int DistanceToPlaneCode(int distanceCode, int width)
    {
        if (distanceCode > 120)
            return distanceCode - 120;

        ReadOnlySpan<sbyte> offsets =
        [
            0, 1, 1, 0, 1, 1, -1, 1, 0, 2, 2, 0, 1, 2, -1, 2,
            2, 1, -2, 1, 2, 2, -2, 2, 0, 3, 3, 0, 1, 3, -1, 3,
            3, 1, -3, 1, 2, 3, -2, 3, 3, 2, -3, 2, 0, 4, 4, 0,
            1, 4, -1, 4, 4, 1, -4, 1, 3, 3, -3, 3, 2, 4, -2, 4,
            4, 2, -4, 2, 0, 5, 3, 4, -3, 4, 4, 3, -4, 3, 5, 0,
            1, 5, -1, 5, 5, 1, -5, 1, 2, 5, -2, 5, 5, 2, -5, 2,
            4, 4, -4, 4, 3, 5, -3, 5, 5, 3, -5, 3, 0, 6, 6, 0,
            1, 6, -1, 6, 6, 1, -6, 1, 2, 6, -2, 6, 6, 2, -6, 2,
            4, 5, -4, 5, 5, 4, -5, 4, 3, 6, -3, 6, 6, 3, -6, 3,
            0, 7, 7, 0, 1, 7, -1, 7, 5, 5, -5, 5, 7, 1, -7, 1,
            4, 6, -4, 6, 6, 4, -6, 4, 2, 7, -2, 7, 7, 2, -7, 2,
            3, 7, -3, 7, 7, 3, -7, 3, 5, 6, -5, 6, 6, 5, -6, 5,
            8, 0, 4, 7, -4, 7, 7, 4, -7, 4, 8, 1, 8, 2, 6, 6,
            -6, 6, 8, 3, 5, 7, -5, 7, 7, 5, -7, 5, 8, 4, 6, 7,
            -6, 7, 7, 6, -7, 6, 8, 5, 7, 7, -7, 7, 8, 6, 8, 7,
        ];

        int index = (distanceCode - 1) * 2;
        int distance = offsets[index] + (offsets[index + 1] * width);
        return distance < 1 ? 1 : distance;
    }

    private static void WriteFullLiteralCode(WebpBitWriter writer, int alphabetSize)
    {
        writer.WriteBits(0, 1); // normal code
        writer.WriteBits(8, 4); // 12 code-length-code lengths

        ReadOnlySpan<int> lengthsInOrder = [0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 1];
        foreach (int length in lengthsInOrder)
            writer.WriteBits(length, 3);

        writer.WriteBits(0, 1); // read the full alphabet size
        for (int i = 0; i < alphabetSize; i++)
            writer.WriteBits(i < 256 ? 1 : 0, 1); // code-length symbol 8 or 0
    }

    private static void WriteSimpleSingleSymbolCode(WebpBitWriter writer, int symbol)
    {
        writer.WriteBits(1, 1); // simple code
        writer.WriteBits(0, 1); // one symbol
        writer.WriteBits(symbol > 1 ? 1 : 0, 1);
        writer.WriteBits(symbol, symbol > 1 ? 8 : 1);
    }

    private static void WriteEightBitLiteral(WebpBitWriter writer, byte value) =>
        writer.WriteBits(WebpHuffmanCode.ReverseBits(value, 8), 8);

    private static bool HasAlpha(ImageBuffer buffer)
    {
        for (int i = 3; i < buffer.Rgba.Length; i += 4)
        {
            if (buffer.Rgba[i] != 255)
                return true;
        }

        return false;
    }

    private static void InsertColorCache(uint[]? colorCache, int colorCacheBits, uint pixel)
    {
        if (colorCache is null)
            return;

        uint key = (0x1E35A7BDu * pixel) >> (32 - colorCacheBits);
        colorCache[key] = pixel;
    }

    private static uint Pack(byte alpha, byte red, byte green, byte blue) =>
        ((uint)alpha << 24) | ((uint)red << 16) | ((uint)green << 8) | blue;

    private static int DivRoundUp(int value, int divisor) => (value + divisor - 1) / divisor;

    private static uint AddArgb(uint a, uint b) =>
        Pack(
            (byte)(((a >> 24) + (b >> 24)) & 0xFF),
            (byte)(((a >> 16) + (b >> 16)) & 0xFF),
            (byte)(((a >> 8) + (b >> 8)) & 0xFF),
            (byte)((a + b) & 0xFF));

    private static byte A(uint pixel) => (byte)(pixel >> 24);

    private static byte R(uint pixel) => (byte)(pixel >> 16);

    private static byte G(uint pixel) => (byte)(pixel >> 8);

    private static byte B(uint pixel) => (byte)pixel;

    private readonly struct PrefixCodeGroup(
        WebpHuffmanCode green,
        WebpHuffmanCode red,
        WebpHuffmanCode blue,
        WebpHuffmanCode alpha,
        WebpHuffmanCode distance)
    {
        public readonly WebpHuffmanCode Green = green;
        public readonly WebpHuffmanCode Red = red;
        public readonly WebpHuffmanCode Blue = blue;
        public readonly WebpHuffmanCode Alpha = alpha;
        public readonly WebpHuffmanCode Distance = distance;
    }

    private abstract class WebpTransform
    {
        public abstract uint[] Apply(uint[] pixels, ref int width, int height);
    }

    private sealed class PredictorTransform(int sizeBits, int transformWidth, uint[] modes) : WebpTransform
    {
        public override uint[] Apply(uint[] pixels, ref int width, int height)
        {
            uint[] output = new uint[pixels.Length];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                uint predictor = BorderPredictor(output, width, height, x, y);
                if (x > 0 && y > 0)
                {
                    int modeIndex = (y >> sizeBits) * transformWidth + (x >> sizeBits);
                    int mode = G(modes[modeIndex]);
                    predictor = Predict(output, width, height, x, y, mode);
                }

                output[index] = AddArgb(pixels[index], predictor);
            }

            return output;
        }

        private static uint BorderPredictor(uint[] output, int width, int height, int x, int y)
        {
            _ = height;
            if (x == 0 && y == 0)
                return 0xFF000000;
            if (y == 0)
                return output[x - 1];
            if (x == 0)
                return output[(y - 1) * width];

            return 0;
        }

        private static uint Predict(uint[] output, int width, int height, int x, int y, int mode)
        {
            int index = y * width + x;
            uint left = output[index - 1];
            uint top = output[index - width];
            uint topLeft = output[index - width - 1];
            uint topRight = x + 1 < width ? output[index - width + 1] : output[y * width];

            return mode switch
            {
                0 => 0xFF000000,
                1 => left,
                2 => top,
                3 => topRight,
                4 => topLeft,
                5 => Average(Average(left, topRight), top),
                6 => Average(left, topLeft),
                7 => Average(left, top),
                8 => Average(topLeft, top),
                9 => Average(top, topRight),
                10 => Average(Average(left, topLeft), Average(top, topRight)),
                11 => Select(left, top, topLeft),
                12 => ClampAddSubtractFull(left, top, topLeft),
                13 => ClampAddSubtractHalf(Average(left, top), topLeft),
                _ => 0xFF000000,
            };
        }

        private static uint Average(uint x, uint y) =>
            Pack(
                (byte)((A(x) + A(y)) >> 1),
                (byte)((R(x) + R(y)) >> 1),
                (byte)((G(x) + G(y)) >> 1),
                (byte)((B(x) + B(y)) >> 1));

        private static uint Select(uint left, uint top, uint topLeft)
        {
            int pA = A(left) + A(top) - A(topLeft);
            int pR = R(left) + R(top) - R(topLeft);
            int pG = G(left) + G(top) - G(topLeft);
            int pB = B(left) + B(top) - B(topLeft);
            int pLeft = Math.Abs(pA - A(left)) + Math.Abs(pR - R(left)) + Math.Abs(pG - G(left)) + Math.Abs(pB - B(left));
            int pTop = Math.Abs(pA - A(top)) + Math.Abs(pR - R(top)) + Math.Abs(pG - G(top)) + Math.Abs(pB - B(top));
            return pLeft < pTop ? left : top;
        }

        private static uint ClampAddSubtractFull(uint left, uint top, uint topLeft) =>
            Pack(
                Clamp(A(left) + A(top) - A(topLeft)),
                Clamp(R(left) + R(top) - R(topLeft)),
                Clamp(G(left) + G(top) - G(topLeft)),
                Clamp(B(left) + B(top) - B(topLeft)));

        private static uint ClampAddSubtractHalf(uint average, uint topLeft) =>
            Pack(
                Clamp(A(average) + ((A(average) - A(topLeft)) / 2)),
                Clamp(R(average) + ((R(average) - R(topLeft)) / 2)),
                Clamp(G(average) + ((G(average) - G(topLeft)) / 2)),
                Clamp(B(average) + ((B(average) - B(topLeft)) / 2)));

        private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);
    }

    private sealed class ColorTransform(int sizeBits, int transformWidth, uint[] elements) : WebpTransform
    {
        public override uint[] Apply(uint[] pixels, ref int width, int height)
        {
            uint[] output = new uint[pixels.Length];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                uint pixel = pixels[index];
                uint element = elements[(y >> sizeBits) * transformWidth + (x >> sizeBits)];

                byte red = R(pixel);
                byte green = G(pixel);
                byte blue = B(pixel);
                red = (byte)(red + ColorTransformDelta(B(element), green));
                blue = (byte)(blue + ColorTransformDelta(G(element), green));
                blue = (byte)(blue + ColorTransformDelta(R(element), red));
                output[index] = Pack(A(pixel), red, green, blue);
            }

            return output;
        }

        private static int ColorTransformDelta(byte transform, byte color) =>
            (unchecked((sbyte)transform) * unchecked((sbyte)color)) >> 5;
    }

    private sealed class SubtractGreenTransform : WebpTransform
    {
        public static SubtractGreenTransform Instance { get; } = new();

        public override uint[] Apply(uint[] pixels, ref int width, int height)
        {
            uint[] output = new uint[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                uint pixel = pixels[i];
                byte green = G(pixel);
                output[i] = Pack(A(pixel), (byte)(R(pixel) + green), green, (byte)(B(pixel) + green));
            }

            return output;
        }
    }

    private sealed class ColorIndexingTransform : WebpTransform
    {
        private readonly uint[] _colorTable;
        private readonly int _widthBits;
        private readonly int _outputWidth;

        public ColorIndexingTransform(uint[] colorTable, int widthBits, int outputWidth)
        {
            _colorTable = AccumulateColorTable(colorTable);
            _widthBits = widthBits;
            _outputWidth = outputWidth;
        }

        public override uint[] Apply(uint[] pixels, ref int width, int height)
        {
            uint[] output = new uint[checked(_outputWidth * height)];
            int pixelsPerPackedPixel = 1 << _widthBits;
            int bitsPerIndex = 8 >> _widthBits;
            int indexMask = (1 << bitsPerIndex) - 1;

            for (int y = 0; y < height; y++)
            for (int x = 0; x < _outputWidth; x++)
            {
                uint packed = pixels[y * width + (x >> _widthBits)];
                int tableIndex = _widthBits == 0
                    ? G(packed)
                    : (G(packed) >> ((x & (pixelsPerPackedPixel - 1)) * bitsPerIndex)) & indexMask;

                output[y * _outputWidth + x] = tableIndex < _colorTable.Length ? _colorTable[tableIndex] : 0;
            }

            width = _outputWidth;
            return output;
        }

        private static uint[] AccumulateColorTable(uint[] colorTable)
        {
            uint[] accumulated = new uint[colorTable.Length];
            uint previous = 0;
            for (int i = 0; i < colorTable.Length; i++)
            {
                previous = AddArgb(previous, colorTable[i]);
                accumulated[i] = previous;
            }

            return accumulated;
        }
    }
}
