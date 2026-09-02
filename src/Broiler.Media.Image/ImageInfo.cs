using System;

namespace Broiler.Media.Image;

/// <summary>
/// What a bounded header inspection learned about an encoded image, without
/// decoding a single pixel.
/// </summary>
/// <remarks>
/// <para>
/// This exists because a caller frequently needs an image's intrinsic size before
/// it needs the image. Placing a picture in a document at its natural dimensions,
/// checking a pixel budget before committing to a decode, and deciding whether a
/// resource is placeable at all are all questions about the header, and answering
/// them by decoding is both wasteful and — for untrusted input — the wrong order:
/// a decompression bomb is refused by measuring it, and measuring it is exactly
/// what this does.
/// </para>
/// <para>
/// The distinction from <see cref="MediaProbeResult"/> is what each is for. A
/// probe answers "which codec owns these bytes", reads a few bytes, and is how a
/// catalog dispatches. An inspection answers "what image is this", and needs the
/// format's own header structure — so it belongs to the codec that already knows
/// that structure rather than to the catalog.
/// </para>
/// <para>
/// Every field here is read from a header the format defines. Nothing is inferred
/// from sample data, and nothing is decoded to produce it.
/// </para>
/// </remarks>
public sealed class ImageInfo
{
    public ImageInfo(
        int width,
        int height,
        int components,
        int bitDepth,
        string formatName,
        string mediaType)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), width, "An inspected image has a positive width.");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), height, "An inspected image has a positive height.");
        if (components <= 0)
            throw new ArgumentOutOfRangeException(nameof(components), components, "An inspected image has at least one component.");
        if (bitDepth <= 0)
            throw new ArgumentOutOfRangeException(nameof(bitDepth), bitDepth, "An inspected image has a positive bit depth.");
        if (string.IsNullOrWhiteSpace(formatName))
            throw new ArgumentException("An inspected image names its format.", nameof(formatName));
        if (string.IsNullOrWhiteSpace(mediaType))
            throw new ArgumentException("An inspected image names its media type.", nameof(mediaType));

        Width = width;
        Height = height;
        Components = components;
        BitDepth = bitDepth;
        FormatName = formatName;
        MediaType = mediaType;
    }

    /// <summary>Intrinsic width in pixels, as the header declares it.</summary>
    public int Width { get; }

    /// <summary>Intrinsic height in pixels, as the header declares it.</summary>
    public int Height { get; }

    /// <summary>
    /// Components per pixel as the format stores them — one for greyscale or a
    /// palette index, three for colour, four with alpha. This is the stored
    /// count, not the count a decoder produces: a palettized PNG says one here
    /// and still decodes to RGBA.
    /// </summary>
    public int Components { get; }

    /// <summary>Bits per component as stored.</summary>
    public int BitDepth { get; }

    /// <summary>The format's short name, matching its codec descriptor.</summary>
    public string FormatName { get; }

    /// <summary>The IANA media type for <see cref="FormatName"/>.</summary>
    public string MediaType { get; }

    /// <summary>
    /// Pixels the image would decode to, as a 64-bit count so a hostile header
    /// declaring 65535x65535 is compared rather than overflowed.
    /// </summary>
    public long PixelCount => (long)Width * Height;

    public override string ToString() =>
        $"{Width}x{Height} {FormatName}, {Components} component{(Components == 1 ? string.Empty : "s")} at {BitDepth}-bit";
}
