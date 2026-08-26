namespace Broiler.Media.Video;

public enum VideoSessionEventKind
{
    Loading,
    Ready,
    Playing,
    Paused,
    Seeked,
    Ended,
    Failed,
    Disposed,
    TargetChanged,
    TargetLost,
}
