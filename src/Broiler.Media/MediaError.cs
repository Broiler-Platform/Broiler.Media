using System;

namespace Broiler.Media;

public sealed class MediaError
{
    public MediaError(MediaErrorCode code, string message, MediaCodecId? codecId = null, long? byteOffset = null)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("A media error needs a message.", nameof(message));
        if (byteOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(byteOffset));

        Code = code;
        Message = message;
        CodecId = codecId;
        ByteOffset = byteOffset;
    }

    public MediaErrorCode Code { get; }

    public string Message { get; }

    public MediaCodecId? CodecId { get; }

    public long? ByteOffset { get; }
}

