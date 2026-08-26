using System;
using System.Threading;

namespace Broiler.Media.Image;

/// <summary>
/// The presentation time a still render is taken at — the point on every animated image's
/// own timeline that a single-shot render should show.
/// </summary>
/// <remarks>
/// <para>
/// A live browser repaints as an animation advances, so "which frame" is answered by the
/// compositor's clock. A still render has no clock: it decodes each image once and paints it,
/// which is why an animated image would otherwise always show frame 0. This clock supplies the
/// missing value — a renderer pins it for the duration of a render pass and the decode path
/// selects frames against it via <see cref="ImageSequence.FrameAt"/>.
/// </para>
/// <para>
/// It is deliberately process-wide rather than <c>[ThreadStatic]</c>: image loading is dispatched
/// to the thread pool unless the host asks for synchronous loading, so a thread-local value would
/// be invisible to the very code that reads it. That makes concurrent renders at *different*
/// presentation times unsupported, which is the honest state of a stack that renders one document
/// per process; <see cref="Pin"/> exists so a caller restores the previous value rather than
/// leaving its own behind.
/// </para>
/// <para>
/// The default is <see cref="TimeSpan.Zero"/> — the first frame, which is what a render with no
/// opinion about time has always produced.
/// </para>
/// </remarks>
public static class ImageAnimationClock
{
    private static long _presentationTimeTicks;

    /// <summary>
    /// A per-image override of <see cref="PresentationTime"/>, for an image whose element asks for
    /// a different point on the timeline than the document is at — CSS Image Animation 1's
    /// <c>image-animation: paused</c> / <c>stopped</c>. Set by <see cref="PinForImage"/> around one
    /// image's load.
    /// </summary>
    /// <remarks>
    /// <see cref="AsyncLocal{T}"/> rather than <c>[ThreadStatic]</c>, and that is the whole reason
    /// this works where the process-wide field could not be narrowed: an async host hands the
    /// decode to <c>ThreadPool.QueueUserWorkItem</c>, which captures the calling
    /// <c>ExecutionContext</c> — and an <c>AsyncLocal</c> travels with it, so the pool thread
    /// running the decode observes the value the layout thread pinned before queuing. A
    /// thread-local would be invisible there, which is exactly the trap the class remark below
    /// describes.
    /// </remarks>
    private static readonly AsyncLocal<TimeSpan?> _imageOverride = new();

    /// <summary>
    /// Time elapsed since an animated image started animating, as of the render being produced.
    /// Never negative; assigning a negative value stores <see cref="TimeSpan.Zero"/>.
    /// <para>
    /// Reads the per-image override first (<see cref="PinForImage"/>), so an image whose element
    /// paused it holds its frame while the rest of the document advances. Assignment always writes
    /// the document-wide value — an override is scoped, never assigned.
    /// </para>
    /// </summary>
    public static TimeSpan PresentationTime
    {
        get => _imageOverride.Value ?? TimeSpan.FromTicks(Interlocked.Read(ref _presentationTimeTicks));
        set => Interlocked.Exchange(ref _presentationTimeTicks, Math.Max(0, value.Ticks));
    }

    /// <summary>
    /// Overrides <see cref="PresentationTime"/> for the lifetime of the returned scope, for the
    /// image loaded inside it and any decode that load defers to another thread. Restores the
    /// previous override — not the document time — when disposed, so nesting is safe.
    /// </summary>
    public static IDisposable PinForImage(TimeSpan presentationTime)
    {
        var scope = new ImageScope(_imageOverride.Value);
        _imageOverride.Value = presentationTime < TimeSpan.Zero ? TimeSpan.Zero : presentationTime;
        return scope;
    }

    private sealed class ImageScope(TimeSpan? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _imageOverride.Value = previous;
        }
    }

    /// <summary>
    /// Sets <see cref="PresentationTime"/> for the lifetime of the returned scope and restores
    /// the previous value when it is disposed.
    /// </summary>
    public static IDisposable Pin(TimeSpan presentationTime)
    {
        var scope = new Scope(PresentationTime);
        PresentationTime = presentationTime;
        return scope;
    }

    private sealed class Scope(TimeSpan previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            PresentationTime = previous;
        }
    }
}
