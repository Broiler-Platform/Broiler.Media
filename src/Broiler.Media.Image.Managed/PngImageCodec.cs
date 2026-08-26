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

    public override async ValueTask<ImageSequence> DecodeAsync(
        MediaInput input,
        ImageDecodeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        byte[] data = await EncodedInputReader.ReadAllAsync(input, options, cancellationToken).ConfigureAwait(false);
        return options?.PreserveAnimation == false
            ? ImageSequence.Static(Decode(data))
            : DecodeAnimation(data);
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
}

