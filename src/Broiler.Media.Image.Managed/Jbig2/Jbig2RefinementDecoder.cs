using System;
using Broiler.Media.Image.Managed.Entropy;

namespace Broiler.Media.Image.Managed.Jbig2;

/// <summary>
/// The generic refinement region decoding procedure, T.88 6.3: a bitmap decoded
/// as a correction to one that already exists.
/// </summary>
// Broiler-AI:           Origin=AI; Spec=T.88 s6.3; IP=High; Security=High; Resources=9; Fingerprint=TBF
// Broiler-Falsified-If: a refinement bitmap decoded from a conforming encoder's output differs from the bitmap it encoded, for either template (the values are transcribed from ITU-T T.88, whose reproduction licence is doubtful)
// Broiler-Human:        PENDING
public static class Jbig2RefinementDecoder
{
    /// <summary>The context width both refinement templates pack into.</summary>
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a refinement template packs more than 13 coding and reference pixels, so a context value indexes past the MqContexts array
    // Broiler-Human:        PENDING
    public const int RefinementContextBits = 13;

    /// <summary>
    /// Template 0's pixels in the bitmap being decoded, most significant first.
    /// The null is A1's slot, which the region header may move.
    /// </summary>
    // Broiler-AI:           Origin=Specification; Spec=T.88 s6.3; IP=High; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: template 0's coding pixels differ from T.88's (0,-1), (1,-1), (-1,0) followed by the A1 slot (the values are transcribed from ITU-T T.88, whose reproduction licence is doubtful)
    // Broiler-Human:        PENDING
    private static readonly (int X, int Y)?[] Coding0 = [(0, -1), (1, -1), (-1, 0), null];

    /// <summary>Template 0's pixels in the reference, with A2's slot last.</summary>
    // Broiler-AI:           Origin=Specification; Spec=T.88 s6.3; IP=High; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: template 0's reference pixels differ from T.88's three-by-three neighbourhood without its top-left corner, in raster order, followed by the A2 slot (the values are transcribed from ITU-T T.88, whose reproduction licence is doubtful)
    // Broiler-Human:        PENDING
    private static readonly (int X, int Y)?[] Reference0 =
        [(0, -1), (1, -1), (-1, 0), (0, 0), (1, 0), (-1, 1), (0, 1), (1, 1), null];

    /// <summary>Template 1 has no adaptive pixels: ten fixed positions.</summary>
    // Broiler-AI:           Origin=Specification; Spec=T.88 s6.3; IP=High; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: template 1's coding pixels differ from T.88's (-1,-1), (0,-1), (1,-1), (-1,0) (the values are transcribed from ITU-T T.88, whose reproduction licence is doubtful)
    // Broiler-Human:        PENDING
    private static readonly (int X, int Y)?[] Coding1 = [(-1, -1), (0, -1), (1, -1), (-1, 0)];

    // Broiler-AI:           Origin=Specification; Spec=T.88 s6.3; IP=High; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: template 1's reference pixels differ from T.88's (0,-1), (-1,0), (0,0), (1,0), (0,1), (1,1) (the values are transcribed from ITU-T T.88, whose reproduction licence is doubtful)
    // Broiler-Human:        PENDING
    private static readonly (int X, int Y)?[] Reference1 = [(0, -1), (-1, 0), (0, 0), (1, 0), (0, 1), (1, 1)];

    /// <summary>
    /// Decodes a refinement of <paramref name="reference"/>, or null when the
    /// request is outside what this decodes.
    /// </summary>
    /// <param name="decoder">MQ arithmetic decoder.</param>
    /// <param name="contexts">Probability contexts.</param>
    /// <param name="width">Bitmap width.</param>
    /// <param name="height">Bitmap height.</param>
    /// <param name="template">Refinement template (0 or 1).</param>
    /// <param name="typicalPrediction">True if TPGRON is set.</param>
    /// <param name="reference">Reference bitmap.</param>
    /// <param name="referenceDx">Reference X offset.</param>
    /// <param name="referenceDy">Reference Y offset.</param>
    /// <param name="adaptive">Adaptive template pixels.</param>
    // Broiler-AI:           Origin=AI; Spec=T.88 s6.3; IP=Low; Security=High; Resources=9; Fingerprint=TBF
    // Broiler-Falsified-If: a TPGRON row bit is decoded against a context other than the one where only the reference pixel at the current position is set, 0x0020 for template 0 and 0x0008 for template 1 in this pixel order
    // Broiler-Human:        PENDING
    public static byte[]? Decode(
        MqDecoder decoder,
        MqContexts contexts,
        int width,
        int height,
        int template,
        bool typicalPrediction,
        Jbig2Bitmap reference,
        int referenceDx,
        int referenceDy,
        ReadOnlySpan<(int X, int Y)> adaptive)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(contexts);
        ArgumentNullException.ThrowIfNull(reference);

