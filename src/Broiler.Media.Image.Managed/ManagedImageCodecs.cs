using System.Collections.Generic;

namespace Broiler.Media.Image.Managed;

public static class ManagedImageCodecs
{
    public static IReadOnlyList<ImageCodec> CreateCodecs() =>
    [
        new PngImageCodec(),
        new JpegImageCodec(),
        new BmpImageCodec(),
        new GifImageCodec(),
        new WebpImageCodec(),
    ];
}

