using System;
using System.Buffers.Binary;

namespace Broiler.Media.Image.Managed;

/// <summary>
/// Pure-managed JPEG decoder for both <b>baseline</b> (SOF0) and <b>progressive</b>
/// (SOF2) Huffman-coded images. Handles the common chroma subsamplings
/// (4:4:4, 4:2:2, 4:2:0, …), restart markers, spectral selection and successive
/// approximation, and 1- or 3-component (grayscale / YCbCr) frames, producing
/// 8-bit RGBA. Arithmetic-coded and lossless JPEGs are rejected.
/// <para>
/// All scans accumulate dequantized-later coefficients into per-component buffers;
/// the image is reconstructed (dequantize → IDCT → upsample → YCbCr→RGB) once every
/// scan has been parsed. Baseline is treated as a single full-spectrum scan.
/// </para>
/// </summary>
internal static class JpegDecoder
{
    /// <summary>True if <paramref name="data"/> begins with the JPEG SOI + marker prefix.</summary>
    public static bool IsJpeg(ReadOnlySpan<byte> data) =>
        data.Length >= 3 && data[0] == 0xFF && data[1] == JpegTables.MarkerSoi && data[2] == 0xFF;

    private sealed class Component
    {
        public int Id;
        public int H;          // horizontal sampling factor
        public int V;          // vertical sampling factor
        public int QuantId;
        public int DcTableId;
        public int AcTableId;

        // Coefficient storage is padded to whole MCUs (so interleaved scans address
        // it directly); reconstruction only walks the "real" block extent.
        public int AllocBlocksPerLine;
        public int BlocksPerLine;
        public int BlocksPerColumn;
        public int[] Coefficients = [];

        public int PlaneWidth;
        public byte[] Samples = [];
        public int Pred; // DC predictor (transient, per scan)
    }

    public static ImageBuffer Decode(ReadOnlySpan<byte> data)
    {
        if (!IsJpeg(data))
            throw new FormatException("Data does not start with a JPEG SOI marker.");

        byte[] bytes = data.ToArray();
        var quant = new int[4][];
        var dcTables = new JpegHuffmanTable[4];
        var acTables = new JpegHuffmanTable[4];
        Component[]? components = null;
        int width = 0, height = 0;
        int restartInterval = 0;
        bool progressive = false;

        int pos = 2; // past SOI
        while (pos + 2 <= bytes.Length)
        {
            if (bytes[pos] != 0xFF)
            {
                pos++; // tolerate stray fill bytes between segments
                continue;
            }

            byte marker = bytes[pos + 1];
            pos += 2;

            if (marker == JpegTables.MarkerEoi)
                break;
            if (marker == JpegTables.MarkerSoi || marker == 0xFF ||
                marker is >= JpegTables.MarkerRst0 and <= JpegTables.MarkerRst7 || marker == 0x01)
                continue; // standalone markers / padding without a payload

            if (pos + 2 > bytes.Length)
                throw new FormatException("Truncated JPEG segment header.");
            int length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(pos, 2));
            if (length < 2 || pos + length > bytes.Length)
                throw new FormatException("Corrupt JPEG segment length.");
            ReadOnlySpan<byte> segment = bytes.AsSpan(pos + 2, length - 2);

            switch (marker)
            {
                case JpegTables.MarkerDqt:
                    ReadQuantTables(segment, quant);
                    break;
                case JpegTables.MarkerDht:
                    ReadHuffmanTables(segment, dcTables, acTables);
                    break;
                case JpegTables.MarkerDri:
                    restartInterval = BinaryPrimitives.ReadUInt16BigEndian(segment[..2]);
                    break;
                case JpegTables.MarkerSof0:
                case 0xC1: // extended sequential (decodes like baseline at 8-bit)
                    components = ReadFrameHeader(segment, out width, out height);
                    SetupComponents(components, width, height);
                    progressive = false;
                    break;
                case JpegTables.MarkerSof2:
                    components = ReadFrameHeader(segment, out width, out height);
                    SetupComponents(components, width, height);
                    progressive = true;
                    break;
                case 0xC3: // lossless
                case 0xC5: case 0xC6: case 0xC7:
                case 0xC9: case 0xCA: case 0xCB:
                case 0xCD: case 0xCE: case 0xCF:
                    throw new NotSupportedException(
                        $"Unsupported JPEG frame type 0x{marker:X2} (only baseline and progressive Huffman).");
                case JpegTables.MarkerSos:
                {
                    if (components is null)
                        throw new FormatException("JPEG SOS encountered before a frame header.");
                    Component[] scanComponents = ReadScanHeader(
                        segment, components, dcTables, acTables, progressive,
                        out int ss, out int se, out int ah, out int al);

                    int scanDataStart = pos + length;
                    DecodeScan(bytes, scanDataStart, components, scanComponents, dcTables, acTables,
                        width, height, restartInterval, ss, se, ah, al);

                    pos = FindNextMarker(bytes, scanDataStart);
                    continue; // pos already advanced past the entropy data
                }
                default:
                    break; // APPn, COM, and other ancillary segments are skipped
            }

            pos += length;
        }

