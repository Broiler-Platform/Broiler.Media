using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media;

public interface IMediaOutput
{
    ValueTask CompleteAsync(CancellationToken cancellationToken = default);

    ValueTask FailAsync(MediaError error, CancellationToken cancellationToken = default);
}

