using System;
using System.IO;

namespace Broiler.Media.Image.Managed;

/// <summary>
/// Pure-managed baseline JPEG encoder. Writes a 3-component YCbCr image with
/// 4:2:0 chroma subsampling and optional restart markers. By default it derives
/// <b>optimal</b> Huffman tables from the actual symbol statistics (a two-pass
/// encode that yields smaller files); the standard tables can be requested
/// instead. The alpha channel of the source buffer is discarded (JPEG has no
/// alpha).
/// </summary>
internal static class JpegEncoder
{
    public static byte[] Encode(ImageBuffer buffer, int quality) =>
        Encode(buffer, quality, restartInterval: 0, optimize: true);

    /// <param name="restartInterval">MCUs between restart markers; 0 disables them.</param>
    public static byte[] Encode(ImageBuffer buffer, int quality, int restartInterval) =>
        Encode(buffer, quality, restartInterval, optimize: true);

    /// <param name="optimize">When true, generate Huffman tables from the image's symbol statistics.</param>
    public static byte[] Encode(ImageBuffer buffer, int quality, int restartInterval, bool optimize)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        int width = buffer.Width;
        int height = buffer.Height;

        int[] quantLuma = JpegTables.BuildQuantTable(JpegTables.LuminanceQuant, quality);
        int[] quantChroma = JpegTables.BuildQuantTable(JpegTables.ChrominanceQuant, quality);

        int mcusX = (width + 15) / 16;
        int mcusY = (height + 15) / 16;
        int yW = mcusX * 16, yH = mcusY * 16;
        int cW = mcusX * 8, cH = mcusY * 8;

        byte[] planeY = new byte[yW * yH];
        byte[] planeCb = new byte[cW * cH];
        byte[] planeCr = new byte[cW * cH];
        BuildPlanes(buffer, width, height, yW, yH, cW, cH, planeY, planeCb, planeCr);

        // Quantize every block up front so both encode passes share the work.
        int yBlocksX = mcusX * 2, cBlocksX = mcusX;
        int[] yc = ComputeBlocks(planeY, yW, yBlocksX, mcusY * 2, quantLuma);
        int[] cbc = ComputeBlocks(planeCb, cW, cBlocksX, mcusY, quantChroma);
        int[] crc = ComputeBlocks(planeCr, cW, cBlocksX, mcusY, quantChroma);

        // Determine the Huffman table specifications.
        byte[] dcLBits, dcLVals, acLBits, acLVals, dcCBits, dcCVals, acCBits, acCVals;
        if (optimize)
        {
            int[] fDcL = new int[256], fAcL = new int[256], fDcC = new int[256], fAcC = new int[256];
            RunScan(mcusX, mcusY, restartInterval, yc, cbc, crc, yBlocksX, cBlocksX,
                writer: null, ms: null, null, null, null, null, fDcL, fAcL, fDcC, fAcC);

            (dcLBits, dcLVals) = JpegOptimalHuffman.Generate(fDcL);
            (acLBits, acLVals) = JpegOptimalHuffman.Generate(fAcL);
            (dcCBits, dcCVals) = JpegOptimalHuffman.Generate(fDcC);
            (acCBits, acCVals) = JpegOptimalHuffman.Generate(fAcC);
        }
        else
        {
            (dcLBits, dcLVals) = (JpegTables.DcLuminanceBits.ToArray(), JpegTables.DcLuminanceValues.ToArray());
            (acLBits, acLVals) = (JpegTables.AcLuminanceBits.ToArray(), JpegTables.AcLuminanceValues.ToArray());
            (dcCBits, dcCVals) = (JpegTables.DcChrominanceBits.ToArray(), JpegTables.DcChrominanceValues.ToArray());
            (acCBits, acCVals) = (JpegTables.AcChrominanceBits.ToArray(), JpegTables.AcChrominanceValues.ToArray());
        }

        var dcL = JpegHuffmanTable.Build(dcLBits, dcLVals);
        var acL = JpegHuffmanTable.Build(acLBits, acLVals);
        var dcC = JpegHuffmanTable.Build(dcCBits, dcCVals);
        var acC = JpegHuffmanTable.Build(acCBits, acCVals);

        using var ms = new MemoryStream();
        WriteHeaders(ms, width, height, quantLuma, quantChroma, restartInterval,
            dcLBits, dcLVals, acLBits, acLVals, dcCBits, dcCVals, acCBits, acCVals);

        var writer = new JpegBitWriter(ms);
        RunScan(mcusX, mcusY, restartInterval, yc, cbc, crc, yBlocksX, cBlocksX,
            writer, ms, dcL, acL, dcC, acC, null, null, null, null);
        writer.FlushToByte();

