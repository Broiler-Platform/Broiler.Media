using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media.Image.Managed;

public sealed class BmpImageCodec : ImageCodec
{
    public static MediaCodecDescriptor CodecDescriptor { get; } = new(
        new MediaCodecId("broiler.image.bmp.managed"),
        "Broiler managed BMP",
        MediaKind.Image,
        MediaCodecCapabilities.Decode | MediaCodecCapabilities.Encode,
        [
            new MediaFormatDescriptor(
                "BMP",
                ["image/bmp", "image/x-ms-bmp"],
                [".bmp"]),
        ]);

    public BmpImageCodec()
        : base(CodecDescriptor)
    {
    }

    public override ValueTask<MediaProbeResult> ProbeAsync(
        MediaProbeRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MediaProbeResult result = BmpDecoder.IsBmp(request.Prefix.Span)
            ? MediaProbeResult.Match(MediaKind.Image, MediaProbeConfidence.Certain, "BMP", "image/bmp", 2)
            : MediaProbeResult.NoMatch(MediaKind.Image);

        return ValueTask.FromResult(result);
    }

    public ImageBuffer Decode(ReadOnlySpan<byte> data) => BmpDecoder.Decode(data);

    public byte[] Encode(ImageBuffer buffer) => BmpEncoder.Encode(buffer);

    public override async ValueTask<ImageSequence> DecodeAsync(
        MediaInput input,
        ImageDecodeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        byte[] data = await EncodedInputReader.ReadAllAsync(input, options, cancellationToken).ConfigureAwait(false);
        return ImageSequence.Static(Decode(data));
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

        if (sequence.IsAnimated)
            throw new NotSupportedException("BMP encoding only supports still images.");

        ImageEncodeOptions effectiveOptions = options ?? new ImageEncodeOptions(ImageEncodeFormat.Bmp);
        if (effectiveOptions.Format != ImageEncodeFormat.Bmp)
            throw new NotSupportedException($"BMP codec cannot encode {effectiveOptions.Format}.");

        byte[] encoded = Encode(sequence.FirstFrame);
        await output.WriteAsync(encoded, cancellationToken).ConfigureAwait(false);
    }
}

