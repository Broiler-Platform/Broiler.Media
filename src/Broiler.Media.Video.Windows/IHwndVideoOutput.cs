using System;
using System.Runtime.Versioning;

namespace Broiler.Media.Video.Windows;

/// <summary>
/// A Windows HWND-backed video presentation target, as seen by the video backend that
/// <em>borrows</em> it. The window itself is created, owned, resized, and destroyed by the
/// windowing layer outside Broiler.Media that implements this interface; a backend such as
/// <c>Broiler.Media.Video.MediaFoundation</c> only reads the handle and reacts to changes.
/// </summary>
/// <remarks>
/// <para>
/// This interface is the reason Broiler.Media depends on no other Broiler component. The
/// borrowed-HWND arrangement of ADR 0005 originally had the backend hold the owner's
/// concrete target class, which closed a Media → graphics → Media dependency cycle. The
/// contract is declared here instead, on the consuming side, so the dependency runs one way
/// only: the windowing layer implements a Media contract, and Media names no type of its
/// own implementor (ADR 0006).
/// </para>
/// <para>
/// The surface is deliberately borrower-shaped. Every member is an observation — the handle,
/// its current geometry, whether it is still usable, and notification when the owner changes
/// it. The owner-only operations (create, resize, show/hide, destroy) are absent by design,
/// so a borrower cannot reach through this contract to mutate a window it does not own. That
/// makes the ownership split of ADR 0005 a compile-time guarantee rather than a convention.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public interface IHwndVideoOutput : IVideoOutput
{
    /// <summary>
    /// Raised by the owner after the borrowed window is resized, shown/hidden, or destroyed.
    /// A borrower must treat <see cref="HwndVideoTargetChangeKind.Destroyed"/> as terminal.
    /// </summary>
    event EventHandler<HwndVideoTargetChangedEventArgs>? TargetChanged;

    /// <summary>The borrowed native window handle. Never zero for a live target.</summary>
    nint Hwnd { get; }

    /// <summary>Current width of the borrowed window, in physical pixels.</summary>
    int Width { get; }

    /// <summary>Current height of the borrowed window, in physical pixels.</summary>
    int Height { get; }

    /// <summary>Whether the owner currently considers the borrowed window visible.</summary>
    bool IsVisible { get; }

    /// <summary>Whether the owner has destroyed the borrowed window.</summary>
    bool IsDestroyed { get; }

    /// <summary>
    /// Throws <see cref="ObjectDisposedException"/> if the borrowed window has been destroyed
    /// by its owner. Borrowers call this before attaching to or presenting into the handle.
    /// </summary>
    void ThrowIfUsableTargetRequired();
}
