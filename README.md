# Broiler.Media

Broiler.Media is the decode-first media component for Broiler. It owns image, audio,
and video **decoding**, format **probing**, and codec **selection**, behind small
abstraction assemblies with one concrete implementation assembly per media kind.
Rendering, windowing, networking, and HTML media-element behaviour deliberately live
outside this component.

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
  state machine) lives in the application/HTML layer — see `src/Broiler.Playback` — never in
  this component.

## Security and reliability

All media is untrusted input. Decoders enforce configurable `MediaLimits` (encoded byte count,
image/video dimensions and pixel/frame counts, audio channels/sample-rate/duration, probe bytes
and time, queued/decoded memory) and use checked arithmetic for dimensions, strides, and
allocation sizes. Malformed data produces a bounded `MediaException` carrying a `MediaError`
(codec id and byte offset where safe) — never unbounded allocation, hangs, silent partial
success, or arbitrary exception leakage. See
[ADR 0002](docs/adr/0002-buffer-ownership-and-limits.md) for buffer ownership
and limits.

## Packaging

Packages are published per assembly with lockstep suite versioning during preview
(`0.1.0-preview.1`), Apache-2.0 licensed, with symbol packages (`.snupkg`) and SourceLink.
Metadata is vendored from `eng/Broiler.Packaging.props` so each component packs standalone.
`Broiler.Media.Video.MediaFoundation` targets `net10.0-windows`; the rest are `net10.0` and
platform-neutral.

## Design records

- [Current roadmap](docs/roadmap.md)
- [ADR index](docs/adr/README.md)