        if (components is null)
            throw new FormatException("JPEG ended before a frame header was found.");

        Reconstruct(components, quant, width, height);
        return AssembleRgba(components, width, height);
    }

    // ---- Geometry -------------------------------------------------------

    private static void SetupComponents(Component[] components, int width, int height)
    {
        int hMax = 0, vMax = 0;
        foreach (Component c in components)
        {
            hMax = Math.Max(hMax, c.H);
            vMax = Math.Max(vMax, c.V);
        }

        int mcusPerLine = (width + hMax * 8 - 1) / (hMax * 8);
        int mcusPerColumn = (height + vMax * 8 - 1) / (vMax * 8);

        foreach (Component c in components)
        {
            c.AllocBlocksPerLine = mcusPerLine * c.H;
            int allocBlocksPerColumn = mcusPerColumn * c.V;

            int samplesPerLine = (width * c.H + hMax - 1) / hMax;
            int samplesPerColumn = (height * c.V + vMax - 1) / vMax;
            c.BlocksPerLine = (samplesPerLine + 7) / 8;
            c.BlocksPerColumn = (samplesPerColumn + 7) / 8;

            c.Coefficients = new int[c.AllocBlocksPerLine * allocBlocksPerColumn * 64];
        }
    }

    // ---- Scan decoding (baseline + progressive) -------------------------

    private static void DecodeScan(
        byte[] bytes, int scanStart, Component[] all, Component[] scan,
        JpegHuffmanTable[] dc, JpegHuffmanTable[] ac, int width, int height,
        int restartInterval, int ss, int se, int ah, int al)
    {
        int hMax = 0, vMax = 0;
        foreach (Component c in all)
        {
            hMax = Math.Max(hMax, c.H);
            vMax = Math.Max(vMax, c.V);
        }
        int mcusPerLine = (width + hMax * 8 - 1) / (hMax * 8);
        int mcusPerColumn = (height + vMax * 8 - 1) / (vMax * 8);

        var reader = new JpegBitReader(bytes, scanStart, bytes.Length);
        int eobrun = 0;
        foreach (Component c in scan)
            c.Pred = 0;

        // --- per-block entropy decode (closes over reader/eobrun/ss/se/ah/al) ---
        void RefineNonzero(int[] coef, int idx, int bit)
        {
            if (coef[idx] == 0)
                return;
            if (reader.ReadBit() == 1 && (coef[idx] & bit) == 0)
                coef[idx] += coef[idx] > 0 ? bit : -bit;
        }

        void DecodeAcFirst(int[] coef, int offset, int kStart, JpegHuffmanTable table)
        {
            if (eobrun > 0)
            {
                eobrun--;
                return;
            }
            int k = kStart;
            while (k <= se)
            {
                int rs = table.Decode(reader);
                if (rs < 0)
                    throw new FormatException("Unexpected end of JPEG AC data.");
                int r = rs >> 4, s = rs & 0x0F;
                if (s == 0)
                {
                    if (r < 15)
                    {
                        eobrun = (1 << r) - 1;
                        if (r > 0)
                            eobrun += reader.ReadBits(r);
                        break;
                    }
                    k += 16; // ZRL
                }
                else
                {
                    k += r;
                    if (k > se)
                        break;
                    coef[offset + JpegTables.ZigZag[k]] = JpegTables.Extend(reader.ReadBits(s), s) << al;
                    k++;
                }
            }
        }

        void DecodeAcRefine(int[] coef, int offset, int kStart, JpegHuffmanTable table)
        {
            int bit = 1 << al;

            if (eobrun > 0)
            {
                eobrun--;
                for (int k = kStart; k <= se; k++)
                    RefineNonzero(coef, offset + JpegTables.ZigZag[k], bit);
                return;
            }

            int kk = kStart;
            while (kk <= se)
            {
                int rs = table.Decode(reader);
                if (rs < 0)
                    throw new FormatException("Unexpected end of JPEG AC refinement data.");
                int r = rs >> 4, s = rs & 0x0F;
                int value = 0;
                if (s == 0)
                {
                    if (r < 15)
                    {
                        eobrun = (1 << r) - 1;
                        if (r > 0)
                            eobrun += reader.ReadBits(r);
                        r = 64; // refine to the end of the band
                    }
                    // r == 15 → run of sixteen zero-history coefficients
                }
                else
                {
                    value = reader.ReadBit() == 1 ? bit : -bit;
                }

                while (kk <= se)
                {
                    int idx = offset + JpegTables.ZigZag[kk];
                    kk++;
                    if (coef[idx] != 0)
                    {
                        RefineNonzero(coef, idx, bit);
                    }
                    else
                    {
                        if (r == 0)
                        {
                            if (value != 0)
                                coef[idx] = value;
                            break;
                        }
                        r--;
                    }
                }
            }
        }

        void DecodeBlock(Component comp, int offset)
        {
            int[] coef = comp.Coefficients;

            if (ss == 0)
            {
                JpegHuffmanTable table = dc[comp.DcTableId]
                    ?? throw new FormatException("JPEG scan references a missing DC table.");
                if (ah == 0)
                {
                    int t = table.Decode(reader);
                    if (t < 0)
                        throw new FormatException("Unexpected end of JPEG DC data.");
                    int diff = t == 0 ? 0 : JpegTables.Extend(reader.ReadBits(t), t);
                    comp.Pred += diff;
                    coef[offset] = comp.Pred << al;
                }
                else if (reader.ReadBit() == 1)
                {
                    coef[offset] |= 1 << al;
                }
            }

            if (se >= 1)
            {
                int kStart = ss == 0 ? 1 : ss;
                JpegHuffmanTable table = ac[comp.AcTableId]
                    ?? throw new FormatException("JPEG scan references a missing AC table.");
                if (ah == 0)
                    DecodeAcFirst(coef, offset, kStart, table);
                else
                    DecodeAcRefine(coef, offset, kStart, table);
            }
        }

        // --- iterate blocks: non-interleaved (single component) or MCU-interleaved ---
        if (scan.Length == 1)
        {
            Component comp = scan[0];
            int n = 0;
            for (int by = 0; by < comp.BlocksPerColumn; by++)
                for (int bx = 0; bx < comp.BlocksPerLine; bx++)
                {
                    if (restartInterval > 0 && n > 0 && n % restartInterval == 0)
                    {
                        if (!reader.SkipToRestart())
                            throw new FormatException("JPEG restart marker expected but not found.");
                        comp.Pred = 0;
                        eobrun = 0;
                    }
                    DecodeBlock(comp, (by * comp.AllocBlocksPerLine + bx) * 64);
                    n++;
                }
        }
        else
        {
            int n = 0;
            for (int my = 0; my < mcusPerColumn; my++)
                for (int mx = 0; mx < mcusPerLine; mx++)
                {
                    if (restartInterval > 0 && n > 0 && n % restartInterval == 0)
                    {
                        if (!reader.SkipToRestart())
                            throw new FormatException("JPEG restart marker expected but not found.");
                        foreach (Component c in scan)
                            c.Pred = 0;
                        eobrun = 0;
                    }
                    foreach (Component comp in scan)
                        for (int vy = 0; vy < comp.V; vy++)
                            for (int hx = 0; hx < comp.H; hx++)
                            {
                                int bx = mx * comp.H + hx;
                                int by = my * comp.V + vy;
                                DecodeBlock(comp, (by * comp.AllocBlocksPerLine + bx) * 64);
                            }
                    n++;
                }
        }
    }

    // ---- Reconstruction -------------------------------------------------

    /// <summary>
    /// Dequantize + inverse DCT of every block of every component, into per-component sample planes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Parallel over block rows</b> — multithreading roadmap item #6, first half. A block reads
    /// its own 64 coefficients and writes its own 8×8 patch of the component's sample plane, so a
    /// band of block rows touches memory no other band touches. The entropy decode that produced
    /// <c>Coefficients</c> is sequential and has already finished; this is the part of a JPEG that
    /// is not.
    /// </para>
    /// <para>
    /// <b>The scratch buffers move inside the band.</b> The dequantized block and the IDCT's
    /// intermediate were one allocation shared across the whole image; they are now one pair per
    /// band, which is both what makes the pass thread-safe and — because <c>JpegDct.Inverse</c>
    /// used to allocate its intermediate per block — less garbage than the sequential version
    /// produced.
    /// </para>
    /// </remarks>
    private static void Reconstruct(Component[] components, int[][] quant, int width, int height)
    {
        foreach (Component c in components)
        {
            int[] q = quant[c.QuantId] ?? throw new FormatException("JPEG references a missing quant table.");
            c.PlaneWidth = c.BlocksPerLine * 8;
            c.Samples = new byte[c.PlaneWidth * c.BlocksPerColumn * 8];

            Component component = c;
            ImageDecodeParallelism.For(
                0,
                c.BlocksPerColumn,
                BlockRowsPerBand(c.BlocksPerLine),
                (fromRow, toRow) =>
                {
                    var block = new double[64];
                    var scratch = new double[64];

                    for (int by = fromRow; by < toRow; by++)
                        for (int bx = 0; bx < component.BlocksPerLine; bx++)
                        {
                            int coefOffset = (by * component.AllocBlocksPerLine + bx) * 64;
                            for (int i = 0; i < 64; i++)
                                block[i] = component.Coefficients[coefOffset + i] * (double)q[i];
                            JpegDct.Inverse(block, scratch);
                            StoreBlock(component, block, bx * 8, by * 8);
                        }
                });
        }
    }

    /// <summary>
    /// Smallest number of block rows worth giving a thread, as a pixel budget: one block row of a
    /// wide component is far more work than one of a narrow chroma plane, and the split should
    /// follow the work rather than the row count.
    /// </summary>
    private static int BlockRowsPerBand(int blocksPerLine) =>
        Math.Max(1, MinimumPixelsPerBand / Math.Max(1, blocksPerLine * 64));

    /// <summary>
    /// Pixels a band must be worth before a pass is split. Small JPEGs — thumbnails, sprites —
    /// decode inline rather than pay a scheduler round trip for a few hundred microseconds of work.
    /// </summary>
    private const int MinimumPixelsPerBand = 32 * 1024;

    private static void StoreBlock(Component c, double[] block, int x0, int y0)
    {
        for (int y = 0; y < 8; y++)
        {
            int row = (y0 + y) * c.PlaneWidth + x0;
            for (int x = 0; x < 8; x++)
            {
                int v = (int)Math.Round(block[y * 8 + x]) + 128;
                c.Samples[row + x] = (byte)Math.Clamp(v, 0, 255);
            }
        }
    }

    private static ImageBuffer AssembleRgba(Component[] components, int width, int height)
    {
        int hMax = 0, vMax = 0;
        foreach (Component c in components)
        {
            hMax = Math.Max(hMax, c.H);
            vMax = Math.Max(vMax, c.V);
        }

        byte[] rgba = new byte[(long)width * height * 4];
        int rowsPerBand = Math.Max(1, MinimumPixelsPerBand / Math.Max(1, width));

        // Parallel over output rows — multithreading roadmap item #6, second half. Upsampling reads
        // the finished sample planes and colour conversion is per pixel, so a band of output rows
        // reads shared immutable state and writes only its own rows.
        if (components.Length == 1)
        {
            Component y = components[0];
            ImageDecodeParallelism.For(0, height, rowsPerBand, (fromRow, toRow) =>
            {
                for (int py = fromRow; py < toRow; py++)
                    for (int px = 0; px < width; px++)
                    {
                        byte g = y.Samples[py * y.PlaneWidth + px];
                        int d = (py * width + px) * 4;
                        rgba[d] = g;
                        rgba[d + 1] = g;
                        rgba[d + 2] = g;
                        rgba[d + 3] = 255;
                    }
            });
            return new ImageBuffer(width, height, rgba);
        }

        Component cy = components[0];
        Component cb = components[1];
        Component cr = components[2];

        ImageDecodeParallelism.For(0, height, rowsPerBand, (fromRow, toRow) =>
        {
            for (int py = fromRow; py < toRow; py++)
                for (int px = 0; px < width; px++)
                {
                    int yy = Sample(cy, px, py, hMax, vMax);
                    int cbv = Sample(cb, px, py, hMax, vMax) - 128;
                    int crv = Sample(cr, px, py, hMax, vMax) - 128;

                    int r = (int)Math.Round(yy + 1.402 * crv);
                    int g = (int)Math.Round(yy - 0.344136 * cbv - 0.714136 * crv);
                    int b = (int)Math.Round(yy + 1.772 * cbv);

                    int d = (py * width + px) * 4;
                    rgba[d] = (byte)Math.Clamp(r, 0, 255);
                    rgba[d + 1] = (byte)Math.Clamp(g, 0, 255);
                    rgba[d + 2] = (byte)Math.Clamp(b, 0, 255);
                    rgba[d + 3] = 255;
                }
        });

        return new ImageBuffer(width, height, rgba);
    }

    private static int Sample(Component c, int px, int py, int hMax, int vMax)
    {
        int sx = px * c.H / hMax;
        int sy = py * c.V / vMax;
        return c.Samples[sy * c.PlaneWidth + sx];
    }

    // ---- Header parsing -------------------------------------------------

    private static void ReadQuantTables(ReadOnlySpan<byte> seg, int[][] quant)
    {
        int i = 0;
        while (i < seg.Length)
        {
            int pq = seg[i] >> 4;   // 0 = 8-bit, 1 = 16-bit
            int tq = seg[i] & 0x0F; // table id
            i++;
            if (tq > 3)
                throw new FormatException("JPEG quantization table id out of range.");

            var table = new int[64];
            for (int k = 0; k < 64; k++)
            {
                int natural = JpegTables.ZigZag[k];
                if (pq == 0)
                {
                    table[natural] = seg[i];
                    i++;
                }
                else
                {
                    table[natural] = BinaryPrimitives.ReadUInt16BigEndian(seg.Slice(i, 2));
                    i += 2;
                }
            }
            quant[tq] = table;
        }
    }

    private static void ReadHuffmanTables(ReadOnlySpan<byte> seg, JpegHuffmanTable[] dc, JpegHuffmanTable[] ac)
    {
        int i = 0;
        while (i < seg.Length)
        {
            int tc = seg[i] >> 4;   // 0 = DC, 1 = AC
            int th = seg[i] & 0x0F; // table id
            i++;
            if (th > 3)
                throw new FormatException("JPEG Huffman table id out of range.");

            ReadOnlySpan<byte> bits = seg.Slice(i, 16);
            i += 16;
            int count = 0;
            for (int l = 0; l < 16; l++)
                count += bits[l];
            ReadOnlySpan<byte> values = seg.Slice(i, count);
            i += count;

            var table = JpegHuffmanTable.Build(bits, values);
            if (tc == 0)
                dc[th] = table;
            else
                ac[th] = table;
        }
    }

    private static Component[] ReadFrameHeader(ReadOnlySpan<byte> seg, out int width, out int height)
    {
        int precision = seg[0];
        if (precision != 8)
            throw new NotSupportedException($"Only 8-bit JPEG is supported (got {precision}-bit).");

        height = BinaryPrimitives.ReadUInt16BigEndian(seg.Slice(1, 2));
        width = BinaryPrimitives.ReadUInt16BigEndian(seg.Slice(3, 2));
        int count = seg[5];
        if (count is not (1 or 3))
            throw new NotSupportedException($"Only 1- or 3-component JPEG is supported (got {count}).");
        if (width <= 0 || height <= 0)
            throw new FormatException("JPEG frame has non-positive dimensions.");

        var components = new Component[count];
        for (int c = 0; c < count; c++)
        {
            int off = 6 + c * 3;
            components[c] = new Component
            {
                Id = seg[off],
                H = seg[off + 1] >> 4,
                V = seg[off + 1] & 0x0F,
                QuantId = seg[off + 2],
            };
            if (components[c].H is < 1 or > 4 || components[c].V is < 1 or > 4)
                throw new FormatException("JPEG component has an invalid sampling factor.");
        }
        return components;
    }

    private static Component[] ReadScanHeader(
        ReadOnlySpan<byte> seg, Component[] components, JpegHuffmanTable[] dc, JpegHuffmanTable[] ac,
        bool progressive, out int ss, out int se, out int ah, out int al)
    {
        int ns = seg[0];
        var scan = new Component[ns];
        for (int s = 0; s < ns; s++)
        {
            int id = seg[1 + s * 2];
            int tables = seg[2 + s * 2];
            Component comp = FindComponent(components, id);
            comp.DcTableId = tables >> 4;
            comp.AcTableId = tables & 0x0F;
            scan[s] = comp;
        }

        int p = 1 + ns * 2;
        ss = seg[p];
        se = seg[p + 1];
        ah = seg[p + 2] >> 4;
        al = seg[p + 2] & 0x0F;

        if (!progressive)
        {
            ss = 0;
            se = 63;
            ah = 0;
            al = 0;
        }
        return scan;
    }

    private static Component FindComponent(Component[] components, int id)
    {
        foreach (Component c in components)
            if (c.Id == id)
                return c;
        throw new FormatException($"JPEG scan references unknown component id {id}.");
    }

    /// <summary>Finds the next real marker (skipping entropy data, stuffed bytes and RSTn).</summary>
    private static int FindNextMarker(byte[] bytes, int start)
    {
        int i = start;
        while (i + 1 < bytes.Length)
        {
            if (bytes[i] == 0xFF)
            {
                int m = bytes[i + 1];
                if (m == 0x00 || (m >= JpegTables.MarkerRst0 && m <= JpegTables.MarkerRst7))
                {
                    i += 2;
                    continue;
                }
                if (m == 0xFF)
                {
                    i++;
                    continue;
                }
                return i;
            }
            i++;
        }
        return bytes.Length;
    }

    /// <summary>
    /// Walks marker segments to the first frame header and stops there. It steps
    /// by declared segment lengths, never enters entropy-coded data, and never
    /// allocates in proportion to the input.
    /// </summary>
    public static bool TryInspect(ReadOnlySpan<byte> data, out ImageInfo? info)
    {
        info = null;
        if (!IsJpeg(data))
            return false;

        int pos = 2;
        while (pos + 1 < data.Length)
        {
            // Fill bytes are legal between segments, so advance one byte at a
            // time rather than assuming the next byte opens a marker.
            if (data[pos] != 0xFF)
            {
                pos++;
                continue;
            }

            byte marker = data[pos + 1];
            pos += 2;

            if (marker == 0xFF || marker == 0x01 || marker is >= 0xD0 and <= 0xD8)
                continue;
            if (marker == 0xD9 || marker == 0xDA)
                return false; // end of image, or entropy data, before any frame

            if (pos + 2 > data.Length)
                return false;

            int length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(pos, 2));
            if (length < 2 || pos + length > data.Length)
                return false;

            // Every SOFn but DHT (0xC4), the reserved 0xC8, and DAC (0xCC).
            if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
            {
                ReadOnlySpan<byte> frame = data.Slice(pos + 2, length - 2);
                if (frame.Length < 6)
                    return false;

                int precision = frame[0];
                int height = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(1, 2));
                int width = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(3, 2));
                int components = frame[5];

                if (width <= 0 || height <= 0 || components <= 0 || precision <= 0)
                    return false;

                info = new ImageInfo(width, height, components, precision, "JPEG", "image/jpeg");
                return true;
            }

            pos += length;
        }

        return false;
    }
}
