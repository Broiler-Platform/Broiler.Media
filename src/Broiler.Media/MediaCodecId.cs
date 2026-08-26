using System;

namespace Broiler.Media;

public readonly record struct MediaCodecId
{
    public MediaCodecId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A media codec id cannot be empty.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

