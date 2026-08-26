using System;

namespace Broiler.Media.Image;

public sealed class ImageFrame
{
    public ImageFrame(ImageBuffer pixels, int delayNumerator, int delayDenominator)
        : this(pixels, delayNumerator, delayDenominator, DelayFromParts(delayNumerator, delayDenominator))
    {
    }

    public ImageFrame(ImageBuffer pixels, TimeSpan duration)
        : this(pixels, 0, 100, duration)
    {
    }

    private ImageFrame(ImageBuffer pixels, int delayNumerator, int delayDenominator, TimeSpan duration)
    {
        if (delayNumerator < 0)
            throw new ArgumentOutOfRangeException(nameof(delayNumerator));
        if (delayDenominator < 0)
            throw new ArgumentOutOfRangeException(nameof(delayDenominator));
        if (duration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));

        Pixels = pixels ?? throw new ArgumentNullException(nameof(pixels));
        DelayNumerator = delayNumerator;
        DelayDenominator = delayDenominator;
        Duration = duration;
    }

    public ImageBuffer Pixels { get; }

    public int DelayNumerator { get; }

    public int DelayDenominator { get; }

    public TimeSpan Delay => Duration;

    public TimeSpan Duration { get; }

    /// <summary>
    /// The duration this frame occupies on an animation timeline, with the very short
    /// delays browsers refuse to honour replaced by <see cref="DefaultAnimationDuration"/>.
    /// GIFs in the wild carry 0 or 10 ms delays meaning "as fast as possible"; every engine
    /// substitutes a tenth of a second rather than spin, so a timeline built from the raw
    /// <see cref="Duration"/> would run far ahead of the browser reference it is compared
    /// against. Used by <see cref="ImageSequence.FrameIndexAt"/>; the raw
    /// <see cref="Duration"/> is left untouched for callers that re-encode.
    /// </summary>
    public TimeSpan EffectiveDuration =>
        Duration < AnimationDurationThreshold ? DefaultAnimationDuration : Duration;

    /// <summary>Delays below this are treated as "unspecified" — Blink's threshold, which is
    /// what the Chromium screenshots the renderer is compared against use.</summary>
    public static TimeSpan AnimationDurationThreshold { get; } = TimeSpan.FromMilliseconds(11);

    /// <summary>The duration substituted for a delay below <see cref="AnimationDurationThreshold"/>.</summary>
    public static TimeSpan DefaultAnimationDuration { get; } = TimeSpan.FromMilliseconds(100);

    private static TimeSpan DelayFromParts(int numerator, int denominator)
    {
        if (numerator < 0)
            throw new ArgumentOutOfRangeException(nameof(numerator));
        if (denominator < 0)
            throw new ArgumentOutOfRangeException(nameof(denominator));

        return TimeSpan.FromSeconds(numerator / (double)(denominator == 0 ? 100 : denominator));
    }
}
