using System;
using Pokey;
using Step = Pokey.StepEnvelope.Step;

namespace Demo1;

/// <summary>
/// A shelf of ready-made arcade sounds.
///
/// <b>Deliberately free of MonoGame.</b> Nothing here is an audio object — it is a table
/// of integers, which is what lets a headless test render and measure every entry without
/// an audio device. <c>ChipSound</c> turns one into something playable.
///
/// The numbers came out of PokeyLabForms. To change one, dial it in there, press
/// <b>Copy as C#</b>, and paste over the entry; see <c>docs/SOUND-RECIPE.md</c>.
///
/// <b>Two rules every entry obeys, because breaking either is silent.</b> AUDF must stay
/// inside a byte across its whole sweep, and a volume delta must not take the nibble past
/// 0 or 15 — it wraps rather than clamping, so a fade that overshoots comes back at full
/// volume. The self-check in <c>tools</c> asserts both for every recipe here.
/// </summary>
public static class SoundLibrary
{
    /// <summary>The clock the dividers divide. Every pitch here scales with it, so a
    /// recipe designed at one clock and played at another comes out in the wrong
    /// key.</summary>
    public const double ClockHz = PokeyClocks.TempestHz;

    /// <summary>How often the envelope rewrites the registers. This sets duration and
    /// nothing else -- pitch comes from the divider and the clock.</summary>
    public const double TickHz = 220.0;

    // Volume lives in AUDC's low nibble and distortion in bits 5-7, so an AUDC byte is
    // (distortion * 32 + volume). Named here so the table reads as intent rather than as
    // arithmetic.
    private const int Tone = 5 * 32;      // pure tone
    private const int Buzz = 6 * 32;      // 4-bit poly -- short, pitched growl
    private const int Growl = 1 * 32;     // 5-bit poly -- coarse, still musical
    private const int Noise = 4 * 32;     // 17-bit poly -- true noise
    private const int Grind = 2 * 32;     // 5- and 4-bit together -- roughest pitched

