using System;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media;

public abstract class MediaCodec(MediaCodecDescriptor descriptor)
{
    public MediaCodecDescriptor Descriptor { get; } = descriptor ?? throw new ArgumentNullException(nameof(descriptor));

    public MediaCodecId Id => Descriptor.Id;

    public MediaKind Kind => Descriptor.Kind;

    public abstract ValueTask<MediaProbeResult> ProbeAsync(MediaProbeRequest request, CancellationToken cancellationToken = default);
}

