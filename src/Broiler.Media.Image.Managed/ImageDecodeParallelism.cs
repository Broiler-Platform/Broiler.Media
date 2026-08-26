using System;
using System.Threading.Tasks;

namespace Broiler.Media.Image.Managed;

/// <summary>
/// The thread budget the managed decoders spend on their data-parallel passes, and the range
/// partitioner they all go through. Multithreading roadmap items #6 and #7.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only the disjoint-output passes come through here.</b> Both decoders have a genuinely
/// sequential front half — DEFLATE is a back-reference stream and PNG's Up/Paeth filters read the
/// reconstructed previous row; JPEG's Huffman bitstream is sequential except at restart intervals —
/// and the roadmap records both as things not to parallelize. What is left is per-row and per-block
/// work whose outputs never overlap, which is what <see cref="For"/> partitions.
/// </para>
/// <para>
/// <b>Why a range partitioner rather than <c>Parallel.For</c> over the index.</b> A single row of an
/// expand-to-RGBA pass is a few microseconds of work; handing the scheduler one work item per row
/// spends more on the delegate than on the pixels. <see cref="For"/> hands each worker a contiguous
/// <em>band</em> of at least <paramref name="minimumUnitsPerBand"/> units instead, so the caller
/// states the granularity in units it understands (rows, block rows) and the partitioning stays in
/// one place.
/// </para>
/// <para>
/// <b>Identical output at any thread count is structural, not incidental.</b> Every band writes to
/// its own rows of the destination and reads only immutable input, so the bytes a band produces do
/// not depend on which other bands have run. That is what makes the global exit gate — a
/// single-threaded setting that reproduces the parallel output exactly — checkable by comparing
/// decoded buffers, and it is what
/// <c>Broiler.Media.Image.Managed.Tests</c> compares.
/// </para>
/// <para>
/// <b>The budget is a process-wide setting with an environment override</b>
/// (<c>BROILER_IMAGE_DECODE_THREADS</c>), read once at type initialization. Decoding happens deep
/// inside <c>ImageCodec.Decode</c>, which has no options parameter to thread a value through and
/// should not grow one for a knob whose only consumers are a benchmark and a determinism test.
/// Setting it to <c>1</c> is the sequential path — not an approximation of it, the same code with
/// one band.
/// </para>
/// <para>
/// <b>It stays internal, and item #8 is the reason that is worth saying.</b> Layout now issues a
/// document's image loads concurrently, so <em>N</em> loads each splitting their own decode into
/// <em>N</em> bands is <em>N²</em> runnable threads on <em>N</em> cores — an obvious candidate for a
/// host-side division of this budget. It was built and measured, and dividing was worth nothing
/// either way (see <c>ImagePrefetch</c>) — so the seam that would have exposed this type to the hosts
/// was removed again and the environment variable is still the only way a host configures it.
/// </para>
/// </remarks>
internal static class ImageDecodeParallelism
{
    /// <summary>Environment variable that overrides the default thread budget.</summary>
    internal const string ThreadsEnvironmentVariable = "BROILER_IMAGE_DECODE_THREADS";

    private static int _maxDegreeOfParallelism = ReadConfiguredDegree();

    /// <summary>
    /// Maximum threads a decode pass may use. Clamped to at least one; <c>1</c> runs every pass
    /// inline on the calling thread, with no scheduler involvement at all.
    /// </summary>
    internal static int MaxDegreeOfParallelism
    {
        get => _maxDegreeOfParallelism;
        set => _maxDegreeOfParallelism = Math.Max(1, value);
    }

    private static int ReadConfiguredDegree()
    {
        string? configured = Environment.GetEnvironmentVariable(ThreadsEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured) &&
            int.TryParse(configured, out int threads) &&
            threads > 0)
        {
            return threads;
        }

        return Environment.ProcessorCount;
    }

    /// <summary>
    /// Runs <paramref name="band"/> over contiguous bands covering
    /// <c>[fromInclusive, toExclusive)</c>, in parallel when that is worth doing and inline when it
    /// is not. The callback receives the half-open bounds of its band.
    /// </summary>
    /// <param name="minimumUnitsPerBand">
    /// Smallest band the caller considers worth a thread. A run that cannot be split into two such
    /// bands is executed inline, so small images — favicons, the 1×1 spacers a page is full of —
    /// never pay for a scheduler round trip.
    /// </param>
    internal static void For(int fromInclusive, int toExclusive, int minimumUnitsPerBand, Action<int, int> band)
    {
        ArgumentNullException.ThrowIfNull(band);

        int total = toExclusive - fromInclusive;
        if (total <= 0)
            return;

        int threads = Math.Min(_maxDegreeOfParallelism, total / Math.Max(1, minimumUnitsPerBand));
        if (threads <= 1)
        {
            band(fromInclusive, toExclusive);
            return;
        }

        // Ceiling division, so `threads` bands cover the range with the remainder spread over the
        // leading bands rather than piled onto a lone trailing one.
        int unitsPerBand = (total + threads - 1) / threads;
        Parallel.For(
            0,
            threads,
            new ParallelOptions { MaxDegreeOfParallelism = threads },
            i =>
            {
                int start = fromInclusive + i * unitsPerBand;
                int end = Math.Min(start + unitsPerBand, toExclusive);
                if (start < end)
                    band(start, end);
            });
    }
}
