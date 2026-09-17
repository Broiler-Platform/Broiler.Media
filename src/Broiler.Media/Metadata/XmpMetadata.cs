using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;

namespace Broiler.Media.Metadata;

/// <summary>
/// An XMP timestamp, keeping the distinction the format makes between a value
/// that stated a UTC offset and one that did not.
/// </summary>
public readonly struct XmpDate : IEquatable<XmpDate>
{
    private XmpDate(DateTimeOffset value, bool hasUtcOffset)
    {
        Value = value;
        HasUtcOffset = hasUtcOffset;
    }

    public DateTimeOffset Value { get; }

    /// <summary>True when the source stated a UTC offset.</summary>
    public bool HasUtcOffset { get; }

    /// <summary>A timestamp whose source stated an offset.</summary>
    public static XmpDate WithOffset(DateTimeOffset value) => new(value, true);

    /// <summary>A timestamp whose source stated no offset; none is invented.</summary>
    public static XmpDate WithoutOffset(DateTime value) =>
        new(new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), TimeSpan.Zero), false);

    public bool Equals(XmpDate other) => Value == other.Value && HasUtcOffset == other.HasUtcOffset;

    public override bool Equals(object? obj) => obj is XmpDate other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Value, HasUtcOffset);

    public static bool operator ==(XmpDate left, XmpDate right) => left.Equals(right);

    public static bool operator !=(XmpDate left, XmpDate right) => !left.Equals(right);

    public override string ToString() => Value.ToString(
        HasUtcOffset ? "yyyy-MM-ddTHH:mm:sszzz" : "yyyy-MM-ddTHH:mm:ss",
        CultureInfo.InvariantCulture);
}

/// <summary>
/// What an XMP packet said, restricted to the properties Broiler normalizes.
/// </summary>
public sealed class XmpMetadata
{
    private static readonly ReadOnlyCollection<string> EmptyList = new([]);

    /// <summary>A packet that supplied nothing this build normalizes.</summary>
    public static XmpMetadata Empty { get; } = new();

    public XmpMetadata(string? title = null, IReadOnlyList<string>? authors = null, string? description = null,
        IReadOnlyList<string>? keywords = null, string? language = null, string? creatorTool = null,
        string? producer = null, XmpDate? createDate = null, XmpDate? modifyDate = null)
    {
        Title = title;
        Authors = authors ?? EmptyList;
        Description = description;
        Keywords = keywords ?? EmptyList;
        Language = language;
        CreatorTool = creatorTool;
        Producer = producer;
        CreateDate = createDate;
        ModifyDate = modifyDate;
    }

    /// <summary><c>dc:title</c>, resolved through its language alternative.</summary>
    public string? Title { get; }

    /// <summary><c>dc:creator</c> in source order. These are authors, not tools.</summary>
    public IReadOnlyList<string> Authors { get; }

    /// <summary><c>dc:description</c>, resolved through its language alternative.</summary>
    public string? Description { get; }

    /// <summary><c>dc:subject</c> in source order.</summary>
    public IReadOnlyList<string> Keywords { get; }

    /// <summary>The first <c>dc:language</c> entry.</summary>
    public string? Language { get; }

    /// <summary><c>xmp:CreatorTool</c> — the application that authored the original.</summary>
    public string? CreatorTool { get; }

    /// <summary><c>pdf:Producer</c> — the application that produced the file.</summary>
    public string? Producer { get; }

    /// <summary><c>xmp:CreateDate</c>.</summary>
    public XmpDate? CreateDate { get; }

    /// <summary><c>xmp:ModifyDate</c>.</summary>
    public XmpDate? ModifyDate { get; }

    /// <summary>True when the packet supplied no normalized field at all.</summary>
    public bool IsEmpty => FieldCount == 0;

    /// <summary>How many normalized fields the packet supplied.</summary>
    public int FieldCount
    {
        get
        {
            int count = 0;
            if (Title is not null)
                count++;
            if (Authors.Count > 0)
                count++;
            if (Description is not null)
                count++;
            if (Keywords.Count > 0)
                count++;
            if (Language is not null)
                count++;
            if (CreatorTool is not null)
                count++;
            if (Producer is not null)
                count++;
            if (CreateDate is not null)
                count++;
            if (ModifyDate is not null)
                count++;
            return count;
        }
    }
}

