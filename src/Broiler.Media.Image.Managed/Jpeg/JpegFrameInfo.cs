using System;
using System.Globalization;

namespace Broiler.Media.Image.Managed.Jpeg;

/// <summary>
/// What a JPEG's own headers declare about the frame, read without decoding it.
/// </summary>
public readonly record struct JpegFrameInfo(
    byte FrameMarker,
    int Precision,
    int Width,
    int Height,
    int Components,
    bool HasAdobeMarker,
    int AdobeTransform)
{
    /// <summary>The frame process and entropy mode, named as the standard names them.</summary>
    public string DescribeProcess() => FrameMarker switch
    {
        0xC0 => "baseline sequential DCT, Huffman (SOF0)",
        0xC1 => "extended sequential DCT, Huffman (SOF1)",
        0xC2 => "progressive DCT, Huffman (SOF2)",
        0xC3 => "lossless, Huffman (SOF3)",
        0xC5 => "differential sequential DCT, Huffman (SOF5)",
        0xC6 => "differential progressive DCT, Huffman (SOF6)",
        0xC7 => "differential lossless, Huffman (SOF7)",
        0xC9 => "extended sequential DCT, arithmetic (SOF9)",
        0xCA => "progressive DCT, arithmetic (SOF10)",
        0xCB => "lossless, arithmetic (SOF11)",
        0xCD => "differential sequential DCT, arithmetic (SOF13)",
        0xCE => "differential progressive DCT, arithmetic (SOF14)",
        0xCF => "differential lossless, arithmetic (SOF15)",
        _ => string.Create(CultureInfo.InvariantCulture, $"an unrecognized frame process (marker 0x{FrameMarker:X2})"),
    };

    /// <summary>The whole tuple as one phrase, for a diagnostic.</summary>
    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Width}x{Height}, {Precision}-bit, {Components} component{(Components == 1 ? string.Empty : "s")}, {DescribeProcess()}");
}
