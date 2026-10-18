using System;

namespace Pokey;

/// <summary>
/// What a voice sounds like, chosen by ear rather than by register.
///
/// <b>The distortion field's eight codes are six sounds.</b> 1 and 3 are both the plain
/// 5-bit counter and 5 and 7 are both an ungated square, because the field's low bit
/// bypasses a counter stage -- a fact about the silicon, not about music. Naming the six
/// throws the duplicates away, which is the first thing that gets simpler once fidelity
/// to the register stops being the goal.
/// </summary>
public enum Timbre
{
    /// <summary>A square wave. Shots, blips, melody.</summary>
    Tone,
    /// <summary>The long counter: true noise. Explosions, thrust, wind.</summary>
    Noise,
    /// <summary>The 5-bit counter: rough, still musical.</summary>
    Growl,
    /// <summary>The 4-bit counter: a short buzzing growl with a pitch of its own.</summary>
    Rasp,
    /// <summary>Both short counters at once: the roughest pitched texture there is.</summary>
    Grind,
    /// <summary>Short then long: coarse, gritty noise.</summary>
    Gritty,
}

/// <summary>
/// What the loudness does over the life of a sound.
///
/// <b>You get one of these, not two, and that is the hardware talking.</b> A step has a
/// single delta and on AUDC it is masked into the volume nibble, so the level can fade or
/// it can wobble -- there is no second delta to do both. Saying so here is better than
/// offering both and quietly letting one win.
/// </summary>
public enum LevelMove
{
    /// <summary>Constant for the whole sound.</summary>
    Hold,
    /// <summary>Walks down to silence over the duration.</summary>
    FadeOut,
    /// <summary>The wrap trick: the nibble rolls over instead of clamping, so the level
    /// alternates instead of decaying. See <see cref="SoundDesign.TremoloHz"/>.</summary>
    Tremolo,
}

/// <summary>
/// A voice described the way someone designing a sound thinks about one -- a pitch in
/// hertz that goes somewhere over a number of seconds, a texture, and a level.
///
/// <b>This compiles down to the step chains and nothing else.</b> <see cref="Compile"/>
/// hands back the same <c>StepEnvelope.Step</c> pair the ROM stores, so anything built
/// here can be printed, edited in the lab, saved to a <c>.sound</c> bank, or checked
/// against the arcade's own numbers. That constraint is deliberate: an easy API that
/// could express things the engine cannot would be a second engine.
///
/// <b>What it hides is the hardware, not the character.</b> Dividers, the packed AUDC
/// byte, the tick arithmetic and the <c>outer - 1</c> rule are all gone from the caller's
/// view. The staircase, the integer quantisation and the wrap are all still here, because
/// those are what make it sound like this rather than like a sine sweep.
/// </summary>
public sealed record SoundDesign
{
    /// <summary>Where the pitch starts, in hertz.</summary>
    public double FromHz { get; init; } = 440;

    /// <summary>Where it ends. Equal to <see cref="FromHz"/> for a steady note.</summary>
    public double ToHz { get; init; } = 440;

    public double Seconds { get; init; } = 0.2;

    public Timbre Timbre { get; init; } = Timbre.Tone;

    /// <summary>Loudness, 0 to 1. Quantised to the chip's sixteen levels on compile --
    /// that coarseness is part of the sound, so it is not smoothed away.</summary>
    public double Level { get; init; } = 0.75;

    public LevelMove Move { get; init; } = LevelMove.Hold;

    /// <summary>How fast <see cref="LevelMove.Tremolo"/> alternates, in hertz. Tempest's
    /// player shot runs at 110, which is high enough to read as roughness on the tone
    /// rather than as pulsing.</summary>
    public double TremoloHz { get; init; } = 110;

    /// <summary>
    /// How often the envelope is stepped. Sets duration and resolution, nothing else --
    /// pitch comes from the divider and the clock.
    /// </summary>
    public double TickHz { get; init; } = 220;

    /// <summary>
    /// The clock the divider divides.
    ///
    /// <b>Worth one thought even though it is an artefact.</b> Pitch resolution is a
    /// property of this number: a higher clock puts more distinct dividers under the same
    /// hertz range, so a sweep lands on a finer ladder. Tempest's is the default because
    /// it is what the transcribed voices assume.
    /// </summary>
    public double ClockHz { get; init; } = PokeyClocks.TempestHz;

    /// <summary>
    /// Turns the description into the two step chains that produce it.
    ///
    /// The result reports what it actually achieved rather than what was asked for,
    /// because integer dividers and whole ticks mean those differ -- see
    /// <see cref="CompiledVoice.StartHz"/>.
    /// </summary>
    public CompiledVoice Compile()
    {
        int ticks = Math.Max(1, (int)Math.Round(Seconds * TickHz));
        double slowClock = ClockHz / PokeyClocks.SlowDivider;

        int from = DividerFor(FromHz, slowClock);
        int to = DividerFor(ToHz, slowClock);

        var audf = StepFor(from, to - from, ticks);
        var audc = LevelStep(ticks);

        return new CompiledVoice(audf, audc, TickHz, ClockHz);
    }

