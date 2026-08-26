using System;
using System.Collections.Generic;
using System.Linq;

namespace Broiler.Media;

public sealed class MediaCodecDescriptor
{
    public MediaCodecDescriptor(
        MediaCodecId id,
        string displayName,
        MediaKind kind,
        MediaCodecCapabilities capabilities,
        IEnumerable<MediaFormatDescriptor>? formats = null)
    {
        if (string.IsNullOrWhiteSpace(id.Value))
            throw new ArgumentException("A media codec descriptor needs a non-empty id.", nameof(id));
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("A media codec descriptor needs a display name.", nameof(displayName));

        Id = id;
        DisplayName = displayName;
        Kind = kind;
        Capabilities = capabilities;
        Formats = Array.AsReadOnly((formats ?? Array.Empty<MediaFormatDescriptor>()).ToArray());
    }

    public MediaCodecId Id { get; }

    public string DisplayName { get; }

    public MediaKind Kind { get; }

    public MediaCodecCapabilities Capabilities { get; }

    public IReadOnlyList<MediaFormatDescriptor> Formats { get; }
}

