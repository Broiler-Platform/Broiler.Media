# ADR 0001: Component Topology And Consumption Policy

Date: 2026-07-03

Status: Accepted for Phase 1 scaffolding

## Context

`Broiler.Graphics` is a top-level submodule. `Broiler.HTML` also contains a
nested `Broiler.Graphics` submodule at the same commit. Moving image codecs
without a single canonical Media checkout would risk duplicate source copies and
divergent package references.

## Decision

Create `Broiler.Media` as one root component in the aggregate workspace.

During aggregate development, all local project references to Media must point to
this single root checkout. Standalone downstream components should consume
versioned packages once packages exist. Conditional local project references are
allowed only for aggregate workspace development and must not target nested
component mirrors.

No component may create an independent editable copy of `Broiler.Media`.

The first runtime assembly set remains:

- `Broiler.Media`
- `Broiler.Media.Audio`
- `Broiler.Media.Audio.Managed`
- `Broiler.Media.Video`
- `Broiler.Media.Video.MediaFoundation`
- `Broiler.Media.Image`
- `Broiler.Media.Image.Managed`

Phase 0 adds no runtime assembly yet.

## Consequences

- Phase 1 can scaffold projects under this component without cutting consumers
  over.
- Existing components keep their current references until a later migration
  phase.
- The nested `Broiler.HTML/Broiler.Graphics` checkout remains read-only for
  Media extraction decisions.
- Dependency architecture tests should reject accidental nested Media references.

