using System;

namespace Broiler.Media.Audio;

// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=3; Fingerprint=TBF
// Broiler-Falsified-If: a samples buffer shorter than frameCount times channels times the bytes per sample of its format is accepted
// Broiler-Human:        PENDING
public sealed class AudioBuffer
{
    // Broiler-AI:           Origin=AI; Spec=ADR-0002; IP=Low; Security=High; Resources=3; Fingerprint=TBF
    // Broiler-Falsified-If: a samples buffer shorter than frameCount times channels times the bytes per sample of its format is accepted
    // Broiler-Human:        PENDING
    public AudioBuffer(ReadOnlyMemory<byte> samples, AudioSampleFormat sampleFormat, int sampleRate, 
        int channels, int frameCount, TimeSpan timestamp, TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        ArgumentOutOfRangeException.ThrowIfNegative(frameCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(timestamp, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);

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

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a 64-channel Float32Interleaved buffer reports a frame size other than 256 bytes
    // Broiler-Human:        PENDING
    public int BytesPerFrame => checked(Channels * BytesPerSample(SampleFormat));

    public TimeSpan Timestamp { get; }

    public TimeSpan Duration { get; }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a timestamp and duration near TimeSpan.MaxValue wrap to an end timestamp earlier than the start instead of throwing OverflowException
    // Broiler-Human:        PENDING
    public TimeSpan EndTimestamp => Timestamp + Duration;

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: Float32Interleaved or PcmS32Interleaved maps to a size other than 4, so decode output and the AudioBuffer length check are sized too small
    // Broiler-Human:        PENDING
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
