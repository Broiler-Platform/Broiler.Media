using System;

namespace Broiler.Media.Image.Managed;

/// <summary>
/// A JPEG Huffman table built from a <c>BITS</c>/<c>HUFFVAL</c> specification
/// (Annex C). Exposes the decode lookup structures (min/max code and value
/// pointers per length, Annex F) and the encode lookup (code and size per symbol).
/// </summary>
internal sealed class JpegHuffmanTable
{
    // Decode structures, indexed by code length 1..16.
    private readonly int[] _minCode = new int[17];
    private readonly int[] _maxCode = new int[17]; // -1 when no code of that length
    private readonly int[] _valPtr = new int[17];
    private readonly byte[] _values;

    // Encode structures, indexed by symbol value 0..255.
    private readonly int[] _encodeCode = new int[256];
    private readonly int[] _encodeSize = new int[256];

    private JpegHuffmanTable(byte[] values)
    {
        _values = values;
    }

    /// <param name="bits">16 entries; <c>bits[i]</c> is the count of codes of length <c>i+1</c>.</param>
    /// <param name="values">The symbols, ordered by increasing code length.</param>
    public static JpegHuffmanTable Build(ReadOnlySpan<byte> bits, ReadOnlySpan<byte> values)
    {
        if (bits.Length != 16)
            throw new FormatException("JPEG Huffman BITS must have 16 entries.");

        var table = new JpegHuffmanTable(values.ToArray());

        // Code length for each symbol, in symbol order.
        int total = values.Length;
        Span<int> sizes = total <= 256 ? stackalloc int[total + 1] : new int[total + 1];
        int k = 0;
        for (int l = 1; l <= 16; l++)
            for (int j = 0; j < bits[l - 1]; j++)
                sizes[k++] = l;
        sizes[k] = 0;

        // Canonical code for each symbol.
        var codes = new int[total];
        int code = 0;
        int si = sizes.Length > 0 ? sizes[0] : 0;
        k = 0;
        while (k < total && sizes[k] != 0)
        {
            while (k < total && sizes[k] == si)
            {
                codes[k] = code;
                code++;
                k++;
            }
            code <<= 1;
            si++;
        }

        // Encode lookup.
        for (int i = 0; i < total; i++)
        {
            table._encodeCode[values[i]] = codes[i];
            table._encodeSize[values[i]] = sizes[i];
        }

        // Decode lookup (min/max code + value pointer per length).
        int p = 0;
        for (int l = 1; l <= 16; l++)
        {
            if (bits[l - 1] > 0)
            {
                table._valPtr[l] = p;
                table._minCode[l] = codes[p];
                p += bits[l - 1];
                table._maxCode[l] = codes[p - 1];
            }
            else
            {
                table._maxCode[l] = -1;
            }
        }

        return table;
    }

    /// <summary>Code length in bits for <paramref name="symbol"/> (0 if the symbol is not in the table).</summary>
    public int SizeOf(int symbol) => _encodeSize[symbol];

    /// <summary>Canonical code for <paramref name="symbol"/>.</summary>
    public int CodeOf(int symbol) => _encodeCode[symbol];

    /// <summary>
    /// Decodes one symbol by reading bits from <paramref name="reader"/>. Returns
    /// the symbol, or -1 if the stream ended / hit a marker before a code completed.
    /// </summary>
    public int Decode(JpegBitReader reader)
    {
        int code = 0;
        for (int l = 1; l <= 16; l++)
        {
            int bit = reader.ReadBit();
            if (bit < 0)
                return -1;
            code = (code << 1) | bit;
            if (_maxCode[l] >= 0 && code <= _maxCode[l])
                return _values[_valPtr[l] + code - _minCode[l]];
        }
        throw new FormatException("Invalid JPEG Huffman code (no matching code in 16 bits).");
    }
}
