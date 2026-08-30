namespace Broiler.Media.Video.Windows;

/// <summary>
/// The kind of change reported by an <see cref="IHwndVideoOutput"/> when the window it
/// presents into is resized, shown/hidden, or destroyed by its owner.
/// </summary>
public enum HwndVideoTargetChangeKind
{
    Resized,
    VisibilityChanged,
    Destroyed,
}
