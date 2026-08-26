using System;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media;

public abstract class MediaCodec
{
    protected MediaCodec(MediaCodecDescriptor descriptor)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
    }

    public MediaCodecDescriptor Descriptor { get; }

    public MediaCodecId Id => Descriptor.Id;

    public MediaKind Kind => Descriptor.Kind;

    public abstract ValueTask<MediaProbeResult> ProbeAsync(
        MediaProbeRequest request,
        CancellationToken cancellationToken = default);
}

