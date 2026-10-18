using Pokey;
using Step = Pokey.StepEnvelope.Step;

namespace Lasers;

/// <summary>
/// Ten laser shots, as the numbers that make them.
///
/// <b>A table of integers and nothing else</b>, in the same shape as
/// <c>Demo1/SoundLibrary.cs</c>, so it can be rendered, measured and checked with no
/// audio device anywhere near it. <c>Program.cs</c> turns one into samples.
///
/// <b>The one rule that makes a laser a laser: the pitch falls fast and it is over
/// quickly.</b> Everything else in the set is a variation on that -- what texture it
/// falls through, how far it falls, and whether the volume does anything on the way. A
/// shot that lasts a third of a second stops reading as a shot and starts reading as an
/// engine, which is why nine of these are under 200ms.
///
/// <b>AUDF is a divider, so it is upside down.</b> A positive delta makes the pitch
/// <i>fall</i>. That is why almost every AUDF step here counts up.
///
/// Two rules every entry obeys, because breaking either is silent rather than loud:
/// AUDF stays inside a byte across its whole sweep, and a volume delta does not walk the
/// nibble past 0 -- it wraps rather than clamping, so a fade that overshoots by one comes
/// back at full volume. <c>Program.cs</c> asserts both for every entry here on every run.
/// </summary>
public static class LaserSounds
{
    /// <summary>The clock the dividers divide. Every pitch below scales with it, so a
    /// recipe designed at this clock and played at another comes out in the wrong key --
    /// pass this same value to <see cref="SoundRender.Render"/>.</summary>
    public const double ClockHz = PokeyClocks.TempestHz;

    /// <summary>How often the envelope rewrites the registers: 4.5ms a tick. Sets
    /// duration and resolution, nothing else -- pitch comes from the divider and the
    /// clock.</summary>
    public const double TickHz = 220.0;

    // AUDC packs both fields into one byte: distortion * 32 + volume, volume being the
    // low nibble, 0..15. Named so the table reads as intent rather than as arithmetic.
    private const int Tone = 5 * 32;     // pure square -- the clean zap
    private const int Growl = 1 * 32;    // 5-bit poly -- coarse, still musical
    private const int Grind = 2 * 32;    // 5- and 4-bit together -- roughest pitched
    private const int Noise = 4 * 32;    // 17-bit poly -- true noise
    private const int Buzz = 6 * 32;     // 4-bit poly -- short buzzing growl

