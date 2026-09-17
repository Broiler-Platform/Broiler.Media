using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media.Image.Managed.Jbig2;

public sealed class Jbig2ImageCodec : ImageCodec
{
    public static MediaCodecDescriptor CodecDescriptor { get; } = new(
        new MediaCodecId("broiler.image.jbig2.managed"),
        "Broiler managed JBIG2",
        MediaKind.Image,
        MediaCodecCapabilities.Decode,
        [new MediaFormatDescriptor("JBIG2", ["image/x-jbig2"], [".jb2", ".jbig2"])]);

    public Jbig2ImageCodec() : base(CodecDescriptor) { }

    public override ValueTask<MediaProbeResult> ProbeAsync(MediaProbeRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        MediaProbeResult result = Jbig2Decoder.IsJbig2(request.Prefix.Span)
            ? MediaProbeResult.Match(MediaKind.Image, MediaProbeConfidence.Certain, "JBIG2", "image/x-jbig2", 8)
            : MediaProbeResult.NoMatch(MediaKind.Image);

        return ValueTask.FromResult(result);
    }

    public static ImageBuffer Decode(ReadOnlySpan<byte> data, ReadOnlySpan<byte> globals = default)
    {
        Jbig2Result result = Jbig2Decoder.Decode(data, globals);
        if (result.Outcome != Jbig2DecodeOutcome.Decoded || result.Page is null)
            throw new FormatException(result.Failure ?? "Failed to decode JBIG2 stream.");

        return result.ToImageBuffer()!;
    }

    protected override ImageSequence DecodeCore(ReadOnlySpan<byte> data, ImageDecodeOptions options) =>
        ImageSequence.Static(Decode(data));

    public override ValueTask EncodeAsync(ImageSequence sequence, Stream output,
        ImageEncodeOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("JBIG2 encoding is not supported.");

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
