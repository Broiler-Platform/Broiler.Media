using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Spec = Broiler.Media.Image.Managed.Tests.PngFormatBuilder.ApngFrameSpec;

namespace Broiler.Media.Image.Managed.Tests;

internal static class Program
{
    private const string ProgressiveGradientBase64 =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wCEAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDIBCQkJDAsMGA0NGDIhHCEyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMv/CABEIAEAAYAMBIgACEQEDEQH/xABXAAEBAQAAAAAAAAAAAAAAAAADBgIQAQADAAAAAAAAAAAAAAAAAAABA2EBAQEBAQEAAAAAAAAAAAAAAAIEBQYBEQEBAQEBAAAAAAAAAAAAAAAAAgERQP/aAAwDAQACEQMRAAABhUZPCKMjoFGRUgjo6QR9qgUZHTPIyZ3DijI6BRkVIoyOkUZFQKMjpnkZM/hx26KgEdHSCOipBGR0CjIqZ5GTP4cUZFQKMjpFGRUijI6BRkVP/9oADAMBAAIRAxEAABAIELDfz704w8ABPDL37y8wIEL/2gAIAQEAAT8BitFaK0VorRWitFaK0VorRWitFaK0VorRWitFaK0VorRWitFaK0VorRWitFaK0VorRWitFaK0VorRWitFaK0VorRWitFaK0VorRWitFaK0VorRWitFaK0VorRWitFaK0VorRWitFaK0VorRWitFaK0VorRWitFaK0VorRWitFaK0VorRW/9oACAECEQE/Aeuuuuuuuuuuuuuuuuuuuuuuuv/aAAgBAxEBPwGbTabTabTabTabTabTabTabTabTabTabTabTb/2gAIAQEAAT8QyZMmTJkyZMmTJkyZMmTJkyZMmTJkyZMmTJkyZMmTJkyZMmTJkyZMmTJkyZMmTJkyZMmTJkyZMmTJkyZMmTJkyZMmTJkyZMmTJkyZMmTJkyZMmTJk/9oACAECEQE/EMMMMMMMMMMMMMMMMMMMMMMMMP/aAAgBAxEBPxD0f/8A/wD/AP8A/wD/AP/Z";

    private const string LossyWebpOneByOneBase64 =
        "UklGRiIAAABXRUJQVlA4IBYAAAAwAQCdASoBAAEADsD+JaQAA3AAAAAA";

    private static async Task<int> Main()
    {
        var tests = new List<(string Name, Func<ValueTask> Body)>
        {
            ("Managed image provider exposes PNG, JPEG, BMP, GIF and WebP codecs", ProviderExposesCodecs),
            ("Catalog selects concrete managed codecs by signature", CatalogSelectsBySignature),
            ("GIF fixtures decode and roundtrip", GifFixturesDecodeAndRoundTrip),
            ("WebP lossless fixtures decode and roundtrip", WebpLosslessFixturesDecodeAndRoundTrip),
            ("WebP lossy VP8 fixture decodes through runtime codec", WebpLossyVp8FixtureDecodes),
            ("PNG encode is deterministic and roundtrips", PngEncodeRoundTrips),
            ("BMP encode is deterministic and roundtrips", BmpEncodeRoundTrips),
            ("JPEG encode is deterministic and decodes", JpegEncodeDecodes),
            ("PNG grayscale, palette, tRNS and Adam7 fixtures decode", PngVariantsDecode),
            ("APNG blend/dispose/timing fixtures decode", ApngFixturesDecode),
            ("APNG encode bytes and decoded frames roundtrip", ApngEncodeRoundTrips),
            ("Progressive JPEG fixture decodes", ProgressiveJpegDecodes),
            ("Malformed PNG CRC is rejected", MalformedPngRejected),
            ("Encoded input limit is enforced", EncodedInputLimitEnforced),
            ("Still codecs reject animated encode", StillCodecsRejectAnimatedEncode),
            ("Media.Image.Managed runtime has no Graphics dependency", RuntimeHasNoGraphicsReference),
            ("Parallel decode is byte-identical to single-threaded decode", ParallelDecodeMatchesSequential),
            ("Decoders are re-entrant across threads", DecodersAreReentrant),
        };

        int passed = 0;
        var failures = new List<string>();
        Console.WriteLine($"Running {tests.Count} managed image codec test(s)...\n");

        foreach ((string name, Func<ValueTask> body) in tests)
        {
            try
            {
                await body().ConfigureAwait(false);
                passed++;
                Console.WriteLine($"  [PASS] {name}");
            }
            catch (Exception ex)
            {
                failures.Add(name);
                Console.WriteLine($"  [FAIL] {name}");
                Console.WriteLine($"         {ex.GetType().Name}: {ex.Message}");
            }
        }

        Console.WriteLine($"\n{passed}/{tests.Count} passed, {failures.Count} failed.");
        return failures.Count;
    }

