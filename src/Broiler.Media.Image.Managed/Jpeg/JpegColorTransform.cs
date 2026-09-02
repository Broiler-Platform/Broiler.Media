namespace Broiler.Media.Image.Managed;

/// <summary>
/// How a three-component JPEG's samples are to be read.
/// </summary>
/// <remarks>
/// <para>
/// A JPEG does not say what its three channels mean in the frame header; the
/// convention is that they are YCbCr, and an Adobe <c>APP14</c> marker can
/// declare otherwise. The decoder cannot resolve that on its own in every case —
/// a container can carry its own declaration that disagrees with the marker — so
/// the answer is passed in rather than guessed at here.
/// </para>
/// <para>
/// Grayscale frames have one channel and nothing to convert, so this does not
/// apply to them.
/// </para>
/// </remarks>
public enum JpegColorTransform
{
    /// <summary>
    /// The three channels are YCbCr and are converted to RGB. The format's
    /// default, and what almost every JPEG means.
    /// </summary>
    YCbCr,

    /// <summary>
    /// The three channels are already RGB and are taken as they are. Converting
    /// these would report colours the image does not contain.
    /// </summary>
    None,
}
