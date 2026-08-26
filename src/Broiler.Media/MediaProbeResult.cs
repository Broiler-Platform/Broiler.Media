using System;

namespace Broiler.Media;

public sealed class MediaProbeResult
{
    private MediaProbeResult(
        MediaKind kind,
        MediaProbeConfidence confidence,
        string? formatName,
        string? mimeType,
        long? bytesConsumed,
        string? diagnostic)
    {
        if (confidence is < MediaProbeConfidence.None or > MediaProbeConfidence.Certain)
            throw new ArgumentOutOfRangeException(nameof(confidence));
        if (bytesConsumed < 0)
            throw new ArgumentOutOfRangeException(nameof(bytesConsumed));

        Kind = kind;
        Confidence = confidence;
        FormatName = formatName;
        MimeType = mimeType;
        BytesConsumed = bytesConsumed;
        Diagnostic = diagnostic;
    }

    public MediaKind Kind { get; }

    public MediaProbeConfidence Confidence { get; }

    public string? FormatName { get; }

    public string? MimeType { get; }

    public long? BytesConsumed { get; }

    public string? Diagnostic { get; }

    public bool IsMatch => Confidence != MediaProbeConfidence.None;

    public static MediaProbeResult NoMatch(MediaKind kind, string? diagnostic = null) =>
        new(kind, MediaProbeConfidence.None, null, null, null, diagnostic);

    public static MediaProbeResult Match(
        MediaKind kind,
        MediaProbeConfidence confidence,
        string formatName,
        string? mimeType = null,
        long? bytesConsumed = null,
        string? diagnostic = null)
    {
        if (confidence == MediaProbeConfidence.None)
            throw new ArgumentException("Matched probe results need positive confidence.", nameof(confidence));
        if (string.IsNullOrWhiteSpace(formatName))
            throw new ArgumentException("Matched probe results need a format name.", nameof(formatName));

        return new MediaProbeResult(kind, confidence, formatName, mimeType, bytesConsumed, diagnostic);
    }
}

