using System;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media.Video;

public interface IVideoSession : IAsyncDisposable
{
    event EventHandler<VideoSessionEvent>? StateChanged;

    VideoSessionState State { get; }

    VideoStreamInfo StreamInfo { get; }

    TimeSpan Position { get; }

    ValueTask PlayAsync(CancellationToken cancellationToken = default);

    ValueTask PauseAsync(CancellationToken cancellationToken = default);

    ValueTask SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);

    ValueTask StopAsync(CancellationToken cancellationToken = default);
}
