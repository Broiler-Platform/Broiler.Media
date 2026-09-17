using System.Collections.Generic;
using Broiler.Media.Image.Managed.Jbig2;
using Broiler.Media.Image.Managed.Jpx;

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
        new Jbig2ImageCodec(),
        new JpxImageCodec(),
    ];
}

