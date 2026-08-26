using System;

namespace Broiler.Media.Audio;

public sealed class AudioDecodeOptions
{
    public const int DefaultMaxFramesPerBuffer = 4096;

    public AudioDecodeOptions(
        AudioSampleFormat outputSampleFormat = AudioSampleFormat.PcmS16Interleaved,
        int maxFramesPerBuffer = DefaultMaxFramesPerBuffer,
        MediaLimits? limits = null)
    {
        if (maxFramesPerBuffer <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxFramesPerBuffer));

        OutputSampleFormat = outputSampleFormat;
        MaxFramesPerBuffer = maxFramesPerBuffer;
        Limits = limits ?? MediaLimits.Default;
    }

    public AudioSampleFormat OutputSampleFormat { get; }

    public int MaxFramesPerBuffer { get; }

    public MediaLimits Limits { get; }
}

