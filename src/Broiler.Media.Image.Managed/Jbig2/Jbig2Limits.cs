namespace Broiler.Media.Image.Managed.Jbig2;

/// <summary>
/// The bounds this decoder refuses past, in one place so that a reviewer can see
/// the whole envelope at once.
/// </summary>
/// <remarks>
/// None of these comes from T.88, which sets no such limits: they are this
/// build's answer to a hostile stream. A JBIG2 segment states its own sizes and
/// counts before any of them is checked against the data that must supply them,
/// so a file can ask for a dictionary of four billion symbols in eight bytes. The
/// numbers below are chosen to be far above any real scanned page and far below
/// anything that costs the process its memory.
/// </remarks>
// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=0; Fingerprint=TBF
// Broiler-Falsified-If: a limit here admits more than its consumer can index, as when MaxSymbols needs more identifier bits than MaxSymbolCodeLength allows
// Broiler-Human:        PENDING
public static class Jbig2Limits
{
    /// <summary>Symbols one dictionary may define, and one region may refer to.</summary>
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a symbol dictionary whose new symbols together with its inputs exceed 100,000 is decoded instead of refused
    // Broiler-Human:        PENDING
    public const int MaxSymbols = 100_000;

    /// <summary>Bits in a symbol identifier, which bounds the ID decoder's contexts.</summary>
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: the value is below the 17 bits CodeLength gives for MaxSymbols, so the identifier decoder's constructor throws for a symbol count MaxSymbols admits
    // Broiler-Human:        PENDING
    public const int MaxSymbolCodeLength = 17;

    /// <summary>Rows or columns in one symbol.</summary>
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a symbol or a refined text-region instance wider or taller than 8192 pixels is decoded instead of refused
    // Broiler-Human:        PENDING
    public const int MaxSymbolExtent = 8192;

    /// <summary>Symbol instances one text region may place.</summary>
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a text region declaring more than 1,000,000 symbol instances is decoded instead of refused
    // Broiler-Human:        PENDING
    public const int MaxInstances = 1_000_000;
}