    public static readonly Recipe[] All =
    {
        // 1 -------------------------------------------------------------------
        new("Lasers", "Classic zap",
            new[] { new Step(6, 1, +6, 16) },
            new[] { new Step(Tone + 9, 16, 0, 1) }, 0.45f,
            "A pure tone falling most of four octaves in 73ms at a volume that never "
            + "moves. The shortest description of a laser there is, and the one to modify "
            + "first: a bigger AUDF delta drops it further, a bigger Outer holds it longer."),

        // 2 -------------------------------------------------------------------
        new("Lasers", "Rapid pew",
            new[] { new Step(10, 1, +9, 10) },
            new[] { new Step(Tone + 8, 10, 0, 1) }, 0.40f,
            "The same gesture in 45ms. Short enough to fire ten a second without the shots "
            + "treading on each other, which is what a repeating weapon actually needs -- "
            + "the design here is duration, not timbre."),

        // 3 -------------------------------------------------------------------
        new("Lasers", "Heavy blaster",
            new[] { new Step(40, 1, +7, 26) },
            new[] { new Step(Growl + 11, 2, -1, 12) }, 0.55f,
            "Starts well below the zap, on the 5-bit poly, and fades as it falls. Weight "
            + "comes from starting low and taking 118ms about it; the poly repeats every "
            + "31 clocks, so the bottom of the sweep stays musical instead of hissing."),

        // 4 -------------------------------------------------------------------
        new("Lasers", "Charged beam",
            new[] { new Step(120, 1, -8, 14), new Step(16, 1, +1, 20) },
            new[] { new Step(Tone + 10, 14, 0, 1), new Step(Tone + 10, 2, -1, 11) }, 0.50f,
            "Two AUDF steps: a fast climb, then a slow settle back down while the volume "
            + "fades out under it. The wind-up is what sells it as something charged "
            + "rather than merely fired, and it costs one extra row in each chain."),

        // 5 -------------------------------------------------------------------
        new("Lasers", "Buzz cannon",
            new[] { new Step(12, 1, +10, 18) },
            new[] { new Step(Buzz + 9, 18, 0, 1) }, 0.50f,
            "The classic zap's sweep put through the 4-bit poly. That counter repeats "
            + "every 15 clocks, so it has a pitch of its own underneath the sweep -- two "
            + "pitches moving at once, which is the cheapest way to sound electrical."),

        // 6 -------------------------------------------------------------------
        new("Lasers", "Pulse rifle",
            new[] { new Step(20, 1, +2, 18) },
            new[]
            {
                new Step(Tone + 10, 3, 0, 1), new Step(Tone, 3, 0, 1),
                new Step(Tone + 10, 3, 0, 1), new Step(Tone, 3, 0, 1),
                new Step(Tone + 10, 3, 0, 1), new Step(Tone, 3, 0, 1),
            }, 0.45f,
            "Three shots in 82ms, and the pitch chain knows nothing about it: AUDF falls "
            + "smoothly the whole way while AUDC gates the volume on and off. The two "
            + "chains run independently, so rhythm is AUDC's job and pitch is AUDF's."),

        // 7 -------------------------------------------------------------------
        new("Lasers", "Ion beam",
            new[] { new Step(30, 2, +5, 30) },
            new[] { new Step(Noise + 12, 5, -1, 12) }, 0.55f,
            "273ms of true noise sweeping down and fading -- the 17-bit poly, which does "
            + "not repeat inside the life of the sound. Long and unpitched reads as a beam "
            + "being held rather than a shot being fired."),

        // 8 -------------------------------------------------------------------
        new("Lasers", "Rasp",
            new[] { new Step(18, 1, +6, 30) },
            new[] { new Step(163, 1, -8, 30) }, 0.45f,
            "Its AUDC delta of -8 flips the volume nibble's top bit every tick and wraps "
            + "rather than clamping, so the level alternates between 3 and 11 at 110Hz -- "
            + "that buzz *is* the sound, and it is a wrap being used on purpose. The arcade "
            + "shooters of 1981 used exactly this trick.")
            { AllowWrap = true },

        // 9 -------------------------------------------------------------------
        new("Lasers", "Ricochet",
            new[] { new Step(90, 1, -6, 13), new Step(18, 1, +9, 14) },
            new[] { new Step(Tone + 9, 27, 0, 1) }, 0.45f,
            "Up then down, 123ms, volume flat throughout. An inverted V in pitch is what a "
            + "shot glancing off something sounds like, and it needs no envelope work at "
            + "all -- the whole shape is in the divider."),

        // 10 ------------------------------------------------------------------
        new("Lasers", "Siege cannon",
            new[] { new Step(60, 2, +9, 22) },
            new[] { new Step(Grind + 11, 4, -1, 11) }, 0.60f,
            "The bottom of the set: both short polys at once, falling for 200ms and "
            + "fading. Inner 2 rather than 1 is what buys the length without twice the "
            + "steps -- a step lasts Inner x Outer ticks."),
    };

    /// <summary>The ten as a bank, so they can be handed to anything that browses one --
    /// or written out as a <c>.sound</c> file and opened in the lab.</summary>
    public static readonly EnvelopeBank Bank = new()
    {
        Name = "Lasers",
        Source = "tools/lasers",
        ClockHz = ClockHz,
        TickHz = TickHz,
        Recipes = All,
    };
}