/// <summary>How an attempt to read an XMP packet ended.</summary>
public enum XmpReadOutcome
{
    /// <summary>The packet parsed; <see cref="XmpReadResult.Metadata"/> is its content.</summary>
    Read,

    /// <summary>The packet was not well-formed XML, or nested past the depth ceiling.</summary>
    Unusable,

    /// <summary>The packet exceeded the byte ceiling and was never parsed.</summary>
    TooLarge,
}

/// <summary>The outcome of reading one XMP packet, with what it cost to say so.</summary>
public sealed class XmpReadResult
{
    public XmpReadResult(XmpReadOutcome outcome, XmpMetadata metadata, int ignoredProperties = 0,
        bool propertiesTruncated = false, string? failure = null)
    {
        Outcome = outcome;
        Metadata = metadata;
        IgnoredProperties = ignoredProperties;
        PropertiesTruncated = propertiesTruncated;
        Failure = failure;
    }

    public XmpReadOutcome Outcome { get; }

    /// <summary>What the packet supplied; <see cref="XmpMetadata.Empty"/> unless <see cref="Outcome"/> is <see cref="XmpReadOutcome.Read"/>.</summary>
    public XmpMetadata Metadata { get; }

    /// <summary>Properties the packet carried that are outside the normalized allowlist.</summary>
    public int IgnoredProperties { get; }

    /// <summary>True when the packet held more properties than the reader would examine.</summary>
    public bool PropertiesTruncated { get; }

    /// <summary>
    /// Why the packet could not be read, as a structural reason only — an
    /// exception type name or a limit name.
    /// </summary>
    public string? Failure { get; }
}

/// <summary>
/// Reads the normalized subset of an XMP packet (ISO 16684-1) into
/// <see cref="XmpMetadata"/>.
/// </summary>
public static class XmpReader
{
    /// <summary>The RDF syntax namespace.</summary>
    public const string RdfNamespace = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    /// <summary>The Dublin Core elements namespace.</summary>
    public const string DublinCoreNamespace = "http://purl.org/dc/elements/1.1/";

    /// <summary>The XMP basic schema namespace.</summary>
    public const string XmpBasicNamespace = "http://ns.adobe.com/xap/1.0/";

    /// <summary>The Adobe PDF schema namespace.</summary>
    public const string AdobePdfNamespace = "http://ns.adobe.com/pdf/1.3/";

    /// <summary>Maximum properties examined in one packet.</summary>
    public const int MaxProperties = 512;

    /// <summary>Maximum characters kept for a single property value or item.</summary>
    public const int MaxValueLength = 4096;

    /// <summary>Maximum <c>rdf:li</c> items kept for a single property.</summary>
    public const int MaxItems = 128;

    /// <summary>Maximum element nesting depth before the packet is refused.</summary>
    public const int MaxDepth = 32;

    /// <summary>The language qualifier a language alternative prefers.</summary>
    private const string DefaultLanguage = "x-default";

    private const string XmlNamespace = "http://www.w3.org/XML/1998/namespace";
    private const string XmlnsNamespace = "http://www.w3.org/2000/xmlns/";

    /// <summary>
    /// Reads <paramref name="packet"/>, refusing it outright above
    /// <paramref name="maxBytes"/>.
    /// </summary>
    public static XmpReadResult Read(byte[] packet, long maxBytes)
    {
        ArgumentNullException.ThrowIfNull(packet);

        if (maxBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytes), maxBytes, "A byte ceiling must be positive; zero never means unlimited.");

        if (packet.LongLength > maxBytes)
            return new XmpReadResult(XmpReadOutcome.TooLarge, XmpMetadata.Empty, failure: nameof(maxBytes));

