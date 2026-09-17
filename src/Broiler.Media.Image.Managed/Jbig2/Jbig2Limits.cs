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
public static class Jbig2Limits
{
    /// <summary>Symbols one dictionary may define, and one region may refer to.</summary>
    public const int MaxSymbols = 100_000;

    /// <summary>Bits in a symbol identifier, which bounds the ID decoder's contexts.</summary>
    public const int MaxSymbolCodeLength = 17;

    /// <summary>Rows or columns in one symbol.</summary>
    public const int MaxSymbolExtent = 8192;

    /// <summary>Symbol instances one text region may place.</summary>
    public const int MaxInstances = 1_000_000;
}
