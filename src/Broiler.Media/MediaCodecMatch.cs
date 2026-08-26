using System;

namespace Broiler.Media;

public sealed class MediaCodecMatch
{
    public MediaCodecMatch(MediaCodec codec, MediaProbeResult result)
    {
        Codec = codec ?? throw new ArgumentNullException(nameof(codec));
        Result = result ?? throw new ArgumentNullException(nameof(result));
        if (!result.IsMatch)
            throw new ArgumentException("A codec match requires a positive probe result.", nameof(result));
    }

    public MediaCodec Codec { get; }

    public MediaProbeResult Result { get; }
}

