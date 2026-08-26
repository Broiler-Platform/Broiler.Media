using System;

namespace Broiler.Media.Video;

public sealed class VideoStreamInfo
{
    public VideoStreamInfo(
        int codedWidth,
        int codedHeight,
        int displayWidth,
        int displayHeight,
        TimeSpan? duration = null,
        double? frameRateHint = null,
        int rotationDegrees = 0)
    {
        if (codedWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(codedWidth));
        if (codedHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(codedHeight));
        if (displayWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(displayWidth));
        if (displayHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(displayHeight));
        if (duration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));
        if (frameRateHint <= 0)
            throw new ArgumentOutOfRangeException(nameof(frameRateHint));

        CodedWidth = codedWidth;
        CodedHeight = codedHeight;
        DisplayWidth = displayWidth;
        DisplayHeight = displayHeight;
        Duration = duration;
        FrameRateHint = frameRateHint;
        RotationDegrees = rotationDegrees;
    }

    public int CodedWidth { get; }

    public int CodedHeight { get; }

    public int DisplayWidth { get; }

    public int DisplayHeight { get; }

    public TimeSpan? Duration { get; }

    public double? FrameRateHint { get; }

    public int RotationDegrees { get; }
}

