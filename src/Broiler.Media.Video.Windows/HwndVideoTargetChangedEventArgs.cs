using System;

namespace Broiler.Media.Video.Windows;

/// <summary>
/// Notifies a borrower (e.g. a Media Foundation video session) that the HWND-backed
/// presentation target behind an <see cref="IHwndVideoOutput"/> changed size, visibility,
/// or was destroyed by its owner.
/// </summary>
public sealed class HwndVideoTargetChangedEventArgs : EventArgs
{
    public HwndVideoTargetChangedEventArgs(
        HwndVideoTargetChangeKind kind,
        int width,
        int height,
        bool isVisible)
    {
        if (width < 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        Kind = kind;
        Width = width;
        Height = height;
        IsVisible = isVisible;
    }

    public HwndVideoTargetChangeKind Kind { get; }

    public int Width { get; }

    public int Height { get; }

    public bool IsVisible { get; }
}
