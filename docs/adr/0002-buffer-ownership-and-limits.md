# ADR 0002: Buffer Ownership And Limits

Date: 2026-07-03

Status: Accepted for Phase 1 scaffolding; revisit before API freeze

## Context

Media decoders will consume untrusted streams and produce decoded buffers. The
current image path often materializes whole encoded inputs and decoded images in
memory. Audio and video need streaming, cancellation, and bounded output.

## Decision

Media APIs distinguish ownership explicitly:

- Callers own encoded input streams and buffers unless a method says otherwise.
- Decoders do not fetch URLs or files on their own.
- Decoded outputs are transferred through typed outputs or immutable/owned buffer
  models.
- Buffer-returning APIs must document whether the caller may mutate, dispose, or
  retain the returned memory.
- Streaming decode APIs must accept `CancellationToken`.
- Decode options must carry limits for encoded bytes, decoded bytes, dimensions,
  sample counts, queued buffers, and frame counts.
- Arithmetic for dimensions, durations, strides, and allocation sizes must use
  checked operations or equivalent explicit bounds checks.

The common `Broiler.Media` assembly may define shared limit and lifecycle
contracts, but typed decode methods belong to `AudioCodec`, `VideoCodec`, and
`ImageCodec`.

## Consequences

- No hidden process-wide mutable codec state is needed.
- Large media can be rejected before unbounded allocation.
- Backpressure and cancellation become testable contracts.
- Existing image convenience APIs can be adapted later, but the new core API
  starts with explicit ownership.

