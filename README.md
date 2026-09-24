# Broiler.Media

[![CI](https://github.com/Broiler-Platform/Broiler.Media/actions/workflows/ci.yml/badge.svg)](https://github.com/Broiler-Platform/Broiler.Media/actions/workflows/ci.yml)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](https://github.com/Broiler-Platform/Broiler.Media/blob/main/LICENSE)

Broiler.Media is the decode-first media component for Broiler. It owns image, audio,
and video **decoding**, format **probing**, and codec **selection**, behind small
abstraction assemblies with one concrete implementation assembly per media kind.
Rendering, windowing, networking, and HTML media-element behaviour deliberately live
outside this component.

> **Preview release.** Packages are published to [NuGet.org](https://www.nuget.org/packages?q=Broiler.Media).
> Public contracts and `MediaLimits`/pixel-format APIs are in active preview. See the
> [roadmap](https://github.com/Broiler-Platform/Broiler.Media/blob/main/docs/roadmap.md)
> for planned roadmap items.

---

## Installation

Preview packages are hosted on NuGet.org and require an explicit prerelease flag:

```bash
dotnet add package Broiler.Media.All --prerelease
```

`Broiler.Media.All` is a dependencies-only meta-package covering all eight runtime
assemblies, including the Windows video contracts and Media Foundation backend.
To take only what you need, reference individual packages:

### Image decoding only
```bash
dotnet add package Broiler.Media.Image.Managed --prerelease
```

### Audio decoding only
```bash
dotnet add package Broiler.Media.Audio.Managed --prerelease
```

### Windows Media Foundation video only
```bash
dotnet add package Broiler.Media.Video.MediaFoundation --prerelease
```

All packages target `net10.0` and can be restored and built on both Linux and Windows.
Media Foundation video playback requires Windows; its public APIs carry platform annotations.

---

## Assemblies and Packages

| Package | Assembly | Role |
| --- | --- | --- |
| `Broiler.Media` | `Broiler.Media` | Shared base: `MediaCodec`, the immutable `MediaCodecCatalog`, format probing, `MediaInput`, `MediaLimits`, error reporting, and output lifecycles. |
| `Broiler.Media.Audio` | `Broiler.Media.Audio` | Audio abstractions: `AudioCodec`, `AudioBuffer`, `AudioStreamInfo`, `IAudioOutput`. |
| `Broiler.Media.Audio.Managed` | `Broiler.Media.Audio.Managed` | Managed audio decoders (RIFF/WAVE PCM 8/16/24/32-bit & float). |
| `Broiler.Media.Video` | `Broiler.Media.Video` | Video abstractions: `VideoCodec`, `IVideoSession`, `IVideoOutput`, session states and events. |
| `Broiler.Media.Video.Windows` | `Broiler.Media.Video.Windows` | Borrowed-HWND presentation contracts, including `IHwndVideoOutput`. |
| `Broiler.Media.Video.MediaFoundation` | `Broiler.Media.Video.MediaFoundation` | Windows-only video playback via `IMFMediaEngine`, presenting directly to a borrowed HWND. |
| `Broiler.Media.Image` | `Broiler.Media.Image` | Image abstractions: `ImageCodec`, `ImageBuffer`, `ImageFrame`, `ImageSequence`. |
| `Broiler.Media.Image.Managed` | `Broiler.Media.Image.Managed` | Managed image codecs (PNG/APNG, JPEG, BMP, GIF, WebP, JBIG2, JPEG 2000). |
| `Broiler.Media.All` | *(none)* | Meta-package aggregating all eight runtime packages above. |

### Dependency Direction

```text
Broiler.Media.Audio.Managed          -> Broiler.Media.Audio -> Broiler.Media
Broiler.Media.Video.MediaFoundation  -> Broiler.Media.Video -> Broiler.Media
Broiler.Media.Video.MediaFoundation  -> Broiler.Media.Video.Windows -> Broiler.Media.Video
Broiler.Media.Image.Managed          -> Broiler.Media.Image -> Broiler.Media

Broiler.Graphics                     -> Broiler.Media.Image          (abstractions only)
Broiler.Graphics.Windows             -> Broiler.Media.Video.Windows  (implements IHwndVideoOutput)
```

The abstraction assemblies (`Broiler.Media`, `Broiler.Media.Audio`, `Broiler.Media.Video`, `Broiler.Media.Image`, `Broiler.Media.Video.Windows`) are platform-neutral, safe-code only, trimming- and NativeAOT-compatible, and carry zero dependencies on Graphics, HTML, or native runtimes.

---

## Supported Formats

| Kind | Format | Decode | Encode | Notes | Package |
| --- | --- | :---: | :---: | --- | --- |
| **Image** | PNG / APNG | ✅ | ✅ | Full animation (frame blend, dispose, loop count), Adam7 interlacing, transparency, palette, grayscale. | `Broiler.Media.Image.Managed` |
| **Image** | JPEG | ✅ | ✅ | Baseline and progressive decode; baseline encode; RGB/YCbCr color transforms. | `Broiler.Media.Image.Managed` |
| **Image** | BMP | ✅ | ✅ | 24-bit and 32-bit decode; 32-bit encode. | `Broiler.Media.Image.Managed` |
| **Image** | GIF | ✅ | ✅ | Multi-frame animation, color palette, transparency. | `Broiler.Media.Image.Managed` |
| **Image** | WebP | ✅ | ✅ | Lossless decode/encode and animation. Lossy VP8 decode uses Windows platform codecs when present. | `Broiler.Media.Image.Managed` |
| **Image** | JBIG2 | ✅ | — | 1-bit bi-level image decode (standard ITU-T T.88 / ISO 14492). | `Broiler.Media.Image.Managed` |
| **Image** | JPEG 2000 (JPX) | ✅ | — | Wavelet-based image decode (`.jp2`, `.j2k`, `.jpx`, ITU-T T.800 / ISO 15444-1). | `Broiler.Media.Image.Managed` |
| **Audio** | RIFF/WAVE PCM | ✅ | — | Streaming decode for 8, 16, 24, 32-bit integer PCM and 32-bit IEEE float. | `Broiler.Media.Audio.Managed` |
| **Video** | MP4 (H.264 / AAC) | ✅ | — | Windows-only hardware-accelerated playback via `IMFMediaEngine` presenting to a borrowed HWND. | `Broiler.Media.Video.MediaFoundation` |

---

## Usage Guide

Codec selection is explicit: there is **no** mutable global `Current` singleton and **no** module-initializer side effects. Applications build a `MediaCodecCatalog` at their composition root.

### 1. Image Decoding and Inspection

```csharp
using Broiler.Media;
using Broiler.Media.Image;
using Broiler.Media.Image.Managed;

// Build catalog with managed image codecs
var catalog = new MediaCodecCatalog(ManagedImageCodecs.CreateCodecs());

// Provide input stream with optional hints
await using var fileStream = File.OpenRead("photo.jpg");
using var input = new MediaInput(fileStream, new MediaSourceHints(mimeType: "image/jpeg"));

// Select codec using content-first probing
MediaCodecMatch? match = await catalog.SelectAsync(MediaKind.Image, input);
if (match?.Codec is ImageCodec codec)
{
    // Decode image (still or animated sequence)
    ImageSequence sequence = await codec.DecodeAsync(input);

    foreach (ImageFrame frame in sequence.Frames)
    {
        ImageBuffer buffer = frame.Buffer;
        Console.WriteLine($"Decoded frame: {buffer.Width}x{buffer.Height}, format: {buffer.PixelFormat}");

        // Pixel data is exposed as RGBA8 memory/span
        ReadOnlyMemory<byte> pixels = buffer.Pixels;
        ReadOnlySpan<byte> pixelSpan = buffer.Pixels.Span;
    }
}
```

### 2. Audio Decoding

```csharp
using Broiler.Media;
using Broiler.Media.Audio;
using Broiler.Media.Audio.Managed;

var catalog = new MediaCodecCatalog(ManagedAudioCodecs.CreateCodecs());

await using var audioStream = File.OpenRead("sound.wav");
using var input = new MediaInput(audioStream, new MediaSourceHints(mimeType: "audio/wav"));

MediaCodecMatch? match = await catalog.SelectAsync(MediaKind.Audio, input);
if (match?.Codec is AudioCodec audioCodec)
{
    // Implement IAudioOutput to receive decoded PCM buffers with backpressure
    var output = new CustomAudioOutput();
    await audioCodec.DecodeAsync(input, output);
}
```

### 3. Video Playback (Windows Media Foundation)

```csharp
using Broiler.Media;
using Broiler.Media.Video;
using Broiler.Media.Video.Windows;
using Broiler.Media.Video.MediaFoundation;

// On Windows, initialize Media Foundation video codec
var videoCodec = new MediaFoundationVideoCodec();

// Provide an implementation of IHwndVideoOutput (e.g. from your window/UI layer)
IHwndVideoOutput videoOutput = myWindow.GetVideoOutput();

// Open session and control playback
await using IVideoSession session = await videoCodec.OpenSessionAsync(
    videoOutput,
    new VideoSessionOptions(autoplay: false, muted: false));

session.StateChanged += (s, e) =>
{
    Console.WriteLine($"Video state: {e.Kind}");
};

VideoStreamInfo info = await session.LoadAsync("https://example.com/clip.mp4");
Console.WriteLine($"Video size: {info.DisplayWidth}x{info.DisplayHeight}, duration: {info.Duration}");

await session.PlayAsync();
```

> **Thread Safety & COM Lifetime:** Each Media Foundation session runs its engine on a dedicated COM worker thread. Callbacks and state changes are delivered asynchronously. UI applications must marshal presentation callbacks to their UI thread dispatcher.

---

## Security and Limits

All media inputs are treated as untrusted data:
- Decoders enforce configurable `MediaLimits` (maximum encoded bytes, image dimensions, pixel counts, audio channels, sample rates, durations, and memory consumption).
- Checked arithmetic prevents integer overflows on strides and dimensions.
- Malformed inputs throw structured `MediaException` with bounded error codes (`MediaError`), avoiding hangs, excessive allocations, or silent corruption.
- Details: [ADR 0002: Buffer Ownership and Limits](docs/adr/0002-buffer-ownership-and-limits.md).

---

## Developer Guide

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (`10.0.x`)
- [Node.js](https://nodejs.org/) (version 24 for release verification scripts)
- PowerShell (PowerShell 7 `pwsh` or Windows PowerShell 5.1)
- Bash (Linux bash or Git Bash on Windows)

### Restoring and Building

All dependencies, including `Broiler.Native.Windows` (`0.1.0-preview.6`), are restored directly from **NuGet.org**. No private package feeds, tokens, or credentials are required.

```bash
git clone https://github.com/Broiler-Platform/Broiler.Media.git
cd Broiler.Media

# Build the solution in Release configuration
dotnet build Broiler.Media.slnx -c Release
```

The solution contains two configurations: `Debug` and `Release`. Both build cleanly with zero warnings (`TreatWarningsAsErrors=true`).

### Running Tests

Tests are self-hosted console runner executables under `src/tests/`. Run all test suites for the current platform:

```bash
# On Linux/macOS or Git Bash on Windows:
./eng/run-tests.sh Release
```

To run a specific test suite individually:

```bash
dotnet run --project src/tests/Broiler.Media.Image.Managed.Tests -c Release
dotnet run --project src/tests/Broiler.Media.Audio.Managed.Tests -c Release
dotnet run --project src/tests/Broiler.Media.Video.MediaFoundation.Tests -c Release
```

Run release script tests:

```bash
node --test eng/resolve-preview-version.test.mjs eng/select-restore-feed.test.mjs
```

### Packaging

To build and validate all 9 packages and 8 symbol packages locally:

```powershell
pwsh -File eng/pack.ps1
# or on Windows PowerShell:
powershell -File eng/pack.ps1
```

`eng/pack.ps1` compiles and packages all packable projects to `artifacts/`, verifying:
- Identical package versions across all projects.
- Required assets (`README.md`, `icon.png`, XML API documentation).
- Valid assembly manifests and dependencies.
- Matching symbol packages (`.snupkg`).

---

## CI/CD and Publishing

- **CI Workflow (`.github/workflows/ci.yml`):**
  - Runs on pull requests, pushes to `main`, and manual dispatch.
  - Builds and tests `Release` configuration on Ubuntu and Windows.
  - Validates feed dependencies against NuGet.org.
  - Packs and verifies all 9 packages on Windows, archiving them as `nuget-packages`.
- **Publish Workflow (`.github/workflows/publish.yml`):**
  - Targets **NuGet.org** exclusively.
  - Automatically queries NuGet.org to resolve the next cumulative preview version number.
  - Invokes CI to build and pack with the resolved version.
  - Performs a clean consumer restore test using `eng/verify-feed.ps1 -Target nuget` with an isolated cache.
  - Pushes validated `.nupkg` and `.snupkg` artifacts to NuGet.org using the `NUGET_TOKEN` secret.
  - Manual dispatches default to a dry run (`dry-run: true`).

---

## Architecture Decision Records (ADRs)

- [ADR Index](docs/adr/README.md)
- [ADR 0001: Component Topology and Consumption Policy](docs/adr/0001-component-topology-and-consumption-policy.md)
- [ADR 0002: Buffer Ownership and Limits](docs/adr/0002-buffer-ownership-and-limits.md)
- [ADR 0003: Image Pixel and Alpha Format](docs/adr/0003-image-pixel-and-alpha-format.md)
- [ADR 0004: Graphics Compatibility Window (superseded)](docs/adr/0004-compatibility-window.md)
- [ADR 0005: Windows Media Foundation Borrowed HWND](docs/adr/0005-windows-media-foundation-borrowed-hwnd.md)
- [ADR 0006: No Graphics Dependency](docs/adr/0006-no-graphics-dependency.md)

---

## License

This project is licensed under the [Apache-2.0 License](LICENSE).
