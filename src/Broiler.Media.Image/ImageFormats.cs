using System;

namespace Broiler.Media.Image;

/// <summary>
/// Raster image signatures, MIME media types, and file extensions.
/// </summary>
public static class ImageFormats
{
    /// <summary>The media type for a file extension, or null when it is not a recognized raster image.</summary>
    public static string? ContentTypeForExtension(string? extension)
    {
        if (string.IsNullOrEmpty(extension))
            return null;

        return extension.TrimStart('.').ToLowerInvariant() switch
        {
            "png" or "apng" => "image/png",
            "jpg" or "jpeg" or "jpe" or "jfif" => "image/jpeg",
            "gif" => "image/gif",
            "bmp" or "dib" => "image/bmp",
            "tif" or "tiff" => "image/tiff",
            "webp" => "image/webp",
            "ico" => "image/x-icon",
            "ppm" => "image/x-portable-pixmap",
            "pgm" => "image/x-portable-graymap",
            "pnm" => "image/x-portable-anymap",
            "jp2" or "j2k" or "jpx" => "image/jp2",
            "jb2" or "jbig2" => "image/x-jbig2",
            _ => null,
        };
    }

    /// <summary>
    /// The media type implied by the leading magic bytes. Sniffing only identifies formats
    /// on the recognized list; it never invents a type for unknown bytes.
    /// </summary>
    public static string? ContentTypeForSignature(ReadOnlySpan<byte> data)
    {
        if (StartsWith(data, [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]))
            return "image/png";
        if (StartsWith(data, [0xFF, 0xD8, 0xFF]))
            return "image/jpeg";
        if (StartsWith(data, "GIF87a"u8) || StartsWith(data, "GIF89a"u8))
            return "image/gif";
        if (StartsWith(data, "BM"u8))
            return "image/bmp";
        if (StartsWith(data, [0x49, 0x49, 0x2A, 0x00]) || StartsWith(data, [0x4D, 0x4D, 0x00, 0x2A]))
            return "image/tiff";
        if (data.Length >= 12 && StartsWith(data, "RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
            return "image/webp";
        if (StartsWith(data, [0x00, 0x00, 0x01, 0x00]))
            return "image/x-icon";
        if (StartsWith(data, [0x97, (byte)'J', (byte)'B', (byte)'2', 0x0D, 0x0A, 0x1A, 0x0A]))
            return "image/x-jbig2";
        if (StartsWith(data, [0x00, 0x00, 0x00, 0x0C, (byte)'j', (byte)'P', 0x20, 0x20, 0x0D, 0x0A, 0x87, 0x0A]) ||
            StartsWith(data, [0xFF, 0x4F, 0xFF, 0x51]))
            return "image/jp2";
        if (data.Length >= 2 && data[0] == (byte)'P' && data[1] is (byte)'5' or (byte)'6')
            return data[1] == (byte)'5' ? "image/x-portable-graymap" : "image/x-portable-pixmap";

        return null;
    }

    /// <summary>The standard file extension for <paramref name="contentType"/>.</summary>
    public static string ExtensionForContentType(string? contentType) =>
        contentType?.ToLowerInvariant() switch
        {
            "image/png" => "png",
            "image/jpeg" or "image/jpg" or "image/pjpeg" => "jpeg",
            "image/gif" => "gif",
            "image/bmp" or "image/x-ms-bmp" => "bmp",
            "image/tiff" => "tiff",
            "image/webp" => "webp",
            "image/x-icon" or "image/vnd.microsoft.icon" => "ico",
            "image/jp2" or "image/jpx" => "jp2",
            "image/x-jbig2" => "jb2",
            "image/x-portable-graymap" => "pgm",
            "image/x-portable-pixmap" or "image/x-portable-anymap" => "ppm",
            _ => "png",
        };

    private static bool StartsWith(ReadOnlySpan<byte> data, ReadOnlySpan<byte> prefix) =>
        data.Length >= prefix.Length && data[..prefix.Length].SequenceEqual(prefix);
}
