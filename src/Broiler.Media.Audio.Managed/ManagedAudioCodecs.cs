using System.Collections.Generic;

namespace Broiler.Media.Audio.Managed;

public static class ManagedAudioCodecs
{
    public static IReadOnlyList<AudioCodec> CreateCodecs() => [new WaveAudioCodec()];
}
