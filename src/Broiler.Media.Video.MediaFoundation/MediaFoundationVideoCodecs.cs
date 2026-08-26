using System.Collections.Generic;
using System.Runtime.Versioning;

namespace Broiler.Media.Video.MediaFoundation;

[SupportedOSPlatform("windows")]
public static class MediaFoundationVideoCodecs
{
    public static IReadOnlyList<VideoCodec> CreateCodecs() => [new MediaFoundationVideoCodec()];
}
