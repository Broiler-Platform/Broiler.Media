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

    /// <summary>JPEG's own ceiling, and past any real image.</summary>
    public const int DefaultMaxImageDimension = 65_535;

    /// <summary>Four covers CMYK, which is more than this build decodes.</summary>
    public const int DefaultMaxComponents = 4;

    /// <summary>The largest sampling factor JPEG itself permits.</summary>
    public const int DefaultMaxSamplingFactor = 4;

    /// <summary>Progressive JPEGs use tens of scans; hundreds is already hostile.</summary>
    public const int DefaultMaxScans = 256;

    public const int DefaultMaxRestartInterval = 65_535;

    /// <summary>
    /// Marker segments before a decoder gives up. A file can legitimately carry
    /// many tables and application segments; it cannot carry thousands.
    /// </summary>
    public const int DefaultMaxMarkerSegments = 4_096;

    /// <summary>Coefficient memory, which is four bytes per sample and dwarfs the output.</summary>
    public const long DefaultMaxCoefficientBytes = 512L * 1024 * 1024;

    /// <summary>
    /// Total 8×8 blocks decoded across every component and scan — the unit of
    /// work a block-transform codec actually does.
    /// </summary>
    public const long DefaultMaxBlocks = 64L * 1024 * 1024;

    public static MediaLimits Default { get; } = new();

    public MediaLimits(
        int maxProbeBytes = DefaultMaxProbeBytes,
        long maxEncodedBytes = DefaultMaxEncodedBytes,
        long maxDecodedBytes = DefaultMaxDecodedBytes,
        long maxDecodedSamples = DefaultMaxDecodedSamples,
        long maxImagePixels = DefaultMaxImagePixels,
        int maxFrames = DefaultMaxFrames,
        int maxImageDimension = DefaultMaxImageDimension,
        int maxComponents = DefaultMaxComponents,
        int maxSamplingFactor = DefaultMaxSamplingFactor,
        int maxScans = DefaultMaxScans,
        int maxRestartInterval = DefaultMaxRestartInterval,
        int maxMarkerSegments = DefaultMaxMarkerSegments,
        long maxCoefficientBytes = DefaultMaxCoefficientBytes,
        long maxBlocks = DefaultMaxBlocks)
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
        if (maxImageDimension <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxImageDimension));
        if (maxComponents <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxComponents));
        if (maxSamplingFactor <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxSamplingFactor));
        if (maxScans <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxScans));
        if (maxRestartInterval < 0)
            throw new ArgumentOutOfRangeException(nameof(maxRestartInterval));
        if (maxMarkerSegments <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxMarkerSegments));
        if (maxCoefficientBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxCoefficientBytes));
        if (maxBlocks <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBlocks));

        MaxProbeBytes = maxProbeBytes;
        MaxEncodedBytes = maxEncodedBytes;
        MaxDecodedBytes = maxDecodedBytes;
        MaxDecodedSamples = maxDecodedSamples;
        MaxImagePixels = maxImagePixels;
        MaxFrames = maxFrames;
        MaxImageDimension = maxImageDimension;
        MaxComponents = maxComponents;
        MaxSamplingFactor = maxSamplingFactor;
        MaxScans = maxScans;
        MaxRestartInterval = maxRestartInterval;
        MaxMarkerSegments = maxMarkerSegments;
        MaxCoefficientBytes = maxCoefficientBytes;
        MaxBlocks = maxBlocks;
    }

    public int MaxProbeBytes { get; }

    public long MaxEncodedBytes { get; }

    public long MaxDecodedBytes { get; }

    public long MaxDecodedSamples { get; }

    public long MaxImagePixels { get; }

    public int MaxFrames { get; }

    /// <summary>Largest width or height a frame header may declare.</summary>
    /// <remarks>
    /// Checked separately from <see cref="MaxImagePixels"/> and before it,
    /// because the product of two declared dimensions is what overflows: a
    /// decoder that multiplies first has already lost.
    /// </remarks>
    public int MaxImageDimension { get; }

    /// <summary>Largest number of components a frame may declare.</summary>
    public int MaxComponents { get; }

    /// <summary>
    /// Largest horizontal or vertical sampling factor. Sampling multiplies the
    /// block count, so an out-of-range factor buys a large allocation from a
    /// small header.
    /// </summary>
    public int MaxSamplingFactor { get; }

    /// <summary>Largest number of scans in one image.</summary>
    public int MaxScans { get; }

    /// <summary>Largest restart interval a file may declare.</summary>
    public int MaxRestartInterval { get; }

    /// <summary>
    /// Largest number of marker segments a decoder walks before giving up.
    /// </summary>
    public int MaxMarkerSegments { get; }

    /// <summary>
    /// Largest total coefficient memory. Four bytes per sample, so this is the
    /// allocation that dwarfs the decoded image rather than the decoded image
    /// itself.
    /// </summary>
    public long MaxCoefficientBytes { get; }

    /// <summary>
    /// Largest total block count across components and scans — the work budget,
    /// which bounds effort where the byte budgets bound memory.
    /// </summary>
    public long MaxBlocks { get; }
}

