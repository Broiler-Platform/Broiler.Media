namespace Broiler.Media.Video.MediaFoundation;

internal readonly record struct MediaFoundationMediaEngineEvent(
    MediaFoundationMediaEngineEventKind Kind,
    nuint Param1,
    uint Param2);
