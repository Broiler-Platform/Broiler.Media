using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media.Image.Managed;

public sealed class PngImageCodec : ImageCodec
{
    public static MediaCodecDescriptor CodecDescriptor { get; } = new(
        new MediaCodecId("broiler.image.png.managed"),
        "Broiler managed PNG/APNG",
        MediaKind.Image,
        MediaCodecCapabilities.Decode | MediaCodecCapabilities.Encode | MediaCodecCapabilities.Animation,
        [
            new MediaFormatDescriptor(
                "PNG",
                ["image/png"],
                [".png", ".apng"]),
        ]);

    public PngImageCodec()
        : base(CodecDescriptor)
    {
    }

    public override ValueTask<MediaProbeResult> ProbeAsync(
        MediaProbeRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MediaProbeResult result = PngDecoder.IsPng(request.Prefix.Span)
            ? MediaProbeResult.Match(MediaKind.Image, MediaProbeConfidence.Certain, "PNG", "image/png", 8)
            : MediaProbeResult.NoMatch(MediaKind.Image);

        return ValueTask.FromResult(result);
    }

    public ImageBuffer Decode(ReadOnlySpan<byte> data) => PngDecoder.Decode(data);

    public ImageSequence DecodeAnimation(ReadOnlySpan<byte> data) => PngDecoder.DecodeAnimation(data);

    public byte[] Encode(ImageBuffer buffer) => PngEncoder.Encode(buffer);

    public byte[] EncodeAnimation(ImageSequence sequence) => PngEncoder.EncodeAnimation(sequence);

    /// <summary>The CPU half; both public paths reach the image through this.</summary>
    protected override ImageSequence DecodeCore(ReadOnlySpan<byte> data, ImageDecodeOptions options)
    {
        return options.PreserveAnimation
            ? DecodeAnimation(data)
            : ImageSequence.Static(Decode(data));
    }

    public override async ValueTask EncodeAsync(
        ImageSequence sequence,
        Stream output,
        ImageEncodeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentNullException.ThrowIfNull(output);
        cancellationToken.ThrowIfCancellationRequested();

        ImageEncodeOptions effectiveOptions = options ?? new ImageEncodeOptions(ImageEncodeFormat.Png);
        if (effectiveOptions.Format != ImageEncodeFormat.Png)
            throw new NotSupportedException($"PNG codec cannot encode {effectiveOptions.Format}.");

        byte[] encoded = sequence.IsAnimated ? EncodeAnimation(sequence) : Encode(sequence.FirstFrame);
        await output.WriteAsync(encoded, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads what the PNG header declares, decoding nothing.</summary>
    public override bool TryInspect(ReadOnlySpan<byte> data, out ImageInfo? info) =>
        PngDecoder.TryInspect(data, out info);
}

