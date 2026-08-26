using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Broiler.Media;

public sealed class MediaFormatDescriptor
{
    public MediaFormatDescriptor(
        string name,
        IEnumerable<string>? mimeTypes = null,
        IEnumerable<string>? fileExtensions = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A media format name cannot be empty.", nameof(name));

        Name = name;
        MimeTypes = NormalizeList(mimeTypes, nameof(mimeTypes));
        FileExtensions = NormalizeList(fileExtensions, nameof(fileExtensions));
    }

    public string Name { get; }

    public IReadOnlyList<string> MimeTypes { get; }

    public IReadOnlyList<string> FileExtensions { get; }

    private static ReadOnlyCollection<string> NormalizeList(IEnumerable<string>? values, string parameterName)
    {
        if (values is null)
            return Array.AsReadOnly(Array.Empty<string>());

        string[] normalized = values
            .Select(value =>
            {
                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException("Format descriptors cannot contain empty values.", parameterName);
                return value.Trim();
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Array.AsReadOnly(normalized);
    }
}

