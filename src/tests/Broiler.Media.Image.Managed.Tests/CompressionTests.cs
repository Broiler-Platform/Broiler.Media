using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Broiler.Media.Image;
using Broiler.Media.Image.Managed.Compression;

namespace Broiler.Media.Image.Managed.Tests;

internal static class CompressionTests
{
    public static void Register(List<(string Name, Func<ValueTask> Body)> tests)
    {
        tests.Add(("PackBits literal and repeat runs round-trip", PackBitsRoundTrip));
        tests.Add(("PackBits EOD 128 terminates decode", PackBitsEod));
        tests.Add(("LZW basic decode and clear code", LzwBasicDecode));
        tests.Add(("LZW EarlyChange 0 vs 1 behavior", LzwEarlyChange));
        tests.Add(("ImagePredictors TIFF 2 round-trip", TiffPredictorRoundTrip));
        tests.Add(("ImagePredictors PNG row predictors round-trip", PngPredictorsRoundTrip));
        tests.Add(("ImageSampleConverter 1-bit monochrome to RGBA8", SampleConverter1Bit));
        tests.Add(("ImageSampleConverter RGB8 with decode interval inversion", SampleConverterRgbDecode));
    }

    private static ValueTask PackBitsRoundTrip()
    {
        // Construct PackBits encoded data:
        // literal run of 3 bytes "ABC" (length = 2 -> 3 bytes)
        // repeat run of byte 'X' 5 times (repeat = 5 -> byte = 257 - 5 = 252)
        // EOD (128)
        byte[] encoded = [2, (byte)'A', (byte)'B', (byte)'C', 252, (byte)'X', 128];
        PackBitsResult result = PackBitsDecoder.Decode(encoded);

        if (result.Outcome != PackBitsOutcome.Decoded || result.Data is null)
            throw new Exception($"PackBits decode failed: {result.Failure}");

        byte[] expected = [(byte)'A', (byte)'B', (byte)'C', (byte)'X', (byte)'X', (byte)'X', (byte)'X', (byte)'X'];
        Assert.BytesEqual(expected, result.Data, "PackBits decompressed data");
        return ValueTask.CompletedTask;
    }

    private static ValueTask PackBitsEod()
    {
        byte[] encoded = [0, 0x42, 128, 0, 0x99]; // 0x99 past 128 should be ignored
        PackBitsResult result = PackBitsDecoder.Decode(encoded);
        Assert.Equal(1, result.Data!.Length, "Should only decode before 128");
        Assert.Equal(0x42, result.Data[0], "First byte decoded");
        return ValueTask.CompletedTask;
    }

    private static ValueTask LzwBasicDecode()
    {
        // Simple LZW stream with ClearCode(256), literal 'A'(65), literal 'B'(66), EOD(257)
        // 9-bit codes:
        // 256: 1 0000 0000
        // 65:  0 0100 0001
        // 66:  0 0100 0010
        // 257: 1 0000 0001
        var writer = new BitWriter();
        writer.Write(256, 9);
        writer.Write(65, 9);
        writer.Write(66, 9);
        writer.Write(257, 9);
        byte[] input = writer.ToArray();

        LzwResult result = LzwDecoder.Decode(input, earlyChange: 1);
        if (result.Outcome != LzwOutcome.Decoded || result.Data is null)
            throw new Exception($"LZW decode failed: {result.Failure}");

        Assert.BytesEqual([(byte)'A', (byte)'B'], result.Data, "LZW decoded bytes");
        return ValueTask.CompletedTask;
    }

    private static ValueTask LzwEarlyChange()
    {
        // LZW with EarlyChange = 0 vs 1 test
        var writer = new BitWriter();
        writer.Write(256, 9);
        writer.Write(65, 9);
        writer.Write(257, 9);
        byte[] input = writer.ToArray();

        LzwResult r0 = LzwDecoder.Decode(input, earlyChange: 0);
        LzwResult r1 = LzwDecoder.Decode(input, earlyChange: 1);

        Assert.BytesEqual(r0.Data!, r1.Data!, "Both should decode 'A'");
        return ValueTask.CompletedTask;
    }

