using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Broiler.Media.Image.Managed;

/// <summary>
/// Pure-managed PNG decoder. Handles every PNG colour type (grayscale, RGB,
/// palette, grayscale+alpha, RGBA) at bit depths 1/2/4/8/16, both non-interlaced
/// and Adam7-interlaced, including <c>tRNS</c> transparency, and expands them to
/// straight-alpha 8-bit RGBA. DEFLATE is handled by the in-box
/// <see cref="ZLibStream"/>, so the decoder has no external dependency.
/// </summary>
internal static class PngDecoder
{
    // Adam7 pass geometry: starting offset and stride for each of the seven passes.
    private static ReadOnlySpan<int> Adam7XStart => [0, 4, 0, 2, 0, 1, 0];
    private static ReadOnlySpan<int> Adam7YStart => [0, 0, 4, 0, 2, 0, 1];
    private static ReadOnlySpan<int> Adam7XStep => [8, 8, 4, 4, 2, 2, 1];
    private static ReadOnlySpan<int> Adam7YStep => [8, 8, 8, 4, 4, 2, 2];

    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    /// <summary>True if <paramref name="data"/> starts with the 8-byte PNG signature.</summary>
    public static bool IsPng(ReadOnlySpan<byte> data) =>
        data.Length >= 8 && data[..8].SequenceEqual(Signature);

    /// <summary>Decodes the PNG (or an APNG's default image) into a single RGBA buffer.</summary>
    public static ImageBuffer Decode(ReadOnlySpan<byte> data)
    {
        PngData png = Parse(data);
        return new ImageBuffer(png.Width, png.Height, DecodeImageData(png.DefaultIdat, ImageContextFor(png, png.Width, png.Height), png.Interlace));
    }

    /// <summary>
    /// Decodes a PNG or APNG into a frame sequence. A plain PNG yields a single
    /// frame; an APNG yields its composited animation frames with per-frame delays.
    /// </summary>
    public static ImageSequence DecodeAnimation(ReadOnlySpan<byte> data)
    {
        PngData png = Parse(data);
        if (!png.IsAnimated || png.Frames.Count == 0)
            return ImageSequence.Static(
                new ImageBuffer(png.Width, png.Height,
                    DecodeImageData(png.DefaultIdat, ImageContextFor(png, png.Width, png.Height), png.Interlace)));

        return Composite(png);
    }

    /// <summary>Inflate → unfilter → expand a single image (or APNG frame) into RGBA.</summary>
    private static byte[] DecodeImageData(MemoryStream idat, in ImageContext ctx, int interlace)
    {
        int bitsPerPixel = ctx.BitDepth * SamplesPerPixel(ctx.ColorType);
        int bpp = (bitsPerPixel + 7) / 8;
        byte[] rgba = new byte[(long)ctx.Width * ctx.Height * 4];

        if (interlace == 0)
        {
            int stride = (ctx.Width * bitsPerPixel + 7) / 8;
            byte[] raw = Inflate(idat, (long)(stride + 1) * ctx.Height);
            Unfilter(raw, 0, ctx.Height, stride, bpp);
            ExpandPass(raw, 0, rgba, ctx, ctx.Width, ctx.Height, stride, xStart: 0, yStart: 0, xStep: 1, yStep: 1);
        }
        else
        {
            DecodeAdam7(idat, rgba, ctx, bitsPerPixel, bpp);
        }
        return rgba;
    }

    private static ImageContext ImageContextFor(PngData png, int width, int height) =>
        new(width, height, png.BitDepth, png.ColorType, png.Palette, png.PaletteAlpha,
            png.HaveTrnsColor, png.TrnsGray, png.TrnsR, png.TrnsG, png.TrnsB);

    // ---- Chunk parsing (PNG + APNG) -------------------------------------

    private sealed class PngData
    {
        public int Width, Height, BitDepth, ColorType, Interlace;
        public byte[]? Palette, PaletteAlpha;
        public bool HaveTrnsColor;
        public ushort TrnsGray, TrnsR, TrnsG, TrnsB;
        public readonly MemoryStream DefaultIdat = new();
        public bool IsAnimated;
        public int NumPlays;
        public readonly List<ApngFrame> Frames = [];
    }

