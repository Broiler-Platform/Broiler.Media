using System;
using System.Collections.Generic;

namespace Broiler.Media.Image.Managed;

internal sealed class WebpHuffmanCode
{
    private const int MaxCodeLength = 15;
    private readonly Dictionary<int, int> _symbols;
    private readonly int _singleSymbol;

    private WebpHuffmanCode(Dictionary<int, int> symbols, int singleSymbol)
    {
        _symbols = symbols;
        _singleSymbol = singleSymbol;
    }

    public static WebpHuffmanCode Read(ref WebpBitReader reader, int alphabetSize)
    {
        if (alphabetSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(alphabetSize));

        int[] codeLengths = new int[alphabetSize];
        bool simple = reader.ReadBits(1) != 0;
        if (simple)
        {
            int numSymbols = reader.ReadBits(1) + 1;
            int isFirst8Bits = reader.ReadBits(1);
            int symbol0 = reader.ReadBits(1 + (7 * isFirst8Bits));
            if (symbol0 >= alphabetSize)
                throw new FormatException("WebP Huffman symbol exceeds the alphabet size.");

            codeLengths[symbol0] = 1;
            if (numSymbols == 2)
            {
                int symbol1 = reader.ReadBits(8);
                if (symbol1 >= alphabetSize)
                    throw new FormatException("WebP Huffman symbol exceeds the alphabet size.");

                codeLengths[symbol1] = 1;
            }
        }
        else
        {
            int numCodeLengths = 4 + reader.ReadBits(4);
            int[] codeLengthCodeLengths = new int[19];
            ReadOnlySpan<int> order = [17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];
            for (int i = 0; i < numCodeLengths; i++)
                codeLengthCodeLengths[order[i]] = reader.ReadBits(3);

            int maxSymbol = alphabetSize;
            if (reader.ReadBits(1) != 0)
            {
                int lengthBits = 2 + (2 * reader.ReadBits(3));
                maxSymbol = 2 + reader.ReadBits(lengthBits);
                if (maxSymbol > alphabetSize)
                    throw new FormatException("WebP Huffman max symbol exceeds the alphabet size.");
            }

            WebpHuffmanCode codeLengthCode = Build(codeLengthCodeLengths, codeLengthCodeLengths.Length);
            int symbol = 0;
            int previous = 8;
            while (symbol < maxSymbol)
            {
                int length = codeLengthCode.ReadSymbol(ref reader);
                if (length < 16)
                {
                    codeLengths[symbol++] = length;
                    if (length != 0)
                        previous = length;
                    continue;
                }

                int repeat = length switch
                {
                    16 => 3 + reader.ReadBits(2),
                    17 => 3 + reader.ReadBits(3),
                    18 => 11 + reader.ReadBits(7),
                    _ => throw new FormatException("Invalid WebP code length repeat symbol."),
                };
                int repeatedLength = length == 16 ? previous : 0;
                if (symbol + repeat > maxSymbol)
                    throw new FormatException("WebP code length repeat exceeds the alphabet size.");

                for (int i = 0; i < repeat; i++)
                    codeLengths[symbol++] = repeatedLength;
            }
        }

        return Build(codeLengths, alphabetSize);
    }

    public int ReadSymbol(ref WebpBitReader reader)
    {
        if (_singleSymbol >= 0)
            return _singleSymbol;

        int code = 0;
        for (int length = 1; length <= MaxCodeLength; length++)
        {
            code |= reader.ReadBits(1) << (length - 1);
            if (_symbols.TryGetValue((length << 16) | code, out int symbol))
                return symbol;
        }

        throw new FormatException("Invalid WebP Huffman code.");
    }

    private static WebpHuffmanCode Build(int[] codeLengths, int alphabetSize)
    {
        int[] counts = new int[MaxCodeLength + 1];
        int singleSymbol = -1;
        int nonZero = 0;
        for (int i = 0; i < alphabetSize; i++)
        {
            int length = codeLengths[i];
            if (length < 0 || length > MaxCodeLength)
                throw new FormatException("Invalid WebP Huffman code length.");
            if (length == 0)
                continue;

            counts[length]++;
            singleSymbol = i;
            nonZero++;
        }

        if (nonZero == 0)
            throw new FormatException("WebP Huffman tree has no symbols.");
        if (nonZero == 1)
            return new WebpHuffmanCode(new Dictionary<int, int>(), singleSymbol);

        int left = 1;
        for (int length = 1; length <= MaxCodeLength; length++)
        {
            left = (left << 1) - counts[length];
            if (left < 0)
                throw new FormatException("WebP Huffman tree is oversubscribed.");
        }

        if (left != 0)
            throw new FormatException("WebP Huffman tree is incomplete.");

        int[] nextCode = new int[MaxCodeLength + 1];
        int code = 0;
        for (int bits = 1; bits <= MaxCodeLength; bits++)
        {
            code = (code + counts[bits - 1]) << 1;
            nextCode[bits] = code;
        }

        var symbols = new Dictionary<int, int>();
        for (int symbol = 0; symbol < alphabetSize; symbol++)
        {
            int length = codeLengths[symbol];
            if (length == 0)
                continue;

            int canonical = nextCode[length]++;
            int reversed = ReverseBits(canonical, length);
            symbols[(length << 16) | reversed] = symbol;
        }

        return new WebpHuffmanCode(symbols, singleSymbol: -1);
    }

    public static int ReverseBits(int value, int bitCount)
    {
        int result = 0;
        for (int i = 0; i < bitCount; i++)
        {
            result = (result << 1) | (value & 1);
            value >>= 1;
        }

        return result;
    }
}

