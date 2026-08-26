namespace Broiler.Media.Video.MediaFoundation;

internal enum MediaFoundationMediaEngineEventKind : uint
{
    LoadStart = 1,
    Error = 5,
    Play = 8,
    Pause = 9,
    LoadedMetadata = 10,
    Playing = 13,
    Seeking = 16,
    Seeked = 17,
    Ended = 19,
    DurationChange = 21,
    VolumeChange = 22,
    FormatChange = 1000,
    BufferingStarted = 1005,
    BufferingEnded = 1006,
    FirstFrameReady = 1009,
    ResourceLost = 1012,
    StreamRenderingError = 1014,
}
