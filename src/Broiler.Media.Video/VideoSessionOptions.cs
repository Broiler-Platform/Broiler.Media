namespace Broiler.Media.Video;

public sealed class VideoSessionOptions(bool autoplay = false, bool muted = false)
{
    public bool Autoplay { get; } = autoplay;

    public bool Muted { get; } = muted;
}

