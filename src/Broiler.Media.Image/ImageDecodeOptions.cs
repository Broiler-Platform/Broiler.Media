namespace Broiler.Media.Image;

public sealed class ImageDecodeOptions
{
    public ImageDecodeOptions(MediaLimits? limits = null, bool preserveAnimation = true)
    {
        Limits = limits ?? MediaLimits.Default;
        PreserveAnimation = preserveAnimation;
    }

    public MediaLimits Limits { get; }

    public bool PreserveAnimation { get; }
}