    /// <summary>
    /// The divider that lands nearest a wanted pitch.
    ///
    /// <c>pitch = clock / (2 * (divider + 1))</c> rearranged. Rounded and clamped to a
    /// byte, which is where the ladder comes from: near the top of the range consecutive
    /// dividers are most of a semitone apart, so "nearest" can be audibly off. That is the
    /// instrument, not an error, and <see cref="CompiledVoice.StartHz"/> reports where it
    /// actually landed.
    /// </summary>
    private static int DividerFor(double hz, double slowClock)
    {
        if (hz <= 0) return 255;
        return Math.Clamp((int)Math.Round(slowClock / (2.0 * hz) - 1.0), 0, 255);
    }

    /// <summary>
    /// Fits a move of <paramref name="change"/> across <paramref name="ticks"/> ticks into
    /// one step.
    ///
    /// <b>The interesting case is a slow move.</b> A delta is an integer, so a sweep that
    /// only travels five dividers over a hundred ticks cannot step every tick -- rounding
    /// would give 0 and the pitch would not move at all. Widening <c>inner</c> instead
    /// makes it step once every twenty ticks, which is both correct and exactly the shape
    /// the ROM's own slower voices use.
    /// </summary>
    private static StepEnvelope.Step StepFor(int start, int change, int ticks)
    {
        int deltas = Math.Min(ticks - 1, Math.Abs(change));
        if (deltas <= 0) return new StepEnvelope.Step(start, ticks, 0, 1);

        int outer = deltas + 1;
        int inner = Math.Max(1, (int)Math.Round(ticks / (double)outer));
        int delta = (int)Math.Round(change / (double)deltas);
        return new StepEnvelope.Step(start, inner, delta, outer);
    }

    /// <summary>The AUDC chain: a packed byte, and whichever one thing the level does.</summary>
    private StepEnvelope.Step LevelStep(int ticks)
    {
        int volume = Math.Clamp((int)Math.Round(Level * PokeyChip.AudcVolumeMask), 0, 15);
        int value = (DistortionFor(Timbre) << 5) | volume;

        switch (Move)
        {
            case LevelMove.FadeOut when volume > 0:
            {
                // One level per step, down to silence. Outer is the number of levels so the
                // walk ends on zero rather than wrapping past it into loud again.
                int outer = volume + 1;
                return new StepEnvelope.Step(value, Math.Max(1, ticks / outer), -1, outer);
            }

            case LevelMove.Tremolo when TremoloHz > 0:
            {
                // A delta of 8 flips the nibble's top bit, so the level alternates between
                // v and v+8 -- a two-tick cycle. modulation = TickHz / (cycle * inner),
                // rearranged for inner.
                int inner = Math.Max(1, (int)Math.Round(TickHz / (2.0 * TremoloHz)));
                return new StepEnvelope.Step(value, inner, -8, Math.Max(1, ticks / inner));
            }

            default:
                return new StepEnvelope.Step(value, ticks, 0, 1);
        }
    }

    /// <summary>Which distortion code a named timbre is. Only six of the eight are
    /// reachable, because the other two are duplicates.</summary>
    public static int DistortionFor(Timbre timbre) => timbre switch
    {
        Timbre.Tone => 5,
        Timbre.Noise => 4,
        Timbre.Growl => 1,
        Timbre.Rasp => 6,
        Timbre.Grind => 2,
        _ => 0,
    };
}

/// <summary>
/// A design turned into the chains that produce it, plus what those chains actually do.
///
/// <b>The achieved values are the point of this type.</b> Ask for 1600 Hz and you get
/// whichever divider is nearest, which near the top of the range can be most of a semitone
/// away; ask for 0.15 s and you get a whole number of ticks. Reporting the difference is
/// more useful than hiding it, and it is how you tell a design that quantised badly from
/// one that landed.
/// </summary>
public readonly record struct CompiledVoice(
    StepEnvelope.Step Audf, StepEnvelope.Step Audc, double TickHz, double ClockHz)
{
    /// <summary>The pitch the first tick actually produces.</summary>
    public double StartHz => HzAt(0);

    /// <summary>The pitch the last tick actually produces.</summary>
    public double EndHz => HzAt(Ticks - 1);

    public int Ticks => StepEnvelope.Span(Audf);

    public double Seconds => Ticks / Math.Max(1.0, TickHz);

    /// <summary>Distortion code and starting volume, read back out of the packed byte.</summary>
    public int Distortion => (Audc.Value >> 5) & 7;
    public int Volume => Audc.Value & PokeyChip.AudcVolumeMask;

    private double HzAt(int tick)
    {
        int divider = StepEnvelope.ValueAt(new[] { Audf }, Math.Max(0, tick), false);
        return ClockHz / PokeyClocks.SlowDivider / (2.0 * (divider + 1));
    }

    /// <summary>Renders this voice to samples, at an amplitude the caller sets.</summary>
    public float[] Render(float amplitude = 1f, int sampleRate = 44100)
        => SoundRender.Render(new[] { Audf }, new[] { Audc }, TickHz, ClockHz, amplitude, sampleRate);

    /// <summary>The steps, as the C# a game would paste -- the same shape the lab's
    /// "Copy C#" emits, so a design can be moved into a hand-tuned voice without
    /// retyping it.</summary>
    public override string ToString()
        => $"audf: new Step({Audf.Value}, {Audf.Inner}, {Audf.Delta:+0;-0;0}, {Audf.Outer}), "
         + $"audc: new Step({Audc.Value}, {Audc.Inner}, {Audc.Delta:+0;-0;0}, {Audc.Outer})";
}
