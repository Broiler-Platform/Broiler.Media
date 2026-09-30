using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;

namespace Broiler.Media.Image.Managed.Jbig2;

/// <summary>One segment of a JBIG2 stream, located but not interpreted.</summary>
// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=1; Fingerprint=TBF
// Broiler-Falsified-If: an intermediate region type (4, 36 or 40) is classed as immediate, so a bitmap meant for an auxiliary buffer is composited onto the page
// Broiler-Human:        PENDING
public readonly record struct Jbig2Segment(
    uint Number,
    int Type,
    uint Page,
    int DataStart,
    int DataLength)
{
    /// <summary>The segment type as ITU-T T.88 names it.</summary>
    // Broiler-AI:           Origin=AI; Spec=T.88 s7.3; IP=None; Security=Low; Resources=1; Fingerprint=TBF
    // Broiler-Falsified-If: segment type 38 is described as anything other than a generic region
    // Broiler-Human:        PENDING
    public string Describe() => Type switch
    {
        0 => "symbol dictionary",
        4 or 6 or 7 => "text region",
        16 => "pattern dictionary",
        20 or 22 or 23 => "halftone region",
        36 or 38 or 39 => "generic region",
        40 or 42 or 43 => "refinement region",
        48 => "page information",
        49 => "end of page",
        50 => "end of stripe",
        51 => "end of file",
        52 => "profiles",
        53 => "custom Huffman tables",
        62 => "extension",
        _ => string.Create(CultureInfo.InvariantCulture, $"segment type {Type}"),
    };

    /// <summary>True for the segment types that carry no image data to lose.</summary>
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: segment type 53 (custom Huffman tables) is classed as structural, so a stream whose regions need those tables is decoded without them instead of refused
    // Broiler-Human:        PENDING
    public bool IsStructural => Type is 48 or 49 or 50 or 51 or 52 or 62;

    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: an intermediate generic region (type 36) is classed with the immediate ones, so its bitmap is composited onto the page instead of kept for the refinement that refers to it
    // Broiler-Human:        PENDING
    public bool IsGenericRegion => Type is 36 or 38 or 39;

    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a pattern dictionary (type 16) is classed as a symbol dictionary and its bytes reach the symbol dictionary decoder
    // Broiler-Human:        PENDING
    public bool IsSymbolDictionary => Type is 0;

    /// <summary>Every segment type that draws a region, whatever it draws it from.</summary>
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a page information segment (type 48) is classed as a region, so its page width and height are read as a region extent when the page is sized
    // Broiler-Human:        PENDING
    public bool IsRegion => Type is 4 or 6 or 7 or 20 or 22 or 23 or 36 or 38 or 39 or 40 or 42 or 43;

    /// <summary>
    /// An immediate refinement region, which corrects the page under it. Type 40
    /// is the intermediate form, kept in an auxiliary buffer rather than drawn.
    /// </summary>
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: intermediate refinement type 40 is classed as immediate, so a refinement meant for an auxiliary buffer is drawn onto the page
    // Broiler-Human:        PENDING
    public bool IsImmediateRefinementRegion => Type is 42 or 43;

    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a segment type other than 40, 42 or 43 is classed as a refinement region
    // Broiler-Human:        PENDING
    public bool IsRefinementRegion => Type is 40 or 42 or 43;

    /// <summary>
    /// An immediate text region, drawn straight onto the page. Type 4 is the
    /// intermediate form, which is kept in an auxiliary buffer for another
    /// segment to refer to rather than composited, and is not one of these.
    /// </summary>
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: intermediate text region type 4 is classed as immediate, so its symbols are composited onto the page instead of kept for another segment
    // Broiler-Human:        PENDING
    public bool IsImmediateTextRegion => Type is 6 or 7;

    /// <summary>
    /// The segments this one refers to, by number. A text region's symbols come
    /// from the dictionaries named here, so the order matters: identifiers count
    /// through the referred-to dictionaries' exports in this sequence.
    /// </summary>
    public uint[] Referred { get; init; } = [];
}

