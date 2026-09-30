using System;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media.Audio;

// Broiler-AI:           Origin=AI; Spec=ADR-0002; IP=Low; Security=High; Resources=7; Fingerprint=TBF
// Broiler-Falsified-If: an implementation writes buffers totalling more samples than options.Limits.MaxDecodedSamples to the output instead of failing with LimitExceeded
// Broiler-Human:        PENDING
public abstract class AudioCodec : MediaCodec
{
    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a descriptor whose kind is Image or Video constructs an AudioCodec without throwing ArgumentException
    // Broiler-Human:        PENDING
    protected AudioCodec(MediaCodecDescriptor descriptor) : base(descriptor)
    {
        if (descriptor.Kind != MediaKind.Audio)
            throw new ArgumentException("Audio codecs must use MediaKind.Audio descriptors.", nameof(descriptor));
    }

    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=4; Fingerprint=TBF
    // Broiler-Falsified-If: an implementation handed a truncated or malformed stream returns stream info instead of throwing MediaException
    // Broiler-Human:        PENDING
    public abstract ValueTask<AudioStreamInfo> GetInfoAsync(MediaInput input,
        AudioDecodeOptions? options = null, CancellationToken cancellationToken = default);

    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=7; Fingerprint=TBF
    // Broiler-Falsified-If: an implementation writes buffers totalling more samples than options.Limits.MaxDecodedSamples to the output instead of failing with LimitExceeded
    // Broiler-Human:        PENDING
    public abstract ValueTask DecodeAsync(MediaInput input, IAudioOutput output, 
        AudioDecodeOptions? options = null, CancellationToken cancellationToken = default);
}

