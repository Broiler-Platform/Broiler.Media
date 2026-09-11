using System;

namespace Broiler.Media.Video;

public sealed class VideoSessionEvent : EventArgs
{
    public VideoSessionEvent(VideoSessionEventKind kind, VideoSessionState state, TimeSpan position, MediaError? error = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(position, TimeSpan.Zero);

        Kind = kind;
        State = state;
        Position = position;
        Error = error;
    }

    public VideoSessionEventKind Kind { get; }

    public VideoSessionState State { get; }

    public TimeSpan Position { get; }

    public MediaError? Error { get; }
}
