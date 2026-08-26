using System;

namespace Broiler.Media;

public sealed class MediaLimits
{
    public const int DefaultMaxProbeBytes = 4096;
    public const long DefaultMaxEncodedBytes = 256L * 1024 * 1024;
    public const long DefaultMaxDecodedBytes = 512L * 1024 * 1024;
    public const long DefaultMaxDecodedSamples = 48_000L * 60L * 60L * 2L;
    public const long DefaultMaxImagePixels = 16_384L * 16_384L;
    public const int DefaultMaxFrames = 10_000;

    public static MediaLimits Default { get; } = new();

    public MediaLimits(
        int maxProbeBytes = DefaultMaxProbeBytes,
        long maxEncodedBytes = DefaultMaxEncodedBytes,
        long maxDecodedBytes = DefaultMaxDecodedBytes,
        long maxDecodedSamples = DefaultMaxDecodedSamples,
        long maxImagePixels = DefaultMaxImagePixels,
        int maxFrames = DefaultMaxFrames)
    {
        if (maxProbeBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxProbeBytes));
        if (maxEncodedBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxEncodedBytes));
        if (maxDecodedBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxDecodedBytes));
        if (maxDecodedSamples <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxDecodedSamples));
        if (maxImagePixels <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxImagePixels));
        if (maxFrames <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxFrames));

        MaxProbeBytes = maxProbeBytes;
        MaxEncodedBytes = maxEncodedBytes;
        MaxDecodedBytes = maxDecodedBytes;
        MaxDecodedSamples = maxDecodedSamples;
        MaxImagePixels = maxImagePixels;
        MaxFrames = maxFrames;
    }

    public int MaxProbeBytes { get; }

    public long MaxEncodedBytes { get; }

    public long MaxDecodedBytes { get; }

    public long MaxDecodedSamples { get; }

    public long MaxImagePixels { get; }

    public int MaxFrames { get; }
}

