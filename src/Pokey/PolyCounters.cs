using System;

namespace Pokey;

/// <summary>
/// POKEY's three polynomial counters, plus the 9-bit reduction of the largest.
///
/// <b>These are the chip's character.</b> A POKEY's "noise" is not noise: it is a
/// handful of maximal-length shift registers, free-running at the base clock, whose
/// bits each channel <i>samples</i> at its own divider rate. That is why POKEY
/// distortion has pitch -- you are hearing a deterministic pattern strobed at the
/// channel's frequency, not a random signal. RimWalker's `Core/Sfx.cs` substitutes
/// white noise for all of it, which is why its non-tone sounds read as generic
/// hiss rather than as this chip.
///
/// The periods are what matter audibly: 15, 31, 511 and 131071 steps. A 4-bit poly
/// repeats fifteen times faster than a 9-bit one and sounds obviously buzzy where
/// the 17-bit one sounds like true noise, and the short ones are what make POKEY
/// "growl".
///
/// <b>The tap positions are the standard ones, and are an approximation to verify.</b>
/// Any maximal-length LFSR of a given width produces a sequence of the right period
/// and so the right broad character, but the real chip's exact bit order affects the
/// precise waveform. Confirming these against hardware or a reference emulator is
/// open work; it is flagged here rather than presented as settled.
/// </summary>
public sealed class PolyCounters
{
    // Widths, and therefore periods: 2^n - 1.
    private int _poly4 = 1;
    private int _poly5 = 1;
    private int _poly9 = 1;
    private int _poly17 = 1;

    /// <summary>AUDCTL bit 7 shortens the 17-bit counter to 9 bits, which turns the
    /// chip's "true noise" into an audibly repeating rasp -- one of the most
    /// recognisable POKEY textures, and free once both counters exist.</summary>
    public bool NineBitPoly { get; set; }

    public void Reset()
    {
        // Never zero: an all-zero LFSR is a fixed point and would sit silent forever.
        _poly4 = _poly5 = _poly9 = _poly17 = 1;
    }

    /// <summary>Advances every counter one base clock. They free-run together
    /// regardless of what any channel is doing, which is exactly why two channels
    /// sampling the same poly at different rates stay related rather than
    /// independent.</summary>
    public void Tick()
    {
        // The four standard maximal-length polynomials, as their tap distances.
        _poly4 = Step(_poly4, 4, 1);      // x^4  + x^3  + 1
        _poly5 = Step(_poly5, 5, 2);      // x^5  + x^3  + 1
        _poly9 = Step(_poly9, 9, 4);      // x^9  + x^5  + 1
        _poly17 = Step(_poly17, 17, 5);   // x^17 + x^12 + 1
    }

    /// <summary>
    /// One step of a Fibonacci LFSR shifting right: XOR bit 0 with the bit
    /// <paramref name="tap"/> places up, shift, and feed the result into the top.
    ///
    /// <paramref name="tap"/> is a <i>distance</i>, not an absolute position -- for
    /// x^n + x^k + 1 it is n-k. Writing these as two absolute bit positions is the
    /// obvious-looking mistake and it silently produces a non-maximal sequence: the
    /// counter still runs, still sounds like noise, and merely has the wrong period.
    /// That is what the period assertions in the self-test are guarding.
    /// </summary>
    private static int Step(int state, int width, int tap)
    {
        int bit = (state ^ (state >> tap)) & 1;
        return (state >> 1) | (bit << (width - 1));
    }

    public bool Bit4 => (_poly4 & 1) != 0;
    public bool Bit5 => (_poly5 & 1) != 0;

    // --- Inspection -------------------------------------------------------
    //
    // The counters are deterministic: from reset, each produces exactly one sequence.
    // That means a tool can show what a counter *is* without sampling a running chip,
    // which is the only way to display something advancing at over a megahertz.

    public enum Kind { Four, Five, Nine, Seventeen }

    /// <summary>
    /// How many steps a counter takes to repeat: 2^n - 1, because the all-zero state
    /// is unreachable.
    ///
    /// <b>This is the number that explains POKEY's distortion field.</b> A 4-bit
    /// counter repeats every fifteen steps, so sampling it gives a buzz with an
    /// audible pitch of its own; a 17-bit one repeats every 131071, which at any
    /// sensible clock is longer than anyone waits, so it reads as noise. Same
    /// mechanism, opposite character, and the only difference is the width.
    /// </summary>
    public static int Period(Kind kind) => kind switch
    {
        Kind.Four => 15,
        Kind.Five => 31,
        Kind.Nine => 511,
        _ => 131071,
    };

    /// <summary>
    /// The first <paramref name="count"/> bits one counter produces from reset.
    ///
    /// Built by running a fresh counter rather than by a closed form, so what a
    /// display shows is the same arithmetic the audio path runs -- if the taps are
    /// ever changed, the picture changes with the sound rather than disagreeing
    /// with it.
    /// </summary>
    public static bool[] Sequence(Kind kind, int count)
    {
        var poly = new PolyCounters { NineBitPoly = kind == Kind.Nine };
        var bits = new bool[count];

        for (int i = 0; i < count; i++)
        {
            bits[i] = kind switch
            {
                Kind.Four => poly.Bit4,
                Kind.Five => poly.Bit5,
                _ => poly.BitLong,
            };
            poly.Tick();
        }
        return bits;
    }

    /// <summary>
    /// Which counters gate a channel set to a given distortion code, in the order the
    /// chip ANDs them. Empty means a pure tone -- no poly at all, the flip-flop simply
    /// toggles every time.
    /// </summary>
    public static Kind[] GatesFor(int distortion, bool nineBitPoly)
    {
        var longPoly = nineBitPoly ? Kind.Nine : Kind.Seventeen;
        return (distortion & 7) switch
        {
            0 => new[] { Kind.Five, longPoly },
            1 => new[] { Kind.Five },
            2 => new[] { Kind.Five, Kind.Four },
            3 => new[] { Kind.Five },
            4 => new[] { longPoly },
            6 => new[] { Kind.Four },
            _ => System.Array.Empty<Kind>(),
        };
    }

    /// <summary>The long counter, or its 9-bit stand-in when AUDCTL asks for it.</summary>
    public bool BitLong => NineBitPoly ? (_poly9 & 1) != 0 : (_poly17 & 1) != 0;
}
