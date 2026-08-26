using System;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media.Audio;

public abstract class AudioCodec : MediaCodec
{
    protected AudioCodec(MediaCodecDescriptor descriptor)
        : base(descriptor)
    {
        if (descriptor.Kind != MediaKind.Audio)
            throw new ArgumentException("Audio codecs must use MediaKind.Audio descriptors.", nameof(descriptor));
    }

    public abstract ValueTask<AudioStreamInfo> GetInfoAsync(
        MediaInput input,
        AudioDecodeOptions? options = null,
        CancellationToken cancellationToken = default);

    public abstract ValueTask DecodeAsync(
        MediaInput input,
        IAudioOutput output,
        AudioDecodeOptions? options = null,
        CancellationToken cancellationToken = default);
}

