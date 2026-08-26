# ADR 0004: Compatibility Window

Date: 2026-07-03

Status: Superseded by implementation

## Context

Image extraction changes assembly ownership, namespaces, and the codec
abstraction. At the start of the migration, the public API included `BImageCodec`, `IBImageCodec`,
`BPixelBuffer`, `BImageFrame`, `BImageSequence`, `BImageEncodeFormat`, and
image-adjacent `BBitmap` encode/decode helpers.

## Decision

Treat the new Media API as the canonical clean surface.

Graphics may keep source-level obsolete adapters for one announced transition
window where practical. The adapters must not preserve hidden global codec
selection indefinitely and must not force Media to depend on Graphics.

Implementation note: the Graphics codec adapters were removed rather than kept
for a compatibility window. `BBitmap` encode/save now accepts
`Broiler.Media.Image.ImageEncodeFormat`, and encoded image decode paths call
Broiler.Media.

Use type forwarding only where identity and semantics can be preserved without
breaking the final dependency direction. Otherwise, use explicit adapters.

`BBitmap`, `BCanvas`, render lists, surfaces, renderer handles, and backend
resource ownership stay in Graphics.

## Consequences

- Consumers get a migration path without compromising Media's dependency graph.
- Binary compatibility is not promised by this Phase 0 record.
- A release owner must still choose the exact version window before public API
  freeze.
