using System;

namespace Broiler.Media;

public sealed class MediaSourceHints
{
    public static MediaSourceHints Empty { get; } = new();

    public MediaSourceHints(
        string? mimeType = null,
        string? fileExtension = null,
        string? displayName = null,
        string? sourceUri = null)
    {
        MimeType = string.IsNullOrWhiteSpace(mimeType) ? null : mimeType.Trim();
        FileExtension = string.IsNullOrWhiteSpace(fileExtension) ? null : fileExtension.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        SourceUri = string.IsNullOrWhiteSpace(sourceUri) ? null : sourceUri.Trim();
        if (SourceUri is not null && !Uri.TryCreate(SourceUri, UriKind.RelativeOrAbsolute, out _))
            throw new ArgumentException("Source URI hints must be valid URI strings.", nameof(sourceUri));
    }

    public string? MimeType { get; }

    public string? FileExtension { get; }

    public string? DisplayName { get; }

    public string? SourceUri { get; }
}
