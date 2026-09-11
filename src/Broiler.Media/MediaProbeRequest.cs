using System;

namespace Broiler.Media;

public sealed class MediaProbeRequest(ReadOnlyMemory<byte> prefix, MediaSourceHints? hints = null, MediaLimits? limits = null)
{
    public ReadOnlyMemory<byte> Prefix { get; } = prefix;

    public MediaSourceHints Hints { get; } = hints ?? MediaSourceHints.Empty;

    public MediaLimits Limits { get; } = limits ?? MediaLimits.Default;
}

