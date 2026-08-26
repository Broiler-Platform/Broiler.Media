using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media.Image.Managed;

public sealed class JpegImageCodec : ImageCodec
{
    public static MediaCodecDescriptor CodecDescriptor { get; } = new(
        new MediaCodecId("broiler.image.jpeg.managed"),
        "Broiler managed JPEG",
        MediaKind.Image,
        MediaCodecCapabilities.Decode | MediaCodecCapabilities.Encode,
        [
            new MediaFormatDescriptor(
                "JPEG",
                ["image/jpeg"],
                [".jpg", ".jpeg"]),
        ]);

    public JpegImageCodec()
        : base(CodecDescriptor)
    {
    }

    public override ValueTask<MediaProbeResult> ProbeAsync(
        MediaProbeRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MediaProbeResult result = JpegDecoder.IsJpeg(request.Prefix.Span)
            ? MediaProbeResult.Match(MediaKind.Image, MediaProbeConfidence.Certain, "JPEG", "image/jpeg", 2)
            : MediaProbeResult.NoMatch(MediaKind.Image);

        return ValueTask.FromResult(result);
    }

    public ImageBuffer Decode(ReadOnlySpan<byte> data) => JpegDecoder.Decode(data);

    public byte[] Encode(ImageBuffer buffer, int quality = 100) => JpegEncoder.Encode(buffer, quality);

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
            throw new NotSupportedException("JPEG encoding only supports still images.");

        ImageEncodeOptions effectiveOptions = options ?? new ImageEncodeOptions(ImageEncodeFormat.Jpeg);
        if (effectiveOptions.Format != ImageEncodeFormat.Jpeg)
            throw new NotSupportedException($"JPEG codec cannot encode {effectiveOptions.Format}.");

        byte[] encoded = Encode(sequence.FirstFrame, effectiveOptions.Quality);
        await output.WriteAsync(encoded, cancellationToken).ConfigureAwait(false);
    }
}

