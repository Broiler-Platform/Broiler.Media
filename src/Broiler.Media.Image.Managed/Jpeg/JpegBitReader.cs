using System;

namespace Broiler.Media.Image.Managed.Jpeg;

/// <summary>
/// Reads bits MSB-first from a JPEG entropy-coded segment. Handles the <c>0xFF 0x00</c>
/// byte-stuffing transparently and stops (returning -1 from <see cref="ReadBit"/>) when
/// it reaches a real marker such as a restart (RSTn) or EOI.
/// </summary>
// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=3; Fingerprint=TBF
// Broiler-Falsified-If: a 0xFF followed by a marker code inside the scan is delivered as entropy bits instead of ending the read
// Broiler-Human:        PENDING
internal sealed class JpegBitReader
{
    private readonly byte[] _data;
    private readonly int _end;
    private int _pos;
    private int _bitBuffer;
    private int _bitCount;
    private bool _markerHit;

    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: an end beyond data.Length is accepted, so the first read past the array throws IndexOutOfRangeException instead of reporting end of data
    // Broiler-Human:        PENDING
    public JpegBitReader(byte[] data, int start, int end)
    {
        _data = data;
        _pos = start;
        _end = end;
    }

    /// <summary>Returns the next bit (0 or 1), or -1 if a marker or end-of-data was reached.</summary>
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a read after the reader stopped at a marker returns 0 or 1 instead of -1
    // Broiler-Human:        PENDING
    public int ReadBit()
    {
        if (_bitCount == 0 && !FillByte())
            return -1;
        
        _bitCount--;
        return (_bitBuffer >> _bitCount) & 1;
    }

    /// <summary>Reads <paramref name="count"/> bits as an unsigned integer (MSB first).</summary>
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=1; Fingerprint=TBF
    // Broiler-Falsified-If: a marker reached partway through a value returns the partial value instead of throwing FormatException
    // Broiler-Human:        PENDING
    public int ReadBits(int count)
    {
        int value = 0;
        for (int i = 0; i < count; i++)
        {
            int bit = ReadBit();
            if (bit < 0)
                throw new FormatException("Unexpected end of JPEG entropy data.");
            value = (value << 1) | bit;
        }
        return value;
    }

    // Broiler-AI:           Origin=AI; Spec=T.81 sF.1.2.3; IP=Low; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a stuffed 0xFF 0x00 pair is delivered as two data bytes instead of a single 0xFF
    // Broiler-Human:        PENDING
    private bool FillByte()
    {
        if (_markerHit || _pos >= _end)
            return false;

        int b = _data[_pos];
        if (b == 0xFF)
        {
            int next = _pos + 1 < _end ? _data[_pos + 1] : 0xFF;
            if (next == 0x00)
            {
                _pos += 2; // stuffed 0xFF
            }
            else
            {
                _markerHit = true; // a real marker starts here; leave _pos on the 0xFF
                return false;
            }
        }
        else
        {
            _pos++;
        }

        _bitBuffer = b;
        _bitCount = 8;

        return true;
    }

    /// <summary>
    /// Re-synchronizes at a restart boundary: discards buffered bits, skips to the
    /// next RSTn marker and consumes it. Returns false if no restart marker is found.
    /// </summary>
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=3; Fingerprint=TBF
    // Broiler-Falsified-If: a restart marker preceded by one or more 0xFF fill bytes is reported as not found
    // Broiler-Human:        PENDING
    public bool SkipToRestart()
    {
        _bitCount = 0;
        _markerHit = false;

        while (_pos + 1 < _end)
        {
            if (_data[_pos] == 0xFF)
            {
                int m = _data[_pos + 1];
                if (m >= JpegTables.MarkerRst0 && m <= JpegTables.MarkerRst7)
                {
                    _pos += 2;
                    return true;
                }
            
                if (m != 0x00)
                    return false; // some other marker — restart sequence is broken
            }
        
            _pos++;
        }

        return false;
    }
}
