using System;

namespace Pokey;

/// <summary>
/// A chain of linear steps driving one POKEY channel: the arcade's sound engine.
///
/// <b>This is not part of the chip.</b> POKEY has no envelopes -- it holds whatever
/// was last written until something writes again. What Tempest has is an interrupt
/// routine that rewrites <c>AUDF</c> and <c>AUDC</c> on a timer, walking a table of
/// four-byte steps, and that is what this reproduces. Kept in the core library
/// anyway because it is pure arithmetic with no UI and no audio API, so it can be
/// tested headlessly and reused.
///
/// <b>A step is (value, inner, delta, outer)</b>, verified against the ISR at
/// <c>tempest.a65:17461</c>: the four fields are fetched at <c>Lcbd1-6,y</c>,
/// <c>-5</c>, <c>-4</c> and <c>-3</c>, which is where that order comes from rather
/// than from guessing.
///
/// <b>Sounds are chains, not single steps.</b> When a step's <c>inner</c> is zero the
/// ISR reinterprets its <c>value</c> as a pointer to another step -- and zero there
/// means stop. Most of Tempest's sounds are one step plus a terminator, but the
/// extra-life flourish is eight steps, so a player that stops after the first would
/// play an eighth of it. <see cref="AudfChain"/> and <see cref="AudcChain"/> run
/// <b>independently</b>, exactly as the two ports do in the ISR, and the voice lasts
/// as long as the longer of them.
///
/// The one asymmetry that matters: <b>on AUDC only the volume nibble moves.</b> The
/// ISR masks the delta so the distortion nibble survives (<c>and #$f0</c>), which
/// keeps a sound's timbre fixed for its whole life.
/// </summary>
public sealed class StepEnvelope
{
    /// <summary>One segment of an envelope, as the arcade stores it.</summary>
    public readonly record struct Step(int Value, int Inner, int Delta, int Outer);

    // --- A starting voice ----------------------------------------------------

    /// <summary>
    /// A laser zap, and what a new envelope plays until it is given chains of its own.
    ///
    /// <b>An original, not the arcade's.</b> Tempest's own eight voices used to be
    /// compiled in here; they are Atari's numbers, read out of the ROM, so they live in a
    /// <c>.sound</c> bank outside this library and load through
    /// <see cref="EnvelopeBank.Load"/> like any other set. Everything this class does
    /// still follows that ISR -- the step layout, the chains, the outer - 1 rule, the
    /// volume-nibble mask -- because that is how the engine works, not what it played.
    ///
    /// The divider climbs 20 to 194, so the pitch falls over three octaves in 136ms,
    /// while the volume steps down from 8 and holds a moment longer than the pitch does.
    /// </summary>
    public static readonly Step[] ZapAudf = { new(20, 1, +6, 30) };
    public static readonly Step[] ZapAudc = { new(0xa8, 4, -1, 8) };

    // --- Configuration -----------------------------------------------------

    public Step[] AudfChain { get; set; } = ZapAudf;
    public Step[] AudcChain { get; set; } = ZapAudc;

    /// <summary>How often the ISR rewrites the registers. Sets duration and nothing
    /// else -- pitch comes from the divider and the clock.</summary>
    public double TickHz { get; set; } = 220.0;

    public int Channel { get; set; }

    /// <summary>Retrigger at the end instead of stopping. Not something the arcade
    /// does; a sound you can hold is far easier to compare against a reference than
    /// one you have to keep re-firing.</summary>
    public bool Looping { get; set; }

    // --- State -------------------------------------------------------------

    public bool Playing { get; private set; }

    /// <summary>Ticks elapsed since the trigger, for a playhead.</summary>
    public int Tick { get; private set; }

    private double _samplesToNextTick;

    /// <summary>
    /// How long a step occupies, in ticks.
    ///
    /// <c>Le0</c> counts <c>inner</c> ticks; each time it empties, <c>Lf0</c>
    /// decrements, and the step ends when <c>Lf0</c> reaches zero. So the step spans
    /// <c>inner x outer</c> ticks whatever the delta does.
    /// </summary>
    public static int Span(in Step step)
        => Math.Max(1, step.Inner) * Math.Max(1, step.Outer);