    private static ValueTask ProviderExposesCodecs()
    {
        IReadOnlyList<ImageCodec> codecs = ManagedImageCodecs.CreateCodecs();
        Assert.Equal(5, codecs.Count);
        Assert.True(codecs[0] is PngImageCodec, "PNG should be the first managed image codec.");
        Assert.True(codecs[1] is JpegImageCodec, "JPEG should be the second managed image codec.");
        Assert.True(codecs[2] is BmpImageCodec, "BMP should be the third managed image codec.");
        Assert.True(codecs[3] is GifImageCodec, "GIF should be the fourth managed image codec.");
        Assert.True(codecs[4] is WebpImageCodec, "WebP should be the fifth managed image codec.");
        return ValueTask.CompletedTask;
    }

    private static async ValueTask CatalogSelectsBySignature()
    {
        var catalog = new MediaCodecCatalog(ManagedImageCodecs.CreateCodecs());
        ImageBuffer src = MakeGradient(8, 8);
        byte[] png = new PngImageCodec().Encode(src);
        byte[] jpeg = new JpegImageCodec().Encode(src, quality: 90);
        byte[] bmp = new BmpImageCodec().Encode(src);
        byte[] gif = new GifImageCodec().Encode(src);
        byte[] webp = new WebpImageCodec().Encode(src);

        Assert.True((await SelectAsync(catalog, png).ConfigureAwait(false))?.Codec is PngImageCodec);
        Assert.True((await SelectAsync(catalog, jpeg).ConfigureAwait(false))?.Codec is JpegImageCodec);
        Assert.True((await SelectAsync(catalog, bmp).ConfigureAwait(false))?.Codec is BmpImageCodec);
        Assert.True((await SelectAsync(catalog, gif).ConfigureAwait(false))?.Codec is GifImageCodec);
        Assert.True((await SelectAsync(catalog, webp).ConfigureAwait(false))?.Codec is WebpImageCodec);
        Assert.True(await SelectAsync(catalog, [0, 1, 2, 3]).ConfigureAwait(false) is null);
    }