    public static readonly Recipe[] All =
    {
        // --- Weapons -------------------------------------------------------
        new("Weapons", "Laser",
            new[] { new Step(8, 1, +5, 20) },
            new[] { new Step(Tone + 8, 2, 0, 10) }, 0.45f,
            "Starts above everything else in the mix and drops fast. Short and high is what reads as a zap."),

        new("Weapons", "Heavy cannon",
            new[] { new Step(90, 2, +10, 16) },
            new[] { new Step(Noise + 10, 2, -1, 11) }, 0.6f,
            "Noise rather than tone, falling, with the volume fading out under it. Weight comes from length."),

        new("Weapons", "Plasma",
            new[] { new Step(12, 1, +8, 18) },
            new[] { new Step(Grind + 9, 1, -1, 10) }, 0.5f,
            "Both short polys at once -- the roughest pitched texture the chip has."),

        new("Weapons", "Machine gun",
            new[] { new Step(40, 1, 0, 32) },
            new[]
            {
                new Step(Noise + 10, 2, 0, 2), new Step(Noise, 2, 0, 2),
                new Step(Noise + 10, 2, 0, 2), new Step(Noise, 2, 0, 2),
                new Step(Noise + 10, 2, 0, 2), new Step(Noise, 2, 0, 2),
                new Step(Noise + 10, 2, 0, 2), new Step(Noise, 2, 0, 2),
            }, 0.5f,
            "The pitch never moves. The volume chain gates it on and off, so the rhythm comes from AUDC."),

        // --- Impacts -------------------------------------------------------
        new("Impacts", "Explosion",
            new[] { new Step(20, 3, +12, 20) },
            new[] { new Step(Noise + 12, 3, -1, 13) }, 0.7f,
            "Noise sweeping from high to low over a quarter second, fading as it goes."),

        new("Impacts", "Hit",
            new[] { new Step(120, 1, +15, 9) },
            new[] { new Step(Noise + 10, 1, -2, 6) }, 0.55f,
            "The explosion's shape in a tenth of the time. Impacts are mostly about duration."),

        new("Impacts", "Tick",
            new[] { new Step(40, 1, 0, 3) },
            new[] { new Step(Buzz + 8, 1, -3, 3) }, 0.4f,
            "Three ticks long. Almost too short to have a pitch, which is the point."),

        // --- Movement ------------------------------------------------------
        new("Movement", "Jump",
            new[] { new Step(90, 1, -5, 16) },
            new[] { new Step(Tone + 8, 1, 0, 16) }, 0.4f,
            "A rising pure tone. Negative delta on AUDF, because the divider works upside down."),

        // An evocation of the 1985 NES platformer's jump, not a copy of it -- the original
        // is an NES pulse channel driven by that chip's hardware sweep unit, and this is a
        // POKEY doing the same gesture with the parts it has. See the note.
        new("Movement", "Jump, NES style",
            new[] { new Step(140, 1, -5, 22), new Step(35, 1, 0, 22) },
            new[] { new Step(Tone + 10, 1, 0, 26), new Step(Tone + 10, 2, -1, 10) }, 0.45f,
            "Two octaves up in a fifth of a second, then a fade. The rise accelerates on "
            + "its own because AUDF is a divider, which is what makes a constant delta the "
            + "right shape here rather than a compromise."),

        new("Movement", "Land",
            new[] { new Step(180, 1, +10, 6) },
            new[] { new Step(Noise + 10, 1, -2, 6) }, 0.5f,
            "Low, brief, gone. The counterweight to the jump."),

        new("Movement", "Footstep",
            new[] { new Step(60, 1, 0, 3) },
            new[] { new Step(Noise + 5, 1, -2, 3) }, 0.4f,
            "Three ticks of noise, fading as it goes. Short enough to repeat at walking pace "
            + "without blurring into the next one."),

        new("Movement", "Engine hum",
            new[] { new Step(150, 20, 0, 10) },
            new[] { new Step(Growl + 8, 20, 0, 10) }, 0.3f,
            "Nothing moves at all. A held divider on a coarse poly is a machine idling."),

        // --- Pickups -------------------------------------------------------
        new("Pickups", "Coin",
            new[] { new Step(60, 1, 0, 4), new Step(40, 1, 0, 8) },
            new[] { new Step(Tone + 9, 1, 0, 12) }, 0.45f,
            "Two flat notes, low then high. The oldest pickup sound there is."),

        new("Pickups", "Power up",
            new[] { new Step(120, 2, -5, 20) },
            new[] { new Step(Tone + 8, 2, 0, 20) }, 0.4f,
            "A long rise. Anything that climbs reads as a reward."),

        new("Pickups", "Health",
            new[] { new Step(80, 2, -3, 12) },
            new[] { new Step(Tone + 7, 2, 0, 12) }, 0.35f,
            "The power-up, gentler and shorter -- a smaller version of the same idea."),

        new("Pickups", "Extra life",
            new[] { new Step(80, 1, 0, 6), new Step(60, 1, 0, 6), new Step(48, 1, 0, 6), new Step(40, 1, 0, 12) },
            new[] { new Step(Tone + 9, 1, 0, 24), new Step(Tone + 9, 2, -1, 6) }, 0.45f,
            "Four notes climbing, the last one held and fading. A chain of flat steps is a "
            + "tune, and a tune that goes up is the oldest reward there is."),

        // --- Interface -----------------------------------------------------
        new("Interface", "Menu blip",
            new[] { new Step(30, 1, 0, 3) },
            new[] { new Step(Tone + 7, 1, 0, 3) }, 0.3f,
            "Fourteen milliseconds. A menu sound that outstays that becomes hateful by the tenth press."),

        new("Interface", "Select",
            new[] { new Step(50, 1, 0, 3), new Step(35, 1, 0, 4) },
            new[] { new Step(Tone + 8, 1, 0, 7) }, 0.35f,
            "The blip with a second, higher note after it. Up means yes."),

        new("Interface", "Error",
            new[] { new Step(160, 2, 0, 8) },
            new[] { new Step(Growl + 9, 2, 0, 8) }, 0.4f,
            "Low, flat and coarse. Down and rough means no."),

        // --- States --------------------------------------------------------
        new("States", "Death",
            new[] { new Step(30, 2, +6, 32) },
            new[] { new Step(Growl + 8, 2, 0, 32) }, 0.6f,
            "A long fall on the 5-bit poly, which repeats every 31 steps and so stays musical at the bottom."),

        new("States", "Game over",
            new[] { new Step(40, 2, +4, 20), new Step(60, 2, +6, 20) },
            new[] { new Step(Growl + 9, 2, 0, 40) }, 0.55f,
            "Two falls in a row. The second starts below the first and ends lower still, so it gives up twice."),

        new("States", "Alarm",
            new[]
            {
                new Step(60, 3, 0, 4), new Step(90, 3, 0, 4),
                new Step(60, 3, 0, 4), new Step(90, 3, 0, 4),
            },
            new[] { new Step(Tone + 9, 3, 0, 16) }, 0.45f,
            "Two notes alternating. Nothing says urgency like a pitch that will not settle."),

        new("States", "Level start",
            new[] { new Step(120, 6, -1, 80) },
            new[] { new Step(Tone + 7, 60, 0, 8) }, 0.5f,
            "A slow climb over two seconds at a steady level. Long on purpose: it is the "
            + "sound of a screen changing, and it has to last as long as the change does."),
    };

