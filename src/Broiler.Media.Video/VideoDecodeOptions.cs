namespace Broiler.Media.Video;

public sealed class VideoDecodeOptions
{
    public VideoDecodeOptions(MediaLimits? limits = null)
    {
        Limits = limits ?? MediaLimits.Default;
    }

    public MediaLimits Limits { get; }
}

