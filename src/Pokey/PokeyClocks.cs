using System;

namespace Pokey;

/// <summary>
/// The clocks a POKEY divides, and where they come from.
///
/// <b>Everything on this chip is one base clock and a chain of dividers.</b> The
/// "64kHz" and "15kHz" clocks are not separate oscillators -- they are the base
/// clock divided by 28 and 114. That matters more than it sounds: a POKEY in a
/// machine whose base clock is not an Atari 800's does not have a 64kHz clock at
/// all, it has base/28, and every pitch moves with it.
/// </summary>
public static class PokeyClocks
{
    /// <summary>NTSC Atari 800 / 5200: the base clock nearly every published POKEY
    /// frequency table assumes.</summary>
    public const double Atari800NtscHz = 1789790.0;

    /// <summary>PAL Atari 800.</summary>
    public const double Atari800PalHz = 1773447.0;

    /// <summary>
    /// <b>Tempest's POKEYs, and the reason RimWalker's pitches have never lined up.</b>
    /// The cabinet clocks them at 1.512MHz, not an Atari's 1.79MHz, so its "64kHz"
    /// divider clock is 1512000/28 = 54000Hz. `Core/Sfx.cs` assumes a flat 64000,
    /// which is wrong by 18% before any other question is asked.
    ///
    /// Note this does <i>not</i> by itself explain the factor of two that
    /// `PokeyPitchFudge` still carries -- 54kHz moves the shot's opening pitch to
    /// 1588Hz against a measured ~1000, needing a 0.63 correction rather than the
    /// 0.5 in place. Settling that is what this project is for.
    /// </summary>
    public const double TempestHz = 1512000.0;

    /// <summary>Base clock divided by 28. On an NTSC Atari this is 63921Hz -- the
    /// "64kHz" clock, which is not 64000.</summary>
    public const int SlowDivider = 28;

    /// <summary>
    /// <b>The clock RimWalker actually plays at, and it is not a real machine's.</b>
    ///
    /// Its sounds were tuned until the shot's opening pitch matched the reference
    /// captures, which put the divider clock at 32kHz -- so the base clock implied is
    /// 32000 x 28. That is neither an Atari's 1.79MHz nor Tempest's 1.512MHz, and
    /// nothing explains it yet.
    ///
    /// It is offered here so a voice can be auditioned at the pitch the game will
    /// actually produce. Picking it is not a claim that any hardware ran at this rate;
    /// it is the open question made selectable, sitting next to the two clocks that
    /// are real so the discrepancy is one keypress wide.
    /// </summary>
    public const double RimWalkerHz = 32000.0 * SlowDivider;

    /// <summary>Base clock divided by 114. On an NTSC Atari this is 15700Hz -- the
    /// "15kHz" clock.</summary>
    public const int VerySlowDivider = 114;

    public static double Slow(double baseHz) => baseHz / SlowDivider;
    public static double VerySlow(double baseHz) => baseHz / VerySlowDivider;

    /// <summary>
    /// Output pitch for a channel, from the divider value and which clock feeds it.
    ///
    /// <paramref name="addend"/> is the hardware's own fudge and is not optional:
    /// on the slow clocks the counter reloads with <c>AUDF + 1</c>, but a channel
    /// running directly off the base clock reloads with <c>AUDF + 4</c> (8-bit) or
    /// <c>AUDF + 7</c> (16-bit) because of pipeline delay in the reload path. Getting
    /// this wrong is a small error at large dividers and a large one at small ones.
    /// </summary>
    public static double Pitch(double inputHz, int audf, int addend = 1)
        => inputHz / (2.0 * (audf + addend));
}