    private static ValueTask GifFixturesDecodeAndRoundTrip()
    {
        byte[] blackOneByOne =
        [
            0x47, 0x49, 0x46, 0x38, 0x39, 0x61,
            0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00,
            0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF,
            0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
            0x02, 0x02, 0x44, 0x01, 0x00, 0x3B,
        ];
        byte[] transparentOneByOne =
        [
            0x47, 0x49, 0x46, 0x38, 0x39, 0x61,
            0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00,
            0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF,
            0x21, 0xF9, 0x04, 0x01, 0x00, 0x00, 0x00, 0x00,
            0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
            0x02, 0x02, 0x44, 0x01, 0x00, 0x3B,
        ];

        var codec = new GifImageCodec();
        Assert.BytesEqual([0, 0, 0, 255], codec.Decode(blackOneByOne).Rgba, "GIF black fixture pixels");
        Assert.BytesEqual([0, 0, 0, 0], codec.Decode(transparentOneByOne).Rgba, "GIF transparent fixture pixels");

        ImageBuffer still = new(2, 2,
        [
            255, 0, 0, 255, 0, 255, 0, 255,
            0, 0, 255, 255, 255, 255, 255, 255,
        ]);
        byte[] encodedStill = codec.Encode(still);
        ImageBuffer decodedStill = codec.Decode(encodedStill);
        Assert.Equal(2, decodedStill.Width, "GIF still width");
        Assert.Equal(2, decodedStill.Height, "GIF still height");
        Assert.BytesEqual(still.Rgba, decodedStill.Rgba, "GIF still roundtrip pixels");

        ImageSequence animated = new(
            [
                new ImageFrame(FillImage(2, 2, 255, 0, 0, 255), 3, 100),
                new ImageFrame(FillImage(2, 2, 0, 0, 255, 255), 7, 100),
            ],
            2,
            2,
            loopCount: 0);
        ImageSequence decodedAnimation = codec.DecodeAnimation(codec.EncodeAnimation(animated));
        Assert.True(decodedAnimation.IsAnimated, "GIF animation should decode as animated.");
        Assert.Equal(0, decodedAnimation.LoopCount, "GIF animation loop count");
        Assert.Equal(3, decodedAnimation.Frames[0].DelayNumerator, "GIF frame 0 delay");
        Assert.Equal(7, decodedAnimation.Frames[1].DelayNumerator, "GIF frame 1 delay");
        Assert.BytesEqual(animated.Frames[0].Pixels.Rgba, decodedAnimation.Frames[0].Pixels.Rgba, "GIF frame 0 pixels");
        Assert.BytesEqual(animated.Frames[1].Pixels.Rgba, decodedAnimation.Frames[1].Pixels.Rgba, "GIF frame 1 pixels");
        return ValueTask.CompletedTask;
    }

    private static async ValueTask WebpLosslessFixturesDecodeAndRoundTrip()
    {
        var codec = new WebpImageCodec();
        ImageBuffer still = MakeGradient(8, 8);
        byte[] encodedStill = codec.Encode(still);
        Assert.True(WebpDecoder.IsWebp(encodedStill), "Encoded WebP should have a RIFF/WEBP signature.");
        ImageBuffer decodedStill = codec.Decode(encodedStill);
        Assert.Equal(still.Width, decodedStill.Width, "WebP still width");
        Assert.Equal(still.Height, decodedStill.Height, "WebP still height");
        Assert.BytesEqual(still.Rgba, decodedStill.Rgba, "WebP still roundtrip pixels");

        using var output = new MemoryStream();
        await codec.EncodeAsync(ImageSequence.Static(still), output, new ImageEncodeOptions(ImageEncodeFormat.WebP)).ConfigureAwait(false);
        Assert.BytesEqual(encodedStill, output.ToArray(), "WebP async encode bytes");

        ImageSequence animated = new(
            [
                new ImageFrame(FillImage(3, 2, 255, 32, 16, 255), 1, 10),
                new ImageFrame(FillImage(3, 2, 16, 32, 255, 128), 7, 100),
            ],
            3,
            2,
            loopCount: 2);
        ImageSequence decodedAnimation = codec.DecodeAnimation(codec.EncodeAnimation(animated));
        Assert.True(decodedAnimation.IsAnimated, "WebP animation should decode as animated.");
        Assert.Equal(2, decodedAnimation.LoopCount, "WebP animation loop count");
        Assert.Equal(100, decodedAnimation.Frames[0].DelayNumerator, "WebP frame 0 delay ms");
        Assert.Equal(1000, decodedAnimation.Frames[0].DelayDenominator, "WebP frame 0 delay denominator");
        Assert.Equal(70, decodedAnimation.Frames[1].DelayNumerator, "WebP frame 1 delay ms");
        Assert.BytesEqual(animated.Frames[0].Pixels.Rgba, decodedAnimation.Frames[0].Pixels.Rgba, "WebP frame 0 pixels");
        Assert.BytesEqual(animated.Frames[1].Pixels.Rgba, decodedAnimation.Frames[1].Pixels.Rgba, "WebP frame 1 pixels");
    }