/// <summary>
/// The region segment information every region type begins with: where it goes
/// and how it combines with what is already there.
/// </summary>
// Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=TBF
// Broiler-Human:        PENDING
public readonly record struct Jbig2RegionInfo(
    int Width,
    int Height,
    int X,
    int Y,
    int CombinationOperator);

/// <summary>A refinement region's header, read without decoding its data.</summary>
// Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=TBF
// Broiler-Human:        PENDING
public readonly record struct Jbig2RefinementRegion(
    Jbig2RegionInfo Info,
    int Template,
    bool TypicalPrediction,
    int DataStart,
    int DataLength)
{
    /// <summary>The two adaptive pixels template 0 carries, A1 then A2.</summary>
    public (int X, int Y)[] Adaptive { get; init; } = [];
}

/// <summary>A generic region's header, read without decoding its data.</summary>
// Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=TBF
// Broiler-Human:        PENDING
public readonly record struct Jbig2GenericRegion(
    int Width,
    int Height,
    int X,
    int Y,
    int CombinationOperator,
    bool UsesMmr,
    int Template,
    bool TypicalPrediction,
    int DataStart,
    int DataLength)
{
    /// <summary>
    /// The adaptive template pixels the header supplied, A1 first. Empty for an
    /// MMR region, which has none, and for a header that stated none.
    /// </summary>
    public (int X, int Y)[] Adaptive { get; init; } = [];
}

/// <summary>
/// Reads the segment structure of a JBIG2 embedded stream.
/// </summary>
/// <remarks>
/// <para>
/// PDF embeds JBIG2 in its sequential organisation: no file header, no random
/// access table, just segment headers each followed immediately by their data.
/// This walks that chain and says what is in it. It reads a header only as far as
/// locating what the header itself states — a generic region's bitmap, a page's
/// declared size, and the segment numbers a segment refers to — and leaves the
/// symbol dictionaries and text regions to the decoders that own them.
/// </para>
/// <para>
/// The referred-to-segment field is the fiddly part and the reason this is worth
/// its own type: its size depends on a count that is itself encoded in one of two
/// forms, and the width of each reference depends on the number of the segment
/// doing the referring. Getting it wrong does not fail, it desynchronises the
/// walk, so it is bounded and checked rather than trusted.
/// </para>
/// </remarks>
// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=4; Fingerprint=TBF
// Broiler-Falsified-If: a segment whose declared data length runs past the end of the stream is returned with DataStart plus DataLength beyond data.Length
// Broiler-Human:        PENDING
public static class Jbig2SegmentReader
{
    /// <summary>Segments read before a stream is refused as unreasonable.</summary>
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a stream of 4097 minimal segment headers is walked in full instead of refused
    // Broiler-Human:        PENDING
    private const int MaxSegments = 4096;

    /// <summary>The region segment information every region type begins with.</summary>
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a region segment with exactly 16 data bytes is read as region information instead of refused
    // Broiler-Human:        PENDING
    private const int RegionInfoLength = 17;

    /// <summary>The position a region may declare before it is nonsense.</summary>
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a region declaring an X or Y of 2^24 + 1 is accepted by TryReadRegionInfo
    // Broiler-Human:        PENDING
    private const int MaxCoordinate = 1 << 24;

    /// <summary>Marks a segment whose length the header does not state.</summary>
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a segment whose data length field is 0xFFFFFFFF is taken as a length instead of refused as unresolved
    // Broiler-Human:        PENDING
    private const uint UnknownLength = 0xFFFFFFFFu;

