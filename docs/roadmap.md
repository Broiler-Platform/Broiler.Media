# Broiler.Media Roadmap

**Status:** Active preview. The seven runtime assemblies, managed image codecs,
WAVE PCM decoder, and Windows Media Foundation video session are implemented.
Only current residual work is listed here.

## Contract and conformance freeze

- Revisit ADR 0002 limits/ownership and ADR 0003 pixel-format choices against
  real consumers before freezing public names and XML documentation.
- Add formal allocation and throughput gates for representative image, audio,
  and video workloads. Existing malformed-input/conformance tests remain
  mandatory.
- Run real Windows end-to-end video presentation, resize, destruction,
  cancellation, and teardown evidence in addition to the deterministic session
  tests.

## Packaging and release

- Validate every package from a feed without project references to the aggregate
  repository.
- Verify native/runtime requirements and SourceLink/symbol metadata in the
  consumed packages.
- Complete API, dependency, license, and human review, then perform the approved
  version bump and publish.

## Cross-component handoffs

HTML media elements, JavaScript bindings, source selection, a real-time playback
driver, and browser WPT enablement belong to the HTML/application playback layer.
Broiler.Media should provide capability, decode, session, and output contracts
without absorbing DOM, JavaScript, browser policy, or window ownership.