    private static async ValueTask WebpLossyVp8FixtureDecodes()
    {
        byte[] webp = Convert.FromBase64String(LossyWebpOneByOneBase64);
        var catalog = new MediaCodecCatalog(ManagedImageCodecs.CreateCodecs());
        Assert.True((await SelectAsync(catalog, webp).ConfigureAwait(false))?.Codec is WebpImageCodec);

        var codec = new WebpImageCodec();
        if (!OperatingSystem.IsWindows())
        {
            Assert.Throws<NotSupportedException>(() => codec.Decode(webp));
            return;
        }

        ImageBuffer decoded;
        try
        {
            decoded = codec.Decode(webp);
        }
        catch (NotSupportedException)
        {
            // Lossy VP8 goes through the Windows Imaging Component, and WIC's
            // WebP decoder is an OPTIONAL Windows component: present on a
            // typical desktop, absent on Windows Server and hosted CI images.
            // Being Windows is therefore not the same as having the decoder.
            // Reporting a bounded capability error instead of a wrong image is
            // exactly the contract, so there is nothing further to assert here.
            return;
        }

        Assert.Equal(1, decoded.Width, "lossy WebP fixture width");
        Assert.Equal(1, decoded.Height, "lossy WebP fixture height");
        Assert.Equal(4, decoded.Rgba.Length, "lossy WebP fixture pixel byte length");
        Assert.Equal(255, decoded.Rgba[3], "lossy WebP fixture alpha");
    }

    private static ValueTask PngEncodeRoundTrips()
    {
        ImageBuffer src = MakeGradient(37, 19);
        var codec = new PngImageCodec();
        byte[] first = codec.Encode(src);
        byte[] second = codec.Encode(src);

        Assert.BytesEqual(first, second, "PNG encoder should be deterministic.");
        ComparePixels(src, codec.Decode(first));
        CompareStillDecode(first, src);
        return ValueTask.CompletedTask;
    }

    private static ValueTask BmpEncodeRoundTrips()
    {
        ImageBuffer src = MakeGradient(40, 24);
        var codec = new BmpImageCodec();
        byte[] first = codec.Encode(src);
        byte[] second = codec.Encode(src);

        Assert.BytesEqual(first, second, "BMP encoder should be deterministic.");
        ComparePixels(src, codec.Decode(first));
        CompareStillDecode(first, src);
        return ValueTask.CompletedTask;
    }

    private static ValueTask JpegEncodeDecodes()
    {
        ImageBuffer src = MakeGradient(64, 48);
        var codec = new JpegImageCodec();
        byte[] first = codec.Encode(src, quality: 90);
        byte[] second = codec.Encode(src, quality: 90);

        Assert.BytesEqual(first, second, "JPEG encoder should be deterministic.");
        ImageBuffer decoded = codec.Decode(first);
        Assert.Equal(src.Width, decoded.Width, "JPEG width");
        Assert.Equal(src.Height, decoded.Height, "JPEG height");
        CompareStillDecode(first);
        return ValueTask.CompletedTask;
    }

    private static ValueTask PngVariantsDecode()
    {
        var fixtures = new List<byte[]>
        {
            PngFormatBuilder.Build(2, 2, 8, 0, [[0, 128], [255, 64]]),
            PngFormatBuilder.Build(8, 1, 1, 0, [[0xA1]]),
            PngFormatBuilder.Build(3, 1, 8, 3, [[0, 1, 2]], [10, 20, 30, 40, 50, 60, 70, 80, 90], [0, 128]),
            PngFormatBuilder.Build(2, 1, 8, 4, [[100, 50, 200, 255]]),
            PngFormatBuilder.Build(2, 1, 8, 2, [[255, 0, 255, 1, 2, 3]], trns: [0, 255, 0, 0, 0, 255]),
            PngFormatBuilder.Build(1, 1, 16, 2, [[0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC]]),
        };

        ImageBuffer adam7 = MakeGradient(33, 21);
        fixtures.Add(PngFormatBuilder.BuildInterlacedRgba(adam7.Width, adam7.Height, adam7.Rgba));

        foreach (byte[] fixture in fixtures)
        {
            ImageSequence sequence = DecodeStillViaCatalog(fixture);
            Assert.False(sequence.IsAnimated, "PNG variant should decode as a still image.");
            Assert.True(sequence.Width > 0, "PNG variant width");
            Assert.True(sequence.Height > 0, "PNG variant height");
        }

        CompareStillDecode(fixtures[^1], adam7);

        return ValueTask.CompletedTask;
    }

