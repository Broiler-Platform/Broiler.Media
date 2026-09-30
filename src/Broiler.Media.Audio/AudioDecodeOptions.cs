using System;

namespace Broiler.Media.Audio;

// Broiler-AI:           Origin=AI; Spec=ADR-0002; IP=Low; Security=High; Resources=0; Fingerprint=TBF
// Broiler-Falsified-If: options built with limits left null decode under no MediaLimits instead of MediaLimits.Default
// Broiler-Human:        PENDING
public sealed class AudioDecodeOptions
{
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: the default source buffer for 64-channel 32-bit PCM, this value times 256 bytes, exceeds 1 MiB
    // Broiler-Human:        PENDING
    public const int DefaultMaxFramesPerBuffer = 4096;

    // Broiler-AI:           Origin=AI; Spec=ADR-0002; IP=Low; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a maxFramesPerBuffer of 0 or less is accepted, or limits left null leave Limits null instead of MediaLimits.Default
    // Broiler-Human:        PENDING
    public AudioDecodeOptions(AudioSampleFormat outputSampleFormat = AudioSampleFormat.PcmS16Interleaved, 
        int maxFramesPerBuffer = DefaultMaxFramesPerBuffer, MediaLimits? limits = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFramesPerBuffer);

        OutputSampleFormat = outputSampleFormat;
        MaxFramesPerBuffer = maxFramesPerBuffer;
        Limits = limits ?? MediaLimits.Default;
    }

    public AudioSampleFormat OutputSampleFormat { get; }

    public int MaxFramesPerBuffer { get; }

    public MediaLimits Limits { get; }
}