    public static int Span(Step[] chain)
    {
        int total = 0;
        foreach (var step in chain) total += Span(step);
        return Math.Max(1, total);
    }

    /// <summary>The voice lasts as long as its longer chain -- the two ports are
    /// independent and routinely disagree about length.</summary>
    public int TotalTicks => Math.Max(Span(AudfChain), Span(AudcChain));

    public double Seconds => TotalTicks / TickHz;

    // --- The envelope, as pure functions ------------------------------------

    /// <summary>
    /// <b>Deltas applied within a step: <c>outer - 1</c>, not <c>outer</c>.</b>
    ///
    /// The ISR applies a delta only on the decrements of <c>Lf0</c> that leave it
    /// non-zero; the one that reaches zero advances to the next step instead. Loading
    /// <c>Lf0</c> with <c>outer</c> therefore yields <c>outer - 1</c> deltas.
    ///
    /// This is off by one from the "AUDF 16 to 240" quoted in ARCADE-SOURCE section 9
    /// and from an earlier version of this code: the shot's sweep actually ends at
    /// <c>16 + 7 x 31 = 233</c>. Audibly it changes almost nothing -- two hertz at the
    /// bottom of the sweep -- but it is the difference between a model derived from
    /// the ISR and one derived from a plausible reading of it.
    /// </summary>
    private static int Deltas(in Step step, int ticksIntoStep)
        => Math.Min(ticksIntoStep / Math.Max(1, step.Inner), Math.Max(0, step.Outer - 1));

    /// <summary>
    /// One delta application.
    ///
    /// <b>Both cases wrap rather than clamp</b>, because the ISR's <c>adc</c> is plain
    /// 8-bit arithmetic with no guard: AUDF wraps at a byte, and AUDC's volume wraps
    /// within its nibble while the distortion nibble is masked back in. Clamping AUDF
    /// is the intuitive reading and it parks the tube descent at divider 0 -- a 16kHz
    /// squeal -- where the hardware passes straight through to 255.
    /// </summary>
    private static int Apply(int value, int delta, bool volumeNibbleOnly)
        => volumeNibbleOnly
            ? (value & 0xf0) | ((value + delta) & 0x0f)
            : (value + delta) & 0xff;

    /// <summary>
    /// What an AUDC delta actually does, given that <see cref="Apply"/> masks it into the
    /// four-bit volume nibble.
    ///
    /// <b>Only <c>delta mod 16</c> has any effect, which is not obvious from the number
    /// you typed.</b> The nibble wraps rather than clamping, so a delta and that delta
    /// plus sixteen are indistinguishable: <c>-8</c> and <c>+8</c> are the same edit, and
    /// <c>+16</c>, <c>-32</c> and <c>+64</c> all move nothing at all. A field offering
    /// -64 to 64 therefore offers 129 numbers and sixteen behaviours.
    ///
    /// The useful way to read what is left is that the volume walks a ring of sixteen
    /// levels, stepping <c>Effective</c> places each tick, so it returns to where it
    /// started after <c>Cycle</c> of them. A delta of 8 gives the tightest cycle there
    /// is -- two -- which is the hard alternation you hear as the rasp on Tempest's
    /// shot, and is the reason that sound buzzes instead of fading.
    ///
    /// <c>Cycle</c> is 1 when nothing moves.
    /// </summary>
    public static (int Effective, int Cycle) VolumeDelta(int delta)
    {
        int effective = ((delta % 16) + 16) % 16;
        return (effective, 16 / Gcd(effective, 16));
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return a;
    }

