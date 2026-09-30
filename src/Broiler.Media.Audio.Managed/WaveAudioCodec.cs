using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media.Audio.Managed;

// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=7; Fingerprint=TBF
// Broiler-Falsified-If: a RIFF/WAVE stream whose data chunk holds more samples than MaxDecodedSamples is decoded into audio buffers instead of failing with LimitExceeded
// Broiler-Human:        PENDING
public sealed class WaveAudioCodec : AudioCodec
{
    public static MediaCodecDescriptor CodecDescriptor { get; } = new(new MediaCodecId("broiler.audio.wave.managed"),
        "Broiler managed RIFF/WAVE PCM", MediaKind.Audio, MediaCodecCapabilities.Decode | MediaCodecCapabilities.Streaming,
        [
            new MediaFormatDescriptor("WAVE",
                ["audio/wav", "audio/wave", "audio/x-wav"],
                [".wav", ".wave"]),
        ]);

    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: the codec registers under a descriptor other than CodecDescriptor, so catalog selection by id or MIME type misses it
    // Broiler-Human:        PENDING
    public WaveAudioCodec() : base(CodecDescriptor) { }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=1; Fingerprint=TBF
    // Broiler-Falsified-If: a prefix shorter than 12 bytes, or one with RIFF at offset 0 but not WAVE at offset 8, is reported as a certain WAVE match
    // Broiler-Human:        PENDING
    public override ValueTask<MediaProbeResult> ProbeAsync(MediaProbeRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ReadOnlySpan<byte> prefix = request.Prefix.Span;
        MediaProbeResult result = prefix.Length >= 12
            && prefix[..4].SequenceEqual("RIFF"u8)
            && prefix[8..12].SequenceEqual("WAVE"u8)
                ? MediaProbeResult.Match(MediaKind.Audio, MediaProbeConfidence.Certain, "WAVE", "audio/wav", 12)
                : MediaProbeResult.NoMatch(MediaKind.Audio);

        return ValueTask.FromResult(result);
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=4; Fingerprint=TBF
    // Broiler-Falsified-If: a stream with no data chunk, or with a data chunk ahead of its fmt chunk, returns an AudioStreamInfo instead of throwing MediaException with InvalidData
    // Broiler-Human:        PENDING
    public override async ValueTask<AudioStreamInfo> GetInfoAsync(MediaInput input,
        AudioDecodeOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        AudioDecodeOptions effectiveOptions = options ?? new AudioDecodeOptions();
        var reader = new WaveReader(input.Stream, effectiveOptions.Limits);
        WaveDataChunk data = await reader.ReadToDataAsync(cancellationToken).ConfigureAwait(false);

        return data.ToStreamInfo();
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=7; Fingerprint=TBF
    // Broiler-Falsified-If: a malformed or truncated WAVE stream makes this throw something other than MediaException, or return, without FailAsync having been called on the output
    // Broiler-Human:        PENDING
    public override async ValueTask DecodeAsync(MediaInput input, IAudioOutput output,
        AudioDecodeOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        AudioDecodeOptions effectiveOptions = options ?? new AudioDecodeOptions();
        ValidateOutputFormat(effectiveOptions.OutputSampleFormat);

        try
        {
            var reader = new WaveReader(input.Stream, effectiveOptions.Limits);
            WaveDataChunk data = await reader.ReadToDataAsync(cancellationToken).ConfigureAwait(false);

            await DecodeDataAsync(reader, data, output, effectiveOptions, cancellationToken).ConfigureAwait(false);
            await output.CompleteAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (MediaException ex)
        {
            await SignalFailureAsync(output, ex.Error).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            var error = new MediaError(MediaErrorCode.InvalidData, "WAVE decode failed.", Id);
            await SignalFailureAsync(output, error).ConfigureAwait(false);
            throw new MediaException(error, ex);
        }
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=7; Fingerprint=TBF
    // Broiler-Falsified-If: a data length that is not a multiple of BlockAlign leaves remainingBytes positive with a frame count of 0, so the read loop never ends
    // Broiler-Human:        PENDING
    private static async ValueTask DecodeDataAsync(WaveReader reader, WaveDataChunk data, IAudioOutput output,
        AudioDecodeOptions options, CancellationToken cancellationToken)
    {
        int outputBytesPerSample = AudioBuffer.BytesPerSample(options.OutputSampleFormat);
        int outputBytesPerFrame = checked(data.Format.Channels * outputBytesPerSample);
        int maxFrames = Math.Min(options.MaxFramesPerBuffer, int.MaxValue / Math.Max(data.Format.BlockAlign, outputBytesPerFrame));
        if (maxFrames <= 0)
            throw new MediaException(new MediaError(MediaErrorCode.LimitExceeded, "Audio buffer size would exceed the supported allocation limit."));

        byte[] source = new byte[checked(maxFrames * data.Format.BlockAlign)];
        long remainingBytes = data.DataByteLength;
        long frameIndex = 0;

        while (remainingBytes > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int frameCount = (int)Math.Min(maxFrames, remainingBytes / data.Format.BlockAlign);
            int sourceByteCount = checked(frameCount * data.Format.BlockAlign);
            await reader.ReadExactlyAsync(source.AsMemory(0, sourceByteCount), "WAVE data", cancellationToken).ConfigureAwait(false);

            byte[] converted = ConvertSamples(source.AsSpan(0, sourceByteCount), data.Format, options.OutputSampleFormat, frameCount);
            var buffer = new AudioBuffer(converted, options.OutputSampleFormat, data.Format.SampleRate, data.Format.Channels, frameCount,
                FramesToTime(frameIndex, data.Format.SampleRate), FramesToTime(frameCount, data.Format.SampleRate));

            await output.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            frameIndex += frameCount;
            remainingBytes -= sourceByteCount;
        }
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=3; Fingerprint=TBF
    // Broiler-Falsified-If: a converted sample is written at an offset other than its sample index times the output sample size, so channels come out of interleave order
    // Broiler-Human:        PENDING
    private static byte[] ConvertSamples(ReadOnlySpan<byte> source, WaveFormat format, AudioSampleFormat outputFormat, int frameCount)
    {
        int sampleCount = checked(frameCount * format.Channels);
        int outputBytesPerSample = AudioBuffer.BytesPerSample(outputFormat);
        byte[] output = new byte[checked(sampleCount * outputBytesPerSample)];

        for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            int sourceOffset = sampleIndex * format.SourceBytesPerSample;
            int sample = ReadPcmSample(source.Slice(sourceOffset, format.SourceBytesPerSample), format.BitsPerSample);
            int outputOffset = sampleIndex * outputBytesPerSample;

            if (outputFormat == AudioSampleFormat.PcmS16Interleaved)
            {
                short value = ConvertToInt16(sample, format.BitsPerSample);
                BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(outputOffset, 2), value);
            }
            else
            {
                float value = ConvertToFloat(sample, format.BitsPerSample);
                BinaryPrimitives.WriteSingleLittleEndian(output.AsSpan(outputOffset, 4), value);
            }
        }

        return output;
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: the unsigned 8-bit byte 0x00 is read as anything other than -128, or a 16, 24 or 32-bit sample is read in big-endian byte order
    // Broiler-Human:        PENDING
    private static int ReadPcmSample(ReadOnlySpan<byte> source, int bitsPerSample) => bitsPerSample switch
    {
        8 => source[0] - 128,
        16 => BinaryPrimitives.ReadInt16LittleEndian(source),
        24 => SignExtend24(source[0] | (source[1] << 8) | (source[2] << 16)),
        32 => BinaryPrimitives.ReadInt32LittleEndian(source),
        _ => throw Unsupported($"Unsupported PCM bit depth {bitsPerSample}."),
    };

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: the 24-bit value 0x800000 comes back as 8388608 instead of -8388608
    // Broiler-Human:        PENDING
    private static int SignExtend24(int value) => (value & 0x800000) != 0 ? value | unchecked((int)0xFF000000) : value;

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a full-scale negative 24-bit or 32-bit sample throws OverflowException from the checked cast instead of mapping to -32768
    // Broiler-Human:        PENDING
    private static short ConvertToInt16(int sample, int bitsPerSample) => bitsPerSample switch
    {
        8 => checked((short)(sample << 8)),
        16 => checked((short)sample),
        24 => checked((short)(sample >> 8)),
        32 => checked((short)(sample >> 16)),
        _ => throw Unsupported($"Unsupported PCM bit depth {bitsPerSample}."),
    };

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a full-scale negative sample of any supported bit depth does not convert to exactly -1.0f
    // Broiler-Human:        PENDING
    private static float ConvertToFloat(int sample, int bitsPerSample) => bitsPerSample switch
    {
        8 => sample / 128f,
        16 => sample / 32768f,
        24 => sample / 8388608f,
        32 => sample / 2147483648f,
        _ => throw Unsupported($"Unsupported PCM bit depth {bitsPerSample}."),
    };

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a frame count whose product with TicksPerSecond exceeds the range of long wraps to a negative timestamp instead of throwing OverflowException
    // Broiler-Human:        PENDING
    private static TimeSpan FramesToTime(long frames, int sampleRate) =>
        TimeSpan.FromTicks(checked(frames * TimeSpan.TicksPerSecond / sampleRate));

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: PcmU8Interleaved, PcmS24Interleaved or PcmS32Interleaved passes as a requested output format
    // Broiler-Human:        PENDING
    private static void ValidateOutputFormat(AudioSampleFormat format)
    {
        if (format is not (AudioSampleFormat.PcmS16Interleaved or AudioSampleFormat.Float32Interleaved))
            throw Unsupported($"WAVE decode cannot output {format}.");
    }

    // Broiler-AI:           Origin=AI; IP=Low; Security=Low; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: an exception thrown by the output from FailAsync replaces the original decode exception seen by the caller of DecodeAsync
    // Broiler-Human:        PENDING
    private static async ValueTask SignalFailureAsync(IAudioOutput output, MediaError error)
    {
        try
        {
            await output.FailAsync(error).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=1; Fingerprint=TBF
    // Broiler-Falsified-If: the exception built here carries an error code other than InvalidData or omits the WAVE codec id
    // Broiler-Human:        PENDING
    private static MediaException Invalid(string message, long? byteOffset = null) =>
        new(new MediaError(MediaErrorCode.InvalidData, message, CodecDescriptor.Id, byteOffset));

    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=1; Fingerprint=TBF
    // Broiler-Falsified-If: the exception built here carries an error code other than UnsupportedFormat or omits the WAVE codec id
    // Broiler-Human:        PENDING
    private static MediaException Unsupported(string message, long? byteOffset = null) =>
        new(new MediaError(MediaErrorCode.UnsupportedFormat, message, CodecDescriptor.Id, byteOffset));

    // Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=1; Fingerprint=TBF
    // Broiler-Falsified-If: the exception built here carries an error code other than LimitExceeded or omits the WAVE codec id
    // Broiler-Human:        PENDING
    private static MediaException Limit(string message, long? byteOffset = null) =>
        new(new MediaError(MediaErrorCode.LimitExceeded, message, CodecDescriptor.Id, byteOffset));

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=4; Fingerprint=TBF
    // Broiler-Falsified-If: a chunk size field larger than the bytes left in the stream is skipped or read past without InvalidData or LimitExceeded being thrown
    // Broiler-Human:        PENDING
    private sealed class WaveReader(Stream stream, MediaLimits limits)
    {
        private readonly byte[] _scratch = new byte[16];
        private long _offset;

        // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=4; Fingerprint=TBF
        // Broiler-Falsified-If: a chunk whose declared padded size would take the offset past MaxEncodedBytes is skipped or read instead of refused with LimitExceeded
        // Broiler-Human:        PENDING
        public async ValueTask<WaveDataChunk> ReadToDataAsync(CancellationToken cancellationToken)
        {
            await ReadExactlyAsync(_scratch.AsMemory(0, 12), "RIFF header", cancellationToken).ConfigureAwait(false);
            ReadOnlySpan<byte> header = _scratch.AsSpan(0, 12);
            if (!header[..4].SequenceEqual("RIFF"u8) || !header[8..12].SequenceEqual("WAVE"u8))
                throw Invalid("Input is not a RIFF/WAVE stream.", 0);

            uint riffSize = BinaryPrimitives.ReadUInt32LittleEndian(header[4..8]);
            if (checked(riffSize + 8L) > limits.MaxEncodedBytes)
                throw Limit("RIFF payload exceeds the configured encoded-byte limit.", _offset);

            WaveFormat? format = null;
            while (await TryReadChunkHeaderAsync(cancellationToken).ConfigureAwait(false))
            {
                ReadOnlySpan<byte> chunk = _scratch.AsSpan(0, 8);
                uint chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..8]);
                long paddedChunkSize = chunkSize + (chunkSize & 1u);
                EnsureDeclaredEncodedBytes(paddedChunkSize, "WAVE chunk");

                if (chunk[..4].SequenceEqual("fmt "u8))
                {
                    format = await ReadFormatAsync(chunkSize, cancellationToken).ConfigureAwait(false);
                }
                else if (chunk[..4].SequenceEqual("data"u8))
                {
                    if (format is null)
                        throw Invalid("WAVE data chunk appeared before the fmt chunk.", _offset - 8);

                    return CreateDataChunk(format.Value, chunkSize);
                }
                else
                {
                    await SkipAsync(paddedChunkSize, cancellationToken).ConfigureAwait(false);
                }
            }

            throw Invalid("WAVE stream does not contain a data chunk.", _offset);
        }

        // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=3; Fingerprint=TBF
        // Broiler-Falsified-If: a stream that returns 0 before the buffer is full lets the method return normally with a partly filled buffer
        // Broiler-Human:        PENDING
        public async ValueTask ReadExactlyAsync(Memory<byte> buffer, string context, CancellationToken cancellationToken)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer[total..], cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    throw Invalid($"Truncated {context}.", _offset);

                total += read;
                _offset += read;
                EnsureActualEncodedBytes();
            }
        }

        // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=1; Fingerprint=TBF
        // Broiler-Falsified-If: a stream that ends after 1 to 7 bytes of a chunk header returns false as a clean end instead of throwing InvalidData
        // Broiler-Human:        PENDING
        private async ValueTask<bool> TryReadChunkHeaderAsync(CancellationToken cancellationToken)
        {
            int total = 0;
            Memory<byte> header = _scratch.AsMemory(0, 8);
            while (total < 8)
            {
                int read = await stream.ReadAsync(header[total..], cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    if (total == 0)
                        return false;

                    throw Invalid("Truncated WAVE chunk header.", _offset);
                }

                total += read;
                _offset += read;
                EnsureActualEncodedBytes();
            }

            return true;
        }

        // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=4; Fingerprint=TBF
        // Broiler-Falsified-If: a fmt chunk whose block alignment differs from the channel count times the bytes per sample is accepted
        // Broiler-Human:        PENDING
        private async ValueTask<WaveFormat> ReadFormatAsync(uint chunkSize, CancellationToken cancellationToken)
        {
            if (chunkSize < 16)
                throw Invalid("WAVE fmt chunk is too small.", _offset - 8);

            await ReadExactlyAsync(_scratch.AsMemory(0, 16), "WAVE fmt chunk", cancellationToken).ConfigureAwait(false);
            ReadOnlySpan<byte> fmt = _scratch.AsSpan(0, 16);
            ushort formatTag = BinaryPrimitives.ReadUInt16LittleEndian(fmt[0..2]);
            ushort channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt[2..4]);
            uint sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(fmt[4..8]);
            uint byteRate = BinaryPrimitives.ReadUInt32LittleEndian(fmt[8..12]);
            ushort blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(fmt[12..14]);
            ushort bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(fmt[14..16]);

            long remaining = chunkSize - 16L;
            if (remaining > 0)
                await SkipAsync(remaining, cancellationToken).ConfigureAwait(false);

            if ((chunkSize & 1) != 0)
                await SkipAsync(1, cancellationToken).ConfigureAwait(false);

            if (formatTag != 1)
                throw Unsupported($"Unsupported WAVE format tag {formatTag}.", _offset);

            if (channels == 0 || channels > 64)
                throw Invalid("WAVE channel count is invalid.", _offset);

            if (sampleRate == 0 || sampleRate > int.MaxValue)
                throw Invalid("WAVE sample rate is invalid.", _offset);

            if (bitsPerSample is not (8 or 16 or 24 or 32))
                throw Unsupported($"Unsupported PCM bit depth {bitsPerSample}.", _offset);

            int sourceBytesPerSample = (bitsPerSample + 7) / 8;
            int expectedBlockAlign = checked(channels * sourceBytesPerSample);
            if (blockAlign != expectedBlockAlign)
                throw Invalid("WAVE block alignment does not match channel count and bit depth.", _offset);

            long expectedByteRate = (long)sampleRate * blockAlign;

            if (expectedByteRate > uint.MaxValue)
                throw Invalid("WAVE byte rate exceeds the supported range.", _offset);

            if (byteRate != expectedByteRate)
                throw Invalid("WAVE byte rate does not match sample rate and block alignment.", _offset);

            if (byteRate > int.MaxValue)
                throw Invalid("WAVE byte rate exceeds the supported range.", _offset);

            return new WaveFormat((int)sampleRate, channels, bitsPerSample, blockAlign, (int)byteRate, sourceBytesPerSample);
        }

        // Broiler-AI:           Origin=AI; Spec=ADR-0002; IP=Low; Security=High; Resources=0; Fingerprint=TBF
        // Broiler-Falsified-If: an 8-bit PCM data chunk no larger than MaxDecodedBytes is accepted although its Float32 output is four times MaxDecodedBytes
        // Broiler-Human:        PENDING
        private WaveDataChunk CreateDataChunk(WaveFormat format, uint dataByteLength)
        {
            if (dataByteLength % format.BlockAlign != 0)
                throw Invalid("WAVE data chunk contains a partial audio frame.", _offset - 8);

            if (dataByteLength > limits.MaxDecodedBytes)
                throw Limit("WAVE data exceeds the configured decoded-byte limit.", _offset - 8);

            long totalFrames = dataByteLength / format.BlockAlign;
            long totalSamples = checked(totalFrames * format.Channels);
            if (totalSamples > limits.MaxDecodedSamples)
                throw Limit("WAVE data exceeds the configured decoded-sample limit.", _offset - 8);

            return new WaveDataChunk(format, dataByteLength, totalFrames);
        }

        // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=4; Fingerprint=TBF
        // Broiler-Falsified-If: on a seekable stream a skip longer than Length minus Position moves Position past the end instead of throwing InvalidData
        // Broiler-Human:        PENDING
        private async ValueTask SkipAsync(long byteCount, CancellationToken cancellationToken)
        {
            if (byteCount < 0)
                throw Invalid("WAVE chunk size overflowed.", _offset);

            if (byteCount == 0)
                return;

            if (stream.CanSeek)
            {
                long available = stream.Length - stream.Position;
                if (available < byteCount)
                    throw Invalid("Truncated WAVE chunk payload.", _offset + Math.Max(available, 0));

                stream.Position += byteCount;
                _offset += byteCount;
                EnsureActualEncodedBytes();
                return;
            }

            byte[] discard = new byte[Math.Min(8192, checked((int)Math.Min(byteCount, int.MaxValue)))];
            long remaining = byteCount;
            while (remaining > 0)
            {
                int requested = (int)Math.Min(discard.Length, remaining);
                int read = await stream.ReadAsync(discard.AsMemory(0, requested), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    throw Invalid("Truncated WAVE chunk payload.", _offset);

                remaining -= read;
                _offset += read;
                EnsureActualEncodedBytes();
            }
        }

        // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=0; Fingerprint=TBF
        // Broiler-Falsified-If: an offset of MaxEncodedBytes plus one is allowed through without LimitExceeded
        // Broiler-Human:        PENDING
        private void EnsureActualEncodedBytes()
        {
            if (_offset > limits.MaxEncodedBytes)
                throw Limit("WAVE input exceeds the configured encoded-byte limit.", _offset);
        }

        // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=0; Fingerprint=TBF
        // Broiler-Falsified-If: a declared size that puts the current offset plus the size exactly one byte past MaxEncodedBytes is allowed through without LimitExceeded
        // Broiler-Human:        PENDING
        private void EnsureDeclaredEncodedBytes(long byteCount, string context)
        {
            if (byteCount < 0 || _offset + byteCount > limits.MaxEncodedBytes)
                throw Limit($"{context} exceeds the configured encoded-byte limit.", _offset);
        }
    }

    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a 24-bit format reports a source format other than PcmS24Interleaved
    // Broiler-Human:        PENDING
    private readonly struct WaveFormat(int sampleRate, int channels, int bitsPerSample,
        int blockAlign, int byteRate, int sourceBytesPerSample)
    {
        public int SampleRate { get; } = sampleRate;

        public int Channels { get; } = channels;

        public int BitsPerSample { get; } = bitsPerSample;

        public int BlockAlign { get; } = blockAlign;

        public int ByteRate { get; } = byteRate;

        public int SourceBytesPerSample { get; } = sourceBytesPerSample;

        // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=0; Fingerprint=TBF
        // Broiler-Falsified-If: a 24-bit format reports PcmS16Interleaved or PcmS32Interleaved as its source format
        // Broiler-Human:        PENDING
        public AudioSampleFormat SourceFormat => BitsPerSample switch
        {
            8 => AudioSampleFormat.PcmU8Interleaved,
            16 => AudioSampleFormat.PcmS16Interleaved,
            24 => AudioSampleFormat.PcmS24Interleaved,
            32 => AudioSampleFormat.PcmS32Interleaved,
            _ => throw Unsupported($"Unsupported PCM bit depth {BitsPerSample}."),
        };
    }

    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: the reported stream duration differs from TotalFrames divided by SampleRate seconds by more than one tick
    // Broiler-Human:        PENDING
    private readonly struct WaveDataChunk(WaveFormat format, long dataByteLength, long totalFrames)
    {
        public WaveFormat Format { get; } = format;

        public long DataByteLength { get; } = dataByteLength;

        public long TotalFrames { get; } = totalFrames;

        // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=0; Fingerprint=TBF
        // Broiler-Falsified-If: the returned info reports a block alignment or byte rate other than the values read from the fmt chunk
        // Broiler-Human:        PENDING
        public AudioStreamInfo ToStreamInfo() =>
            new(Format.SampleRate, Format.Channels, Format.SourceFormat, FramesToTime(TotalFrames, Format.SampleRate), 
                TotalFrames, Format.BitsPerSample, Format.BlockAlign, Format.ByteRate);
    }
}