    /// <summary>
    /// This table as a bank, which is what the demo actually browses.
    ///
    /// <b>Built in rather than shipped as a file</b>, so the demo has something to show
    /// even with no banks folder beside it — and so this stays the one set that is checked
    /// by the compiler rather than by a parser.
    /// </summary>
    public static readonly EnvelopeBank BuiltIn = new()
    {
        Name = "Starter set",
        Source = "built in",
        ClockHz = ClockHz,
        TickHz = TickHz,
        Recipes = All,
    };

    /// <summary>
    /// Just the menu blip, for the shell's own navigation clicks.
    ///
    /// <b>A bank of one, not the whole set.</b> The pages share a click sound, and the
    /// browsable bank changes underneath them — so wiring the shell to whichever bank
    /// happens to be loaded would mean the menu goes silent the moment you open a file
    /// that has no sound of that name. Building twenty-two voices to get one was the other
    /// option, and it is the one that costs a fifth of a second at startup.
    /// </summary>
    public static readonly EnvelopeBank Ui = new()
    {
        Name = "Interface",
        ClockHz = ClockHz,
        TickHz = TickHz,
        Recipes = Find("Menu blip"),
    };

    private static Recipe[] Find(string name)
    {
        foreach (var recipe in All)
            if (recipe.Name == name) return new[] { recipe };

        return Array.Empty<Recipe>();
    }

    // --- Reading a recipe without playing it -------------------------------

    /// <summary>How long the sound runs. The two chains are independent and routinely
    /// disagree about length, so this is the longer of them, exactly as the chip
    /// is.</summary>
    public static double Seconds(Recipe recipe) => Envelope(recipe).Seconds;

    /// <summary>
    /// The value the register actually holds on each tick, one entry per tick.
    ///
    /// A chain is written as compressed spans -- start here, hold each value this many
    /// ticks, add this much, that many times -- so what a sweep *does* is not visible in
    /// the numbers. Expanding it is what lets the demo draw the sweep, and it is the same
    /// walk <see cref="StepEnvelope"/> performs while rendering.
    /// </summary>
    public static int[] Trace(Step[] chain, int ticks)
    {
        var trace = new int[ticks];
        int t = 0, held = chain.Length > 0 ? chain[0].Value : 0;

        foreach (var step in chain)
        {
            for (int outer = 0; outer < step.Outer && t < ticks; outer++)
            {
                held = step.Value + step.Delta * outer;
                for (int inner = 0; inner < step.Inner && t < ticks; inner++) trace[t++] = held;
            }
        }

        // A chain that ends before the voice does leaves its register alone rather than
        // resetting it, so the shorter of the two holds its last value to the end.
        while (t < ticks) trace[t++] = held;
        return trace;
    }

    public static int TotalTicks(Recipe recipe) => Envelope(recipe).TotalTicks;

    private static StepEnvelope Envelope(Recipe recipe) => new()
    {
        AudfChain = recipe.Audf, AudcChain = recipe.Audc, TickHz = TickHz, Channel = 0,
    };
}
