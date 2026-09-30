using Broiler.Media.Image.Managed.Bmp;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media.Image.Managed;

// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=8; Fingerprint=TBF
// Broiler-Falsified-If: a BMP whose width times height exceeds options.Limits.MaxImagePixels is decoded instead of refused with LimitExceeded
// Broiler-Human:        PENDING
public sealed class BmpImageCodec : ImageCodec
{
    public static MediaCodecDescriptor CodecDescriptor { get; } = new(new MediaCodecId("broiler.image.bmp.managed"),
        "Broiler managed BMP", MediaKind.Image, MediaCodecCapabilities.Decode | MediaCodecCapabilities.Encode,
        [new MediaFormatDescriptor("BMP", ["image/bmp", "image/x-ms-bmp"], [".bmp"])]);

    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: the codec registers under a descriptor other than CodecDescriptor, so catalog selection by id or MIME type misses it
    // Broiler-Human:        PENDING
    public BmpImageCodec() : base(CodecDescriptor) { }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=1; Fingerprint=TBF
    // Broiler-Falsified-If: a prefix that does not begin with the bytes BM is reported as a certain BMP match
    // Broiler-Human:        PENDING
    public override ValueTask<MediaProbeResult> ProbeAsync(MediaProbeRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        MediaProbeResult result = BmpDecoder.IsBmp(request.Prefix.Span)
            ? MediaProbeResult.Match(MediaKind.Image, MediaProbeConfidence.Certain, "BMP", "image/bmp", 2)
            : MediaProbeResult.NoMatch(MediaKind.Image);

        return ValueTask.FromResult(result);
    }

    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=8; Fingerprint=TBF
    // Broiler-Falsified-If: the returned ImageBuffer has a width or height other than the header width and absolute height
    // Broiler-Human:        PENDING
    public static ImageBuffer Decode(ReadOnlySpan<byte> data) => BmpDecoder.Decode(data);

    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=4; Fingerprint=TBF
    // Broiler-Falsified-If: the returned bytes do not begin with BM followed by a file-size field equal to their own length
    // Broiler-Human:        PENDING
    public static byte[] Encode(ImageBuffer buffer) => BmpEncoder.Encode(buffer);

    /// <summary>The CPU half; both public paths reach the image through this.</summary>
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=8; Fingerprint=TBF
    // Broiler-Falsified-If: a BMP whose width times height exceeds options.Limits.MaxImagePixels is decoded instead of refused with LimitExceeded
    // Broiler-Human:        PENDING
    protected override ImageSequence DecodeCore(ReadOnlySpan<byte> data, ImageDecodeOptions options) => ImageSequence.Static(Decode(data));

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=4; Fingerprint=TBF
    // Broiler-Falsified-If: an animated sequence, or options naming a format other than Bmp, writes bytes to the output stream instead of throwing NotSupportedException
    // Broiler-Human:        PENDING
    public override async ValueTask EncodeAsync(ImageSequence sequence, Stream output,
        ImageEncodeOptions? options = null, CancellationToken cancellationToken = default)
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

    /// <summary>Reads what the BMP header declares, decoding nothing.</summary>
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=1; Fingerprint=TBF
    // Broiler-Falsified-If: a BMP whose DIB header size is below 40 yields image info instead of false
    // Broiler-Human:        PENDING
    public override bool TryInspect(ReadOnlySpan<byte> data, out ImageInfo? info) => BmpDecoder.TryInspect(data, out info);
}

