using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media.Image;

/// <summary>
/// Reads an encoded image into memory, on whichever of the two paths the caller
/// is on.
/// </summary>
/// <remarks>
/// The two loops are deliberately separate rather than one built on the other.
/// A sync path that blocks on an async read is the shape that deadlocks on a
/// context that runs continuations on one thread, and an async path that calls a
/// sync read occupies a pool thread for the duration of the I/O. What they share
/// is the budget, which is the part that has to agree (PDF roadmap §6.5).
/// </remarks>
internal static class EncodedInputReader
{
    public static byte[] ReadAll(
        MediaInput input,
        ImageDecodeOptions? options,
        CancellationToken cancellationToken)
    {
        long maxBytes = Budget(input, options);
        Stream stream = input.Stream;

        using var output = new MemoryStream();
        byte[] buffer = Buffer(maxBytes);
        while (true)
        {
            // The sync path checks for itself; the async one gets this from the
            // token it hands to ReadAsync.
            cancellationToken.ThrowIfCancellationRequested();

            int read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
                break;

            if (output.Length + read > maxBytes)
                throw LimitExceeded(maxBytes);

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    public static async ValueTask<byte[]> ReadAllAsync(
        MediaInput input,
        ImageDecodeOptions? options,
        CancellationToken cancellationToken)
    {
        long maxBytes = Budget(input, options);
        Stream stream = input.Stream;

        using var output = new MemoryStream();
        byte[] buffer = Buffer(maxBytes);
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

    /// <summary>
    /// The caller's byte budget, refusing a seekable stream that already declares
    /// more than it before a byte is read.
    /// </summary>
    private static long Budget(MediaInput input, ImageDecodeOptions? options)
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

        return maxBytes;
    }

    private static byte[] Buffer(long maxBytes) =>
        new byte[Math.Min(81920, checked((int)Math.Min(maxBytes, int.MaxValue)))];

    private static MediaException LimitExceeded(long maxBytes) =>
        new(new MediaError(
            MediaErrorCode.LimitExceeded,
            $"Encoded image input exceeds the configured limit of {maxBytes} byte(s)."));
}

