using System;

namespace Broiler.Media;

public sealed class MediaProbeOptions
{
    public static MediaProbeOptions Default { get; } = new();

    public MediaProbeOptions(MediaLimits? limits = null)
    {
        Limits = limits ?? MediaLimits.Default;
        if (Limits.MaxProbeBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(limits));
    }

    public MediaLimits Limits { get; }
}

