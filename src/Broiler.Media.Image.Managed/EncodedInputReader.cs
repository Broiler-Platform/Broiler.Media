using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media.Image.Managed;

internal static class EncodedInputReader
{
    public static async ValueTask<byte[]> ReadAllAsync(
        MediaInput input,
        ImageDecodeOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        long maxBytes = (options ?? new ImageDecodeOptions()).Limits.MaxEncodedBytes;
        if (maxBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options));

        Stream stream = input.Stream;
        if (stream.CanSeek)
        {
            long remaining = stream.Length - stream.Position;
            if (remaining > maxBytes)
                throw LimitExceeded(maxBytes);
        }

        using var output = new MemoryStream();
        byte[] buffer = new byte[Math.Min(81920, checked((int)Math.Min(maxBytes, int.MaxValue)))];
        while (true)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;

            if (output.Length + read > maxBytes)
                throw LimitExceeded(maxBytes);

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static MediaException LimitExceeded(long maxBytes) =>
        new(new MediaError(
            MediaErrorCode.LimitExceeded,
            $"Encoded image input exceeds the configured limit of {maxBytes} byte(s)."));
}

