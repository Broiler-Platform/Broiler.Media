namespace Broiler.Media.Video;

public sealed class VideoSessionOptions
{
    public VideoSessionOptions(bool autoplay = false, bool muted = false)
    {
        Autoplay = autoplay;
        Muted = muted;
    }

    public bool Autoplay { get; }

    public bool Muted { get; }
}

