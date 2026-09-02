using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media.Image;

public abstract class ImageCodec : MediaCodec
{
    protected ImageCodec(MediaCodecDescriptor descriptor)
        : base(descriptor)
    {
        if (descriptor.Kind != MediaKind.Image)
            throw new ArgumentException("Image codecs must use MediaKind.Image descriptors.", nameof(descriptor));
    }

    /// <summary>
    /// Bytes an inspection may read before giving up. A header is small, but the
    /// markers ahead of it need not be: a JPEG can carry a colour profile or an
    /// EXIF block of some size before its frame header, so this is generous
    /// enough to reach past one and bounded enough that a hostile file cannot
    /// make an inspection read the whole stream.
    /// </summary>
    public const int MaxInspectionBytes = 128 * 1024;

    public abstract ValueTask<ImageSequence> DecodeAsync(
        MediaInput input,
        ImageDecodeOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads what <paramref name="data"/>'s header declares about the image,
    /// decoding nothing. Returns false when this codec does not own the bytes, or
    /// when the header is absent, truncated, or self-contradictory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// False is never an error and never throws: a caller inspecting an unknown
    /// blob is asking a question, and "this is not mine" or "this header does not
    /// hold together" are both answers. A codec that cannot inspect its own format
    /// returns false from the default implementation rather than pretending.
    /// </para>
    /// <para>
    /// Implementations read from the start of <paramref name="data"/> and never
    /// allocate in proportion to it.
    /// </para>
    /// </remarks>
    public virtual bool TryInspect(ReadOnlySpan<byte> data, out ImageInfo? info)
    {
        info = null;
        return false;
    }

    /// <summary>
    /// Reads a bounded prefix of <paramref name="input"/> and inspects it. The
    /// I/O is asynchronous and the parsing is the same code the synchronous
    /// <see cref="TryInspect(ReadOnlySpan{byte}, out ImageInfo?)"/> runs.
    /// </summary>
    /// <returns>The header's contents, or null when it could not be read.</returns>
    public async ValueTask<ImageInfo?> InspectAsync(
        MediaInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        byte[] prefix = new byte[MaxInspectionBytes];
        int read = 0;
        while (read < prefix.Length)
        {
            int got = await input.Stream
                .ReadAsync(prefix.AsMemory(read, prefix.Length - read), cancellationToken)
                .ConfigureAwait(false);
            if (got == 0)
                break;
            read += got;
        }

        return TryInspect(prefix.AsSpan(0, read), out ImageInfo? info) ? info : null;
    }

    public virtual ValueTask EncodeAsync(
        ImageSequence sequence,
        Stream output,
        ImageEncodeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("This image codec does not support encoding.");
    }
}

