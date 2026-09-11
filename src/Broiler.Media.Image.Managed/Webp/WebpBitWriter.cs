using System;
using System.IO;

namespace Broiler.Media.Image.Managed.Webp;

internal sealed class WebpBitWriter(MemoryStream stream)
{
    private readonly MemoryStream _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    private int _bitBuffer;
    private int _bitCount;

    public void WriteBits(int value, int bitCount)
    {
        if (bitCount < 0 || bitCount > 24)
            throw new ArgumentOutOfRangeException(nameof(bitCount));

        _bitBuffer |= value << _bitCount;
        _bitCount += bitCount;
        while (_bitCount >= 8)
        {
            _stream.WriteByte((byte)_bitBuffer);
            _bitBuffer >>= 8;
            _bitCount -= 8;
        }
    }

    public void Flush()
    {
        if (_bitCount == 0)
            return;

        _stream.WriteByte((byte)_bitBuffer);
        _bitBuffer = 0;
        _bitCount = 0;
    }
}

