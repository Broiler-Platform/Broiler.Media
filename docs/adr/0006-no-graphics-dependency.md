# ADR 0006: Broiler.Media Depends On No Graphics Component

Date: 2026-08-30

Status: Accepted; refines ADR 0005

## Context

ADR 0005 split ownership of the Windows video presentation target: `Broiler.Graphics.Windows`
declares and owns the HWND, and `Broiler.Media.Video.MediaFoundation` borrows it for a session
lifetime. The split itself was right. Its *implementation* was not: the backend named the
owner's concrete type, so `Broiler.Media.Video.MediaFoundation` carried a project reference to
`Broiler.Graphics.Windows`.

`Broiler.Graphics` already depended on Media in the other direction — its core references
`Broiler.Media.Image`, and `Broiler.Graphics.Direct2D` references `Broiler.Media.Video`. Those
two directions closed a component-level cycle:

```
Broiler.Graphics ──────────► Broiler.Media.Image
Broiler.Graphics.Direct2D ─► Broiler.Media.Video
Broiler.Media.Video.MediaFoundation ─► Broiler.Graphics.Direct2D   ← closes the cycle
```

The MSBuild project graph stayed acyclic, so nothing failed to build and the cycle was easy to
miss. It was still real, and it was paid for at the repository layer:

- **Mutually recursive submodules.** `Broiler.Media` carried a `Broiler.Graphics` submodule and
  `Broiler.Graphics` carried a `Broiler.Media` submodule. The pointer graph had a loop.
- **Duplicate source, compiled twice.** Because those references are relative paths, each
  component resolved the other through its *own nested mirror* rather than the canonical
  checkout. A single `dotnet build` of `Broiler.HtmlBridge.Dom` in the aggregate workspace
  compiled `Broiler.Media.Image` twice — once from `Broiler.Media/src/`, once from
  `Broiler.Graphics/Broiler.Media/src/` — producing two different `Broiler.Media.Image.dll`
  files, one of which won the copy to the output directory. ADR 0001 forbids exactly this
  ("No component may create an independent editable copy of `Broiler.Media`").
- **Pins that drift silently.** The two checkouts of a component are pinned independently and
  had already diverged: the `Broiler.Graphics` nested inside `Broiler.Media` sat on a
  pre-`src/` layout commit while the canonical one had moved on.
- **Neither component could be released or versioned without the other.**

## Decision

**`Broiler.Media` depends on no other Broiler component.** It is a leaf. The dependency between
Media and Graphics runs one way only: Graphics references Media.

The borrowed-HWND arrangement of ADR 0005 is preserved by inverting the contract rather than
the ownership. A new Windows-only contracts assembly, `Broiler.Media.Video.Windows`, declares
`IHwndVideoOutput` — the borrower's view of a presentation target:

- `Broiler.Graphics.Windows.HwndVideoOutput` *implements* `IHwndVideoOutput`. It still creates,
  owns, resizes, shows/hides and destroys the window; nothing about ADR 0005's ownership split
  changes.
- `Broiler.Media.Video.MediaFoundation` consumes `IHwndVideoOutput` and names no graphics type.

The contract carries only what a borrower may legitimately do — read the handle and its current
geometry, check that the window is still usable, and be notified when the owner changes it. The
owner-only operations (`Resize`, `SetVisible`, `NotifyDestroyed`) are deliberately absent, so a
borrower *cannot* reach through the contract to mutate a window it does not own. ADR 0005's
ownership split becomes a compile-time guarantee instead of a convention.

`Broiler.Media.Video.Windows` is a contracts assembly: no implementation, no windowing code, no
interop, and never a reference to `Broiler.Graphics`. It is separate from `Broiler.Media.Video`
because that assembly is cross-platform and must stay HWND-free.

The `Broiler.Graphics` submodule is removed from this repository.

## Alternatives considered

**A neutral `Broiler.Platform.Abstractions` component** holding OS handle contracts, depended on
by both Media and Graphics. Rejected for now: it is a twelfth submodule to pin and version, and
the cycle is closed by exactly one interface. If and when video presentation lands on Linux and
Android, the same question ("who owns the native surface handle?") recurs for Wayland/X11 and
`ANativeWindow`, and a shared platform-handles leaf earns its keep. `IHwndVideoOutput` is shaped
so that promoting it later is a namespace move, not a redesign.

**Moving `HwndVideoOutput` wholesale into Media.** Rejected: it contradicts ADR 0005 — window
creation, destruction and thread affinity belong to the layer that owns the window.

## Consequences

- `Broiler.Media` builds, tests, versions and releases standalone, with no Broiler dependency.
- The aggregate workspace compiles one copy of each Media assembly.
- `Broiler.Graphics` keeps its `Broiler.Media` submodule; that direction is acyclic and stays.
- `HwndVideoTargetChangeKind` and `HwndVideoTargetChangedEventArgs` move from the
  `Broiler.Graphics.Windows` namespace to `Broiler.Media.Video.Windows`. This is a breaking
  change for any external consumer of those two types; no in-tree consumer outside
  `HwndVideoOutput` itself existed.
- Tests for the concrete `HwndVideoOutput` move to `Broiler.Graphics`' own suite, where the type
  lives. Media's Media Foundation tests supply their own `IHwndVideoOutput` double.
- `Broiler.Media.Tests` fails if any project in the component references `Broiler.Graphics`, if
  any runtime source names it, or if a `Broiler.Graphics` checkout reappears here.