        try
        {
            using var stream = new MemoryStream(packet, writable: false);
            using XmlReader reader = XmlReader.Create(stream, SettingsFor(maxBytes));
            return Parse(reader);
        }
        catch (Exception ex) when (
            ex is XmlException or InvalidDataException or NotSupportedException or
                 ArgumentException or DecoderFallbackException or FormatException)
        {
            return new XmpReadResult(XmpReadOutcome.Unusable, XmpMetadata.Empty, failure: ex.GetType().Name);
        }
    }

    /// <summary>
    /// Parses an XMP date: a W3C-DTF profile of ISO 8601, from a bare year to a
    /// fractional second with an offset.
    /// </summary>
    public static bool TryParseDate(string? value, out XmpDate date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string trimmed = value.Trim();

        if (StatesOffset(trimmed))
        {
            if (!DateTimeOffset.TryParseExact(trimmed, OffsetFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset zoned))
                return false;

            date = XmpDate.WithOffset(zoned);
            return true;
        }

        if (!DateTime.TryParseExact(trimmed, LocalFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime local))
            return false;

        date = XmpDate.WithoutOffset(local);
        return true;
    }

    private static readonly string[] OffsetFormats =
    [
        "yyyy-MM-ddTHH:mm:ss.FFFFFFFK",
        "yyyy-MM-ddTHH:mm:ssK",
        "yyyy-MM-ddTHH:mmK",
    ];

    private static readonly string[] LocalFormats =
    [
        "yyyy-MM-ddTHH:mm:ss.FFFFFFF",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm",
        "yyyy-MM-dd",
        "yyyy-MM",
        "yyyy",
    ];

    private static bool StatesOffset(string value)
    {
        if (value.Length == 0)
            return false;

        if (value[^1] is 'Z' or 'z')
            return true;

        return value.Length >= 6 && value[^6] is '+' or '-' && value[^3] == ':';
    }

    private static XmlReaderSettings SettingsFor(long maxBytes) => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true,
        CloseInput = false,
        MaxCharactersInDocument = maxBytes,
    };

    private static XmpReadResult Parse(XmlReader reader)
    {
        var bag = new PropertyBag();
        int descriptionDepth = -1;

        while (reader.Read())
        {
            if (reader.Depth > MaxDepth)
                return new XmpReadResult(XmpReadOutcome.Unusable, XmpMetadata.Empty, failure: nameof(MaxDepth));

            switch (reader.NodeType)
            {
                case XmlNodeType.EndElement when descriptionDepth >= 0 && reader.Depth <= descriptionDepth:
                    descriptionDepth = -1;
                    break;

                case XmlNodeType.Element when IsRdf(reader, "Description"):
                    ReadDescriptionAttributes(reader, bag);
                    descriptionDepth = reader.IsEmptyElement ? -1 : reader.Depth;
                    break;

                case XmlNodeType.Element when descriptionDepth >= 0 && reader.Depth == descriptionDepth + 1:
                    ReadProperty(reader, bag);
                    break;
            }
        }

        return bag.Build();
    }

    private static void ReadDescriptionAttributes(XmlReader reader, PropertyBag bag)
    {
        if (!reader.HasAttributes)
            return;

        for (int i = 0; i < reader.AttributeCount; i++)
        {
            reader.MoveToAttribute(i);

            if (reader.NamespaceURI is RdfNamespace or XmlnsNamespace or XmlNamespace ||
                string.Equals(reader.Name, "xmlns", StringComparison.Ordinal))
            {
                continue;
            }

            bag.Add(reader.NamespaceURI, reader.LocalName, Clamp(reader.Value), items: null);
        }

        reader.MoveToElement();
    }

    private static void ReadProperty(XmlReader reader, PropertyBag bag)
    {
        string propertyNamespace = reader.NamespaceURI;
        string propertyName = reader.LocalName;

        if (reader.IsEmptyElement)
        {
            bag.Add(propertyNamespace, propertyName, string.Empty, items: null);
            return;
        }

        int propertyDepth = reader.Depth;
        int itemDepth = -1;
        string? itemLanguage = null;
        var itemText = new StringBuilder();
        var text = new StringBuilder();
        List<XmpItem>? items = null;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement)
            {
                if (itemDepth >= 0 && reader.Depth == itemDepth)
                {
                    (items ??= []).Add(new XmpItem(itemLanguage, Clamp(itemText.ToString())));
                    itemText.Clear();
                    itemDepth = -1;
                    itemLanguage = null;
                }
                else if (reader.Depth == propertyDepth)
                {
                    break;
                }

                continue;
            }

            if (reader.NodeType == XmlNodeType.Element)
            {
                if (itemDepth < 0 && IsRdf(reader, "li") && (items?.Count ?? 0) < MaxItems)
                {
                    string? language = reader.GetAttribute("lang", XmlNamespace);
                    if (reader.IsEmptyElement)
                        (items ??= []).Add(new XmpItem(language, string.Empty));
                    else
                        (itemDepth, itemLanguage) = (reader.Depth, language);
                }

                continue;
            }

            if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace)
            {
                StringBuilder target = itemDepth >= 0 ? itemText : text;
                if (target.Length < MaxValueLength)
                    target.Append(reader.Value);
            }
        }

        bag.Add(propertyNamespace, propertyName, Clamp(text.ToString()), items);
    }

    private static bool IsRdf(XmlReader reader, string localName) =>
        string.Equals(reader.NamespaceURI, RdfNamespace, StringComparison.Ordinal) &&
        string.Equals(reader.LocalName, localName, StringComparison.Ordinal);

    private static string Clamp(string value) =>
        value.Length <= MaxValueLength ? value : value[..MaxValueLength];

    private readonly record struct XmpItem(string? Language, string Text);

    private sealed class PropertyBag
    {
        private readonly Dictionary<string, Property> _properties = new(StringComparer.Ordinal);
        private int _ignored;
        private bool _truncated;

        public void Add(string namespaceUri, string localName, string text, List<XmpItem>? items)
        {
            string? key = KeyFor(namespaceUri, localName);
            if (key is null)
            {
                _ignored++;
                return;
            }

            if (_properties.ContainsKey(key))
            {
                _ignored++;
                return;
            }

            if (_properties.Count >= MaxProperties)
            {
                _truncated = true;
                return;
            }

            _properties[key] = new Property(text, items);
        }

        public XmpReadResult Build()
        {
            var metadata = new XmpMetadata(Alternative("dc:title"), Ordered("dc:creator"), Alternative("dc:description"), Ordered("dc:subject"),
                First("dc:language"), Simple("xmp:CreatorTool"), Simple("pdf:Producer"), Date("xmp:CreateDate"), Date("xmp:ModifyDate"));

            return new XmpReadResult(XmpReadOutcome.Read, metadata, _ignored, _truncated);
        }

        private string? Simple(string key) =>
            _properties.TryGetValue(key, out Property property) && property.Text.Length > 0
                ? property.Text
                : null;

        private string? Alternative(string key)
        {
            if (!_properties.TryGetValue(key, out Property property))
                return null;

            if (property.Items is not { Count: > 0 } items)
                return property.Text.Length > 0 ? property.Text : null;

            foreach (XmpItem item in items)
            {
                if (string.Equals(item.Language, DefaultLanguage, StringComparison.OrdinalIgnoreCase) && item.Text.Length > 0)
                    return item.Text;
            }

            foreach (XmpItem item in items)
            {
                if (item.Text.Length > 0)
                    return item.Text;
            }

            return null;
        }

        private ReadOnlyCollection<string>? Ordered(string key)
        {
            if (!_properties.TryGetValue(key, out Property property))
                return null;

            var values = new List<string>();
            if (property.Items is { } items)
            {
                foreach (XmpItem item in items)
                {
                    if (item.Text.Length > 0)
                        values.Add(item.Text);
                }
            }
            else if (property.Text.Length > 0)
            {
                values.Add(property.Text);
            }

            return values.Count == 0 ? null : new ReadOnlyCollection<string>(values);
        }

        private string? First(string key) => Ordered(key) is { Count: > 0 } values ? values[0] : null;

        private XmpDate? Date(string key) =>
            TryParseDate(Simple(key), out XmpDate date) ? date : null;

        private static string? KeyFor(string namespaceUri, string localName) => namespaceUri switch
        {
            DublinCoreNamespace => localName switch
            {
                "title" or "creator" or "description" or "subject" or "language" => "dc:" + localName,
                _ => null,
            },
            XmpBasicNamespace => localName switch
            {
                "CreatorTool" or "CreateDate" or "ModifyDate" => "xmp:" + localName,
                _ => null,
            },
            AdobePdfNamespace => string.Equals(localName, "Producer", StringComparison.Ordinal) ? "pdf:Producer" : null,
            _ => null,
        };

        private readonly record struct Property(string Text, List<XmpItem>? Items);
    }
}