    // Broiler-AI:           Origin=AI; Spec=T.88 s7.2; IP=Low; Security=High; Resources=4; Fingerprint=TBF
    // Broiler-Falsified-If: a header whose referred-to count, reference width, page field and length together run past the end of the stream is read instead of refused
    // Broiler-Human:        PENDING
    public static bool TryRead(ReadOnlySpan<byte> data, out List<Jbig2Segment> segments, out string? error)
    {
        segments = [];
        int position = 0;

        while (position < data.Length)
        {
            if (segments.Count >= MaxSegments)
            {
                error = "The JBIG2 stream declares more segments than this reader will walk.";
                return false;
            }

            if (position + 11 > data.Length)
            {
                // A trailing fragment too short to be a header ends the walk
                // rather than failing it: what was read is still usable.
                break;
            }

            uint number = ReadUInt32(data, position);
            byte flags = data[position + 4];
            int type = flags & 0x3F;
            bool longPage = (flags & 0x40) != 0;
            int cursor = position + 5;

            byte referredFlags = data[cursor];
            long referredCount = referredFlags >> 5;
            if (referredCount == 7)
            {
                if (cursor + 4 > data.Length)
                {
                    error = "A JBIG2 segment header declares a referred-to count that does not fit the stream.";
                    return false;
                }

                referredCount = ReadUInt32(data, cursor) & 0x1FFFFFFF;
                cursor += 4;

                long retainBytes = (referredCount + 8) / 8;
                if (referredCount > MaxSegments || cursor + retainBytes > data.Length)
                {
                    error = "A JBIG2 segment header declares more referred-to segments than the stream holds.";
                    return false;
                }

                cursor += (int)retainBytes;
            }
            else
            {
                cursor += 1;
            }

            int referenceSize = number <= 256 ? 1 : number <= 65536 ? 2 : 4;
            long referencesLength = referredCount * referenceSize;
            if (cursor + referencesLength + (longPage ? 4 : 1) + 4 > data.Length)
            {
                error = "A JBIG2 segment header runs past the end of the stream.";
                return false;
            }

            // The references were skipped while nothing referred to anything: a
            // generic region carries its own bitmap. A text region does not, and
            // these numbers are the only statement of which dictionaries hold the
            // symbols it draws.
            var referred = new uint[referredCount];
            for (int i = 0; i < referred.Length; i++)
            {
                referred[i] = referenceSize switch
                {
                    1 => data[cursor],
                    2 => BinaryPrimitives.ReadUInt16BigEndian(data.Slice(cursor, 2)),
                    _ => ReadUInt32(data, cursor),
                };

                cursor += referenceSize;
            }

            uint page = longPage ? ReadUInt32(data, cursor) : data[cursor];
            cursor += longPage ? 4 : 1;

            uint length = ReadUInt32(data, cursor);
            cursor += 4;

            if (length == UnknownLength)
            {
                error = "A JBIG2 segment declares an unknown data length, which this reader does not resolve.";
                return false;
            }

            if (cursor + length > data.Length)
            {
                error = "A JBIG2 segment declares a data length that does not fit the stream.";
                return false;
            }

            segments.Add(new Jbig2Segment(number, type, page, cursor, (int)length) { Referred = referred });
            position = cursor + (int)length;
        }

        if (segments.Count == 0)
        {
            error = "The stream carries no JBIG2 segments.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Reads the region information every region segment starts with, without
    /// interpreting what follows it.
    /// </summary>
    /// <remarks>
    /// This is what lets the page be sized before anything is decoded, which a
    /// refinement region needs: it refines the page under it, so the page has to
    /// exist before the segment that corrects it is read.
    /// </remarks>
    // Broiler-AI:           Origin=AI; Spec=T.88 s7.4.1; IP=Low; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a region declaring a width or height of 0 or above 65536 is returned as valid region information
    // Broiler-Human:        PENDING
    public static bool TryReadRegionInfo(ReadOnlySpan<byte> data, in Jbig2Segment segment, out Jbig2RegionInfo info)
    {
        info = default;
        if (segment.DataLength < RegionInfoLength)
            return false;

        ReadOnlySpan<byte> body = data.Slice(segment.DataStart, segment.DataLength);
        long width = ReadUInt32(body, 0);
        long height = ReadUInt32(body, 4);
        long x = ReadUInt32(body, 8);
        long y = ReadUInt32(body, 12);

        if (width is <= 0 or > (1 << 16) || height is <= 0 or > (1 << 16))
            return false;

        if (x > MaxCoordinate || y > MaxCoordinate)
            return false;

        info = new Jbig2RegionInfo((int)width, (int)height, (int)x, (int)y, body[16] & 0x07);
        return true;
    }

    /// <summary>Reads a refinement region segment's header, up to its data.</summary>
    // Broiler-AI:           Origin=AI; Spec=T.88 s7.4.7; IP=Low; Security=High; Resources=1; Fingerprint=TBF
    // Broiler-Falsified-If: a template-0 refinement segment that ends inside its four adaptive-pixel bytes is returned with a negative DataLength instead of refused
    // Broiler-Human:        PENDING
    public static bool TryReadRefinementRegion(
        ReadOnlySpan<byte> data,
        in Jbig2Segment segment,
        out Jbig2RefinementRegion region,
        out string? error)
    {
        region = default;

        if (!TryReadRegionInfo(data, segment, out Jbig2RegionInfo info))
        {
            error = "A JBIG2 refinement region segment does not describe a region.";
            return false;
        }

        ReadOnlySpan<byte> body = data.Slice(segment.DataStart, segment.DataLength);
        if (body.Length < RegionInfoLength + 1)
        {
            error = "A JBIG2 refinement region segment is too short to state its flags.";
            return false;
        }

        byte flags = body[RegionInfoLength];
        int template = flags & 0x01;
        bool typicalPrediction = (flags & 0x02) != 0;

        int cursor = RegionInfoLength + 1;
        (int X, int Y)[] adaptive = [];

        // Template 0 names two adaptive pixels: one in the bitmap being decoded
        // and one in the reference. Template 1 has none.
        if (template == 0)
        {
            if (cursor + 4 > body.Length)
            {
                error = "A JBIG2 refinement region declares template pixels the segment does not hold.";
                return false;
            }

            adaptive =
            [
                ((sbyte)body[cursor], (sbyte)body[cursor + 1]),
                ((sbyte)body[cursor + 2], (sbyte)body[cursor + 3]),
            ];

            cursor += 4;
        }

        region = new Jbig2RefinementRegion(
            info, template, typicalPrediction, segment.DataStart + cursor, segment.DataLength - cursor)
        {
            Adaptive = adaptive,
        };

        error = null;
        return true;
    }

    /// <summary>Reads a generic region segment's header, up to its bitmap data.</summary>
    // Broiler-AI:           Origin=AI; Spec=T.88 s7.4.6; IP=Low; Security=High; Resources=1; Fingerprint=TBF
    // Broiler-Falsified-If: a non-MMR template-0 region with fewer than eight bytes after its flags is returned with a negative DataLength instead of refused
    // Broiler-Human:        PENDING
    public static bool TryReadGenericRegion(
        ReadOnlySpan<byte> data,
        in Jbig2Segment segment,
        out Jbig2GenericRegion region,
        out string? error)
    {
        region = default;

        // Region segment information is seventeen bytes, then one byte of
        // generic-region flags.
        if (segment.DataLength < 18)
        {
            error = "A JBIG2 generic region segment is too short to describe a region.";
            return false;
        }

        ReadOnlySpan<byte> body = data.Slice(segment.DataStart, segment.DataLength);
        long width = ReadUInt32(body, 0);
        long height = ReadUInt32(body, 4);
        long x = ReadUInt32(body, 8);
        long y = ReadUInt32(body, 12);
        int combination = body[16] & 0x07;

        byte flags = body[17];
        (int X, int Y)[] adaptivePixels = [];
        bool mmr = (flags & 0x01) != 0;
        int template = (flags >> 1) & 0x03;
        bool typicalPrediction = (flags & 0x08) != 0;

        int cursor = 18;
        if (!mmr)
        {
            // Arithmetic coding carries the adaptive template pixels: four pairs
            // for template 0, one for the rest.
            int count = template == 0 ? 4 : 1;
            if (cursor + (count * 2) > body.Length)
            {
                error = "A JBIG2 generic region declares template pixels the segment does not hold.";
                return false;
            }

            // Signed bytes: an adaptive pixel may sit left of or above the one
            // being decoded, which is most of the point of moving it.
            adaptivePixels = new (int, int)[count];
            for (int i = 0; i < count; i++)
            {
                adaptivePixels[i] = ((sbyte)body[cursor], (sbyte)body[cursor + 1]);
                cursor += 2;
            }
        }

        if (width is <= 0 or > (1 << 16) || height is <= 0 or > (1 << 16))
        {
            error = "A JBIG2 generic region declares a size outside the supported range.";
            return false;
        }

        region = new Jbig2GenericRegion(
            (int)width, (int)height, (int)Math.Min(x, int.MaxValue), (int)Math.Min(y, int.MaxValue),
            combination, mmr, template, typicalPrediction,
            segment.DataStart + cursor, segment.DataLength - cursor)
        {
            Adaptive = adaptivePixels,
        };
        error = null;
        return true;
    }

    /// <summary>Reads the page information segment's declared size and default pixel.</summary>
    /// <remarks>
    /// The default pixel is the page's starting colour, and a page that declares
    /// black starts filled rather than blank. Ignoring it would silently drop the
    /// only statement some pages make about the space between their regions.
    /// </remarks>
    // Broiler-AI:           Origin=AI; Spec=T.88 s7.4.8; IP=Low; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a page information segment declaring height 0xFFFFFFFF yields a nonzero height instead of 0 for the caller to resolve
    // Broiler-Human:        PENDING
    public static bool TryReadPageSize(
        ReadOnlySpan<byte> data,
        in Jbig2Segment segment,
        out int width,
        out int height,
        out byte defaultPixel)
    {
        width = 0;
        height = 0;
        defaultPixel = 0;
        if (segment.DataLength < 17)
            return false;

        ReadOnlySpan<byte> body = data.Slice(segment.DataStart, segment.DataLength);
        long declaredWidth = ReadUInt32(body, 0);
        long declaredHeight = ReadUInt32(body, 4);

        if (declaredWidth is <= 0 or > (1 << 16))
            return false;

        width = (int)declaredWidth;

        // Flags follow the two resolutions: bit 2 is the default pixel value.
        defaultPixel = (byte)((body[16] >> 2) & 1);

        // An unknown page height is legal and resolved from the end-of-stripe
        // segments this reader does not interpret, so it is left for the caller
        // to take from the regions instead.
        height = declaredHeight is > 0 and <= (1 << 16) ? (int)declaredHeight : 0;
        return true;
    }

    /// <summary>An inventory of what a stream contains, for the diagnostic.</summary>
    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=3; Fingerprint=TBF
    // Broiler-Falsified-If: two segments of the same type are listed as two separate entries instead of one counted entry
    // Broiler-Human:        PENDING
    public static string Describe(List<Jbig2Segment> segments)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Jbig2Segment segment in segments)
        {
            string name = segment.Describe();
            counts.TryGetValue(name, out int seen);
            counts[name] = seen + 1;
        }

        var names = new List<string>(counts.Keys);
        names.Sort(StringComparer.Ordinal);

        var parts = new List<string>(names.Count);
        foreach (string name in names)
        {
            int count = counts[name];
            parts.Add(count == 1
                ? string.Create(CultureInfo.InvariantCulture, $"1 {name}")
                : string.Create(CultureInfo.InvariantCulture, $"{count} {name} segments"));
        }

        return string.Join(", ", parts);
    }

    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: an offset within three bytes of the span end returns a value instead of throwing
    // Broiler-Human:        PENDING
    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
}
