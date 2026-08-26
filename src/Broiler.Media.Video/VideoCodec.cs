using System;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media.Video;

public abstract class VideoCodec : MediaCodec
{
    protected VideoCodec(MediaCodecDescriptor descriptor)
        : base(descriptor)
    {
        if (descriptor.Kind != MediaKind.Video)
            throw new ArgumentException("Video codecs must use MediaKind.Video descriptors.", nameof(descriptor));
    }

    public abstract ValueTask<VideoStreamInfo> GetInfoAsync(
        MediaInput input,
        VideoDecodeOptions? options = null,
        CancellationToken cancellationToken = default);

    public abstract ValueTask<IVideoSession> OpenSessionAsync(
        MediaInput input,
        IVideoOutput output,
        VideoSessionOptions? options = null,
        CancellationToken cancellationToken = default);
}

