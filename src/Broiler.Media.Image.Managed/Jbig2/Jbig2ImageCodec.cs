using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media.Image.Managed.Jbig2;

// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=9; Fingerprint=TBF
// Broiler-Falsified-If: a JBIG2 stream the decoder refuses reaches the caller as an image sequence rather than as a FormatException
// Broiler-Human:        PENDING
public sealed class Jbig2ImageCodec : ImageCodec
{
    public static MediaCodecDescriptor CodecDescriptor { get; } = new(
        new MediaCodecId("broiler.image.jbig2.managed"),
        "Broiler managed JBIG2",
        MediaKind.Image,
        MediaCodecCapabilities.Decode,
        [new MediaFormatDescriptor("JBIG2", ["image/x-jbig2"], [".jb2", ".jbig2"])]);

    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=TBF
    // Broiler-Human:        PENDING
    public Jbig2ImageCodec() : base(CodecDescriptor) { }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a prefix shorter than eight bytes throws, or one not starting with the JBIG2 ID string is reported as a Certain match
    // Broiler-Human:        PENDING
    public override ValueTask<MediaProbeResult> ProbeAsync(MediaProbeRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        MediaProbeResult result = Jbig2Decoder.IsJbig2(request.Prefix.Span)
            ? MediaProbeResult.Match(MediaKind.Image, MediaProbeConfidence.Certain, "JBIG2", "image/x-jbig2", 8)
            : MediaProbeResult.NoMatch(MediaKind.Image);

        return ValueTask.FromResult(result);
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=9; Fingerprint=TBF
    // Broiler-Falsified-If: a stream the decoder reports as Unsupported, Malformed or TooLarge returns an ImageBuffer instead of raising FormatException
    // Broiler-Human:        PENDING
    public static ImageBuffer Decode(ReadOnlySpan<byte> data, ReadOnlySpan<byte> globals = default)
    {
        Jbig2Result result = Jbig2Decoder.Decode(data, globals);
        if (result.Outcome != Jbig2DecodeOutcome.Decoded || result.Page is null)
            throw new FormatException(result.Failure ?? "Failed to decode JBIG2 stream.");

        return result.ToImageBuffer()!;
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=9; Fingerprint=TBF
    // Broiler-Falsified-If: an ImageDecodeOptions whose Limits set MaxDecodedBytes below 64 MiB does not refuse a JBIG2 page larger than that limit
    // Broiler-Human:        PENDING
    protected override ImageSequence DecodeCore(ReadOnlySpan<byte> data, ImageDecodeOptions options) =>
        ImageSequence.Static(Decode(data));

    public override ValueTask EncodeAsync(ImageSequence sequence, Stream output,
        ImageEncodeOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("JBIG2 encoding is not supported.");

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=4; Fingerprint=TBF
    // Broiler-Falsified-If: inspecting a stream allocates in proportion to its length, one segment record and referred-to array per segment, against the TryInspect contract
    // Broiler-Human:        PENDING
    public override bool TryInspect(ReadOnlySpan<byte> data, out ImageInfo? info)
    {
        info = null;
        ReadOnlySpan<byte> segmentsData = data;
        if (data.Length >= 9 && Jbig2Decoder.IsJbig2(data))
        {
            int offset = 8;
            byte fileFlags = data[offset++];
            if ((fileFlags & 0x02) == 0 && offset + 4 <= data.Length)
                offset += 4;
            segmentsData = data[offset..];
        }

        if (!Jbig2SegmentReader.TryRead(segmentsData, out var segments, out _))
            return false;

        int width = 0;
        int height = 0;

        foreach (var segment in segments)
        {
            if (segment.Type == 48 && Jbig2SegmentReader.TryReadPageSize(segmentsData, segment, out int w, out int h, out _))
            {
                width = w;
                height = h;
                break;
            }
        }

        if (width <= 0 || height <= 0)
        {
            foreach (var segment in segments)
            {
                if (segment.IsRegion && Jbig2SegmentReader.TryReadRegionInfo(segmentsData, segment, out var reg))
                {
                    width = Math.Max(width, reg.X + reg.Width);
                    height = Math.Max(height, reg.Y + reg.Height);
                }
            }
        }

        if (width <= 0 || height <= 0)
            return false;

        info = new ImageInfo(width, height, 1, 1, "JBIG2", "image/x-jbig2");
        return true;
    }
}
