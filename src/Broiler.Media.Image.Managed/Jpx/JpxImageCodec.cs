using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media.Image.Managed.Jpx;

public sealed class JpxImageCodec : ImageCodec
{
    public static MediaCodecDescriptor CodecDescriptor { get; } = new(
        new MediaCodecId("broiler.image.jpx.managed"),
        "Broiler managed JPEG 2000",
        MediaKind.Image,
        MediaCodecCapabilities.Decode,
        [new MediaFormatDescriptor("JPEG 2000", ["image/jp2", "image/jpx"], [".jp2", ".j2k", ".jpx"])]);

    public JpxImageCodec() : base(CodecDescriptor) { }

    public override ValueTask<MediaProbeResult> ProbeAsync(MediaProbeRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ReadOnlySpan<byte> prefix = request.Prefix.Span;
        bool isJpx = (prefix.Length >= 4 && prefix[0] == 0xFF && prefix[1] == 0x4F && prefix[2] == 0xFF && prefix[3] == 0x51) ||
                     (prefix.Length >= 12 && prefix[..12].SequenceEqual(JpxCodestreamReader.Jp2Signature));

        MediaProbeResult result = isJpx
            ? MediaProbeResult.Match(MediaKind.Image, MediaProbeConfidence.Certain, "JPEG 2000", "image/jp2", 4)
            : MediaProbeResult.NoMatch(MediaKind.Image);

        return ValueTask.FromResult(result);
    }

    public static ImageBuffer Decode(ReadOnlySpan<byte> data, long maxSamples = 64 * 1024 * 1024)
    {
        JpxDecodeResult result = JpxImageDecoder.Decode(data, maxSamples);
        if (result.Samples is null)
            throw new FormatException(result.Refusal ?? "Failed to decode JPEG 2000 image.");

        return result.ToImageBuffer()!;
    }

    protected override ImageSequence DecodeCore(ReadOnlySpan<byte> data, ImageDecodeOptions options) =>
        ImageSequence.Static(Decode(data));

    public override ValueTask EncodeAsync(ImageSequence sequence, Stream output,
        ImageEncodeOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("JPEG 2000 encoding is not supported.");

    public override bool TryInspect(ReadOnlySpan<byte> data, out ImageInfo? info)
    {
        info = null;
        if (!JpxCodestreamReader.TryRead(data, out JpxCodestreamHeader header, out _))
            return false;

        info = new ImageInfo(header.Width, header.Height, header.Components, header.BitDepth, "JPEG 2000", "image/jp2");
        return true;
    }
}
