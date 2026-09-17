using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Broiler.Media.Image.Managed.CcittFax;

namespace Broiler.Media.Image.Managed.Jbig2;

/// <summary>The result of decoding one JBIG2 stream.</summary>
public readonly record struct Jbig2Result(
    Jbig2DecodeOutcome Outcome,
    int Width,
    int Height,
    Jbig2Bitmap? Page,
    string? Failure)
{
    public static Jbig2Result Success(Jbig2Bitmap page) =>
        new(Jbig2DecodeOutcome.Decoded, page.Width, page.Height, page, null);

    public static Jbig2Result Failed(Jbig2DecodeOutcome outcome, string message) =>
        new(outcome, 0, 0, null, message);

    /// <summary>
    /// Packs the page pixels into 1-bit per pixel rows.
    /// By default (invert = false), bit 1 means black and 0 means white (JBIG2 standard).
    /// If invert is true, bit 0 means black and 1 means white (PDF default image sample polarity).
    /// </summary>
    public byte[]? GetPackedBits(bool invert = false)
    {
        if (Page is null)
            return null;

        int stride = (Width + 7) / 8;
        var packed = new byte[(long)stride * Height];

        for (int y = 0; y < Height; y++)
        {
            int row = y * stride;
            int source = y * Width;
            for (int x = 0; x < Width; x++)
            {
                if (Page.Pixels[source + x] != 0)
                    packed[row + (x >> 3)] |= (byte)(0x80 >> (x & 7));
            }
        }

        if (invert)
        {
            for (int i = 0; i < packed.Length; i++)
                packed[i] = (byte)~packed[i];
        }

        return packed;
    }

    /// <summary>Expands the black and white bitmap to an 8-bit RGBA ImageBuffer.</summary>
    public ImageBuffer? ToImageBuffer()
    {
        if (Page is null)
            return null;

        var rgba = new byte[(long)Width * Height * 4];
        int dest = 0;
        byte[] pixels = Page.Pixels;

        for (int i = 0; i < pixels.Length; i++)
        {
            byte v = pixels[i] != 0 ? (byte)0 : (byte)255; // 1 = black (0), 0 = white (255)
            rgba[dest++] = v;
            rgba[dest++] = v;
            rgba[dest++] = v;
            rgba[dest++] = 255;
        }

        return new ImageBuffer(Width, Height, rgba);
    }
}

/// <summary>
/// Decodes JBIG2 streams: generic regions (MMR and arithmetic), symbol dictionaries,
/// text regions, and refinement regions.
/// </summary>
public static class Jbig2Decoder
{
    private const int CombineOr = 0;
    private const int CombineReplace = 4;

    private static readonly byte[] FileHeader = [0x97, 0x4A, 0x42, 0x32, 0x0D, 0x0A, 0x1A, 0x0A];

    public static bool IsJbig2(ReadOnlySpan<byte> data) =>
        data.Length >= 8 && data[..8].SequenceEqual(FileHeader);

    public static Jbig2Result Decode(
        ReadOnlySpan<byte> data,
        ReadOnlySpan<byte> globals = default,
        long maxDecodedBytes = 64 * 1024 * 1024,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Check for standalone file header
        if (data.Length >= 9 && data[..8].SequenceEqual(FileHeader))
        {
            int offset = 8;
            byte fileFlags = data[offset++];
            if ((fileFlags & 0x02) == 0 && offset + 4 <= data.Length)
                offset += 4; // Skip 4-byte page count

            data = data[offset..];
        }

        if (!Jbig2SegmentReader.TryRead(data, out List<Jbig2Segment> segments, out string? error))
            return Jbig2Result.Failed(Jbig2DecodeOutcome.Malformed, error ?? "Malformed JBIG2 segments.");

        var globalSegments = new List<Jbig2Segment>();
        ReadOnlyMemory<byte> globalData = default;
        if (!globals.IsEmpty)
        {
            if (Jbig2SegmentReader.TryRead(globals, out List<Jbig2Segment> parsed, out _))
            {
                globalSegments = parsed;
                globalData = globals.ToArray();
            }
        }

        if (Unsupported(segments, globalSegments, data) is string refusal)
        {
            return Jbig2Result.Failed(
                Jbig2DecodeOutcome.Unsupported,
                Refuse(refusal, segments));
        }

        return Compose(data.ToArray(), segments, globalData, globalSegments, maxDecodedBytes, cancellationToken);
    }

