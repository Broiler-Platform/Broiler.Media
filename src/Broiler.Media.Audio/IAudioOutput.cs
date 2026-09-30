using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Media.Audio;

// Broiler-AI:           Origin=AI; Spec=ADR-0002; IP=None; Security=High; Resources=3; Fingerprint=TBF
// Broiler-Falsified-If: an implementation accepts WriteAsync after CompleteAsync or FailAsync and plays the late buffer
// Broiler-Human:        PENDING
public interface IAudioOutput : IMediaOutput
{
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=3; Fingerprint=TBF
    // Broiler-Falsified-If: an implementation reads more than FrameCount times BytesPerFrame bytes of Samples when handing a buffer to a native device
    // Broiler-Human:        PENDING
    ValueTask WriteAsync(AudioBuffer buffer, CancellationToken cancellationToken = default);
}