    private static ValueTask ApngFixturesDecode()
    {
        var cases = new List<IReadOnlyList<Spec>>
        {
            new Spec[]
            {
                new(4, 4, 0, 0, 1, 10, 0, 0, Fill(4, 4, 255, 0, 0, 255)),
                new(2, 2, 1, 1, 1, 10, 0, 0, Fill(2, 2, 0, 0, 255, 255)),
            },
            new Spec[]
            {
                new(2, 2, 0, 0, 1, 10, 0, 0, Fill(2, 2, 255, 0, 0, 255)),
                new(2, 2, 0, 0, 1, 10, 0, 1, Fill(2, 2, 0, 255, 0, 128)),
            },
            new Spec[]
            {
                new(4, 4, 0, 0, 1, 10, 0, 0, Fill(4, 4, 255, 0, 0, 255)),
                new(2, 2, 0, 0, 2, 10, 1, 0, Fill(2, 2, 0, 0, 255, 255)),
                new(2, 2, 2, 2, 3, 0, 0, 0, Fill(2, 2, 0, 255, 0, 255)),
            },
            new Spec[]
            {
                new(4, 4, 0, 0, 1, 10, 0, 0, Fill(4, 4, 255, 0, 0, 255)),
                new(2, 2, 1, 1, 2, 10, 2, 0, Fill(2, 2, 0, 0, 255, 255)),
                new(1, 1, 0, 0, 3, 0, 0, 0, Fill(1, 1, 0, 255, 0, 255)),
            },
        };

        foreach (IReadOnlyList<Spec> frames in cases)
        {
            ImageSequence sequence = new PngImageCodec().DecodeAnimation(PngFormatBuilder.BuildApng(4, 4, numPlays: 5, frames));
            Assert.Equal(4, sequence.Width, "APNG width");
            Assert.Equal(4, sequence.Height, "APNG height");
            Assert.Equal(5, sequence.LoopCount, "APNG loop count");
            Assert.Equal(frames.Count, sequence.Frames.Count, "APNG frame count");
        }

        return ValueTask.CompletedTask;
    }

    private static ValueTask ApngEncodeRoundTrips()
    {
        ImageSequence mediaSequence = MakeSequence(6, 5, loop: 3, (1, 10), (2, 10), (5, 100));
        var codec = new PngImageCodec();
        byte[] first = codec.EncodeAnimation(mediaSequence);
        byte[] second = codec.EncodeAnimation(mediaSequence);

        Assert.BytesEqual(first, second, "APNG encoder should be deterministic.");
        CompareSequences(mediaSequence, codec.DecodeAnimation(first));
        return ValueTask.CompletedTask;
    }

    private static ValueTask ProgressiveJpegDecodes()
    {
        byte[] jpeg = Convert.FromBase64String(ProgressiveGradientBase64);
        ImageSequence sequence = DecodeStillViaCatalog(jpeg);
        Assert.False(sequence.IsAnimated, "Progressive JPEG should decode as a still image.");
        Assert.True(sequence.Width > 0, "Progressive JPEG width");
        Assert.True(sequence.Height > 0, "Progressive JPEG height");
        return ValueTask.CompletedTask;
    }

    private static ValueTask MalformedPngRejected()
    {
        byte[] png = new PngImageCodec().Encode(MakeGradient(8, 8));
        png[png.Length - 6] ^= 0xFF;

        Assert.Throws<FormatException>(() => new PngImageCodec().Decode(png));
        return ValueTask.CompletedTask;
    }