    private static string? Unsupported(List<Jbig2Segment> segments, List<Jbig2Segment> globals, ReadOnlySpan<byte> data)
    {
        var reasons = new List<string>();
        bool anyRegion = false;

        foreach (Jbig2Segment segment in segments)
        {
            if (segment.IsGenericRegion)
            {
                anyRegion = true;
                if (!Jbig2SegmentReader.TryReadGenericRegion(data, segment, out Jbig2GenericRegion region, out _))
                    continue;

                if (region.CombinationOperator is not (CombineOr or CombineReplace))
                {
                    reasons.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"a generic region composites with operator {region.CombinationOperator}"));
                }

                continue;
            }

            if (segment.IsImmediateTextRegion || segment.IsImmediateRefinementRegion)
            {
                anyRegion = true;
                continue;
            }

            if (segment.IsStructural || segment.IsSymbolDictionary)
                continue;

            reasons.Add($"it holds a {segment.Describe()}, whose decoder is not written");
            break;
        }

        foreach (Jbig2Segment segment in globals)
        {
            if (segment.IsStructural || segment.IsSymbolDictionary)
                continue;

            reasons.Add($"its JBIG2Globals hold a {segment.Describe()}, whose decoder is not written");
            break;
        }

        if (!anyRegion && reasons.Count == 0)
            reasons.Add("it holds no region to decode");

        return reasons.Count == 0 ? null : string.Join("; ", reasons);
    }

    private static string Refuse(string reasons, List<Jbig2Segment> segments) =>
        $"The JBIG2 stream contains unsupported features: {reasons}. " +
        $"The stream holds {Jbig2SegmentReader.Describe(segments)}.";

    private static Jbig2Result Compose(
        ReadOnlyMemory<byte> data,
        List<Jbig2Segment> segments,
        ReadOnlyMemory<byte> globalData,
        List<Jbig2Segment> globals,
        long maxDecodedBytes,
        CancellationToken cancellationToken)
    {
        (ReadOnlyMemory<byte> Buffer, List<Jbig2Segment> Segments)[] sources =
            [(globalData, globals), (data, segments)];

        if (Size(data.Span, segments, sources) is not (int pageWidth, int pageHeight, byte pageDefault))
            return Jbig2Result.Failed(Jbig2DecodeOutcome.Malformed, "The JBIG2 stream declares no page size and no region to take one from.");

        long area = (long)pageWidth * pageHeight;
        if (area > maxDecodedBytes)
            return Jbig2Result.Failed(Jbig2DecodeOutcome.TooLarge, "A JBIG2 page would exceed the decoded-byte ceiling.");

        var page = Jbig2Bitmap.Blank(pageWidth, pageHeight, pageDefault);
        var exports = new Dictionary<uint, Jbig2Bitmap[]>();

        foreach ((ReadOnlyMemory<byte> buffer, List<Jbig2Segment> list) in sources)
        {
            foreach (Jbig2Segment segment in list)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Jbig2Result? failure =
                    segment.IsSymbolDictionary ? ReadDictionary(buffer, segment, exports, segments, maxDecodedBytes)
                    : segment.IsImmediateTextRegion ? ReadTextRegion(buffer, segment, exports, segments, page, maxDecodedBytes)
                    : segment.IsImmediateRefinementRegion ? ReadRefinementRegion(buffer, segment, segments, page, maxDecodedBytes)
                    : segment.IsGenericRegion ? ReadGenericRegion(buffer, segment, page, maxDecodedBytes)
                    : null;

                if (failure is Jbig2Result refused)
                    return refused;
            }
        }

        return Jbig2Result.Success(page);
    }

    private static (int Width, int Height, byte Default)? Size(
        ReadOnlySpan<byte> data,
        List<Jbig2Segment> segments,
        (ReadOnlyMemory<byte> Buffer, List<Jbig2Segment> Segments)[] sources)
    {
        int width = 0;
        int height = 0;
        byte fill = 0;

        foreach (Jbig2Segment segment in segments)
        {
            if (segment.Type == 48 &&
                Jbig2SegmentReader.TryReadPageSize(data, segment, out int declaredWidth, out int declaredHeight, out byte declaredFill))
            {
                width = declaredWidth;
                height = declaredHeight;
                fill = declaredFill;
                break;
            }
        }

        foreach ((ReadOnlyMemory<byte> buffer, List<Jbig2Segment> list) in sources)
        {
            foreach (Jbig2Segment segment in list)
            {
                if (!segment.IsRegion ||
                    !Jbig2SegmentReader.TryReadRegionInfo(buffer.Span, segment, out Jbig2RegionInfo info))
                {
                    continue;
                }

                width = Math.Max(width, info.X + info.Width);
                height = Math.Max(height, info.Y + info.Height);
            }
        }

        return width > 0 && height > 0 ? (width, height, fill) : null;
    }

    private static Jbig2Result? ReadDictionary(
        ReadOnlyMemory<byte> buffer,
        in Jbig2Segment segment,
        Dictionary<uint, Jbig2Bitmap[]> exports,
        List<Jbig2Segment> inventory,
        long maxDecodedBytes)
    {
        List<Jbig2Bitmap> input = Gather(segment, exports);

        Jbig2SymbolDictionaryResult result = Jbig2SymbolDictionary.Decode(
            buffer.Slice(segment.DataStart, segment.DataLength), input, maxDecodedBytes);

        switch (result.Outcome)
        {
            case Jbig2DecodeOutcome.Decoded:
                exports[segment.Number] = result.Symbols;
                return null;

            case Jbig2DecodeOutcome.Unsupported:
                return Jbig2Result.Failed(
                    Jbig2DecodeOutcome.Unsupported, Refuse($"it holds {result.Message}", inventory));

            case Jbig2DecodeOutcome.TooLarge:
                return Jbig2Result.Failed(
                    Jbig2DecodeOutcome.TooLarge,
                    "A JBIG2 symbol dictionary would exceed the decoded-byte ceiling.");

            default:
                return Jbig2Result.Failed(Jbig2DecodeOutcome.Malformed, result.Message ?? "Malformed symbol dictionary.");
        }
    }

    private static Jbig2Result? ReadTextRegion(
        ReadOnlyMemory<byte> buffer,
        in Jbig2Segment segment,
        Dictionary<uint, Jbig2Bitmap[]> exports,
        List<Jbig2Segment> inventory,
        Jbig2Bitmap page,
        long maxDecodedBytes)
    {
        List<Jbig2Bitmap> symbols = Gather(segment, exports);

        Jbig2TextRegionResult result = Jbig2TextRegion.Decode(
            buffer.Slice(segment.DataStart, segment.DataLength), symbols, maxDecodedBytes);

        switch (result.Outcome)
        {
            case Jbig2DecodeOutcome.Decoded:
                if (result.CombinationOperator is not (CombineOr or CombineReplace))
                {
                    return Jbig2Result.Failed(
                        Jbig2DecodeOutcome.Unsupported,
                        Refuse(
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"a text region composites with operator {result.CombinationOperator}"),
                            inventory));
                }

                Draw(page, result.Bitmap!, result.X, result.Y, result.CombinationOperator);
                return null;

            case Jbig2DecodeOutcome.Unsupported:
                return Jbig2Result.Failed(
                    Jbig2DecodeOutcome.Unsupported, Refuse($"it holds {result.Message}", inventory));

            case Jbig2DecodeOutcome.TooLarge:
                return Jbig2Result.Failed(
                    Jbig2DecodeOutcome.TooLarge,
                    "A JBIG2 text region would exceed the decoded-byte ceiling.");

            default:
                return Jbig2Result.Failed(Jbig2DecodeOutcome.Malformed, result.Message ?? "Malformed text region.");
        }
    }

    private static Jbig2Result? ReadRefinementRegion(
        ReadOnlyMemory<byte> buffer,
        in Jbig2Segment segment,
        List<Jbig2Segment> inventory,
        Jbig2Bitmap page,
        long maxDecodedBytes)
    {
        if (!Jbig2SegmentReader.TryReadRefinementRegion(
            buffer.Span, segment, out Jbig2RefinementRegion region, out string? error))
        {
            return Jbig2Result.Failed(Jbig2DecodeOutcome.Malformed, error ?? "Malformed refinement region.");
        }

        Jbig2RegionInfo info = region.Info;
        if (info.CombinationOperator is not (CombineOr or CombineReplace))
        {
            return Jbig2Result.Failed(
                Jbig2DecodeOutcome.Unsupported,
                Refuse(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"a refinement region composites with operator {info.CombinationOperator}"),
                    inventory));
        }

        long pixels = (long)info.Width * info.Height;
        if (pixels > maxDecodedBytes)
            return Jbig2Result.Failed(Jbig2DecodeOutcome.TooLarge, "A JBIG2 refinement region would exceed the decoded-byte ceiling.");

        Jbig2Bitmap reference = Extract(page, info.X, info.Y, info.Width, info.Height);
        var decoder = new Entropy.MqDecoder(buffer.Slice(region.DataStart, region.DataLength));
        var contexts = new Entropy.MqContexts(Jbig2RefinementDecoder.RefinementContextBits);

        byte[]? refined = Jbig2RefinementDecoder.Decode(
            decoder, contexts, info.Width, info.Height, region.Template, region.TypicalPrediction,
            reference, referenceDx: 0, referenceDy: 0, region.Adaptive);

        if (refined is null)
            return Jbig2Result.Failed(Jbig2DecodeOutcome.Malformed, "A JBIG2 refinement region could not be decoded.");

        Draw(page, new Jbig2Bitmap(info.Width, info.Height, refined), info.X, info.Y, info.CombinationOperator);
        return null;
    }

    private static Jbig2Result? ReadGenericRegion(
        ReadOnlyMemory<byte> buffer,
        in Jbig2Segment segment,
        Jbig2Bitmap page,
        long maxDecodedBytes)
    {
        if (!Jbig2SegmentReader.TryReadGenericRegion(buffer.Span, segment, out Jbig2GenericRegion region, out string? error))
            return Jbig2Result.Failed(Jbig2DecodeOutcome.Malformed, error ?? "Malformed generic region.");

        long pixels = (long)region.Width * region.Height;
        if (pixels > maxDecodedBytes)
            return Jbig2Result.Failed(Jbig2DecodeOutcome.TooLarge, "A JBIG2 generic region would exceed the decoded-byte ceiling.");

        Jbig2Bitmap bitmap;
        if (region.UsesMmr)
        {
            var options = new CcittFaxOptions(
                CcittCoding.TwoDimensional, region.Width, region.Height,
                BlackIs1: true, EncodedByteAlign: false, ExpectsEndOfLine: false);

            CcittFaxResult decoded = CcittFaxDecoder.Decode(
                buffer.Span.Slice(region.DataStart, region.DataLength), options, maxDecodedBytes);

            if (decoded.Outcome == CcittFaxOutcome.TooLarge)
                return Jbig2Result.Failed(Jbig2DecodeOutcome.TooLarge, "A JBIG2 generic region would exceed the decoded-byte ceiling.");
            if (decoded.Outcome != CcittFaxOutcome.Decoded)
                return Jbig2Result.Failed(Jbig2DecodeOutcome.Malformed, decoded.Failure ?? "A JBIG2 generic region could not be decoded.");

            bitmap = Unpack(decoded.Rows!, region.Width, region.Height);
        }
        else
        {
            byte[]? arithmetic = Jbig2GenericDecoder.Decode(
                buffer.Slice(region.DataStart, region.DataLength),
                region.Width, region.Height, region.Template, region.TypicalPrediction, region.Adaptive);

            if (arithmetic is null)
                return Jbig2Result.Failed(Jbig2DecodeOutcome.Malformed, "A JBIG2 generic region could not be decoded.");

            bitmap = new Jbig2Bitmap(region.Width, region.Height, arithmetic);
        }

        Draw(page, bitmap, region.X, region.Y, region.CombinationOperator);
        return null;
    }

    private static List<Jbig2Bitmap> Gather(in Jbig2Segment segment, Dictionary<uint, Jbig2Bitmap[]> exports)
    {
        var symbols = new List<Jbig2Bitmap>();
        foreach (uint number in segment.Referred)
        {
            if (exports.TryGetValue(number, out Jbig2Bitmap[]? exported))
                symbols.AddRange(exported);
        }

        return symbols;
    }

    private static Jbig2Bitmap Extract(Jbig2Bitmap page, int x, int y, int width, int height)
    {
        var pixels = new byte[width * height];
        for (int row = 0; row < height; row++)
        {
            for (int column = 0; column < width; column++)
                pixels[(row * width) + column] = page.At(x + column, y + row);
        }

        return new Jbig2Bitmap(width, height, pixels);
    }

    private static Jbig2Bitmap Unpack(byte[] rows, int width, int height)
    {
        int stride = (width + 7) / 8;
        var pixels = new byte[width * height];

        for (int y = 0; y < height; y++)
        {
            int source = y * stride;
            int target = y * width;
            for (int x = 0; x < width; x++)
            {
                int at = source + (x >> 3);
                if (at < rows.Length && ((rows[at] >> (7 - (x & 7))) & 1) != 0)
                    pixels[target + x] = 1;
            }
        }

        return new Jbig2Bitmap(width, height, pixels);
    }

    private static void Draw(Jbig2Bitmap page, Jbig2Bitmap region, int originX, int originY, int combination)
    {
        for (int row = 0; row < region.Height; row++)
        {
            int y = originY + row;
            if (y < 0 || y >= page.Height)
                continue;

            int target = y * page.Width;
            int source = row * region.Width;

            for (int column = 0; column < region.Width; column++)
            {
                int x = originX + column;
                if (x < 0 || x >= page.Width)
                    continue;

                byte value = region.Pixels[source + column];
                if (combination == CombineReplace)
                    page.Pixels[target + x] = value;
                else if (value != 0)
                    page.Pixels[target + x] = 1;
            }
        }
    }
}
