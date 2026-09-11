namespace Broiler.Media.Video;

public sealed class VideoDecodeOptions(MediaLimits? limits = null)
{
    public MediaLimits Limits { get; } = limits ?? MediaLimits.Default;
}