    private static async ValueTask EncodedInputLimitEnforced()
    {
        byte[] png = new PngImageCodec().Encode(MakeGradient(8, 8));
        using var input = new MediaInput(new MemoryStream(png), leaveOpen: false);
        var options = new ImageDecodeOptions(new MediaLimits(maxEncodedBytes: png.Length - 1));

        await Assert.ThrowsAsync<MediaException>(
            () => new PngImageCodec().DecodeAsync(input, options).AsTask()).ConfigureAwait(false);
    }

    private static async ValueTask StillCodecsRejectAnimatedEncode()
    {
        ImageSequence animated = MakeSequence(2, 2, loop: 1, (1, 10), (1, 10));

        await Assert.ThrowsAsync<NotSupportedException>(
            () => new JpegImageCodec().EncodeAsync(animated, Stream.Null, new ImageEncodeOptions(ImageEncodeFormat.Jpeg)).AsTask()).ConfigureAwait(false);
        await Assert.ThrowsAsync<NotSupportedException>(
            () => new BmpImageCodec().EncodeAsync(animated, Stream.Null, new ImageEncodeOptions(ImageEncodeFormat.Bmp)).AsTask()).ConfigureAwait(false);
    }

    private static ValueTask RuntimeHasNoGraphicsReference()
    {
        string root = FindMediaRoot();
        string runtimeRoot = Path.Combine(root, "Broiler.Media.Image.Managed");
        foreach (string file in Directory.EnumerateFiles(runtimeRoot, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            Assert.DoesNotContain("Broiler.Graphics", text, file);
            Assert.DoesNotContain("Gfx.", text, file);
        }

        return ValueTask.CompletedTask;
    }

    private static async ValueTask<MediaCodecMatch?> SelectAsync(MediaCodecCatalog catalog, byte[] data)
    {
        using var input = new MediaInput(new MemoryStream(data), leaveOpen: false);
        return await catalog.SelectAsync(MediaKind.Image, input).ConfigureAwait(false);
    }

    private static void CompareStillDecode(byte[] encoded, ImageBuffer? expected = null)
    {
        ImageSequence sequence = DecodeStillViaCatalog(encoded);

        Assert.False(sequence.IsAnimated, "Still decode should produce a single frame.");
        if (expected is not null)
            ComparePixels(expected, sequence.FirstFrame);
    }

    private static ImageSequence DecodeStillViaCatalog(byte[] encoded)
    {
        var catalog = new MediaCodecCatalog(ManagedImageCodecs.CreateCodecs());
        using var input = new MediaInput(new MemoryStream(encoded), leaveOpen: false);
        ImageCodec codec = (ImageCodec)catalog.SelectAsync(MediaKind.Image, input).AsTask().GetAwaiter().GetResult()!.Codec;

        using var decodeInput = new MediaInput(new MemoryStream(encoded), leaveOpen: false);
        return codec.DecodeAsync(decodeInput).AsTask().GetAwaiter().GetResult();
    }

    private static void CompareSequences(ImageSequence expected, ImageSequence actual)
    {
        Assert.Equal(expected.Width, actual.Width, "animation width");
        Assert.Equal(expected.Height, actual.Height, "animation height");
        Assert.Equal(expected.LoopCount, actual.LoopCount, "animation loop count");
        Assert.Equal(expected.Frames.Count, actual.Frames.Count, "animation frame count");

        for (int i = 0; i < expected.Frames.Count; i++)
        {
            ImageFrame expectedFrame = expected.Frames[i];
            ImageFrame actualFrame = actual.Frames[i];
            Assert.Equal(expectedFrame.DelayNumerator, actualFrame.DelayNumerator, $"frame {i} delay numerator");
            Assert.Equal(expectedFrame.DelayDenominator, actualFrame.DelayDenominator, $"frame {i} delay denominator");
            ComparePixels(expectedFrame.Pixels, actualFrame.Pixels);
        }
    }

    /// <summary>
    /// The exit gate for multithreading items #6 and #7: decoded output byte-identical to the
    /// sequential decoder. The comparison is made at four thread settings over four fixtures,
    /// including the two shapes where "parallel over rows" means something unusual — an
    /// interlaced PNG, whose seven Adam7 passes each write a strided subset of the image, and a
    /// progressive JPEG, which reconstructs from coefficients several scans contributed to.
    /// </summary>
    /// <remarks>
    /// The fixtures are deliberately larger than the decoders' minimum band size. Below it every
    /// setting runs inline and the test would pass without exercising a single thread — which is
    /// exactly the way a concurrency test quietly stops testing anything.
    /// </remarks>
    private static ValueTask ParallelDecodeMatchesSequential()
    {
        ImageBuffer source = MakeGradient(512, 512);
        (string Name, byte[] Bytes)[] fixtures =
        [
            ("png", new PngImageCodec().Encode(source)),
            ("png interlaced", InterlacedGradient(512, 512)),
            ("jpeg", new JpegImageCodec().Encode(source, quality: 90)),
            ("jpeg progressive", Convert.FromBase64String(ProgressiveGradientBase64)),
        ];

        int configured = ImageDecodeParallelism.MaxDegreeOfParallelism;
        try
        {
            foreach ((string name, byte[] bytes) in fixtures)
            {
                ImageDecodeParallelism.MaxDegreeOfParallelism = 1;
                ImageBuffer sequential = Decode(bytes);

                foreach (int threads in new[] { 2, 4, 8 })
                {
                    ImageDecodeParallelism.MaxDegreeOfParallelism = threads;
                    ImageBuffer parallel = Decode(bytes);
                    Assert.Equal(sequential.Width, parallel.Width, $"{name} width at {threads} threads");
                    Assert.Equal(sequential.Height, parallel.Height, $"{name} height at {threads} threads");
                    Assert.BytesEqual(sequential.Rgba, parallel.Rgba, $"{name} pixels at {threads} threads");
                }
            }
        }
        finally
        {
            ImageDecodeParallelism.MaxDegreeOfParallelism = configured;
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Item #8 decodes several of a page's images at once, which only works if a decoder holds no
    /// state between calls. Runs the same four fixtures from eight threads simultaneously and
    /// requires every result to equal the single-threaded one — so a shared buffer or a lazily
    /// published table would show up as wrong pixels, not merely as a crash.
    /// </summary>
    private static async ValueTask DecodersAreReentrant()
    {
        ImageBuffer source = MakeGradient(192, 192);
        byte[][] fixtures =
        [
            new PngImageCodec().Encode(source),
            InterlacedGradient(192, 192),
            new JpegImageCodec().Encode(source, quality: 85),
            Convert.FromBase64String(ProgressiveGradientBase64),
        ];

        byte[][] expected = new byte[fixtures.Length][];
        for (int i = 0; i < fixtures.Length; i++)
            expected[i] = Decode(fixtures[i]).Rgba;

        var workers = new List<Task>();
        for (int worker = 0; worker < 8; worker++)
        {
            workers.Add(Task.Run(() =>
            {
                for (int repeat = 0; repeat < 3; repeat++)
                    for (int i = 0; i < fixtures.Length; i++)
                        Assert.BytesEqual(expected[i], Decode(fixtures[i]).Rgba, $"concurrent decode of fixture {i}");
            }));
        }

        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    /// <summary>An Adam7-interlaced PNG of the shared gradient, at the given size.</summary>
    private static byte[] InterlacedGradient(int width, int height)
    {
        ImageBuffer source = MakeGradient(width, height);
        return PngFormatBuilder.BuildInterlacedRgba(width, height, source.Rgba);
    }

    /// <summary>Decodes through the codec catalog's signature dispatch, as the render path does.</summary>
    private static ImageBuffer Decode(byte[] bytes) =>
        PngDecoder.IsPng(bytes)
            ? new PngImageCodec().Decode(bytes)
            : new JpegImageCodec().Decode(bytes);

    private static void ComparePixels(ImageBuffer expected, ImageBuffer actual)
    {
        Assert.Equal(expected.Width, actual.Width, "width");
        Assert.Equal(expected.Height, actual.Height, "height");
        Assert.Equal(expected.Rgba.Length, actual.Rgba.Length, "rgba length");
        Assert.BytesEqual(expected.Rgba, actual.Rgba, "rgba");
    }

    private static ImageBuffer MakeGradient(int width, int height)
    {
        byte[] rgba = new byte[width * height * 4];
        int i = 0;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            rgba[i++] = (byte)(x * 7 + 1);
            rgba[i++] = (byte)(y * 11 + 2);
            rgba[i++] = (byte)((x ^ y) * 13 + 3);
            rgba[i++] = (byte)(255 - ((x + y) & 0xFF));
        }

        return new ImageBuffer(width, height, rgba);
    }

    private static ImageSequence MakeSequence(int width, int height, int loop, params (int Numerator, int Denominator)[] delays)
    {
        var frames = new List<ImageFrame>();
        for (int frameIndex = 0; frameIndex < delays.Length; frameIndex++)
        {
            byte[] rgba = new byte[width * height * 4];
            for (int i = 0; i < width * height; i++)
            {
                rgba[i * 4] = (byte)(i * 3 + frameIndex * 40);
                rgba[i * 4 + 1] = (byte)(i * 5 + frameIndex * 20);
                rgba[i * 4 + 2] = (byte)(frameIndex * 60 + i);
                rgba[i * 4 + 3] = (byte)(200 + frameIndex * 10);
            }

            frames.Add(new ImageFrame(new ImageBuffer(width, height, rgba), delays[frameIndex].Numerator, delays[frameIndex].Denominator));
        }

        return new ImageSequence(frames, width, height, loop);
    }

    private static byte[] Fill(int width, int height, byte r, byte g, byte b, byte a)
    {
        byte[] rgba = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            rgba[i * 4] = r;
            rgba[i * 4 + 1] = g;
            rgba[i * 4 + 2] = b;
            rgba[i * 4 + 3] = a;
        }

        return rgba;
    }

    private static ImageBuffer FillImage(int width, int height, byte r, byte g, byte b, byte a) =>
        new(width, height, Fill(width, height, r, g, b, a));

    private static string FindMediaRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Broiler.Media.slnx")))
                return Path.Combine(directory.FullName, "src");

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Broiler.Media component root.");
    }
}

internal sealed class AssertException(string message) : Exception(message);

internal static class Assert
{
    public static void True(bool condition, string message = "Expected true.")
    {
        if (!condition)
            throw new AssertException(message);
    }

