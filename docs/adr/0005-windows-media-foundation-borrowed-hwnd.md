# ADR 0005: Windows Media Foundation Borrowed HWND

Date: 2026-07-03

Status: Accepted; validated by the Media Foundation session implementation

## Context

The first real video implementation is Windows-first and built around
`IMFMediaEngine`. The presentation target is an HWND created and owned outside
Media by `Broiler.Graphics.Windows`.

## Decision

`Broiler.Media.Video.MediaFoundation` borrows a Windows video target for a
session lifetime. It never creates, destroys, subclasses, or assumes ownership of
the HWND.

`Broiler.Graphics.Windows` owns:

- HWND creation;
- HWND destruction;
- UI-thread affinity;
- resize notifications;
- visibility/lifetime notifications;
- any graphics/window integration around the target.

`Broiler.Media.Video.MediaFoundation` owns:

- Media Foundation startup/shutdown around its implementation;
- COM object lifetime;
- `IMFMediaEngine` creation and event translation;
- source load/play/pause/seek/end/error session state;
- cancellation and deterministic session disposal;
- validation that the borrowed target remains usable.

OS-dependent interop must use .NET runtime interop features such as
`LibraryImport` or `DllImport`; no third-party native wrapper package is allowed
for the initial Windows implementation.

## Consequences

- Media does not gain a graphics surface, swap chain, generic presenter, or
  windowing backend.
- Windows-only code is isolated to the Media Foundation implementation project.
- Abstraction assemblies remain HWND-free and Media-Foundation-free.
- Media Foundation session tests cover resize, teardown, target destruction,
  cancellation, callback disconnection, and use-after-destroy prevention.

