using System;

namespace Broiler.Media.Image.Managed;

internal ref struct WebpBitReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _bitOffset;

    public WebpBitReader(ReadOnlySpan<byte> data) => _data = data;

    public int ReadBits(int bitCount)
    {
        if (bitCount < 0 || bitCount > 24)
            throw new ArgumentOutOfRangeException(nameof(bitCount));
        if (_bitOffset + bitCount > _data.Length * 8)
            throw new FormatException("Truncated WebP lossless bitstream.");

        int value = 0;
        for (int i = 0; i < bitCount; i++)
        {
            int bit = (_data[(_bitOffset + i) >> 3] >> ((_bitOffset + i) & 7)) & 1;
            value |= bit << i;
        }

        _bitOffset += bitCount;
        return value;
    }
}

