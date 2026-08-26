# Broiler.Media

[![License: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](https://github.com/Broiler-Platform/Broiler.Media/blob/main/LICENSE)

Broiler.Media is the decode-first media component for Broiler. It owns image, audio,
and video **decoding**, format **probing**, and codec **selection**, behind small
abstraction assemblies with one concrete implementation assembly per media kind.
Rendering, windowing, networking, and HTML media-element behaviour deliberately live
outside this component.

> **Preview release.** `0.1.0-preview.1` is the first published preview. Public names,
> XML documentation, and the `MediaLimits`/pixel-format contracts are not frozen yet
> and may change before `1.0`. See the
> [roadmap](https://github.com/Broiler-Platform/Broiler.Media/blob/main/docs/roadmap.md)
> for what is still open.

## Installation

Preview packages need an explicit prerelease opt-in:

```bash
dotnet add package Broiler.Media.All --prerelease
```

`Broiler.Media.All` is a dependencies-only meta-package covering the whole
cross-platform stack. To take only what you need, reference the individual
packages instead — for example, image decoding alone:

```bash
dotnet add package Broiler.Media.Image.Managed --prerelease
```

The Windows-only video backend is a separate package and is **not** pulled in by
the meta-package:

```bash
dotnet add package Broiler.Media.Video.MediaFoundation --prerelease
```

All packages target `net10.0`, except `Broiler.Media.Video.MediaFoundation`, which
targets `net10.0-windows`.

## Assemblies

| Assembly | Role |
| --- | --- |
| `Broiler.Media` | Shared base: `MediaCodec`, the immutable `MediaCodecCatalog`, probing, `MediaInput`, limits, diagnostics, and the base output lifecycle. |
| `Broiler.Media.Audio` | Audio abstraction: `AudioCodec`, `AudioBuffer`, `AudioStreamInfo`, `IAudioOutput`. |
| `Broiler.Media.Audio.Managed` | Managed audio decoders (RIFF/WAVE PCM). |
| `Broiler.Media.Video` | Video abstraction: `VideoCodec`, `IVideoSession`, `IVideoOutput`, session state/events. |
| `Broiler.Media.Video.MediaFoundation` | Windows-only video via `IMFMediaEngine`, presenting to an HWND owned by `Broiler.Graphics.Windows`. |
| `Broiler.Media.Image` | Image abstraction: `ImageCodec`, `ImageBuffer`, `ImageFrame`, `ImageSequence`. |
| `Broiler.Media.Image.Managed` | Managed image codecs (PNG/APNG, JPEG, BMP, GIF, WebP). |

Each runtime assembly ships as its own NuGet package; applications opt into the media
kinds and implementations they need. `Broiler.Media` is the base, not an everything-bundle.

One additional package ships no assembly of its own:

| Package | Role |
| --- | --- |
| `Broiler.Media.All` | Dependencies-only meta-package over the six cross-platform assemblies. Platform-native backends stay separate. |

### Dependency direction

```text
Broiler.Media.Audio.Managed          -> Broiler.Media.Audio -> Broiler.Media
Broiler.Media.Video.MediaFoundation  -> Broiler.Media.Video -> Broiler.Media
Broiler.Media.Video.MediaFoundation  -> Broiler.Graphics.Windows   (borrows the HWND video target only)
Broiler.Media.Image.Managed          -> Broiler.Media.Image -> Broiler.Media

Broiler.Graphics                     -> Broiler.Media.Image          (abstraction only)
Broiler.Graphics.Windows             -> Broiler.Media.Video          (declares the HWND video target)
```

The abstraction assemblies are platform-neutral, safe-code, trimming- and AOT-friendly,
and reference no implementation, no Graphics/HTML, and no native/Media Foundation package.

## Supported formats

| Kind | Format | Decode | Encode | Notes | Assembly |
| --- | --- | :---: | :---: | --- | --- |
| Image | PNG / APNG | ✅ | ✅ | animation (frame blend/dispose, loop) | `.Image.Managed` |
| Image | JPEG | ✅ | ✅ | baseline + progressive decode; baseline encode | `.Image.Managed` |
| Image | BMP | ✅ | ✅ | 24/32-bit decode; 32-bit encode | `.Image.Managed` |
| Image | GIF | ✅ | ✅ | animation | `.Image.Managed` |
| Image | WebP | ✅ | ✅ | lossless + animation | `.Image.Managed` |
| Audio | RIFF/WAVE PCM | ✅ | — | streaming; 8/16/24/32-bit PCM + IEEE float | `.Audio.Managed` |
| Video | MP4 (H.264/AAC) | ✅ | — | Windows-only, direct `IMFMediaEngine` presentation to an HWND | `.Video.MediaFoundation` |

Additional audio codecs (MP3/AAC/Vorbis/Opus/FLAC) and non-Media-Foundation
video providers are future work; the stack reports a deterministic capability
error for formats it does not support rather than a misleading placeholder.

## Selecting and using a codec

Codec selection is explicit — there is **no** process-wide mutable `Current` singleton and no
module-initializer side effects. The application composition root builds one immutable catalog
and reuses it:

```csharp
var catalog = new MediaCodecCatalog(ManagedImageCodecs.CreateCodecs());   // or audio/video codecs
using var input = new MediaInput(stream, new MediaSourceHints(mimeType: "image/png"));
MediaCodecMatch? match = await catalog.SelectAsync(MediaKind.Image, input);
if (match?.Codec is ImageCodec codec)
    ImageSequence decoded = await codec.DecodeAsync(input);
```

Selection is content-probe first: MIME type, file extension, and URL are hints only.

Consumers register the codec set at their own composition root:

- **Graphics** decodes images through an injected catalog via `Broiler.Graphics.BImageCodecs.Use(...)`
  (Graphics references only `Broiler.Media.Image`, never the implementation).
- **Browser/app playback** (the `<audio>`/`<video>` playback clock, transport, and element
  state machine) lives in the Broiler HTML/application component — never in this one.

## Security and reliability

All media is untrusted input. Decoders enforce configurable `MediaLimits` (encoded byte count,
image/video dimensions and pixel/frame counts, audio channels/sample-rate/duration, probe bytes
and time, queued/decoded memory) and use checked arithmetic for dimensions, strides, and
allocation sizes. Malformed data produces a bounded `MediaException` carrying a `MediaError`
(codec id and byte offset where safe) — never unbounded allocation, hangs, silent partial
success, or arbitrary exception leakage. See
[ADR 0002](https://github.com/Broiler-Platform/Broiler.Media/blob/main/docs/adr/0002-buffer-ownership-and-limits.md)
for buffer ownership and limits.

## Repository layout

```text
src/                     runtime assemblies, one directory per package
src/tests/               one self-hosted test runner executable per assembly
eng/                     vendored packaging metadata and package icon
docs/                    roadmap and architecture decision records
Broiler.Media.slnx       solution over every project in src/ and src/tests/
```

## Building and testing

The solution defines six configurations. `Debug`/`Release` are the plain host builds;
the `-Linux` and `-Windows` variants additionally define a `LINUX`/`WINDOWS` compilation
symbol and gate the platform-specific projects.

```bash
dotnet build Broiler.Media.slnx -c Release-Linux
```

`Broiler.Media.Video.MediaFoundation` and its test runner build only under
`Debug-Windows`/`Release-Windows`; every other configuration excludes them.

Tests are self-hosted console runners rather than a test framework, so run each
executable directly — for example:

```bash
dotnet run --project src/tests/Broiler.Media.Image.Managed.Tests -c Release-Linux
```

To produce the packages locally:

```bash
dotnet pack Broiler.Media.slnx -c Release-Linux -o ./artifacts
```

## Packaging

Packages are published per assembly with lockstep suite versioning during preview
(`0.1.0-preview.1`), Apache-2.0 licensed, with symbol packages (`.snupkg`) and SourceLink.
Metadata is vendored from `eng/Broiler.Packaging.props` so each component packs standalone;
component-specific overrides live in `Directory.Build.props` and win over those defaults.
`Broiler.Media.Video.MediaFoundation` targets `net10.0-windows`; the rest are `net10.0` and
platform-neutral.

## Design records

- [Current roadmap](https://github.com/Broiler-Platform/Broiler.Media/blob/main/docs/roadmap.md)
- [ADR index](https://github.com/Broiler-Platform/Broiler.Media/blob/main/docs/adr/README.md)

## License

Apache-2.0. See [LICENSE](https://github.com/Broiler-Platform/Broiler.Media/blob/main/LICENSE).
