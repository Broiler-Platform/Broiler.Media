using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media.Audio;

public interface IAudioOutput : IMediaOutput
{
    ValueTask WriteAsync(AudioBuffer buffer, CancellationToken cancellationToken = default);
}

