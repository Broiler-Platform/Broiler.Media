using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Broiler.Media.Image.Managed.Tests;

/// <summary>
/// The budgets a JPEG decoder has to hold against a header that lies about how
/// much work it is (PDF roadmap §6.5).
/// </summary>
/// <remarks>
/// Every case here is a few dozen bytes that ask for gigabytes. That asymmetry is
/// the whole attack: the cost of writing the file is nothing, the cost of
/// believing it is the machine. So the checks are on the declared numbers, before
/// anything is allocated from them.
/// </remarks>
internal static class JpegLimitTests
{
    internal static void Register(List<(string Name, Func<ValueTask> Body)> tests)
    {
        tests.Add(("A frame larger than the dimension limit is refused", DimensionLimit));
        tests.Add(("A frame past the pixel limit is refused", PixelLimit));
        tests.Add(("A frame with too many components is refused", ComponentLimit));
        tests.Add(("A sampling factor past the limit is refused", SamplingLimit));
        tests.Add(("A restart interval past the limit is refused", RestartLimit));
        tests.Add(("A flood of marker segments is refused", MarkerSegmentLimit));
        tests.Add(("A coefficient allocation that would overflow int is refused", CoefficientOverflow));
        tests.Add(("The block budget refuses work the pixel budget allows", BlockBudget));
        tests.Add(("An empty frame is refused as malformed", EmptyFrame));
        tests.Add(("A frame inside every budget still decodes", WithinBudget));
    }