    /// <summary>
    /// The register value a chain holds at an absolute tick.
    ///
    /// Walks the chain rather than tracking state, so <b>the graph a page plots and
    /// the bytes the chip is given come from one function</b> and cannot drift. Past
    /// the end of the chain the last value is held, which is what POKEY does when
    /// nothing writes to it.
    /// </summary>
    public static int ValueAt(Step[] chain, int tick, bool volumeNibbleOnly)
    {
        if (chain == null || chain.Length == 0) return 0;

        int elapsed = 0;
        int value = chain[0].Value;

        foreach (var step in chain)
        {
            value = step.Value;
            int span = Span(step);

            if (tick < elapsed + span)
            {
                int applied = Deltas(step, tick - elapsed);
                for (int i = 0; i < applied; i++) value = Apply(value, step.Delta, volumeNibbleOnly);
                return value;
            }

            int all = Math.Max(0, step.Outer - 1);
            for (int i = 0; i < all; i++) value = Apply(value, step.Delta, volumeNibbleOnly);
            elapsed += span;
        }

        return value;
    }

    public int AudfAt(int tick) => ValueAt(AudfChain, tick, false);
    public int AudcAt(int tick) => ValueAt(AudcChain, tick, true);

    /// <summary>Pitch at a given tick, for plotting a sweep without rendering it.</summary>
    public double PitchAt(PokeyChip chip, int tick)
        => chip.InputClockHz(Channel) / (2.0 * (AudfAt(tick) + 1));

    // --- Playback ----------------------------------------------------------

    public void Trigger(PokeyChip chip)
    {
        Tick = 0;
        _samplesToNextTick = 0.0;
        Playing = true;
        WriteRegisters(chip);
    }

    /// <summary>
    /// Stops and silences the channel.
    ///
    /// <b>A deviation, stated rather than hidden.</b> The arcade's ISR marks a voice
    /// idle and simply stops writing, so POKEY holds the last value it was given --
    /// which for the shot is a non-zero volume, and would drone. Real cabinets plainly
    /// do not drone, so something else stops them that this decode has not yet
    /// accounted for. Silencing is the useful behaviour for a tool; the open question
    /// is recorded rather than papered over.
    /// </summary>
    public void Stop(PokeyChip chip)
    {
        Playing = false;
        chip.SetAudc(Channel, chip.Audc(Channel) & 0xf0);
    }

    private void WriteRegisters(PokeyChip chip)
    {
        chip.SetAudf(Channel, AudfAt(Tick));
        chip.SetAudc(Channel, AudcAt(Tick));
    }

    /// <summary>
    /// Renders audio while stepping the envelope in time with it.
    ///
    /// <b>The envelope has to advance inside the render, not around it.</b> The chip
    /// runs at over a megahertz and the ISR at a couple of hundred hertz, so a block
    /// of a few hundred samples spans several envelope ticks. Writing the registers
    /// once per block would quantise every sweep to the block rate and make the sound
    /// depend on the audio buffer size -- the kind of error that sounds close enough
    /// and is impossible to chase later. So the block is cut at the tick boundaries.
    ///
    /// Renders the chip flat when not playing, so a caller can always just call this.
    /// </summary>
    public void Render(PokeyChip chip, float[] buffer, int offset, int count, double sampleRate)
    {
        double samplesPerTick = sampleRate / Math.Max(1.0, TickHz);
        int done = 0;

        while (done < count)
        {
            if (!Playing)
            {
                chip.Render(buffer, offset + done, count - done, sampleRate);
                return;
            }

            if (_samplesToNextTick <= 0.0)
            {
                Tick++;
                if (Tick >= TotalTicks)
                {
                    if (Looping) Trigger(chip);
                    else { Stop(chip); continue; }
                }
                else
                {
                    WriteRegisters(chip);
                }
                _samplesToNextTick += samplesPerTick;
            }

            // At least one sample, or a tick rate above the sample rate would spin.
            int slice = Math.Min(count - done, Math.Max(1, (int)_samplesToNextTick));
            chip.Render(buffer, offset + done, slice, sampleRate);
            _samplesToNextTick -= slice;
            done += slice;
        }
    }

}
