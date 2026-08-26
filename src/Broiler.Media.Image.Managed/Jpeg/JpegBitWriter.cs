using System.IO;

namespace Broiler.Media.Image.Managed;

/// <summary>
/// Writes bits MSB-first into a JPEG entropy stream, performing <c>0xFF -&gt; 0xFF 0x00</c>
/// byte-stuffing. Markers (restart, EOI) are written raw and must follow a byte-aligning
/// <see cref="FlushToByte"/>.
/// </summary>
internal sealed class JpegBitWriter
{
    private readonly Stream _stream;
    private int _accumulator;
    private int _bitCount;

    public JpegBitWriter(Stream stream) => _stream = stream;

    /// <summary>Appends the low <paramref name="size"/> bits of <paramref name="code"/> (MSB first).</summary>
    public void WriteBits(int code, int size)
    {
        for (int i = size - 1; i >= 0; i--)
        {
            _accumulator = (_accumulator << 1) | ((code >> i) & 1);
            _bitCount++;
            if (_bitCount == 8)
                Emit();
        }
    }

    /// <summary>Writes a Huffman-coded symbol using the table's code and size.</summary>
    public void WriteSymbol(JpegHuffmanTable table, int symbol) =>
        WriteBits(table.CodeOf(symbol), table.SizeOf(symbol));

    private void Emit()
    {
        byte b = (byte)_accumulator;
        _stream.WriteByte(b);
        if (b == 0xFF)
            _stream.WriteByte(0x00); // byte stuffing
        _accumulator = 0;
        _bitCount = 0;
    }

    /// <summary>Pads the current partial byte with 1-bits and flushes it, restoring byte alignment.</summary>
    public void FlushToByte()
    {
        if (_bitCount > 0)
        {
            while (_bitCount < 8)
            {
                _accumulator = (_accumulator << 1) | 1;
                _bitCount++;
            }
            Emit();
        }
    }
}
