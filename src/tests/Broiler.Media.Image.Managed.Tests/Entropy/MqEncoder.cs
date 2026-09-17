using System;
using System.Collections.Generic;
using Broiler.Media.Image.Managed.Entropy;

namespace Broiler.Media.Image.Managed.Tests.Entropy;

internal sealed class MqEncoder
{
    private readonly List<byte> _output = [];

    private uint _a = 0x8000;
    private uint _c;
    private int _ct = 12;
    private int _b = -1;

    public void Encode(MqContexts contexts, int cx, int d)
    {
        ArgumentNullException.ThrowIfNull(contexts);

        ref byte state = ref contexts.State(cx);
        ref byte mps = ref contexts.Mps(cx);
        (ushort qe, byte nmps, byte nlps, byte exchange) = MqStates.All[state];

        if (d == mps)
        {
            _a -= qe;
            if ((_a & 0x8000) == 0)
            {
                if (_a < qe)
                    _a = qe;
                else
                    _c += qe;

                state = nmps;
                Renormalize();
                return;
            }

            _c += qe;
            return;
        }

        _a -= qe;
        if (_a < qe)
            _c += qe;
        else
            _a = qe;

        if (exchange == 1)
            mps = (byte)(1 - mps);

        state = nlps;
        Renormalize();
    }

    public byte[] Flush()
    {
        uint temp = _c + _a;
        _c |= 0xFFFF;
        if (_c >= temp)
            _c -= 0x8000;

        _c <<= _ct;
        ByteOut();
        _c <<= _ct;
        ByteOut();

        if (_b != 0xFF)
            Emit(0xFF);

        Emit(0xAC);
        return [.. _output];
    }

    private void Renormalize()
    {
        do
        {
            if (_ct == 0)
                ByteOut();

            _a <<= 1;
            _c <<= 1;
            _ct--;
        }
        while ((_a & 0x8000) == 0);

        _c &= 0xFFFFFFF;
    }

    private void ByteOut()
    {
        if (_b == 0xFF)
        {
            Stuff();
            return;
        }

        if (_c > 0x7FFFFFF)
        {
            if (_b >= 0)
            {
                _b++;
                _output[^1] = (byte)_b;
            }

            _c &= 0x7FFFFFF;

            if (_b == 0xFF)
            {
                Stuff();
                return;
            }
        }

        Emit((byte)(_c >> 19));
        _c &= 0x7FFFF;
        _ct = 8;
    }

    private void Stuff()
    {
        Emit((byte)(_c >> 20));
        _c &= 0xFFFFF;
        _ct = 7;
    }

    private void Emit(byte value)
    {
        _output.Add(value);
        _b = value;
    }
}