        ms.WriteByte(0xFF);
        ms.WriteByte(JpegTables.MarkerEoi);
        return ms.ToArray();
    }

    /// <summary>
    /// Walks the MCU grid, processing every block either to gather symbol frequencies
    /// (<paramref name="writer"/> null) or to emit the entropy-coded bitstream.
    /// </summary>
    private static void RunScan(
        int mcusX, int mcusY, int restartInterval,
        int[] yc, int[] cbc, int[] crc, int yBlocksX, int cBlocksX,
        JpegBitWriter? writer, MemoryStream? ms,
        JpegHuffmanTable? dcL, JpegHuffmanTable? acL, JpegHuffmanTable? dcC, JpegHuffmanTable? acC,
        int[]? fDcL, int[]? fAcL, int[]? fDcC, int[]? fAcC)
    {
        int prevY = 0, prevCb = 0, prevCr = 0, mcuIndex = 0, restartCount = 0;

        for (int my = 0; my < mcusY; my++)
            for (int mx = 0; mx < mcusX; mx++)
            {
                if (restartInterval > 0 && mcuIndex > 0 && mcuIndex % restartInterval == 0)
                {
                    if (writer != null)
                    {
                        writer.FlushToByte();
                        ms!.WriteByte(0xFF);
                        ms.WriteByte((byte)(JpegTables.MarkerRst0 + (restartCount & 7)));
                    }
                    restartCount++;
                    prevY = prevCb = prevCr = 0;
                }

                for (int by = 0; by < 2; by++)
                    for (int bx = 0; bx < 2; bx++)
                    {
                        int off = ((my * 2 + by) * yBlocksX + (mx * 2 + bx)) * 64;
                        prevY = ProcessBlock(yc, off, prevY, writer, dcL, acL, fDcL, fAcL);
                    }

                int offC = (my * cBlocksX + mx) * 64;
                prevCb = ProcessBlock(cbc, offC, prevCb, writer, dcC, acC, fDcC, fAcC);
                prevCr = ProcessBlock(crc, offC, prevCr, writer, dcC, acC, fDcC, fAcC);

                mcuIndex++;
            }
    }

    private static int ProcessBlock(
        int[] coeff, int offset, int prevDc,
        JpegBitWriter? writer, JpegHuffmanTable? dcTable, JpegHuffmanTable? acTable,
        int[]? dcFreq, int[]? acFreq)
    {
        int dc = coeff[offset];
        int diff = dc - prevDc;
        int dcSize = JpegTables.Magnitude(diff);
        Emit(writer, dcTable, dcFreq, dcSize);
        if (writer != null && dcSize > 0)
            writer.WriteBits(JpegTables.ToBitPattern(diff, dcSize), dcSize);

        int run = 0;
        for (int k = 1; k < 64; k++)
        {
            int v = coeff[offset + JpegTables.ZigZag[k]];
            if (v == 0)
            {
                run++;
                continue;
            }
            while (run > 15)
            {
                Emit(writer, acTable, acFreq, 0xF0); // ZRL
                run -= 16;
            }
            int size = JpegTables.Magnitude(v);
            Emit(writer, acTable, acFreq, (run << 4) | size);
            if (writer != null)
                writer.WriteBits(JpegTables.ToBitPattern(v, size), size);
            run = 0;
        }
        if (run > 0)
            Emit(writer, acTable, acFreq, 0x00); // EOB

        return dc;
    }

    /// <summary>Either writes a Huffman symbol (write pass) or counts it (gather pass).</summary>
    private static void Emit(JpegBitWriter? writer, JpegHuffmanTable? table, int[]? freq, int symbol)
    {
        if (writer != null)
            writer.WriteSymbol(table!, symbol);
        else
            freq![symbol]++;
    }

    private static int[] ComputeBlocks(byte[] plane, int planeWidth, int blocksX, int blocksY, int[] quant)
    {
        var result = new int[blocksX * blocksY * 64];
        var block = new double[64];
        for (int by = 0; by < blocksY; by++)
            for (int bx = 0; bx < blocksX; bx++)
            {
                int x0 = bx * 8, y0 = by * 8;
                for (int y = 0; y < 8; y++)
                {
                    int row = (y0 + y) * planeWidth + x0;
                    for (int x = 0; x < 8; x++)
                        block[y * 8 + x] = plane[row + x] - 128.0;
                }
                JpegDct.Forward(block);

                int offset = (by * blocksX + bx) * 64;
                for (int i = 0; i < 64; i++)
                    result[offset + i] = (int)Math.Round(block[i] / quant[i]);
            }
        return result;
    }

    private static void BuildPlanes(
        ImageBuffer buffer, int width, int height, int yW, int yH, int cW, int cH,
        byte[] planeY, byte[] planeCb, byte[] planeCr)
    {
        byte[] rgba = buffer.Rgba;

        // Luma at full resolution; edge pixels replicated into the padded region.
        for (int y = 0; y < yH; y++)
        {
            int sy = Math.Min(y, height - 1);
            for (int x = 0; x < yW; x++)
            {
                int sx = Math.Min(x, width - 1);
                int p = (sy * width + sx) * 4;
                planeY[y * yW + x] = (byte)Math.Clamp(
                    (int)Math.Round(0.299 * rgba[p] + 0.587 * rgba[p + 1] + 0.114 * rgba[p + 2]), 0, 255);
            }
        }

        // Chroma: average the 2x2 source block under each chroma sample.
        for (int cy = 0; cy < cH; cy++)
            for (int cx = 0; cx < cW; cx++)
            {
                double cbSum = 0, crSum = 0;
                for (int dy = 0; dy < 2; dy++)
                {
                    int sy = Math.Min(cy * 2 + dy, height - 1);
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int sx = Math.Min(cx * 2 + dx, width - 1);
                        int p = (sy * width + sx) * 4;
                        double r = rgba[p], g = rgba[p + 1], b = rgba[p + 2];
                        cbSum += -0.168736 * r - 0.331264 * g + 0.5 * b + 128.0;
                        crSum += 0.5 * r - 0.418688 * g - 0.081312 * b + 128.0;
                    }
                }
                planeCb[cy * cW + cx] = (byte)Math.Clamp((int)Math.Round(cbSum / 4.0), 0, 255);
                planeCr[cy * cW + cx] = (byte)Math.Clamp((int)Math.Round(crSum / 4.0), 0, 255);
            }
    }

    private static void WriteHeaders(
        Stream s, int width, int height, int[] quantLuma, int[] quantChroma, int restartInterval,
        byte[] dcLBits, byte[] dcLVals, byte[] acLBits, byte[] acLVals,
        byte[] dcCBits, byte[] dcCVals, byte[] acCBits, byte[] acCVals)
    {
        s.WriteByte(0xFF);
        s.WriteByte(JpegTables.MarkerSoi);

        WriteMarker(s, JpegTables.MarkerApp0, [
            (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00,
            0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
        ]);

        byte[] dqt = new byte[65 * 2];
        WriteQuant(dqt, 0, 0, quantLuma);
        WriteQuant(dqt, 65, 1, quantChroma);
        WriteMarker(s, JpegTables.MarkerDqt, dqt);

        WriteMarker(s, JpegTables.MarkerSof0, [
            0x08,
            (byte)(height >> 8), (byte)height,
            (byte)(width >> 8), (byte)width,
            0x03,
            0x01, 0x22, 0x00,
            0x02, 0x11, 0x01,
            0x03, 0x11, 0x01,
        ]);

        using var dht = new MemoryStream();
        AppendHuffman(dht, 0x00, dcLBits, dcLVals);
        AppendHuffman(dht, 0x10, acLBits, acLVals);
        AppendHuffman(dht, 0x01, dcCBits, dcCVals);
        AppendHuffman(dht, 0x11, acCBits, acCVals);
        WriteMarker(s, JpegTables.MarkerDht, dht.ToArray());

        if (restartInterval > 0)
            WriteMarker(s, JpegTables.MarkerDri, [(byte)(restartInterval >> 8), (byte)restartInterval]);

        WriteMarker(s, JpegTables.MarkerSos, [
            0x03,
            0x01, 0x00,
            0x02, 0x11,
            0x03, 0x11,
            0x00, 0x3F, 0x00,
        ]);
    }

    private static void WriteQuant(byte[] dest, int offset, int tableId, int[] quant)
    {
        dest[offset] = (byte)tableId; // precision 0 (8-bit) | id
        for (int k = 0; k < 64; k++)
            dest[offset + 1 + k] = (byte)quant[JpegTables.ZigZag[k]];
    }

    private static void AppendHuffman(Stream s, byte classAndId, byte[] bits, byte[] values)
    {
        s.WriteByte(classAndId);
        s.Write(bits, 0, bits.Length);
        s.Write(values, 0, values.Length);
    }

    private static void WriteMarker(Stream s, byte marker, ReadOnlySpan<byte> payload)
    {
        s.WriteByte(0xFF);
        s.WriteByte(marker);
        int length = payload.Length + 2;
        s.WriteByte((byte)(length >> 8));
        s.WriteByte((byte)length);
        s.Write(payload);
    }
}
