# ADR 0003: Image Pixel And Alpha Format

Date: 2026-07-03

Status: Accepted for Phase 1 scaffolding; revisit before public API freeze

## Context

`Broiler.Graphics.BPixelBuffer` currently represents decoded images as tightly
packed 32-bit straight-alpha RGBA bytes in row-major order. The existing PNG,
APNG, JPEG, BMP, CPU renderer, Direct2D upload, HTML image, and pixel-diff tests
assume this shape.

## Decision

The initial Media image compatibility format is:

- pixel order: RGBA;
- channel size: 8 bits per channel;
- alpha: straight alpha;
- row order: top-to-bottom, row-major;
- default stride: tightly packed `width * 4`;
- default bytes per pixel: 4.

The Phase 1 `ImageBuffer` contract should still include explicit stride, pixel
format, and alpha-mode fields so later codecs and native paths are not forced
through unnecessary copies.

Direct2D-specific BGRA premultiplication remains in Graphics.Windows.

## Consequences

- Existing lossless image fixtures can compare exact RGBA bytes.
- Current `BPixelBuffer` behavior can be ported with minimal semantic churn.
- Graphics keeps backend upload conversion ownership.
- The new Media API can grow additional pixel formats without changing the
  initial compatibility baseline.