        if (template is < 0 or > 1 || width <= 0 || height <= 0)
            return null;

        if ((long)width * height > int.MaxValue)
            return null;

        (int X, int Y)[] coding = Resolve(template == 0 ? Coding0 : Coding1, adaptive, slot: 0);
        (int X, int Y)[] referenced = Resolve(template == 0 ? Reference0 : Reference1, adaptive, slot: 1);

        var bitmap = new byte[width * height];

        // The context a row's typical-prediction bit is decoded against: a fixed
        // value per template, as the standard states it.
        int typicalContext = template == 0 ? 0x0100 : 0x0080;
        bool predicting = false;

        for (int y = 0; y < height; y++)
        {
            if (typicalPrediction && decoder.Decode(contexts, typicalContext) == 1)
                predicting = !predicting;

            for (int x = 0; x < width; x++)
            {
                int referenceX = x - referenceDx;
                int referenceY = y - referenceDy;

                if (predicting && Settled(reference, referenceX, referenceY) is byte settled)
                {
                    // The reference's neighbourhood agrees with itself, so the
                    // refined pixel is that value and costs nothing to say.
                    bitmap[(y * width) + x] = settled;
                    continue;
                }

                int context = 0;
                foreach ((int dx, int dy) in coding)
                    context = (context << 1) | At(bitmap, width, height, x + dx, y + dy);

                foreach ((int dx, int dy) in referenced)
                    context = (context << 1) | reference.At(referenceX + dx, referenceY + dy);

                bitmap[(y * width) + x] = (byte)decoder.Decode(contexts, context);
            }
        }

        return bitmap;
    }

    /// <summary>
    /// The value the reference's three-by-three neighbourhood agrees on, or null
    /// where it does not and the pixel has to be decoded after all.
    /// </summary>
    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a three-by-three reference neighbourhood that is not uniformly white or uniformly black yields a value instead of null
    // Broiler-Human:        PENDING
    public static byte? Settled(Jbig2Bitmap reference, int x, int y)
    {
        int sum = 0;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
                sum += reference.At(x + dx, y + dy);
        }

        return sum switch
        {
            0 => 0,
            9 => 1,
            _ => null,
        };
    }

    /// <summary>The template with its adaptive slot filled from the header.</summary>
    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=1; Fingerprint=TBF
    // Broiler-Falsified-If: a template's null slot takes a header pixel other than adaptive[slot], or a missing one takes a position other than the nominal (-1,-1)
    // Broiler-Human:        PENDING
    public static (int X, int Y)[] Resolve(
        (int X, int Y)?[] template,
        ReadOnlySpan<(int X, int Y)> adaptive,
        int slot)
    {
        var resolved = new (int X, int Y)[template.Length];
        for (int i = 0; i < template.Length; i++)
        {
            resolved[i] = template[i] is (int x, int y)
                ? (x, y)
                : slot < adaptive.Length ? adaptive[slot] : (-1, -1);
        }

        return resolved;
    }

    /// <summary>The template pair a caller needs to resolve its own adaptive pixels.</summary>
    // Broiler-AI:           Origin=AI; IP=None; Security=Medium; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: template 0 yields a pair other than Coding0 and Reference0, or template 1 a pair other than Coding1 and Reference1
    // Broiler-Human:        PENDING
    public static ((int X, int Y)?[] Coding, (int X, int Y)?[] Reference) Templates(int template) =>
        template == 0 ? (Coding0, Reference0) : (Coding1, Reference1);

    // Broiler-AI:           Origin=AI; IP=Low; Security=Medium; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a coordinate left of, above, right of or below the bitmap reads anything but 0 or indexes the array
    // Broiler-Human:        PENDING
    private static int At(byte[] bitmap, int width, int height, int x, int y) =>
        x >= 0 && x < width && y >= 0 && y < height ? bitmap[(y * width) + x] : 0;
}
