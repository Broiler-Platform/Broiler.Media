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

    public abstract ValueTask<ImageSequence> DecodeAsync(
        MediaInput input,
        ImageDecodeOptions? options = null,
        CancellationToken cancellationToken = default);

    public virtual ValueTask EncodeAsync(
        ImageSequence sequence,
        Stream output,
        ImageEncodeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("This image codec does not support encoding.");
    }
}

