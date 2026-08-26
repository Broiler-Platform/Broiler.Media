using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media.Image;

public interface IImageOutput : IMediaOutput
{
    ValueTask WriteAsync(ImageFrame frame, CancellationToken cancellationToken = default);
}

