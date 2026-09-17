using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using Broiler.Media.Image.Managed.Entropy;

namespace Broiler.Media.Image.Managed.Jbig2;

/// <summary>What a symbol dictionary segment produced.</summary>
public readonly record struct Jbig2SymbolDictionaryResult(
    Jbig2DecodeOutcome Outcome,
    Jbig2Bitmap[] Symbols,
    string? Message);

/// <summary>
/// Decodes a JBIG2 symbol dictionary segment: the shapes a text region will
/// later place.
/// </summary>
public static class Jbig2SymbolDictionary
{
    public static Jbig2SymbolDictionaryResult Decode(
        ReadOnlyMemory<byte> body,
        IReadOnlyList<Jbig2Bitmap> input,
        long pixelBudget)
    {
        ArgumentNullException.ThrowIfNull(input);

        ReadOnlySpan<byte> header = body.Span;
        if (header.Length < 2)
            return Malformed("A JBIG2 symbol dictionary is too short to state its flags.");

        int flags = BinaryPrimitives.ReadUInt16BigEndian(header);
        bool huffman = (flags & 0x01) != 0;
        bool refinementOrAggregation = (flags & 0x02) != 0;
        bool importsContexts = (flags & 0x100) != 0;
        int template = (flags >> 10) & 0x03;
        int refinementTemplate = (flags >> 12) & 0x01;

        if (huffman)
            return Unsupported("a Huffman-coded symbol dictionary");

        if (importsContexts)
            return Unsupported("a symbol dictionary that imports another's coding contexts");

        int cursor = 2;
        int adaptiveCount = template == 0 ? 4 : 1;
        if (cursor + (adaptiveCount * 2) > header.Length)
            return Malformed("A JBIG2 symbol dictionary declares template pixels the segment does not hold.");

        var adaptive = new (int X, int Y)[adaptiveCount];
        for (int i = 0; i < adaptiveCount; i++)
        {
            adaptive[i] = ((sbyte)header[cursor], (sbyte)header[cursor + 1]);
            cursor += 2;
        }

        // A refining dictionary using refinement template 0 names two adaptive
        // pixels of its own, after the generic ones and quite separate from them.
        (int X, int Y)[] refinementAdaptive = [];
        if (refinementOrAggregation && refinementTemplate == 0)
        {
            if (cursor + 4 > header.Length)
                return Malformed("A JBIG2 symbol dictionary declares refinement pixels the segment does not hold.");

            refinementAdaptive =
            [
                ((sbyte)header[cursor], (sbyte)header[cursor + 1]),
                ((sbyte)header[cursor + 2], (sbyte)header[cursor + 3]),
            ];

            cursor += 4;
        }

        if (cursor + 8 > header.Length)
            return Malformed("A JBIG2 symbol dictionary does not state its symbol counts.");

        long exportCount = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(cursor, 4));
        long newCount = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(cursor + 4, 4));
        cursor += 8;

        if (newCount > Jbig2Limits.MaxSymbols || exportCount > Jbig2Limits.MaxSymbols)
            return Malformed("A JBIG2 symbol dictionary declares more symbols than this reader will decode.");

        if (input.Count + newCount > Jbig2Limits.MaxSymbols)
            return Malformed("A JBIG2 symbol dictionary and its inputs exceed the symbols this reader will hold.");

        var decoder = new MqDecoder(body[cursor..]);
        var genericContexts = new MqContexts(Jbig2GenericDecoder.GenericContextBits);
        var deltaHeight = new Jbig2IntegerDecoder();
        var deltaWidth = new Jbig2IntegerDecoder();
        var exportRun = new Jbig2IntegerDecoder();

        // A refined symbol names its reference out of the whole list the
        // dictionary will hold, so the identifier is as wide as that total needs
        // even while most of the list is still undecoded.
        Jbig2SymbolRefinement? refinement = refinementOrAggregation
            ? new Jbig2SymbolRefinement(
                refinementTemplate,
                refinementAdaptive,
                Jbig2TextRegion.CodeLength((int)(input.Count + newCount)))
            : null;

        var symbols = new Jbig2Bitmap[newCount];
        int decoded = 0;
        int height = 0;
        long spent = 0;

        while (decoded < newCount)
        {
            if (deltaHeight.Decode(decoder, out int heightDelta) != Jbig2IntegerOutcome.Value)
                return Malformed("A JBIG2 symbol dictionary states no height for a class of symbols.");

            height += heightDelta;
            if (height is <= 0 or > Jbig2Limits.MaxSymbolExtent)
                return Malformed("A JBIG2 symbol dictionary states a symbol height outside the supported range.");

            int startedAt = decoded;
            int width = 0;
            while (true)
            {
                Jbig2IntegerOutcome outcome = deltaWidth.Decode(decoder, out int widthDelta);
                if (outcome == Jbig2IntegerOutcome.OutOfBand)
                    break;

                if (outcome != Jbig2IntegerOutcome.Value)
                    return Malformed("A JBIG2 symbol dictionary states a symbol width it cannot hold.");

                width += widthDelta;
                if (width is <= 0 or > Jbig2Limits.MaxSymbolExtent)
                    return Malformed("A JBIG2 symbol dictionary states a symbol width outside the supported range.");

                // A height class ends on out-of-band and nothing else, so a stream
                // that never sends it would define symbols forever. The count the
                // header promised is therefore also the inner loop's bound.
                if (decoded >= newCount)
                    return Malformed("A JBIG2 symbol dictionary defines more symbols than it declared.");

                spent += (long)width * height;
                if (spent > pixelBudget)
                    return new Jbig2SymbolDictionaryResult(Jbig2DecodeOutcome.TooLarge, [], null);

                byte[]? pixels;
                if (refinement is null)
                {
                    pixels = Jbig2GenericDecoder.Decode(
                        decoder, genericContexts, width, height, template, typicalPrediction: false, adaptive);
                }
                else if (refinement.Decode(decoder, width, height, input, symbols, decoded, out pixels)
                    is Jbig2SymbolDictionaryResult refused)
                {
                    return refused;
                }

                if (pixels is null)
                    return Malformed("A JBIG2 symbol dictionary holds a symbol this build could not decode.");

                symbols[decoded++] = new Jbig2Bitmap(width, height, pixels);
            }

            // A height class that defines nothing leaves the outer loop exactly
            // where it started, and a stream can state one as often as it likes:
            // past the end of the data the decoder feeds itself a fixed pattern,
            // so an empty class can repeat forever. Progress per class is what
            // makes this loop terminate on hostile input, and an encoder has no
            // reason to write an empty one.
            if (decoded == startedAt)
                return Malformed("A JBIG2 symbol dictionary states a height class holding no symbol.");
        }

        return Export(decoder, exportRun, input, symbols, exportCount);
    }

    /// <summary>
    /// Reads the export flags and returns the symbols they select, in the order
    /// the combined list holds them — which is the order a text region's symbol
    /// identifiers count through.
    /// </summary>
    private static Jbig2SymbolDictionaryResult Export(
        MqDecoder decoder,
        Jbig2IntegerDecoder exportRun,
        IReadOnlyList<Jbig2Bitmap> input,
        Jbig2Bitmap[] created,
        long declaredExports)
    {
        int total = input.Count + created.Length;
        var exported = new List<Jbig2Bitmap>();

        int index = 0;
        bool exporting = false;

        // Two runs per symbol is already absurd. The cap exists because a run of
        // zero is legal, and a stream of them would otherwise never advance.
        int runs = 0;
        int maxRuns = (2 * total) + 2;

        while (index < total)
        {
            if (runs++ > maxRuns)
                return Malformed("A JBIG2 symbol dictionary's export flags do not terminate.");

            if (exportRun.Decode(decoder, out int run) != Jbig2IntegerOutcome.Value)
                return Malformed("A JBIG2 symbol dictionary states no export flags.");

            if (run < 0 || index + run > total)
                return Malformed("A JBIG2 symbol dictionary exports a run of symbols it does not hold.");

            if (exporting)
            {
                for (int i = 0; i < run; i++)
                {
                    int at = index + i;
                    exported.Add(at < input.Count ? input[at] : created[at - input.Count]);
                }
            }

            index += run;
            exporting = !exporting;
        }

        if (exported.Count != declaredExports)
        {
            return Malformed(string.Create(
                CultureInfo.InvariantCulture,
                $"A JBIG2 symbol dictionary declared {declaredExports} exported symbols and its flags select {exported.Count}."));
        }

        return new Jbig2SymbolDictionaryResult(Jbig2DecodeOutcome.Decoded, [.. exported], null);
    }

    private static Jbig2SymbolDictionaryResult Malformed(string message) =>
        new(Jbig2DecodeOutcome.Malformed, [], message);

    private static Jbig2SymbolDictionaryResult Unsupported(string construct) =>
        new(Jbig2DecodeOutcome.Unsupported, [], construct);

    /// <summary>
    /// A symbol defined as a correction of one the dictionary already holds.
    /// </summary>
    private sealed class Jbig2SymbolRefinement(int template, (int X, int Y)[] adaptive, int codeLength)
    {
        private readonly Jbig2IntegerDecoder _instances = new();
        private readonly Jbig2IntegerDecoder _x = new();
        private readonly Jbig2IntegerDecoder _y = new();
        private readonly Jbig2SymbolIdDecoder _identifier = new(codeLength);
        private readonly MqContexts _contexts = new(Jbig2RefinementDecoder.RefinementContextBits);

        public Jbig2SymbolDictionaryResult? Decode(
            MqDecoder decoder,
            int width,
            int height,
            IReadOnlyList<Jbig2Bitmap> input,
            Jbig2Bitmap[] created,
            int defined,
            out byte[]? pixels)
        {
            pixels = null;

            if (_instances.Decode(decoder, out int instances) != Jbig2IntegerOutcome.Value)
                return Malformed("A JBIG2 symbol dictionary states no instance count for a refined symbol.");

            if (instances != 1)
                return Unsupported("a symbol dictionary that aggregates several instances into one symbol");

            int id = _identifier.Decode(decoder);
            if (id < 0 || id >= input.Count + defined)
                return Malformed("A JBIG2 symbol dictionary refines a symbol it has not defined yet.");

            if (_x.Decode(decoder, out int offsetX) != Jbig2IntegerOutcome.Value ||
                _y.Decode(decoder, out int offsetY) != Jbig2IntegerOutcome.Value)
            {
                return Malformed("A JBIG2 symbol dictionary states an incomplete refinement for a symbol.");
            }

            Jbig2Bitmap reference = id < input.Count ? input[id] : created[id - input.Count];
            pixels = Jbig2RefinementDecoder.Decode(
                decoder, _contexts, width, height, template, typicalPrediction: false,
                reference, offsetX, offsetY, adaptive);

            return null;
        }
    }
}
