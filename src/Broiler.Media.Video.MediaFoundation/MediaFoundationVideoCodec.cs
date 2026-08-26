using System;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Graphics.Windows;

namespace Broiler.Media.Video.MediaFoundation;

[SupportedOSPlatform("windows")]
public sealed class MediaFoundationVideoCodec : VideoCodec
{
    public static MediaCodecDescriptor CodecDescriptor { get; } = new(
        new MediaCodecId("broiler.video.mediafoundation.windows"),
        "Broiler Windows Media Foundation video",
        MediaKind.Video,
        MediaCodecCapabilities.Decode | MediaCodecCapabilities.Streaming | MediaCodecCapabilities.DirectPresentation,
        [
            new MediaFormatDescriptor(
                "MPEG-4 video",
                ["video/mp4"],
                [".mp4"]),
        ]);

    public MediaFoundationVideoCodec()
        : base(CodecDescriptor)
    {
    }

    public override ValueTask<MediaProbeResult> ProbeAsync(
        MediaProbeRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (IsMp4Prefix(request.Prefix.Span) || IsMp4Hint(request.Hints))
        {
            return ValueTask.FromResult(MediaProbeResult.Match(
                MediaKind.Video,
                MediaProbeConfidence.High,
                "MPEG-4 video",
                "video/mp4",
                bytesConsumed: request.Prefix.Length >= 12 ? 12 : null));
        }

        return ValueTask.FromResult(MediaProbeResult.NoMatch(MediaKind.Video));
    }

    public override async ValueTask<VideoStreamInfo> GetInfoAsync(
        MediaInput input,
        VideoDecodeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        string sourceUri = ResolveSourceUri(input.Hints);
        using var platform = new MediaFoundationPlatformScope();
        using IMediaFoundationMediaEngine engine = MediaFoundationMediaEngine.Create(
            target: null,
            new VideoSessionOptions(autoplay: false, muted: true));

        var metadataReady = new TaskCompletionSource<VideoStreamInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.EventReceived += (_, e) =>
        {
            if (e.Kind is MediaFoundationMediaEngineEventKind.LoadedMetadata or MediaFoundationMediaEngineEventKind.FormatChange)
            {
                try
                {
                    metadataReady.TrySetResult(engine.GetStreamInfo());
                }
                catch (Exception ex)
                {
                    metadataReady.TrySetException(ex);
                }
            }
            else if (e.Kind is MediaFoundationMediaEngineEventKind.Error or
                MediaFoundationMediaEngineEventKind.ResourceLost or
                MediaFoundationMediaEngineEventKind.StreamRenderingError)
            {
                metadataReady.TrySetException(new MediaException(new MediaError(
                    MediaErrorCode.NativeFailure,
                    "Media Foundation failed while loading video metadata.",
                    Id)));
            }
        };

        engine.SetSource(sourceUri);
        engine.Load();
        return await metadataReady.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public override async ValueTask<IVideoSession> OpenSessionAsync(
        MediaInput input,
        IVideoOutput output,
        VideoSessionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        cancellationToken.ThrowIfCancellationRequested();

        if (output is not HwndVideoOutput target)
        {
            throw new ArgumentException(
                "Media Foundation video sessions require a Broiler.Graphics.Windows HwndVideoOutput target.",
                nameof(output));
        }

        target.ThrowIfUsableTargetRequired();
        string sourceUri = ResolveSourceUri(input.Hints);
        VideoSessionOptions effectiveOptions = options ?? new VideoSessionOptions();
        var platform = new MediaFoundationPlatformScope();
        IMediaFoundationMediaEngine? engine = null;
        MediaFoundationVideoSession? session = null;

        try
        {
            engine = MediaFoundationMediaEngine.Create(target, effectiveOptions);
            session = new MediaFoundationVideoSession(engine, target, platform);
            engine = null;
            await session.LoadAsync(sourceUri, cancellationToken).ConfigureAwait(false);

            if (effectiveOptions.Autoplay)
                await session.PlayAsync(cancellationToken).ConfigureAwait(false);

            MediaFoundationVideoSession opened = session;
            session = null;
            return opened;
        }
        catch
        {
            if (session is not null)
                await session.DisposeAsync().ConfigureAwait(false);
            engine?.Dispose();
            if (session is null)
                platform.Dispose();
            throw;
        }
    }

    private static bool IsMp4Prefix(ReadOnlySpan<byte> prefix) =>
        prefix.Length >= 12 &&
        prefix[4] == (byte)'f' &&
        prefix[5] == (byte)'t' &&
        prefix[6] == (byte)'y' &&
        prefix[7] == (byte)'p';

    private static bool IsMp4Hint(MediaSourceHints hints)
    {
        if (string.Equals(hints.MimeType, "video/mp4", StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(NormalizeExtension(hints.FileExtension), ".mp4", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveSourceUri(MediaSourceHints hints)
    {
        if (string.IsNullOrWhiteSpace(hints.SourceUri))
            throw new MediaException(new MediaError(MediaErrorCode.UnsupportedFormat, "Media Foundation video requires a source URI hint."));

        string source = hints.SourceUri.Trim();
        if (Uri.TryCreate(source, UriKind.Absolute, out Uri? uri))
        {
            if (uri.IsFile)
                return uri.AbsoluteUri;

            if (uri.Scheme is "http" or "https")
                throw new MediaException(new MediaError(MediaErrorCode.UnsupportedFormat, "Network video sources remain an integration-layer responsibility."));

            return uri.AbsoluteUri;
        }

        if (Path.IsPathFullyQualified(source))
            return new Uri(source).AbsoluteUri;

        throw new MediaException(new MediaError(MediaErrorCode.UnsupportedFormat, "Media Foundation video source URI hints must be absolute."));
    }

    private static string? NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return null;

        string trimmed = extension.Trim();
        return trimmed.StartsWith(".", StringComparison.Ordinal) ? trimmed : "." + trimmed;
    }
}
