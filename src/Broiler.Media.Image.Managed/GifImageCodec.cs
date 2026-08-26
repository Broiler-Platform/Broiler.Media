using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media.Image.Managed;

public sealed class GifImageCodec : ImageCodec
{
    public static MediaCodecDescriptor CodecDescriptor { get; } = new(
        new MediaCodecId("broiler.image.gif.managed"),
        "Broiler managed GIF",
        MediaKind.Image,
        MediaCodecCapabilities.Decode | MediaCodecCapabilities.Encode | MediaCodecCapabilities.Animation,
        [
            new MediaFormatDescriptor(
                "GIF",
                ["image/gif"],
                [".gif"]),
        ]);

    public GifImageCodec()
        : base(CodecDescriptor)
    {
    }

    public override ValueTask<MediaProbeResult> ProbeAsync(
        MediaProbeRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MediaProbeResult result = GifDecoder.IsGif(request.Prefix.Span)
            ? MediaProbeResult.Match(MediaKind.Image, MediaProbeConfidence.Certain, "GIF", "image/gif", 6)
            : MediaProbeResult.NoMatch(MediaKind.Image);

        return ValueTask.FromResult(result);
    }

    public ImageBuffer Decode(ReadOnlySpan<byte> data) => GifDecoder.Decode(data);

    public ImageSequence DecodeAnimation(ReadOnlySpan<byte> data) => GifDecoder.DecodeAnimation(data);

    public byte[] Encode(ImageBuffer buffer) => GifEncoder.Encode(buffer);

    public byte[] EncodeAnimation(ImageSequence sequence) => GifEncoder.EncodeAnimation(sequence);

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

        ImageEncodeOptions effectiveOptions = options ?? new ImageEncodeOptions(ImageEncodeFormat.Gif);
        if (effectiveOptions.Format != ImageEncodeFormat.Gif)
            throw new NotSupportedException($"GIF codec cannot encode {effectiveOptions.Format}.");

        byte[] encoded = sequence.IsAnimated ? EncodeAnimation(sequence) : Encode(sequence.FirstFrame);
        await output.WriteAsync(encoded, cancellationToken).ConfigureAwait(false);
    }
}

