using System;

namespace Broiler.Media.Image.Managed.Entropy;

/// <summary>
/// The MQ arithmetic decoder specified in ITU-T T.88 Annex E and ITU-T T.800 Annex C.
/// </summary>
public sealed class MqDecoder
{
    private static (ushort Qe, byte Nmps, byte Nlps, byte Switch)[] States => MqStates.All;

    private readonly ReadOnlyMemory<byte> _data;
    private int _bp;
    private uint _c;
    private uint _a;
    private int _ct;

    public MqDecoder(ReadOnlyMemory<byte> data)
    {
        _data = data;

        // INITDEC.
        _bp = 0;
        _c = (uint)ByteAt(_bp) << 16;
        ByteIn();
        _c <<= 7;
        _ct -= 7;
        _a = 0x8000;
    }

    /// <summary>
    /// Decodes one bit against the context <paramref name="cx"/> holds, updating
    /// that context's state.
    /// </summary>
    public int Decode(MqContexts contexts, int cx)
    {
        ref byte state = ref contexts.State(cx);
        ref byte mps = ref contexts.Mps(cx);

        (ushort qe, byte nmps, byte nlps, byte exchange) = States[state];
        int d;

        _a -= qe;

        if (((_c >> 16) & 0xFFFF) < qe)
        {
            // LPS_EXCHANGE, then renormalize.
            if (_a < qe)
            {
                d = mps;
                state = nmps;
            }
            else
            {
                d = 1 - mps;
                if (exchange == 1)
                    mps = (byte)(1 - mps);
                state = nlps;
            }

            _a = qe;
            Renormalize();
            return d;
        }

        _c -= (uint)qe << 16;

        if ((_a & 0x8000) != 0)
            return mps;

        // MPS_EXCHANGE, then renormalize.
        if (_a < qe)
        {
            d = 1 - mps;
            if (exchange == 1)
                mps = (byte)(1 - mps);
            state = nlps;
        }
        else
        {
            d = mps;
            state = nmps;
        }

        Renormalize();
        return d;
    }

    private void Renormalize()
    {
        do
        {
            if (_ct == 0)
                ByteIn();

            _a <<= 1;
            _c <<= 1;
            _ct--;
        }
        while ((_a & 0x8000) == 0);
    }

    private void ByteIn()
    {
        if (ByteAt(_bp) == 0xFF)
        {
            if (ByteAt(_bp + 1) > 0x8F)
            {
                _c += 0xFF00;
                _ct = 8;
                return;
            }

            _bp++;
            _c += (uint)ByteAt(_bp) << 9;
            _ct = 7;
            return;
        }

        _bp++;
        _c += (uint)ByteAt(_bp) << 8;
        _ct = 8;
    }

    private byte ByteAt(int index)
    {
        ReadOnlySpan<byte> data = _data.Span;
        return index >= 0 && index < data.Length ? data[index] : (byte)0xFF;
    }
}

/// <summary>
/// The adaptive contexts one arithmetic-coded procedure keeps: a state index and
/// an MPS sense per context value.
/// </summary>
public sealed class MqContexts
{
    private readonly byte[] _states;
    private readonly byte[] _mps;

    public MqContexts(int bits)
    {
        int size = 1 << bits;
        _states = new byte[size];
        _mps = new byte[size];
    }

    public ref byte State(int cx) => ref _states[cx];

    public ref byte Mps(int cx) => ref _mps[cx];
}
