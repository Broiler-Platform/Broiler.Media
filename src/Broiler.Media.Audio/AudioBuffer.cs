using System;

namespace Broiler.Media.Audio;

public sealed class AudioBuffer
{
    public AudioBuffer(
        ReadOnlyMemory<byte> samples,
        AudioSampleFormat sampleFormat,
        int sampleRate,
        int channels,
        int frameCount,
        TimeSpan timestamp,
        TimeSpan duration)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channels <= 0)
            throw new ArgumentOutOfRangeException(nameof(channels));
        if (frameCount < 0)
            throw new ArgumentOutOfRangeException(nameof(frameCount));
        if (timestamp < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timestamp));
        if (duration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));

        int minimumLength = checked(frameCount * channels * BytesPerSample(sampleFormat));
        if (samples.Length < minimumLength)
            throw new ArgumentException("The audio sample buffer is smaller than the described frame count.", nameof(samples));

        Samples = samples.ToArray();
        SampleFormat = sampleFormat;
        SampleRate = sampleRate;
        Channels = channels;
        FrameCount = frameCount;
        Timestamp = timestamp;
        Duration = duration;
    }

    public ReadOnlyMemory<byte> Samples { get; }

    public AudioSampleFormat SampleFormat { get; }

    public int SampleRate { get; }

    public int Channels { get; }

    public int FrameCount { get; }

    public int BytesPerFrame => checked(Channels * BytesPerSample(SampleFormat));

    public TimeSpan Timestamp { get; }

    public TimeSpan Duration { get; }

    public TimeSpan EndTimestamp => Timestamp + Duration;

    public static int BytesPerSample(AudioSampleFormat format) => format switch
    {
        AudioSampleFormat.PcmU8Interleaved => 1,
        AudioSampleFormat.PcmS16Interleaved => 2,
        AudioSampleFormat.PcmS24Interleaved => 3,
        AudioSampleFormat.PcmS32Interleaved => 4,
        AudioSampleFormat.Float32Interleaved => 4,
        _ => throw new MediaException(new MediaError(MediaErrorCode.InvalidData, $"Unknown audio sample format '{format}'.")),
    };
}
