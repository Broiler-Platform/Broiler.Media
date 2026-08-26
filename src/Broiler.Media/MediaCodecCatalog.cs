using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media;

public sealed class MediaCodecCatalog
{
    private readonly ReadOnlyCollection<MediaCodec> _codecs;

    public MediaCodecCatalog(IEnumerable<MediaCodec> codecs)
    {
        ArgumentNullException.ThrowIfNull(codecs);

        MediaCodec[] codecArray = codecs.ToArray();
        HashSet<MediaCodecId> ids = [];
        foreach (MediaCodec codec in codecArray)
        {
            if (!ids.Add(codec.Id))
                throw new ArgumentException($"Duplicate media codec id '{codec.Id}'.", nameof(codecs));
        }

        _codecs = Array.AsReadOnly(codecArray);
    }

    public IReadOnlyList<MediaCodec> Codecs => _codecs;

    public MediaCodec? FindById(MediaCodecId id) =>
        _codecs.FirstOrDefault(codec => codec.Id == id);

    public IReadOnlyList<MediaCodec> GetByKind(MediaKind kind) =>
        Array.AsReadOnly(_codecs.Where(codec => codec.Kind == kind).ToArray());

    public async ValueTask<MediaCodecMatch?> SelectAsync(
        MediaKind kind,
        MediaInput input,
        MediaProbeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        MediaProbeOptions effectiveOptions = options ?? MediaProbeOptions.Default;
        byte[] prefix = await ReadPrefixAsync(input.Stream, effectiveOptions.Limits.MaxProbeBytes, cancellationToken)
            .ConfigureAwait(false);
        var request = new MediaProbeRequest(prefix, input.Hints, effectiveOptions.Limits);

        MediaCodecMatch? best = null;
        foreach (MediaCodec codec in _codecs)
        {
            if (codec.Kind != kind)
                continue;

            cancellationToken.ThrowIfCancellationRequested();
            MediaProbeResult result = await codec.ProbeAsync(request, cancellationToken).ConfigureAwait(false);
            if (result.Kind != kind)
                throw new InvalidOperationException($"Codec '{codec.Id}' returned a {result.Kind} probe for a {kind} request.");
            if (!result.IsMatch)
                continue;

            if (best is null || result.Confidence > best.Result.Confidence)
                best = new MediaCodecMatch(codec, result);
        }

        return best;
    }

    private static async ValueTask<byte[]> ReadPrefixAsync(Stream stream, int maxBytes, CancellationToken cancellationToken)
    {
        if (maxBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));

        long? originalPosition = stream.CanSeek ? stream.Position : null;
        byte[] buffer = new byte[maxBytes];
        int total = 0;

        try
        {
            while (total < maxBytes)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(total, maxBytes - total), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                    break;

                total += read;
            }
        }
        finally
        {
            if (originalPosition.HasValue)
                stream.Position = originalPosition.Value;
        }

        if (total == buffer.Length)
            return buffer;

        Array.Resize(ref buffer, total);
        return buffer;
    }
}

