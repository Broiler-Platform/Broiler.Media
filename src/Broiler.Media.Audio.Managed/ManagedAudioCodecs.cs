using System.Collections.Generic;

namespace Broiler.Media.Audio.Managed;

// Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=1; Fingerprint=TBF
// Broiler-Falsified-If: the returned list omits WaveAudioCodec or holds a codec whose descriptor kind is not Audio
// Broiler-Human:        PENDING
public static class ManagedAudioCodecs
{
    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=1; Fingerprint=TBF
    // Broiler-Falsified-If: two calls return the same WaveAudioCodec instance instead of a fresh list of new codecs
    // Broiler-Human:        PENDING
    public static IReadOnlyList<AudioCodec> CreateCodecs() => [new WaveAudioCodec()];
}
