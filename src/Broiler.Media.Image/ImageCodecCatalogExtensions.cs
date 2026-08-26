using System;
using System.Linq;
using Broiler.Media;

namespace Broiler.Media.Image;

/// <summary>
/// Abstraction-only helpers for locating image codecs in a <see cref="MediaCodecCatalog"/>
/// by capability. They let a rendering or resource-loading consumer select an encoder for a
/// target <see cref="ImageEncodeFormat"/> without referencing any concrete codec
/// implementation, so the implementation assembly (<c>Broiler.Media.Image.Managed</c>) stays
/// an application-composition concern.
/// </summary>
public static class ImageCodecCatalogExtensions
{
    /// <summary>
    /// Canonical MIME type for each encode format. These are stable web identifiers, not
    /// implementation types, so matching on them keeps callers decoupled from concrete codecs.
    /// </summary>
    public static string GetMimeType(this ImageEncodeFormat format) => format switch
    {
        ImageEncodeFormat.Png => "image/png",
        ImageEncodeFormat.Jpeg => "image/jpeg",
        ImageEncodeFormat.Bmp => "image/bmp",
        ImageEncodeFormat.Gif => "image/gif",
        ImageEncodeFormat.WebP => "image/webp",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown image encode format."),
    };

    /// <summary>
    /// Returns the first image codec in <paramref name="catalog"/> that can encode
    /// <paramref name="format"/> — i.e. it is an <see cref="ImageCodec"/>, advertises the
    /// <see cref="MediaCodecCapabilities.Encode"/> capability, and declares the format's
    /// canonical MIME type. Returns <c>null</c> when no such codec is registered.
    /// </summary>
    public static ImageCodec? FindEncoder(this MediaCodecCatalog catalog, ImageEncodeFormat format)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        string mime = format.GetMimeType();

        foreach (MediaCodec codec in catalog.GetByKind(MediaKind.Image))
        {
            if (codec is not ImageCodec imageCodec)
                continue;
            if (!codec.Descriptor.Capabilities.HasFlag(MediaCodecCapabilities.Encode))
                continue;
            if (codec.Descriptor.Formats.Any(descriptor =>
                    descriptor.MimeTypes.Contains(mime, StringComparer.OrdinalIgnoreCase)))
            {
                return imageCodec;
            }
        }

        return null;
    }
}