    private sealed class ApngFrame
    {
        public int Width, Height, XOffset, YOffset;
        public int DelayNum, DelayDen;
        public byte DisposeOp, BlendOp;
        public readonly MemoryStream Data = new();
    }

    private static PngData Parse(ReadOnlySpan<byte> data)
    {
        if (!IsPng(data))
            throw new FormatException("Data does not start with a PNG signature.");

        var png = new PngData();
        bool seenIhdr = false, seenIdat = false, idatIsFrame0 = false;
        ApngFrame? current = null;

        int offset = 8;
        while (true)
        {
            if (offset + 8 > data.Length)
                throw new FormatException("Truncated PNG: ran out of chunks before IEND.");

            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4)));
            if (length < 0 || offset + 12 + length > data.Length)
                throw new FormatException("Truncated or corrupt PNG chunk.");

            ReadOnlySpan<byte> type = data.Slice(offset + 4, 4);
            ReadOnlySpan<byte> chunk = data.Slice(offset + 8, length);
            uint storedCrc = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset + 8 + length, 4));
            if (storedCrc != Crc32.Compute(data.Slice(offset + 4, 4 + length)))
                throw new FormatException($"PNG chunk '{AsciiType(type)}' failed CRC check.");

            offset += 12 + length;

            if (TypeIs(type, "IHDR"))
            {
                ParseIhdr(chunk, png);
                seenIhdr = true;
            }
            else if (TypeIs(type, "PLTE"))
            {
                if (chunk.Length % 3 != 0)
                    throw new FormatException("PNG PLTE chunk length is not a multiple of 3.");
                png.Palette = chunk.ToArray();
            }
            else if (TypeIs(type, "tRNS"))
            {
                ParseTrns(chunk, png);
            }
            else if (TypeIs(type, "acTL"))
            {
                if (chunk.Length >= 8)
                {
                    png.IsAnimated = true;
                    png.NumPlays = checked((int)BinaryPrimitives.ReadUInt32BigEndian(chunk.Slice(4, 4)));
                }
            }
            else if (TypeIs(type, "fcTL"))
            {
                if (!seenIdat)
                    idatIsFrame0 = true; // the first fcTL precedes IDAT ⇒ IDAT is frame 0
                current = ParseFctl(chunk);
                png.Frames.Add(current);
            }
            else if (TypeIs(type, "IDAT"))
            {
                seenIdat = true;
                png.DefaultIdat.Write(chunk);
                if (idatIsFrame0 && png.Frames.Count > 0)
                    png.Frames[0].Data.Write(chunk);
            }
            else if (TypeIs(type, "fdAT"))
            {
                if (current is null || chunk.Length < 4)
                    throw new FormatException("APNG fdAT chunk without a preceding fcTL.");
                current.Data.Write(chunk[4..]); // skip the 4-byte sequence number
            }
            else if (TypeIs(type, "IEND"))
            {
                break;
            }
            // Other ancillary chunks are ignored.
        }

        if (!seenIhdr)
            throw new FormatException("PNG is missing its IHDR chunk.");
        if (png.ColorType == 3 && png.Palette is null)
            throw new FormatException("Palette PNG is missing its PLTE chunk.");

        return png;
    }

    private static void ParseIhdr(ReadOnlySpan<byte> chunk, PngData png)
    {
        if (chunk.Length != 13)
            throw new FormatException("PNG IHDR chunk must be 13 bytes.");
        png.Width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(chunk[..4]));
        png.Height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(chunk.Slice(4, 4)));
        png.BitDepth = chunk[8];
        png.ColorType = chunk[9];
        int compression = chunk[10];
        int filterMethod = chunk[11];
        png.Interlace = chunk[12];

        if (png.Width <= 0 || png.Height <= 0)
            throw new FormatException("PNG image has non-positive dimensions.");
        if (compression != 0 || filterMethod != 0)
            throw new FormatException("PNG uses an unsupported compression or filter method.");
        if (png.Interlace is not (0 or 1))
            throw new FormatException($"PNG uses an unknown interlace method {png.Interlace}.");
        ValidateColorAndDepth(png.ColorType, png.BitDepth);
    }

    private static void ParseTrns(ReadOnlySpan<byte> chunk, PngData png)
    {
        switch (png.ColorType)
        {
            case 3:
                png.PaletteAlpha = chunk.ToArray();
                break;
            case 0:
                if (chunk.Length >= 2)
                {
                    png.TrnsGray = BinaryPrimitives.ReadUInt16BigEndian(chunk[..2]);
                    png.HaveTrnsColor = true;
                }
                break;
            case 2:
                if (chunk.Length >= 6)
                {
                    png.TrnsR = BinaryPrimitives.ReadUInt16BigEndian(chunk[..2]);
                    png.TrnsG = BinaryPrimitives.ReadUInt16BigEndian(chunk.Slice(2, 2));
                    png.TrnsB = BinaryPrimitives.ReadUInt16BigEndian(chunk.Slice(4, 2));
                    png.HaveTrnsColor = true;
                }
                break;
        }
    }

    private static ApngFrame ParseFctl(ReadOnlySpan<byte> chunk)
    {
        if (chunk.Length < 26)
            throw new FormatException("APNG fcTL chunk is too short.");
        return new ApngFrame
        {
            Width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(chunk.Slice(4, 4))),
            Height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(chunk.Slice(8, 4))),
            XOffset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(chunk.Slice(12, 4))),
            YOffset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(chunk.Slice(16, 4))),
            DelayNum = BinaryPrimitives.ReadUInt16BigEndian(chunk.Slice(20, 2)),
            DelayDen = BinaryPrimitives.ReadUInt16BigEndian(chunk.Slice(22, 2)),
            DisposeOp = chunk[24],
            BlendOp = chunk[25],
        };
    }

    // ---- APNG compositing -----------------------------------------------

    private const byte DisposeNone = 0;
    private const byte DisposeBackground = 1;
    private const byte DisposePrevious = 2;
    private const byte BlendSource = 0;

    private static ImageSequence Composite(PngData png)
    {
        int w = png.Width, h = png.Height;
        byte[] canvas = new byte[w * h * 4]; // starts fully transparent
        var frames = new List<ImageFrame>(png.Frames.Count);

        for (int fi = 0; fi < png.Frames.Count; fi++)
        {
            ApngFrame f = png.Frames[fi];
            if (f.Width <= 0 || f.Height <= 0 || f.XOffset < 0 || f.YOffset < 0 ||
                f.XOffset + f.Width > w || f.YOffset + f.Height > h)
                throw new FormatException("APNG frame region lies outside the canvas.");

            byte[] sub = DecodeImageData(f.Data, ImageContextFor(png, f.Width, f.Height), png.Interlace);

            byte dispose = f.DisposeOp;
            if (fi == 0 && dispose == DisposePrevious)
                dispose = DisposeBackground; // no previous state for the first frame

            byte[]? saved = dispose == DisposePrevious
                ? SnapshotRegion(canvas, w, f.XOffset, f.YOffset, f.Width, f.Height)
                : null;

            Blend(canvas, w, sub, f.XOffset, f.YOffset, f.Width, f.Height, f.BlendOp);
            var snapshot = new ImageBuffer(w, h, (byte[])canvas.Clone());
            frames.Add(new ImageFrame(snapshot, f.DelayNum, f.DelayDen));

            if (dispose == DisposeBackground)
                ClearRegion(canvas, w, f.XOffset, f.YOffset, f.Width, f.Height);
            else if (dispose == DisposePrevious)
                RestoreRegion(canvas, w, saved!, f.XOffset, f.YOffset, f.Width, f.Height);
        }

        return new ImageSequence(frames, w, h, png.NumPlays);
    }

    private static void Blend(byte[] canvas, int canvasW, byte[] sub, int x0, int y0, int fw, int fh, byte blendOp)
    {
        for (int y = 0; y < fh; y++)
            for (int x = 0; x < fw; x++)
            {
                int s = (y * fw + x) * 4;
                int d = ((y0 + y) * canvasW + (x0 + x)) * 4;
                if (blendOp == BlendSource)
                {
                    canvas[d] = sub[s];
                    canvas[d + 1] = sub[s + 1];
                    canvas[d + 2] = sub[s + 2];
                    canvas[d + 3] = sub[s + 3];
                }
                else
                {
                    OverBlend(canvas, d, sub, s);
                }
            }
    }

    /// <summary>Porter-Duff "over" of a straight-alpha source onto a straight-alpha destination.</summary>
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
            return; // fully transparent source leaves the canvas unchanged

        int da = dst[d + 3];
        int outScaled = sa * 255 + da * (255 - sa); // out alpha × 255
        if (outScaled == 0)
        {
            dst[d] = dst[d + 1] = dst[d + 2] = dst[d + 3] = 0;
            return;
        }

        for (int ch = 0; ch < 3; ch++)
        {
            int num = src[s + ch] * sa * 255 + dst[d + ch] * da * (255 - sa);
            dst[d + ch] = (byte)((num + outScaled / 2) / outScaled);
        }
        dst[d + 3] = (byte)((outScaled + 127) / 255);
    }

    private static byte[] SnapshotRegion(byte[] canvas, int canvasW, int x0, int y0, int fw, int fh)
    {
        byte[] snap = new byte[fw * fh * 4];
        for (int y = 0; y < fh; y++)
            Array.Copy(canvas, ((y0 + y) * canvasW + x0) * 4, snap, y * fw * 4, fw * 4);
        return snap;
    }

    private static void RestoreRegion(byte[] canvas, int canvasW, byte[] snap, int x0, int y0, int fw, int fh)
    {
        for (int y = 0; y < fh; y++)
            Array.Copy(snap, y * fw * 4, canvas, ((y0 + y) * canvasW + x0) * 4, fw * 4);
    }

    private static void ClearRegion(byte[] canvas, int canvasW, int x0, int y0, int fw, int fh)
    {
        for (int y = 0; y < fh; y++)
            Array.Clear(canvas, ((y0 + y) * canvasW + x0) * 4, fw * 4);
    }

    /// <summary>Per-image parameters needed to turn raw samples into RGBA pixels.</summary>
    private readonly struct ImageContext(
        int width, int height, int bitDepth, int colorType, byte[]? palette, byte[]? paletteAlpha,
        bool haveTrnsColor, ushort trnsGray, ushort trnsR, ushort trnsG, ushort trnsB)
    {
        public readonly int Width = width;
        public readonly int Height = height;
        public readonly int BitDepth = bitDepth;
        public readonly int ColorType = colorType;
        public readonly int MaxSample = (1 << bitDepth) - 1;
        public readonly byte[]? Palette = palette;
        public readonly byte[]? PaletteAlpha = paletteAlpha;
        public readonly bool HaveTrnsColor = haveTrnsColor;
        public readonly ushort TrnsGray = trnsGray;
        public readonly ushort TrnsR = trnsR;
        public readonly ushort TrnsG = trnsG;
        public readonly ushort TrnsB = trnsB;
    }

    /// <summary>Decodes the seven Adam7 reduced images and scatters their pixels into <paramref name="rgba"/>.</summary>
    private static void DecodeAdam7(MemoryStream idat, byte[] rgba, ImageContext ctx, int bitsPerPixel, int bpp)
    {
        Span<int> passW = stackalloc int[7];
        Span<int> passH = stackalloc int[7];
        Span<int> passStride = stackalloc int[7];

        long total = 0;
        for (int p = 0; p < 7; p++)
        {
            int w = (ctx.Width - Adam7XStart[p] + Adam7XStep[p] - 1) / Adam7XStep[p];
            int h = (ctx.Height - Adam7YStart[p] + Adam7YStep[p] - 1) / Adam7YStep[p];
            if (w <= 0 || h <= 0)
                continue;
            passW[p] = w;
            passH[p] = h;
            passStride[p] = (w * bitsPerPixel + 7) / 8;
            total += (long)(passStride[p] + 1) * h;
        }

        byte[] raw = Inflate(idat, total);

        int offset = 0;
        for (int p = 0; p < 7; p++)
        {
            if (passW[p] == 0)
                continue;
            Unfilter(raw, offset, passH[p], passStride[p], bpp);
            ExpandPass(raw, offset, rgba, ctx, passW[p], passH[p], passStride[p],
                Adam7XStart[p], Adam7YStart[p], Adam7XStep[p], Adam7YStep[p]);
            offset += (passStride[p] + 1) * passH[p];
        }
    }

    private static byte[] Inflate(MemoryStream idat, long expectedLength)
    {
        if (expectedLength <= 0 || expectedLength > int.MaxValue)
            throw new FormatException("PNG decoded size is out of range.");

        idat.Position = 0;
        byte[] raw = new byte[expectedLength];
        using var zlib = new ZLibStream(idat, CompressionMode.Decompress, leaveOpen: true);
        zlib.ReadExactly(raw, 0, raw.Length);
        return raw;
    }

    /// <summary>
    /// Reverses PNG scanline filtering in place over a contiguous region of
    /// <paramref name="height"/> rows beginning at <paramref name="baseOffset"/>;
    /// each row is prefixed by its filter byte. Works for a whole non-interlaced
    /// image or a single Adam7 pass.
    /// </summary>
    private static void Unfilter(byte[] raw, int baseOffset, int height, int stride, int bpp)
    {
        int rowLen = stride + 1;
        for (int y = 0; y < height; y++)
        {
            int rowStart = baseOffset + y * rowLen;
            int filter = raw[rowStart];
            int cur = rowStart + 1;            // first data byte of this row
            int prev = cur - rowLen;           // first data byte of previous row

            for (int x = 0; x < stride; x++)
            {
                int a = x >= bpp ? raw[cur + x - bpp] : 0;          // left
                int b = y > 0 ? raw[prev + x] : 0;                 // up
                int c = (y > 0 && x >= bpp) ? raw[prev + x - bpp] : 0; // upper-left
                int value = raw[cur + x];

                value = filter switch
                {
                    0 => value,
                    1 => value + a,
                    2 => value + b,
                    3 => value + ((a + b) >> 1),
                    4 => value + Paeth(a, b, c),
                    _ => throw new FormatException($"PNG row {y} uses unknown filter type {filter}."),
                };
                raw[cur + x] = (byte)value;
            }
        }
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

    /// <summary>
    /// Expands one reduced image (a non-interlaced full image is the trivial pass
    /// with step 1) into <paramref name="rgba"/>, scattering pixel <c>(i, j)</c> of
    /// the pass to <c>(xStart + i·xStep, yStart + j·yStep)</c> of the full image.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The one parallel pass of a PNG decode</b> — multithreading roadmap item #7. Every row is
    /// addressed by its own <c>rowData</c> offset into the already-unfiltered buffer and writes only
    /// its own destination row, so rows have no dependency on one another at all; the sequential
    /// part of PNG (inflate, and the Up/Paeth filters that read the reconstructed previous row) has
    /// happened by the time this runs. Bands are handed out by
    /// <see cref="ImageDecodeParallelism"/>, which runs them inline when the pass is too small to
    /// be worth a thread.
    /// </para>
    /// <para>
    /// This holds for the interlaced case unchanged: an Adam7 pass writes a strided subset of the
    /// full image, but two rows of one pass still land on two different <c>y</c>, so bands of a
    /// pass remain disjoint. The seven passes themselves stay sequential — they overwrite each
    /// other's pixels by design.
    /// </para>
    /// </remarks>
    private static void ExpandPass(
        byte[] raw, int baseOffset, byte[] rgba, in ImageContext ctx,
        int passWidth, int passHeight, int stride,
        int xStart, int yStart, int xStep, int yStep)
    {
        int rowLen = stride + 1;
        ImageContext context = ctx; // `in` parameters cannot be captured by the band delegate.

        ImageDecodeParallelism.For(
            0,
            passHeight,
            RowsPerBand(passWidth),
            (fromRow, toRow) =>
            {
                for (int j = fromRow; j < toRow; j++)
                {
                    int y = yStart + j * yStep;
                    int rowData = baseOffset + j * rowLen + 1;
                    var reader = new SampleReader(raw, rowData, context.BitDepth);

                    for (int i = 0; i < passWidth; i++)
                    {
                        ExtractPixel(ref reader, context, out byte r, out byte g, out byte b, out byte a);

                        int x = xStart + i * xStep;
                        int dst = (y * context.Width + x) * 4;
                        rgba[dst] = r;
                        rgba[dst + 1] = g;
                        rgba[dst + 2] = b;
                        rgba[dst + 3] = a;
                    }
                }
            });
    }

    /// <summary>
    /// Smallest number of rows worth giving a thread, expressed as a pixel budget so the answer
    /// tracks row width rather than a row count that means something different at 16px and 4096px.
    /// </summary>
    private static int RowsPerBand(int passWidth) =>
        Math.Max(1, MinimumPixelsPerBand / Math.Max(1, passWidth));

    /// <summary>
    /// Pixels a band must be worth before the pass is split. Below roughly this much work the
    /// scheduler round trip costs more than the expansion does, and page images at this size —
    /// icons, spacers, sprites — are common enough that paying it would show up.
    /// </summary>
    private const int MinimumPixelsPerBand = 32 * 1024;

    private static void ExtractPixel(
        ref SampleReader reader, in ImageContext ctx, out byte r, out byte g, out byte b, out byte a)
    {
        int maxSample = ctx.MaxSample;
        a = 255;

        switch (ctx.ColorType)
        {
            case 0: // grayscale
            {
                int s = reader.Next();
                if (ctx.HaveTrnsColor && s == ctx.TrnsGray) a = 0;
                byte v = Scale(s, maxSample);
                r = g = b = v;
                break;
            }
            case 2: // RGB
            {
                int sr = reader.Next(), sg = reader.Next(), sb = reader.Next();
                if (ctx.HaveTrnsColor && sr == ctx.TrnsR && sg == ctx.TrnsG && sb == ctx.TrnsB) a = 0;
                r = Scale(sr, maxSample);
                g = Scale(sg, maxSample);
                b = Scale(sb, maxSample);
                break;
            }
            case 3: // palette
            {
                int idx = reader.Next();
                int pbase = idx * 3;
                if (ctx.Palette is null || pbase + 2 >= ctx.Palette.Length)
                    throw new FormatException("PNG palette index out of range.");
                r = ctx.Palette[pbase];
                g = ctx.Palette[pbase + 1];
                b = ctx.Palette[pbase + 2];
                if (ctx.PaletteAlpha is not null && idx < ctx.PaletteAlpha.Length)
                    a = ctx.PaletteAlpha[idx];
                break;
            }
            case 4: // grayscale + alpha
            {
                int s = reader.Next(), sa = reader.Next();
                byte v = Scale(s, maxSample);
                r = g = b = v;
                a = Scale(sa, maxSample);
                break;
            }
            default: // 6: RGBA
            {
                int sr = reader.Next(), sg = reader.Next(), sb = reader.Next(), sa = reader.Next();
                r = Scale(sr, maxSample);
                g = Scale(sg, maxSample);
                b = Scale(sb, maxSample);
                a = Scale(sa, maxSample);
                break;
            }
        }
    }

    /// <summary>Scales a sample at the source bit depth into 0..255.</summary>
    private static byte Scale(int sample, int maxSample)
    {
        if (maxSample == 255) return (byte)sample;
        if (maxSample == 65535) return (byte)(sample >> 8);
        // 1/2/4-bit: expand to the full range.
        return (byte)(sample * 255 / maxSample);
    }

    /// <summary>Pulls successive samples from a scanline, handling sub-byte and 16-bit depths.</summary>
    private ref struct SampleReader
    {
        private readonly byte[] _data;
        private readonly int _bitDepth;
        private int _bytePos;
        private int _bitPos; // bits already consumed in the current byte (MSB first)

        public SampleReader(byte[] data, int start, int bitDepth)
        {
            _data = data;
            _bitDepth = bitDepth;
            _bytePos = start;
            _bitPos = 0;
        }

        public int Next()
        {
            switch (_bitDepth)
            {
                case 8:
                    return _data[_bytePos++];
                case 16:
                {
                    int hi = _data[_bytePos];
                    int lo = _data[_bytePos + 1];
                    _bytePos += 2;
                    return (hi << 8) | lo;
                }
                default: // 1, 2, 4
                {
                    int shift = 8 - _bitPos - _bitDepth;
                    int mask = (1 << _bitDepth) - 1;
                    int value = (_data[_bytePos] >> shift) & mask;
                    _bitPos += _bitDepth;
                    if (_bitPos == 8)
                    {
                        _bitPos = 0;
                        _bytePos++;
                    }
                    return value;
                }
            }
        }
    }

    private static int SamplesPerPixel(int colorType) => colorType switch
    {
        0 => 1, // grayscale
        2 => 3, // RGB
        3 => 1, // palette index
        4 => 2, // grayscale + alpha
        6 => 4, // RGBA
        _ => throw new FormatException($"Unknown PNG colour type {colorType}."),
    };

    private static void ValidateColorAndDepth(int colorType, int bitDepth)
    {
        bool ok = colorType switch
        {
            0 => bitDepth is 1 or 2 or 4 or 8 or 16,
            3 => bitDepth is 1 or 2 or 4 or 8,
            2 or 4 or 6 => bitDepth is 8 or 16,
            _ => false,
        };
        if (!ok)
            throw new FormatException($"PNG colour type {colorType} with bit depth {bitDepth} is invalid.");
    }

    private static bool TypeIs(ReadOnlySpan<byte> type, string name) =>
        type.Length == 4 && type[0] == name[0] && type[1] == name[1] && type[2] == name[2] && type[3] == name[3];

    private static string AsciiType(ReadOnlySpan<byte> type)
    {
        Span<char> chars = stackalloc char[type.Length];
        for (int i = 0; i < type.Length; i++)
            chars[i] = type[i] is >= 32 and < 127 ? (char)type[i] : '?';
        return new string(chars);
    }

    /// <summary>
    /// Reads the IHDR chunk and stops. The format requires IHDR to be the first
    /// chunk after the signature, so this reads one chunk header at a fixed
    /// offset and never walks the file - which is the difference between
    /// inspecting a hostile PNG and parsing one.
    /// </summary>
    public static bool TryInspect(ReadOnlySpan<byte> data, out ImageInfo? info)
    {
        info = null;
        if (!IsPng(data) || data.Length < 8 + 8 + 13)
            return false;

        if (BinaryPrimitives.ReadUInt32BigEndian(data.Slice(8, 4)) != 13 ||
            !TypeIs(data.Slice(12, 4), "IHDR"))
        {
            return false;
        }

        ReadOnlySpan<byte> chunk = data.Slice(16, 13);
        uint width = BinaryPrimitives.ReadUInt32BigEndian(chunk[..4]);
        uint height = BinaryPrimitives.ReadUInt32BigEndian(chunk.Slice(4, 4));
        int bitDepth = chunk[8];

        // Stored components, not decoded ones: a palettized PNG stores one index
        // per pixel and decodes to four channels, and this reports the one.
        int components = chunk[9] switch
        {
            0 => 1,
            2 => 3,
            3 => 1,
            4 => 2,
            6 => 4,
            _ => 0,
        };

        if (width is 0 or > int.MaxValue || height is 0 or > int.MaxValue || components == 0 || bitDepth == 0)
            return false;

        info = new ImageInfo((int)width, (int)height, components, bitDepth, "PNG", "image/png");
        return true;
    }
}
