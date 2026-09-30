using System.IO;

namespace Broiler.Media.Image.Managed.Jpeg;

/// <summary>
/// Writes bits MSB-first into a JPEG entropy stream, performing <c>0xFF -&gt; 0xFF 0x00</c>
/// byte-stuffing. Markers (restart, EOI) are written raw and must follow a byte-aligning
/// <see cref="FlushToByte"/>.
/// </summary>
// Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=1; Fingerprint=TBF
// Broiler-Falsified-If: an emitted 0xFF byte is not followed by a 0x00 stuffing byte
// Broiler-Human:        PENDING
internal sealed class JpegBitWriter(Stream stream)
{
    private int _accumulator;
    private int _bitCount;

    /// <summary>Appends the low <paramref name="size"/> bits of <paramref name="code"/> (MSB first).</summary>
    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=1; Fingerprint=TBF
    // Broiler-Falsified-If: the bits of code are written least significant first, so code 2 of size 2 comes out as 01
    // Broiler-Human:        PENDING
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
    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a symbol the table does not code is written as zero bits instead of failing
    // Broiler-Human:        PENDING
    public void WriteSymbol(JpegHuffmanTable table, int symbol) => WriteBits(table.CodeOf(symbol), table.SizeOf(symbol));

    // Broiler-AI:           Origin=AI; Spec=T.81 sF.1.2.3; IP=Low; Security=Medium; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: an emitted 0xFF byte is not followed by a 0x00 stuffing byte
    // Broiler-Human:        PENDING
    private void Emit()
    {
        byte b = (byte)_accumulator;
        stream.WriteByte(b);
        if (b == 0xFF)
            stream.WriteByte(0x00); // byte stuffing
        _accumulator = 0;
        _bitCount = 0;
    }

    /// <summary>Pads the current partial byte with 1-bits and flushes it, restoring byte alignment.</summary>
    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a partial final byte is padded with 0 bits instead of 1 bits
    // Broiler-Human:        PENDING
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
