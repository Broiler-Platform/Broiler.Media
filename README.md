# Broiler.Media

[![CI](https://github.com/Broiler-Platform/Broiler.Media/actions/workflows/ci.yml/badge.svg)](https://github.com/Broiler-Platform/Broiler.Media/actions/workflows/ci.yml)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](https://github.com/Broiler-Platform/Broiler.Media/blob/main/LICENSE)

Broiler.Media is the decode-first media component for Broiler. It owns image, audio,
and video **decoding**, format **probing**, and codec **selection**, behind small
abstraction assemblies with one concrete implementation assembly per media kind.
Rendering, windowing, networking, and HTML media-element behaviour deliberately live
outside this component.

> **Preview release.** The next release starts at `0.1.0-preview.7`. Public names,
> XML documentation, and the `MediaLimits`/pixel-format contracts are not frozen yet
> and may change before `1.0`. See the
> [roadmap](https://github.com/Broiler-Platform/Broiler.Media/blob/main/docs/roadmap.md)
> for what is still open.

## Installation

Preview packages need an explicit prerelease opt-in:

```bash
dotnet add package Broiler.Media.All --prerelease
```

`Broiler.Media.All` is a dependencies-only meta-package covering all eight runtime
assemblies, including the Windows video contracts and Media Foundation backend. To take only what you need, reference the individual
packages instead — for example, image decoding alone:

```bash
dotnet add package Broiler.Media.Image.Managed --prerelease
```

The Windows-only video backend can also be referenced individually:

```bash
dotnet add package Broiler.Media.Video.MediaFoundation --prerelease
```

All packages target `net10.0` and can be built on either host. Media Foundation
playback requires Windows; its public APIs carry Windows platform annotations.

Each Media Foundation engine owns a dedicated COM thread. Native notifications and
output lifecycle callbacks are dispatched asynchronously; implementations that touch
UI state must marshal that work to their UI dispatcher. Disposing a session cancels
pending output work and releases the native engine without waiting for application
output callbacks to finish.

## Assemblies

| Assembly | Role |
| --- | --- |
| `Broiler.Media` | Shared base: `MediaCodec`, the immutable `MediaCodecCatalog`, probing, `MediaInput`, limits, diagnostics, and the base output lifecycle. |
| `Broiler.Media.Audio` | Audio abstraction: `AudioCodec`, `AudioBuffer`, `AudioStreamInfo`, `IAudioOutput`. |
| `Broiler.Media.Audio.Managed` | Managed audio decoders (RIFF/WAVE PCM). |
| `Broiler.Media.Video` | Video abstraction: `VideoCodec`, `IVideoSession`, `IVideoOutput`, session state/events. |
| `Broiler.Media.Video.Windows` | Borrowed-HWND presentation contracts, including `IHwndVideoOutput`. |
| `Broiler.Media.Video.MediaFoundation` | Windows-only video via `IMFMediaEngine`, presenting to a borrowed HWND through `IHwndVideoOutput`. |
| `Broiler.Media.Image` | Image abstraction: `ImageCodec`, `ImageBuffer`, `ImageFrame`, `ImageSequence`. |
| `Broiler.Media.Image.Managed` | Managed image codecs (PNG/APNG, JPEG, BMP, GIF, WebP). |

Each runtime assembly ships as its own NuGet package; applications opt into the media
kinds and implementations they need. `Broiler.Media` is the base, not an everything-bundle.

One additional package ships no assembly of its own:

| Package | Role |
| --- | --- |
| `Broiler.Media.All` | Dependencies-only meta-package over all eight runtime assemblies. Media Foundation playback requires Windows. |

### Dependency direction

```text
Broiler.Media.Audio.Managed          -> Broiler.Media.Audio -> Broiler.Media
Broiler.Media.Video.MediaFoundation  -> Broiler.Media.Video -> Broiler.Media
Broiler.Media.Video.MediaFoundation  -> Broiler.Media.Video.Windows -> Broiler.Media.Video
Broiler.Media.Image.Managed          -> Broiler.Media.Image -> Broiler.Media

Broiler.Graphics                     -> Broiler.Media.Image          (abstraction only)
Broiler.Graphics.Windows             -> Broiler.Media.Video.Windows  (implements the borrowed HWND contract)
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
| Image | WebP | ✅ | ✅ | lossless + animation; lossy VP8 decode needs the optional Windows WebP extension and reports a capability error without it | `.Image.Managed` |
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

The repository is standalone and has no Graphics submodule or package dependency.
The application supplies the borrowed HWND through `IHwndVideoOutput`.

WIC and Media Foundation declarations are shared through the
`Broiler.Native.Windows` package, currently `0.1.0-preview.3`. Codec behavior and
Media error mapping remain here. `NuGet.config` restores Native packages from
the Broiler-Platform GitHub Packages feed and other dependencies from NuGet.org.
A sibling Native checkout does not replace these package references.

For local restore, set `NuGetPackageSourceCredentials_github` to
`Username=YOUR_GITHUB_USER;Password=YOUR_TOKEN;ValidAuthenticationTypes=Basic`, using
a personal access token (classic) with `read:packages`. Keep credentials out of
the repository. CI and publishing supply this variable from `GITHUB_TOKEN`.
In the GitHub package settings for **both** `Broiler.Native.Windows` and its
dependency `Broiler.Native`, grant `Broiler-Platform/Broiler.Media` read access
under **Manage Actions access**. Workflow `packages: read` permission alone does
not grant access to another repository's private packages. See
[GitHub's NuGet authentication documentation](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry#authenticating-in-a-github-actions-workflow).

Before releasing Media for consumers using only NuGet.org, publish the required
Native versions there too; selecting the Media publish target does not publish
its dependencies or change the restore feed.

## Building and testing

```bash
git clone https://github.com/Broiler-Platform/Broiler.Media.git
```

The solution uses `Debug` and `Release` configurations.

```bash
dotnet build Broiler.Media.slnx -c Release
```

All runtime libraries and test runners build in both configurations. The test
script runs the Media Foundation suite only on Windows.

Tests are self-hosted console runners rather than a test framework, so there is
nothing for `dotnet test` to discover. After building, run every suite the
configuration enables (pass the same configuration used for the build):

```bash
./eng/run-tests.sh Release
```

Or run one directly:

```bash
dotnet run --project src/tests/Broiler.Media.Image.Managed.Tests -c Release
```

To produce the packages locally:

```bash
dotnet pack Broiler.Media.slnx -c Release -o ./artifacts
```

## Continuous integration and releases

`.github/workflows/ci.yml` builds and runs all seven Media suites in `Release`
on Windows, tests preview version selection, packs all nine packages, and uploads
the packages and symbols as artifacts.

`.github/workflows/publish.yml` builds and tests on Windows before publishing all
nine packages. Run it manually to select GitHub Packages or nuget.org. It defaults
to a dry run that builds and attaches packages without pushing.

Manual publishes select the next unused preview across all nine package IDs.
The configured package version supplies the minimum. The resolver reads NuGet.org
and, when targeting GitHub Packages, that feed too. An optional `preview.N` suffix
must be unused and at least the next preview. Workflow concurrency serializes
publishes; the workflow does not create version reservation tags.

Pushing `v0.1.0-preview.N` publishes that exact preview to NuGet.org, subject to
the same version checks. The current resolver accepts preview releases only.

Publishing to nuget.org needs a `NUGET_API_KEY` repository secret. GitHub Packages
uses the built-in token. Both jobs authenticate to GitHub Packages for Native
restore, regardless of the selected publishing target.

## Packaging

Packages are published per assembly with lockstep versioning, Apache-2.0 licensing,
symbol packages (`.snupkg`), and SourceLink. The meta-package contains dependencies
only and has no symbol package. Shared metadata is vendored from
`eng/Broiler.Packaging.props`; repository overrides, including the preview floor,
live in `Directory.Build.props`. All runtime packages target `net10.0`.

## Design records

- [Current roadmap](https://github.com/Broiler-Platform/Broiler.Media/blob/main/docs/roadmap.md)
- [ADR index](https://github.com/Broiler-Platform/Broiler.Media/blob/main/docs/adr/README.md)

## License

Apache-2.0. See [LICENSE](https://github.com/Broiler-Platform/Broiler.Media/blob/main/LICENSE).
