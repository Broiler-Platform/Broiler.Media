namespace Broiler.Media.Image;

public sealed class ImageDecodeOptions(MediaLimits? limits = null, bool preserveAnimation = true)
{
    public MediaLimits Limits { get; } = limits ?? MediaLimits.Default;

    public bool PreserveAnimation { get; } = preserveAnimation;
}

