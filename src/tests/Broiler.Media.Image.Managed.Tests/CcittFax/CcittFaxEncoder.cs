using System;
using System.Collections.Generic;
using Broiler.Media.Image.Managed.CcittFax;

namespace Broiler.Media.Image.Managed.Tests.CcittFax;

internal sealed class CcittFaxEncoder
{
    private readonly List<byte> _output = [];
    private readonly Dictionary<int, (int Code, int Length)> _white = Invert(CcittFaxTables.White);
    private readonly Dictionary<int, (int Code, int Length)> _black = Invert(CcittFaxTables.Black);
    private readonly int _columns;

    private int _bitBuffer;
    private int _bitCount;

    private CcittFaxEncoder(int columns) => _columns = columns;

    internal static byte[] Encode(bool[][] image, int k, bool byteAlign = false)
    {
        var encoder = new CcittFaxEncoder(image[0].Length);
        encoder.Run(image, k, byteAlign);
        return [.. encoder._output];
    }

    private void Run(bool[][] image, int k, bool byteAlign)
    {
        var reference = new List<int> { _columns, _columns };

        foreach (bool[] row in image)
        {
            List<int> changes = Changes(row);

            if (k > 0)
            {
                WriteCode(CcittFaxTables.EndOfLineCode, CcittFaxTables.EndOfLineLength);
                bool oneDimensional = ReferenceEquals(row, image[0]);
                WriteBit(oneDimensional ? 1 : 0);

                if (oneDimensional)
                    EncodeOneDimensional(changes);
                else
                    EncodeTwoDimensional(changes, reference);
            }
            else if (k < 0)
            {
                EncodeTwoDimensional(changes, reference);
            }
            else
            {
                EncodeOneDimensional(changes);
            }

            if (byteAlign)
                AlignToByte();

            reference = changes;
            reference.Add(_columns);
            reference.Add(_columns);
        }

        AlignToByte();
    }

    private void EncodeOneDimensional(List<int> changes)
    {
        int position = 0;
        bool white = true;

        foreach (int change in changes)
        {
            WriteRun(change - position, white);
            position = change;
            white = !white;
        }

        if (position < _columns)
            WriteRun(_columns - position, white);
    }

    private void EncodeTwoDimensional(List<int> changes, List<int> reference)
    {
        int a0 = -1;
        bool white = true;

        while (a0 < _columns)
        {
            int a1 = NextChange(changes, a0, white);
            int a2 = NextChange(changes, a1, !white);
            int b1 = NextChange(reference, a0, white);
            int b2 = b1 >= _columns ? _columns : NextChange(reference, b1, !white);

            if (b2 < a1)
            {
                WriteMode(CcittFaxTables.ModePass);
                a0 = b2;
                continue;
            }

            int delta = a1 - b1;
            if (delta is >= -3 and <= 3)
            {
                WriteMode(delta switch
                {
                    0 => CcittFaxTables.ModeVertical0,
                    1 => CcittFaxTables.ModeVerticalRight1,
                    2 => CcittFaxTables.ModeVerticalRight2,
                    3 => CcittFaxTables.ModeVerticalRight3,
                    -1 => CcittFaxTables.ModeVerticalLeft1,
                    -2 => CcittFaxTables.ModeVerticalLeft2,
                    _ => CcittFaxTables.ModeVerticalLeft3,
                });

                a0 = a1;
                white = !white;
                continue;
            }

            WriteMode(CcittFaxTables.ModeHorizontal);
            int start = a0 < 0 ? 0 : a0;
            WriteRun(a1 - start, white);
            WriteRun(a2 - a1, !white);
            a0 = a2;
        }
    }

    private int NextChange(List<int> changes, int after, bool white)
    {
        int index = 0;
        while (index < changes.Count && changes[index] <= after)
            index++;

        if ((index & 1) != (white ? 0 : 1))
            index++;

        return index < changes.Count ? Math.Min(changes[index], _columns) : _columns;
    }

    private static List<int> Changes(bool[] row)
    {
        var changes = new List<int>();
        bool black = false;

        for (int x = 0; x < row.Length; x++)
        {
            if (row[x] != black)
            {
                changes.Add(x);
                black = row[x];
            }
        }

        return changes;
    }

    private void WriteRun(int run, bool white)
    {
        Dictionary<int, (int Code, int Length)> table = white ? _white : _black;

        while (run > CcittFaxTables.MaxTerminatingRun)
        {
            int makeup = Math.Min(run / CcittFaxTables.MakeupStep * CcittFaxTables.MakeupStep, 2560);
            (int code, int length) = table[makeup];
            WriteCode(code, length);
            run -= makeup;
        }

        (int terminating, int terminatingLength) = table[run];
        WriteCode(terminating, terminatingLength);
    }

    private void WriteMode(int mode)
    {
        foreach ((int key, int value) in CcittFaxTables.Modes)
        {
            if (value != mode)
                continue;

            WriteCode(key & 0xFFFF, key >> 16);
            return;
        }

        throw new InvalidOperationException($"No code for fax mode {mode}.");
    }

    private void WriteCode(int code, int length)
    {
        for (int i = length - 1; i >= 0; i--)
            WriteBit((code >> i) & 1);
    }

    private void WriteBit(int bit)
    {
        _bitBuffer = (_bitBuffer << 1) | bit;
        _bitCount++;

        if (_bitCount == 8)
        {
            _output.Add((byte)_bitBuffer);
            _bitBuffer = 0;
            _bitCount = 0;
        }
    }

    private void AlignToByte()
    {
        while (_bitCount != 0)
            WriteBit(0);
    }

    private static Dictionary<int, (int Code, int Length)> Invert(IReadOnlyDictionary<int, int> table)
    {
        var inverted = new Dictionary<int, (int Code, int Length)>(table.Count);
        foreach ((int key, int run) in table)
            inverted[run] = (key & 0xFFFF, key >> 16);
        return inverted;
    }
}
