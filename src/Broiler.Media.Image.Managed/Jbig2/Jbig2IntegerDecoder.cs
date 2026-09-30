using System;
using Broiler.Media.Image.Managed.Entropy;

namespace Broiler.Media.Image.Managed.Jbig2;

/// <summary>What one arithmetic integer decoding produced.</summary>
// Broiler-AI:           Origin=AI; IP=None; Security=None; Resources=0; Fingerprint=TBF
// Broiler-Human:        PENDING
public enum Jbig2IntegerOutcome
{
    /// <summary>A value, in the out parameter.</summary>
    Value,

    /// <summary>
    /// OOB. Not an error: it is how the format says "no more" — the end of a
    /// height class in a symbol dictionary, the end of a strip in a text region.
    /// </summary>
    OutOfBand,

    /// <summary>
    /// A value too large to be anything but a malformed or hostile stream. The
    /// procedure's last branch can encode up to 2^32 + 4436, which no field this
    /// decoder reads could legitimately hold.
    /// </summary>
    OutOfRange,
}

/// <summary>
/// The arithmetic integer decoding procedure, T.88 Annex A.
/// </summary>
// Broiler-AI:           Origin=Specification; Spec=T.88 sA.2; IP=Low; Security=High; Resources=1; Fingerprint=TBF
// Broiler-Falsified-If: the accumulated PREV reaches 512 or more and indexes past the nine-bit context array
// Broiler-Human:        PENDING
public sealed class Jbig2IntegerDecoder
{
    /// <summary>Nine bits of accumulated path, which is where PREV saturates.</summary>
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: ContextBits is below nine, so a saturated PREV between 256 and 511 indexes past the contexts it sizes
    // Broiler-Human:        PENDING
    private const int ContextBits = 9;

    private readonly MqContexts _contexts = new(ContextBits);

    // Broiler-AI:           Origin=Specification; Spec=T.88 sA.2; IP=Low; Security=High; Resources=1; Fingerprint=TBF
    // Broiler-Falsified-If: a magnitude above int.MaxValue from the 32-bit branch is returned as a wrapped Value instead of OutOfRange
    // Broiler-Human:        PENDING
    public Jbig2IntegerOutcome Decode(MqDecoder decoder, out int value)
    {
        ArgumentNullException.ThrowIfNull(decoder);

        value = 0;
        int prev = 1;

        int Bit()
        {
            int bit = decoder.Decode(_contexts, prev);

            // PREV grows by one bit until it reaches nine, then keeps the low
            // eight and pins the ninth. Without the pin the context index would
            // run past the array; with it, a long number's later bits share the
            // estimators of its earlier ones, which is the standard's intent.
            prev = prev < 256
                ? (prev << 1) | bit
                : ((((prev << 1) | bit) & 511) | 256);

            return bit;
        }

        long Magnitude(int bits)
        {
            long magnitude = 0;
            for (int i = 0; i < bits; i++)
                magnitude = (magnitude << 1) | (uint)Bit();

            return magnitude;
        }

        int sign = Bit();
        long magnitudeValue;

        // The prefix: each 1 bit moves to a wider field with a larger offset, so
        // that small numbers — which is most of them — cost few bits.
        if (Bit() == 0)
            magnitudeValue = Magnitude(2);
        else if (Bit() == 0)
            magnitudeValue = Magnitude(4) + 4;
        else if (Bit() == 0)
            magnitudeValue = Magnitude(6) + 20;
        else if (Bit() == 0)
            magnitudeValue = Magnitude(8) + 84;
        else if (Bit() == 0)
            magnitudeValue = Magnitude(12) + 340;
        else
            magnitudeValue = Magnitude(32) + 4436;

        // A negative zero is the format's out-of-band signal rather than a
        // number, which is the one piece of this procedure that a caller must
        // handle instead of the decoder.
        if (sign == 1 && magnitudeValue == 0)
            return Jbig2IntegerOutcome.OutOfBand;

        if (magnitudeValue > int.MaxValue)
            return Jbig2IntegerOutcome.OutOfRange;

        value = sign == 1 ? (int)-magnitudeValue : (int)magnitudeValue;
        return Jbig2IntegerOutcome.Value;
    }
}

/// <summary>
/// The IAID decoding procedure, T.88 Annex A.3: which symbol an instance refers
/// to.
/// </summary>
// Broiler-AI:           Origin=Specification; Spec=T.88 sA.3; IP=Low; Security=High; Resources=2; Fingerprint=TBF
// Broiler-Falsified-If: a code length above MaxSymbolCodeLength is accepted, so one decoder allocates more than 2^18 contexts
// Broiler-Human:        PENDING
public sealed class Jbig2SymbolIdDecoder
{
    private readonly MqContexts _contexts;
    private readonly int _codeLength;

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=2; Fingerprint=TBF
    // Broiler-Falsified-If: a negative code length or one above Jbig2Limits.MaxSymbolCodeLength is accepted instead of throwing ArgumentOutOfRangeException
    // Broiler-Human:        PENDING
    public Jbig2SymbolIdDecoder(int codeLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(codeLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(codeLength, Jbig2Limits.MaxSymbolCodeLength);

        _codeLength = codeLength;
        _contexts = new MqContexts(codeLength + 1);
    }

    // Broiler-AI:           Origin=Specification; Spec=T.88 sA.3; IP=Low; Security=High; Resources=1; Fingerprint=TBF
    // Broiler-Falsified-If: a context index of 2^(codeLength + 1) or more is used, indexing past the decoder's contexts
    // Broiler-Human:        PENDING
    public int Decode(MqDecoder decoder)
    {
        ArgumentNullException.ThrowIfNull(decoder);

        int prev = 1;
        for (int i = 0; i < _codeLength; i++)
            prev = (prev << 1) | decoder.Decode(_contexts, prev);

        // The leading 1 was the tree's root rather than part of the identifier.
        return prev - (1 << _codeLength);
    }
}
