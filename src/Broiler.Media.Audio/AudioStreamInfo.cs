using System;

namespace Broiler.Media.Audio;

public sealed class AudioStreamInfo
{
    public AudioStreamInfo(int sampleRate, int channels, AudioSampleFormat sourceFormat, TimeSpan? duration = null, 
        long? totalFrames = null, int bitsPerSample = 0, int blockAlign = 0, int byteRate = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        ArgumentOutOfRangeException.ThrowIfNegative(bitsPerSample);

        if (duration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));

        if (totalFrames < 0)
            throw new ArgumentOutOfRangeException(nameof(totalFrames));

        SampleRate = sampleRate;
        Channels = channels;
        SourceFormat = sourceFormat;
        Duration = duration;
        TotalFrames = totalFrames;
        BitsPerSample = bitsPerSample > 0 ? bitsPerSample : checked(AudioBuffer.BytesPerSample(sourceFormat) * 8);
        BlockAlign = blockAlign > 0 ? blockAlign : checked(channels * AudioBuffer.BytesPerSample(sourceFormat));
        ByteRate = byteRate > 0 ? byteRate : checked(sampleRate * BlockAlign);
    }

    public int SampleRate { get; }

    public int Channels { get; }

    public AudioSampleFormat SourceFormat { get; }

    public TimeSpan? Duration { get; }

    public long? TotalFrames { get; }

    public int BitsPerSample { get; }

    public int BlockAlign { get; }

    public int ByteRate { get; }
}
