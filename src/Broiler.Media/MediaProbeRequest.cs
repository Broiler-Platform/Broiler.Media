using System;

namespace Broiler.Media;

public sealed class MediaProbeRequest
{
    public MediaProbeRequest(ReadOnlyMemory<byte> prefix, MediaSourceHints? hints = null, MediaLimits? limits = null)
    {
        Prefix = prefix;
        Hints = hints ?? MediaSourceHints.Empty;
        Limits = limits ?? MediaLimits.Default;
    }

    public ReadOnlyMemory<byte> Prefix { get; }

    public MediaSourceHints Hints { get; }

    public MediaLimits Limits { get; }
}

