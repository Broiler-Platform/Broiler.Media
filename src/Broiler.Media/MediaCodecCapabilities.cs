using System;

namespace Broiler.Media;

[Flags]
public enum MediaCodecCapabilities
{
    None = 0,
    Decode = 1,
    Encode = 2,
    Streaming = 4,
    Animation = 8,
    DirectPresentation = 16,
}