    private static ValueTask TiffPredictorRoundTrip()
    {
        // 3 components, 8-bit, 4 pixels
        byte[] raw = [10, 20, 30, 15, 25, 35, 12, 22, 32, 100, 150, 200];
        byte[] encoded = (byte[])raw.Clone();

        // Apply TIFF 2 predictor (horizontal difference)
        for (int i = raw.Length - 1; i >= 3; i--)
            encoded[i] = (byte)(encoded[i] - encoded[i - 3]);

        // Undo predictor with ImagePredictors
        bool ok = ImagePredictors.TryReverse(encoded, predictor: ImagePredictors.Tiff, colors: 3, bitsPerComponent: 8, columns: 4, out byte[] result, out _);
        Assert.True(ok, "TIFF 2 predictor reverse");
        Assert.BytesEqual(raw, result, "TIFF 2 predictor reversed");
        return ValueTask.CompletedTask;
    }

    private static ValueTask PngPredictorsRoundTrip()
    {
        // 2 rows of 3 pixels (RGB8, 9 bytes + 1 filter byte per row = 10 bytes/row)
        // Row 0: filter Sub (1)
        // Row 1: filter Up (2)
        byte[] row0Raw = [10, 20, 30, 40, 50, 60, 70, 80, 90];
        byte[] row1Raw = [15, 25, 35, 45, 55, 65, 75, 85, 95];

        byte[] stream = new byte[20];
        stream[0] = 1; // Sub
        stream[1] = row0Raw[0]; stream[2] = row0Raw[1]; stream[3] = row0Raw[2];
        for (int i = 3; i < 9; i++)
            stream[1 + i] = (byte)(row0Raw[i] - row0Raw[i - 3]);

        stream[10] = 2; // Up
        for (int i = 0; i < 9; i++)
            stream[11 + i] = (byte)(row1Raw[i] - row0Raw[i]);

        bool ok = ImagePredictors.TryReverse(stream, predictor: 15, colors: 3, bitsPerComponent: 8, columns: 3, out byte[] result, out _);
        Assert.True(ok, "PNG predictor reverse");
        byte[] expected = new byte[18];
        Array.Copy(row0Raw, 0, expected, 0, 9);
        Array.Copy(row1Raw, 0, expected, 9, 9);

        Assert.BytesEqual(expected, result, "PNG row predictors reversed");
        return ValueTask.CompletedTask;
    }

    private static ValueTask SampleConverter1Bit()
    {
        // 8 pixels: 10100001 -> 0xA1
        byte[] packed = [0xA1];
        byte[]? rgba = ImageSampleConverter.UnpackToRgba(packed, width: 8, height: 1, bitsPerComponent: 1, SampleColorSpace.Gray);

        Assert.True(rgba is not null, "RGBA not null");
        Assert.Equal(8 * 4, rgba!.Length, "8 RGBA pixels");
        // Pixel 0 (bit 7 = 1): sample 1 -> 255
        Assert.Equal(255, rgba[0], "P0 R");
        Assert.Equal(255, rgba[3], "P0 A");
        // Pixel 1 (bit 6 = 0): sample 0 -> 0
        Assert.Equal(0, rgba[4], "P1 R");
        Assert.Equal(255, rgba[7], "P1 A");
        return ValueTask.CompletedTask;
    }

    private static ValueTask SampleConverterRgbDecode()
    {
        // Inverted RGB: decode [1 0 1 0 1 0]
        byte[] rgb = [255, 0, 128];
        double[] decode = [1, 0, 1, 0, 1, 0];
        byte[]? rgba = ImageSampleConverter.UnpackToRgba(rgb, width: 1, height: 1, bitsPerComponent: 8, SampleColorSpace.Rgb, decode: decode);

        Assert.True(rgba is not null, "RGBA not null");
        Assert.Equal(0, rgba![0], "Inverted 255 -> 0");
        Assert.Equal(255, rgba[1], "Inverted 0 -> 255");
        Assert.Equal(127, rgba[2], "Inverted 128 -> 127");
        Assert.Equal(255, rgba[3], "Alpha");
        return ValueTask.CompletedTask;
    }

    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = [];
        private int _buffer;
        private int _count;

        public void Write(int value, int bits)
        {
            for (int i = bits - 1; i >= 0; i--)
            {
                _buffer = (_buffer << 1) | ((value >> i) & 1);
                if (++_count == 8)
                {
                    _bytes.Add((byte)_buffer);
                    _buffer = 0;
                    _count = 0;
                }
            }
        }

        public byte[] ToArray()
        {
            if (_count > 0)
                _bytes.Add((byte)(_buffer << (8 - _count)));
            return _bytes.ToArray();
        }
    }
}
