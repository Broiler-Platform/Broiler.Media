namespace Broiler.Media.Image;

public sealed class ImageEncodeOptions
{
    public ImageEncodeOptions(ImageEncodeFormat format = ImageEncodeFormat.Png, int quality = 100)
    {
        if (quality is < 1 or > 100)
            throw new System.ArgumentOutOfRangeException(nameof(quality));

        Format = format;
        Quality = quality;
    }

    public ImageEncodeFormat Format { get; }

    public int Quality { get; }
}