    /// <summary>
    /// A JPEG carrying just enough to reach the frame header: SOI, one SOF0, EOI.
    /// </summary>
    /// <remarks>
    /// It has no scan, so a decoder that gets past the header fails later for a
    /// different reason. Every assertion below is that it does <em>not</em> get
    /// that far.
    /// </remarks>
    private static byte[] Frame(
        int width,
        int height,
        (int Id, int H, int V, int Quant)[] components,
        byte[]? extraSegments = null)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };
        if (extraSegments is not null)
            bytes.AddRange(extraSegments);

        int length = 8 + components.Length * 3;
        bytes.AddRange([0xFF, 0xC0, (byte)(length >> 8), (byte)length]);
        bytes.Add(8);                                   // 8-bit precision
        bytes.AddRange([(byte)(height >> 8), (byte)height]);
        bytes.AddRange([(byte)(width >> 8), (byte)width]);
        bytes.Add((byte)components.Length);
        foreach ((int id, int h, int v, int quant) in components)
        {
            bytes.Add((byte)id);
            bytes.Add((byte)((h << 4) | v));
            bytes.Add((byte)quant);
        }

        bytes.AddRange([0xFF, 0xD9]);
        return [.. bytes];
    }

    private static (int, int, int, int)[] Grey => [(1, 1, 1, 0)];

    private static MediaException Refused(byte[] jpeg, MediaLimits? limits = null)
    {
        try
        {
            JpegDecoder.Decode(jpeg, JpegColorTransform.YCbCr, limits);
        }
        catch (MediaException ex)
        {
            Assert.Equal(MediaErrorCode.LimitExceeded, ex.Error.Code);
            return ex;
        }

        throw new AssertException("Expected the decoder to refuse this JPEG.");
    }

    private static ValueTask DimensionLimit()
    {
        // The one budget here the decoder had nothing of its own for: it checked
        // that dimensions were positive and nothing more.
        Refused(
            Frame(20_000, 16, Grey),
            new MediaLimits(maxImageDimension: 8_192));
        return default;
    }

    private static ValueTask PixelLimit()
    {
        // Inside the dimension limit on each axis and far past it multiplied,
        // which is why the two are separate checks.
        Refused(
            Frame(60_000, 60_000, Grey),
            new MediaLimits(maxImagePixels: 16L * 1024 * 1024));
        return default;
    }

    private static ValueTask ComponentLimit()
    {
        // Three components, refused by a caller who wants at most two. Four would
        // never reach this check: the decoder's own scope admits 1 or 3 and
        // rejects the rest first, so this budget only matters to a caller
        // stricter than the codec.
        Refused(
            Frame(16, 16, [(1, 1, 1, 0), (2, 1, 1, 0), (3, 1, 1, 0)]),
            new MediaLimits(maxComponents: 2));
        return default;
    }

    private static ValueTask SamplingLimit()
    {
        // Sampling multiplies the block count, so a large factor buys a large
        // allocation from a header that looks small. Like the component count,
        // this only bites below the decoder's own ceiling of 4 — a factor above
        // that is refused as malformed before any budget is consulted.
        Refused(
            Frame(1_024, 1_024, [(1, 4, 4, 0)]),
            new MediaLimits(maxSamplingFactor: 2));
        return default;
    }

    private static ValueTask RestartLimit()
    {
        byte[] dri = [0xFF, 0xDD, 0x00, 0x04, 0xFF, 0xFF];

        Refused(
            Frame(16, 16, Grey, dri),
            new MediaLimits(maxRestartInterval: 1_024));
        return default;
    }

    private static ValueTask MarkerSegmentLimit()
    {
        // Empty comment segments: four bytes each, and a decoder that walks them
        // without counting walks as many as the file has room for.
        var padding = new List<byte>();
        for (int i = 0; i < 64; i++)
            padding.AddRange([0xFF, 0xFE, 0x00, 0x02]);

        Refused(
            Frame(16, 16, Grey, [.. padding]),
            new MediaLimits(maxMarkerSegments: 16));
        return default;
    }

    private static ValueTask CoefficientOverflow()
    {
        // One component, chosen so the overflow is the only thing that could let
        // this through. 8192 x 8192 blocks times 64 is exactly 2^32, which in int
        // arithmetic wraps to zero: the coefficient count becomes nothing, the
        // budget is satisfied by a frame asking for 17 GB, and the decoder
        // allocates an empty array and carries on. A second component would hide
        // that, because its own honest total would trip the budget instead — so
        // this frame has one.
        MediaException refusal = Refused(
            Frame(65_535, 65_535, [(1, 4, 4, 0)]),
            new MediaLimits(
                maxImagePixels: long.MaxValue / 4,
                maxCoefficientBytes: 256L * 1024 * 1024,
                maxBlocks: long.MaxValue / 4));

        Assert.True(
            refusal.Error.Message.Contains("coefficient", StringComparison.OrdinalIgnoreCase),
            "The refusal names the coefficient budget: " + refusal.Error.Message);
        return default;
    }

    private static ValueTask BlockBudget()
    {
        // Memory and effort are different budgets. A frame can sit inside the
        // coefficient allowance and still be more decoding than a caller wants.
        MediaException refusal = Refused(
            Frame(16_384, 16_384, Grey),
            new MediaLimits(
                maxImagePixels: long.MaxValue / 4,
                maxCoefficientBytes: long.MaxValue / 4,
                maxBlocks: 1_024));

        Assert.True(
            refusal.Error.Message.Contains("block", StringComparison.OrdinalIgnoreCase),
            "The refusal names the work budget: " + refusal.Error.Message);
        return default;
    }

    private static ValueTask EmptyFrame()
    {
        // Zero is not a limit question, so it is a format error rather than a
        // refusal — the file is wrong, not merely large.
        Assert.Throws<FormatException>(() => JpegDecoder.Decode(Frame(0, 16, Grey)));
        return default;
    }

    private static ValueTask WithinBudget()
    {
        // The other half of every case above: a frame this size gets past the
        // header checks and fails later, for having no scan rather than for being
        // too big. If the budgets refused this, they would refuse real images.
        Assert.Throws<FormatException>(
            () => JpegDecoder.Decode(Frame(64, 64, Grey), JpegColorTransform.YCbCr, MediaLimits.Default));
        return default;
    }
}