    public static void False(bool condition, string message = "Expected false.") => True(!condition, message);

    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new AssertException(message ?? $"Expected <{expected}>, but was <{actual}>.");
    }

    public static void BytesEqual(IReadOnlyList<byte> expected, IReadOnlyList<byte> actual, string? message = null)
    {
        if (expected.Count != actual.Count)
            throw new AssertException(message ?? $"Expected {expected.Count} byte(s), got {actual.Count}.");

        for (int i = 0; i < expected.Count; i++)
        {
            if (expected[i] != actual[i])
                throw new AssertException(message ?? $"Byte {i} differs: expected {expected[i]}, got {actual[i]}.");
        }
    }

    public static void DoesNotContain(string unexpected, string actual, string? message = null)
    {
        if (actual.Contains(unexpected, StringComparison.Ordinal))
            throw new AssertException(message ?? $"Expected text not to contain '{unexpected}'.");
    }

    public static void Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        catch (Exception ex)
        {
            throw new AssertException($"Expected {typeof(TException).Name}, got {ex.GetType().Name}.");
        }

        throw new AssertException($"Expected {typeof(TException).Name}, but no exception was thrown.");
    }

    public static async ValueTask ThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (TException)
        {
            return;
        }
        catch (Exception ex)
        {
            throw new AssertException($"Expected {typeof(TException).Name}, got {ex.GetType().Name}.");
        }

        throw new AssertException($"Expected {typeof(TException).Name}, but no exception was thrown.");
    }
}
