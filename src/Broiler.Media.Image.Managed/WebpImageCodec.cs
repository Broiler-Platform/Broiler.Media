using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media.Image.Managed;

public sealed class WebpImageCodec : ImageCodec
{
    public static MediaCodecDescriptor CodecDescriptor { get; } = new(
        new MediaCodecId("broiler.image.webp.managed"),
        "Broiler managed WebP",
        MediaKind.Image,
        MediaCodecCapabilities.Decode | MediaCodecCapabilities.Encode | MediaCodecCapabilities.Animation,
        [
            new MediaFormatDescriptor(
                "WebP",
                ["image/webp"],
                [".webp"]),
        ]);

    public WebpImageCodec()
        : base(CodecDescriptor)
    {
    }

    public override ValueTask<MediaProbeResult> ProbeAsync(
        MediaProbeRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MediaProbeResult result = WebpDecoder.IsWebp(request.Prefix.Span)
            ? MediaProbeResult.Match(MediaKind.Image, MediaProbeConfidence.Certain, "WebP", "image/webp", 12)
            : MediaProbeResult.NoMatch(MediaKind.Image);

        return ValueTask.FromResult(result);
    }

    public ImageBuffer Decode(ReadOnlySpan<byte> data) => WebpDecoder.Decode(data);

    public ImageSequence DecodeAnimation(ReadOnlySpan<byte> data) => WebpDecoder.DecodeAnimation(data);

    public byte[] Encode(ImageBuffer buffer) => WebpEncoder.Encode(buffer);

    public byte[] EncodeAnimation(ImageSequence sequence) => WebpEncoder.EncodeAnimation(sequence);

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

        ImageEncodeOptions effectiveOptions = options ?? new ImageEncodeOptions(ImageEncodeFormat.WebP);
        if (effectiveOptions.Format != ImageEncodeFormat.WebP)
            throw new NotSupportedException($"WebP codec cannot encode {effectiveOptions.Format}.");

        byte[] encoded = sequence.IsAnimated ? EncodeAnimation(sequence) : Encode(sequence.FirstFrame);
        await output.WriteAsync(encoded, cancellationToken).ConfigureAwait(false);
    }
}

