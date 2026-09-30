using Broiler.Media.Image.Managed.Gif;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media.Image.Managed;

// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=10; Fingerprint=TBF
// Broiler-Falsified-If: a GIF whose frame count or logical-screen area exceeds the MediaLimits in its ImageDecodeOptions is decoded rather than refused
// Broiler-Human:        PENDING
public sealed class GifImageCodec : ImageCodec
{
    public static MediaCodecDescriptor CodecDescriptor { get; } = new(new MediaCodecId("broiler.image.gif.managed"), "Broiler managed GIF",
        MediaKind.Image, MediaCodecCapabilities.Decode | MediaCodecCapabilities.Encode | MediaCodecCapabilities.Animation,
        [new MediaFormatDescriptor("GIF", ["image/gif"], [".gif"])]);

    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=TBF
    // Broiler-Human:        PENDING
    public GifImageCodec() : base(CodecDescriptor) { }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a prefix that GifDecoder.IsGif rejects is reported as a Certain GIF match, or a GIF87a or GIF89a prefix as no match
    // Broiler-Human:        PENDING
    public override ValueTask<MediaProbeResult> ProbeAsync(MediaProbeRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        MediaProbeResult result = GifDecoder.IsGif(request.Prefix.Span)
            ? MediaProbeResult.Match(MediaKind.Image, MediaProbeConfidence.Certain, "GIF", "image/gif", 6)
            : MediaProbeResult.NoMatch(MediaKind.Image);

        return ValueTask.FromResult(result);
    }

    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=10; Fingerprint=TBF
    // Broiler-Falsified-If: a malformed GIF passed here returns an image where GifDecoder.Decode on the same bytes raises FormatException
    // Broiler-Human:        PENDING
    public static ImageBuffer Decode(ReadOnlySpan<byte> data) => GifDecoder.Decode(data);

    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=10; Fingerprint=TBF
    // Broiler-Falsified-If: a malformed GIF passed here returns a sequence where GifDecoder.DecodeAnimation on the same bytes raises FormatException
    // Broiler-Human:        PENDING
    public static ImageSequence DecodeAnimation(ReadOnlySpan<byte> data) => GifDecoder.DecodeAnimation(data);

    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=6; Fingerprint=TBF
    // Broiler-Human:        PENDING
    public static byte[] Encode(ImageBuffer buffer) => GifEncoder.Encode(buffer);

    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=6; Fingerprint=TBF
    // Broiler-Human:        PENDING
    public static byte[] EncodeAnimation(ImageSequence sequence) => GifEncoder.EncodeAnimation(sequence);

    /// <summary>The CPU half; both public paths reach the image through this.</summary>
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=10; Fingerprint=TBF
    // Broiler-Falsified-If: a decode whose options.Limits sets MaxFrames or MaxImagePixels below what the GIF declares still returns the decoded sequence, because options.Limits is never read
    // Broiler-Human:        PENDING
    protected override ImageSequence DecodeCore(ReadOnlySpan<byte> data, ImageDecodeOptions options) =>
        options.PreserveAnimation ? DecodeAnimation(data) : ImageSequence.Static(Decode(data));

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=6; Fingerprint=TBF
    // Broiler-Falsified-If: options naming a format other than GIF produce GIF bytes instead of NotSupportedException, or an animated sequence is written with only its first frame
    // Broiler-Human:        PENDING
    public override async ValueTask EncodeAsync(ImageSequence sequence, Stream output,
        ImageEncodeOptions? options = null, CancellationToken cancellationToken = default)
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

    /// <summary>Reads what the GIF header declares, decoding nothing.</summary>
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=1; Fingerprint=TBF
    // Broiler-Falsified-If: an input of exactly ten bytes carrying a GIF signature throws instead of returning false
    // Broiler-Human:        PENDING
    public override bool TryInspect(ReadOnlySpan<byte> data, out ImageInfo? info) => GifDecoder.TryInspect(data, out info);
}

