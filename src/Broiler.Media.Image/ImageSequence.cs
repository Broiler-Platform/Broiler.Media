using System;
using System.Collections.Generic;
using System.Linq;

namespace Broiler.Media.Image;

public sealed class ImageSequence
{
    public ImageSequence(IEnumerable<ImageFrame> frames, int width, int height, int loopCount)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (loopCount < 0)
            throw new ArgumentOutOfRangeException(nameof(loopCount));

        ImageFrame[] frameArray = frames.ToArray();
        if (frameArray.Length == 0)
            throw new ArgumentException("An image sequence needs at least one frame.", nameof(frames));

        Frames = Array.AsReadOnly(frameArray);
        Width = width;
        Height = height;
        LoopCount = loopCount;
    }

    public IReadOnlyList<ImageFrame> Frames { get; }

    public int Width { get; }

    public int Height { get; }

    public int LoopCount { get; }

    public bool IsAnimated => Frames.Count > 1;

    public ImageBuffer FirstFrame => Frames[0].Pixels;

    /// <summary>
    /// The index of the frame this sequence's own timeline is showing at
    /// <paramref name="presentationTime"/>, measured from the moment the animation started.
    /// </summary>
    /// <remarks>
    /// Frames occupy their <see cref="ImageFrame.EffectiveDuration"/>, so a delay too short for
    /// any engine to honour counts as a tenth of a second rather than as no time at all.
    /// A <see cref="LoopCount"/> of 0 means "loop forever" (the GIF and WebP convention for an
    /// unbounded animation); any other value plays the sequence that many times and then holds
    /// the last frame, which is where a still render taken long after the end lands.
    /// A time at or before the start selects the first frame.
    /// </remarks>
    public int FrameIndexAt(TimeSpan presentationTime)
    {
        if (Frames.Count == 1 || presentationTime <= TimeSpan.Zero)
            return 0;

        TimeSpan total = TimeSpan.Zero;
        for (int i = 0; i < Frames.Count; i++)
            total += Frames[i].EffectiveDuration;

        // Every frame's effective duration is at least the threshold, so an animated
        // sequence always spans a positive amount of time — no division-by-zero guard needed.
        // Count completed repetitions by division rather than comparing against
        // `total * LoopCount`, which can overflow for a long sequence looped 65535 times.
        if (LoopCount > 0 && presentationTime.Ticks / total.Ticks >= LoopCount)
            return Frames.Count - 1;

        TimeSpan elapsed = TimeSpan.FromTicks(presentationTime.Ticks % total.Ticks);
        TimeSpan start = TimeSpan.Zero;
        for (int i = 0; i < Frames.Count; i++)
        {
            start += Frames[i].EffectiveDuration;
            if (elapsed < start)
                return i;
        }

        return Frames.Count - 1;
    }

    /// <summary>
    /// The frame this sequence's own timeline is showing at <paramref name="presentationTime"/>.
    /// </summary>
    public ImageFrame FrameAt(TimeSpan presentationTime) => Frames[FrameIndexAt(presentationTime)];

    public static ImageSequence Static(ImageBuffer pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        return new ImageSequence(
            [new ImageFrame(pixels, 0, 100)],
            pixels.Width,
            pixels.Height,
            loopCount: 1);
    }
}
