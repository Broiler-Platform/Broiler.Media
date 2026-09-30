using System;

namespace Broiler.Media.Image.Managed.Entropy;

/// <summary>
/// The MQ arithmetic decoder specified in ITU-T T.88 Annex E and ITU-T T.800 Annex C.
/// </summary>
// Broiler-AI:           Origin=Specification; Spec=T.800 sC.3; IP=Low; Security=High; Resources=0; Fingerprint=TBF
// Broiler-Falsified-If: a stream truncated inside a decision makes the decoder read outside its data or throw instead of continuing on 0xFF fill bytes
// Broiler-Human:        PENDING
public sealed class MqDecoder
{
    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: States yields a table other than MqStates.All, so contexts adapt through probabilities the encoder did not use
    // Broiler-Human:        PENDING
    private static (ushort Qe, byte Nmps, byte Nlps, byte Switch)[] States => MqStates.All;

    private readonly ReadOnlyMemory<byte> _data;
    private int _bp;
    private uint _c;
    private uint _a;
    private int _ct;

    // Broiler-AI:           Origin=Specification; Spec=T.800 sC.3.5; IP=Low; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: an empty data buffer makes INITDEC throw instead of loading C from 0xFF fill bytes
    // Broiler-Human:        PENDING
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
    // Broiler-AI:           Origin=Specification; Spec=T.800 sC.3.2; IP=Low; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: an LPS decision in a state whose SWITCH is 1 leaves the MPS sense of the context unflipped
    // Broiler-Human:        PENDING
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

    // Broiler-AI:           Origin=Specification; Spec=T.800 sC.3.3; IP=Low; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a renormalisation that reaches CT of zero shifts C without first calling ByteIn
    // Broiler-Human:        PENDING
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

    // Broiler-AI:           Origin=Specification; Spec=T.800 sC.3.4; IP=Low; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: a 0xFF byte followed by a byte above 0x8F advances the byte pointer, consuming a marker as coded data
    // Broiler-Human:        PENDING
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

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: an index at or past the end of the data, or below zero, returns a value other than 0xFF or throws
    // Broiler-Human:        PENDING
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
// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=3; Fingerprint=TBF
// Broiler-Falsified-If: a context index at or above 1 << bits reaches the state of another context instead of throwing
// Broiler-Human:        PENDING
public sealed class MqContexts
{
    private readonly byte[] _states;
    private readonly byte[] _mps;

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=3; Fingerprint=TBF
    // Broiler-Falsified-If: the state or MPS table holds fewer than 1 << bits entries, so a valid context index is refused
    // Broiler-Human:        PENDING
    public MqContexts(int bits)
    {
        int size = 1 << bits;
        _states = new byte[size];
        _mps = new byte[size];
    }

    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: State(cx) with cx outside 0 to (1 << bits) - 1 returns a reference instead of throwing
    // Broiler-Human:        PENDING
    public ref byte State(int cx) => ref _states[cx];

    // Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=0; Fingerprint=TBF
    // Broiler-Falsified-If: Mps(cx) with cx outside 0 to (1 << bits) - 1 returns a reference instead of throwing
    // Broiler-Human:        PENDING
    public ref byte Mps(int cx) => ref _mps[cx];
}
