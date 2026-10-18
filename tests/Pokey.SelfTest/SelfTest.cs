using System;
using Pokey;
using Speech;
using PokeyLab;

namespace Pokey.SelfTest;

/// <summary>
/// Headless checks on the emulation core, run with <c>--selftest</c>.
///
/// <b>The core is worth testing precisely because it cannot be checked by ear.</b>
/// A divider that is off by one, a 16-bit pair that multiplies its halves instead of
/// combining them, a poly counter that has fallen into its all-zero fixed point --
/// every one of those still makes a sound, just never the one asked for, which is
/// exactly the class of bug that survives listening.
///
/// The measurements here are made the same way the reference clips were measured in
/// ARCADE-SOURCE section 9: render real audio, then count what came out. Nothing
/// asserts against the emulator's own idea of what it should have produced.
/// </summary>
public static class SelfTest
{
    private static bool _pass = true;

    public static int Run()
    {
        Section("clock derivation", Clocks);
        Section("pure tone pitch", PureTone);
        Section("poly counters", Polys);
        Section("sixteen bit pairing", SixteenBit);
        Section("high-pass filters", Filters);
        Section("volume only mode", VolumeOnly);
        Section("streaming continuity", Streaming);
        Section("step envelope", Envelope);
        Section("sound design", Designs);
        Section("wav files", WavFiles);
        Section("sample voice", Sampled);
        // Not "Speech" -- that is a namespace now, and a method sharing the name would
        // be resolvable but needlessly ambiguous to read.
        Section("speech source", SpeechSounds);
        Section("phoneme utterances", Utterances);
        Section("coarticulation", Coarticulation);
        Section("speaking rate", Speed);
        Section("prosody", Stress);
        Section("voice profiles", Voices);
        Section("singing", Singing);
        Section("letter to sound", Spelling);
        Section("scope trace", Scope);
        Section("poly inspection", PolyInspection);

        Console.WriteLine();
        Console.WriteLine(_pass ? "PASS" : "FAIL");
        return _pass ? 0 : 1;
    }

    private static void Section(string name, Action body)
    {
        Console.WriteLine();
        Console.WriteLine($"--- {name} ---");
        body();
    }

    private static void Check(bool ok, string label)
    {
        if (!ok) _pass = false;
        Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {label}");
    }

    // --- Clocks ------------------------------------------------------------

    private static void Clocks()
    {
        double slow = PokeyClocks.Slow(PokeyClocks.Atari800NtscHz);
        Check(Math.Abs(slow - 63921.0) < 1.0,
              $"an NTSC Atari's \"64kHz\" clock is really {slow:0} Hz");

        double tempest = PokeyClocks.Slow(PokeyClocks.TempestHz);
        Check(Math.Abs(tempest - 54000.0) < 1.0,
              $"Tempest's 1.512MHz part gives {tempest:0} Hz, not 64000");

        // RimWalker's clock has to reproduce the game's pitch exactly, or auditioning
        // a voice here says nothing about what the game will play. The shot opens on
        // divider 16, which the captures put near 1000 Hz.
        double rim = PokeyClocks.Slow(PokeyClocks.RimWalkerHz);
        Check(Math.Abs(rim - 32000.0) < 0.001,
              $"RimWalker's clock divides to exactly {rim:0} Hz");

        var shot = new PokeyChip { BaseClockHz = PokeyClocks.RimWalkerHz };
        shot.SetAudf(0, 16);
        shot.SetAudc(0, 0xa2);
        shot.SetControl(0);
        Check(Math.Abs(shot.PitchHz(0) - 941.2) < 0.1,
              $"...so the shot opens at {shot.PitchHz(0):0.0} Hz, as it does in game");
    }

    // --- Pitch -------------------------------------------------------------

    /// <summary>
    /// Renders a pure tone and counts its zero crossings, then compares that against
    /// what <see cref="PokeyChip.PitchHz"/> claims. Both have to agree, and both have
    /// to match the documented formula -- three independent routes to one number.
    /// </summary>
    private static void PureTone()
    {
        const double sampleRate = 48000.0;

        foreach (int audf in new[] { 15, 31, 63, 121 })
        {
            var chip = new PokeyChip { BaseClockHz = PokeyClocks.Atari800NtscHz };
            chip.SetAudf(0, audf);
            chip.SetAudc(0, 0xa8);          // pure tone, volume 8
            chip.SetControl(0);

            double expected = PokeyClocks.Pitch(PokeyClocks.Slow(PokeyClocks.Atari800NtscHz), audf);
            double claimed = chip.PitchHz(0);
            double measured = MeasureHz(chip, sampleRate, 0.25);

            Check(Math.Abs(claimed - expected) < 0.01,
                  $"AUDF {audf,3}: PitchHz agrees with the formula ({claimed:0.0} Hz)");
            Check(Math.Abs(measured - expected) / expected < 0.02,
                  $"AUDF {audf,3}: rendered audio measures {measured:0.0} Hz against {expected:0.0}");
        }
    }

    // --- Poly counters -----------------------------------------------------

    /// <summary>
    /// The polys have to be maximal-length, or they are not POKEY. A counter that
    /// falls into a short cycle still makes a noise; it just makes the wrong one, and
    /// at 17 bits nobody is going to hear that the period is wrong.
    /// </summary>
    private static void Polys()
    {
        Check(Period(p => p.Bit4, 4) == 15, "the 4-bit poly repeats every 15 steps");
        Check(Period(p => p.Bit5, 5) == 31, "the 5-bit poly repeats every 31 steps");

        var nine = new PolyCounters { NineBitPoly = true };
        Check(Period(nine, p => p.BitLong, 9) == 511, "the 9-bit poly repeats every 511 steps");

        // The 17-bit one is checked for not being short rather than for its exact
        // period: 131071 steps is cheap to walk, but the useful property is that it
        // never falls into one of the short cycles the others use.
        var seventeen = new PolyCounters();
        int period = Period(seventeen, p => p.BitLong, 17);
        Check(period == 131071, $"the 17-bit poly repeats every {period} steps");
    }

    private static int Period(Func<PolyCounters, bool> read, int width)
        => Period(new PolyCounters(), read, width);

    private static int Period(PolyCounters poly, Func<PolyCounters, bool> read, int width)
    {
        // Record the bit sequence, then find the shortest run that repeats.
        int limit = (1 << width) * 3;
        var seen = new bool[limit];
        for (int i = 0; i < limit; i++) { seen[i] = read(poly); poly.Tick(); }

        for (int candidate = 1; candidate <= (1 << width); candidate++)
        {
            bool repeats = true;
            for (int i = 0; i + candidate < limit && repeats; i++)
                repeats &= seen[i] == seen[i + candidate];
            if (repeats) return candidate;
        }
        return -1;
    }

    // --- 16-bit ------------------------------------------------------------

    /// <summary>
    /// Joining two channels must give a 16-bit counter, not two 8-bit ones in
    /// series. The difference is a sum against a product, and it is invisible unless
    /// you measure: both make a tone, and the wrong one is merely at a pitch nobody
    /// asked for.
    /// </summary>
    private static void SixteenBit()
    {
        var chip = new PokeyChip { BaseClockHz = PokeyClocks.Atari800NtscHz };
        chip.SetAudf(0, 0x40);              // low byte
        chip.SetAudf(1, 0x02);              // high byte -> 0x0240 = 576
        chip.SetAudc(1, 0xa8);              // the high half drives the mix
        chip.SetAudc(0, 0x00);
        chip.SetControl(PokeyChip.CtlJoin12);

        double slow = PokeyClocks.Slow(PokeyClocks.Atari800NtscHz);
        double expected = slow / (2.0 * (0x0240 + 1));
        double wrongIfSeries = slow / (2.0 * (0x40 + 1) * (0x02 + 1));

        double claimed = chip.PitchHz(1);
        double measured = MeasureHz(chip, 48000.0, 0.5);

        Check(Math.Abs(claimed - expected) < 0.01,
              $"a joined pair divides by its combined 16-bit value ({claimed:0.00} Hz)");
        Check(Math.Abs(measured - expected) / expected < 0.03,
              $"...and renders at that pitch ({measured:0.00} Hz, not {wrongIfSeries:0.00})");
        Check(chip.PitchHz(0) == 0.0, "the low half of a pair reports no pitch of its own");
    }

    // --- High-pass filters -------------------------------------------------

    /// <summary>
    /// Which AUDCTL bit switches on which channel's high-pass filter.
    ///
    /// <b>These were swapped, and nothing could have told you by ear.</b> Both filters
    /// existed and both worked; the two bits that enable them were the wrong way round,
    /// so a program asking for the channel-1 filter silently got the channel-2 one. It
    /// took a reference document to catch, which is exactly why this test pins the
    /// mapping to behaviour rather than to the constants -- asserting
    /// <c>CtlFilter1By3 == 0x02</c> alone would be a tautology that a future swap would
    /// take with it.
    ///
    /// Bit 1 is the channel-1 filter clocked by channel 3, bit 2 the channel-2 filter
    /// clocked by channel 4, per the C012294 documentation.
    /// </summary>
    private static void Filters()
    {
        Check(PokeyChip.CtlFilter1By3 == 0x02 && PokeyChip.CtlFilter2By4 == 0x04,
              $"bit 1 is the channel-1 filter (0x{PokeyChip.CtlFilter1By3:X2}) and "
              + $"bit 2 the channel-2 one (0x{PokeyChip.CtlFilter2By4:X2})");

        // Only channel 1 is audible; channel 3 runs at volume 0 so it still fires and
        // clocks the filter without contributing to the mix.
        Check(!Same(Render(0, 1), Render(PokeyChip.CtlFilter1By3, 1)),
              "setting bit 1 changes channel 1's output");
        Check(Same(Render(0, 1), Render(PokeyChip.CtlFilter2By4, 1)),
              "...and bit 2 leaves channel 1 alone, so the two are not interchangeable");

        // The mirror, which is what makes the pair a real test rather than half of one.
        Check(!Same(Render(0, 2), Render(PokeyChip.CtlFilter2By4, 2)),
              "setting bit 2 changes channel 2's output");
        Check(Same(Render(0, 2), Render(PokeyChip.CtlFilter1By3, 2)),
              "...and bit 1 leaves channel 2 alone");

        /// <summary>Renders with one channel audible and its filter clock running.</summary>
        static float[] Render(int control, int audible)
        {
            var chip = new PokeyChip { BaseClockHz = PokeyClocks.Atari800NtscHz };

            int voice = audible - 1;          // channel 1 -> index 0, channel 2 -> index 1
            int clocker = audible + 1;        // clocked by channel 3 or 4 respectively

            chip.SetAudf(voice, 30);
            chip.SetAudc(voice, 0xa8);        // pure tone, volume 8
            chip.SetAudf(clocker, 7);
            chip.SetAudc(clocker, 0xa0);      // pure tone, volume 0 -- fires but is silent
            chip.SetControl(control);

            var buffer = new float[2048];
            chip.Render(buffer, 0, buffer.Length, 44100.0);
            return buffer;
        }

        static bool Same(float[] a, float[] b)
        {
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }

    // --- Volume only -------------------------------------------------------

    /// <summary>
    /// Volume-only mode disconnects the divider entirely and drives the output from
    /// the volume nibble. It is how sampled sound was played on the Atari, and a
    /// model without it silently turns those writes into tones.
    /// </summary>
    private static void VolumeOnly()
    {
        var chip = new PokeyChip { BaseClockHz = PokeyClocks.Atari800NtscHz };
        chip.SetAudf(0, 30);
        chip.SetAudc(0, PokeyChip.AudcVolumeOnly | 0x0f);
        chip.SetControl(0);

        // Asserted on Mix() rather than on rendered audio, deliberately. Volume-only
        // output IS direct current, and Render's DC blocker exists to remove exactly
        // that -- so checking the rendered stream for a steady level would be
        // asserting the filter does not work. The property belongs to the chip.
        float first = chip.Mix();
        bool steady = true;
        for (int i = 0; i < 20000; i++) { chip.Tick(); steady &= chip.Mix() == first; }

        Check(steady && first > 0f,
              $"a volume-only channel drives a constant level ({first:0.000}), divider ignored");

        // The same channel with the mode off must oscillate, or the check above would
        // pass just as well on a chip that had stopped running altogether.
        var toned = new PokeyChip { BaseClockHz = PokeyClocks.Atari800NtscHz };
        toned.SetAudf(0, 30);
        toned.SetAudc(0, 0xaf);
        toned.SetControl(0);

        float low = 1f, high = 0f;
        for (int i = 0; i < 20000; i++)
        {
            toned.Tick();
            float level = toned.Mix();
            low = Math.Min(low, level);
            high = Math.Max(high, level);
        }

        Check(high - low > 0.1f, $"...while the same channel as a tone swings ({high - low:0.000})");
        Check(chip.VolumeOnly(0) && chip.Volume(0) == 15, "the mode and volume read back");
    }

    // --- Streaming ---------------------------------------------------------

    /// <summary>
    /// Rendering in small blocks must give exactly what one long render gives. The
    /// live front end streams in blocks of a few hundred samples, so if the
    /// fractional cycle count does not carry across calls it would tick a fraction
    /// differently every block and the pitch would drift by a hair.
    /// </summary>
    private static void Streaming()
    {
        var whole = Fresh();
        var oneGo = new float[4096];
        whole.Render(oneGo, 0, oneGo.Length, 44100.0);

        var blocked = Fresh();
        var pieces = new float[4096];
        for (int at = 0; at < pieces.Length; at += 257)
            blocked.Render(pieces, at, Math.Min(257, pieces.Length - at), 44100.0);

        bool identical = true;
        for (int i = 0; i < oneGo.Length; i++) identical &= oneGo[i] == pieces[i];
        Check(identical, "blocked rendering is sample-identical to one long render");

        static PokeyChip Fresh()
        {
            var chip = new PokeyChip { BaseClockHz = PokeyClocks.Atari800NtscHz };
            chip.SetAudf(0, 45);
            chip.SetAudc(0, 0xa9);
            chip.SetControl(0);
            return chip;
        }
    }

    // --- Sample voice ------------------------------------------------------

    /// <summary>
    /// The 4-bit DAC path: S.A.M.'s route, and the opposite of everything else here.
    ///
    /// The property that matters most is the one it shares with the envelope -- that
    /// playback does not depend on the audio buffer size. It is worth asserting rather
    /// than assuming because the failure is inaudible as a defect: writing once per
    /// block instead of at the sample rate still plays the sound, just slightly wrong,
    /// and slightly wrong speech sounds exactly like 4-bit speech is expected to sound.
    /// </summary>
    private static void Sampled()
    {
        // A ramp rather than real speech: every sample distinct, so a write that lands
        // in the wrong place cannot coincidentally match the one that should be there.
        var ramp = new byte[64];
        for (int i = 0; i < ramp.Length; i++) ramp[i] = (byte)(i % 16);

        var oneGo = new float[4096];
        var whole = new PokeyChip { BaseClockHz = PokeyClocks.Atari800NtscHz };
        var wholeVoice = new SampleVoice { Samples = ramp, SampleRateHz = 8000.0, Looping = true };
        wholeVoice.Trigger(whole);
        wholeVoice.Render(whole, oneGo, 0, oneGo.Length, 44100.0);

        var pieces = new float[4096];
        var blocked = new PokeyChip { BaseClockHz = PokeyClocks.Atari800NtscHz };
        var blockedVoice = new SampleVoice { Samples = ramp, SampleRateHz = 8000.0, Looping = true };
        blockedVoice.Trigger(blocked);
        for (int at = 0; at < pieces.Length; at += 257)
            blockedVoice.Render(blocked, pieces, at, Math.Min(257, pieces.Length - at), 44100.0);

        bool identical = true;
        for (int i = 0; i < oneGo.Length; i++) identical &= oneGo[i] == pieces[i];
        Check(identical, "blocked playback is sample-identical to one long render");

        // Trigger must put the channel into volume-only mode, or the samples would be
        // read as a divider and a distortion field and come out as tones.
        var chip = new PokeyChip { BaseClockHz = PokeyClocks.Atari800NtscHz };
        var voice = new SampleVoice { Samples = ramp, SampleRateHz = 8000.0, Channel = 1 };
        voice.Trigger(chip);
        Check(chip.VolumeOnly(1) && chip.Volume(1) == ramp[0],
              $"trigger sets volume-only and writes the first sample ({chip.Volume(1)})");

        // Rate against duration: 64 samples at 8kHz is 8ms, so a 44.1kHz render of
        // 44100 samples must have run out long before the end.
        var run = new float[44100];
        voice.Render(chip, run, 0, run.Length, 44100.0);
        Check(!voice.Playing, "a non-looping voice stops itself at the end of the buffer");
        Check(!chip.VolumeOnly(1) && chip.Volume(1) == 0,
              "...and hands the channel back silent and out of volume-only mode");

        // The duration has to come from the sample rate, not the output rate.
        var timed = new SampleVoice { Samples = new byte[8000], SampleRateHz = 8000.0 };
        Check(Math.Abs(timed.Seconds - 1.0) < 1e-9, $"8000 samples at 8kHz lasts {timed.Seconds:0.000}s");
    }

    // --- Speech source -----------------------------------------------------

    /// <summary>
    /// The formant synthesiser upstream of the DAC.
    ///
    /// Checked by measuring the buffer it produces rather than by trusting its
    /// arithmetic: that a vowel actually resonates where its formants say it should is
    /// the whole claim, and a resonator with its poles misplaced would still produce a
    /// plausible-looking wobble.
    /// </summary>
    private static void SpeechSounds()
    {
        const double Rate = 8000.0;
        const int Levels = PokeyChip.AudcVolumeMask;   // this front end plays it through 4 bits

        var ee = SpeechSource.Vowel(SpeechSource.Vowels[0].Shape, 0.30, Rate, Levels);
        Check(ee.Length == (int)(0.30 * Rate), $"a 0.30s vowel at 8kHz is {ee.Length} samples");

        bool inRange = true;
        foreach (byte sample in ee) inRange &= sample <= PokeyChip.AudcVolumeMask;
        Check(inRange, "every sample fits the 4-bit nibble");

        // Both rails reached: 4 bits is sixteen levels and wasting any of them to
        // headroom would be a real loss, so the quantiser normalises to the peak.
        byte low = 15, high = 0;
        foreach (byte sample in ee) { low = Math.Min(low, sample); high = Math.Max(high, sample); }
        Check(low == 0 && high == 15, $"the nibble's full range is used ({low}..{high})");

        // The claim under test: "ee" and "oo" have nearly the same F1 and opposite F2,
        // so their spectra must differ where F2 lives and agree low down. Measured with
        // a plain Goertzel over the generated signal, not read back off the parameters.
        var oo = SpeechSource.Vowel(SpeechSource.Vowels[4].Shape, 0.30, Rate, Levels);

        double eeLow = Energy(ee, 300.0, Rate), ooLow = Energy(oo, 300.0, Rate);
        double eeHigh = Energy(ee, 2290.0, Rate), ooHigh = Energy(oo, 2290.0, Rate);

        Check(eeHigh > ooHigh * 4.0,
              $"\"ee\" has far more energy at its F2 of 2290Hz than \"oo\" does ({eeHigh / Math.Max(ooHigh, 1e-9):0.0}x)");
        Check(eeLow > 0 && ooLow > 0 && Math.Max(eeLow, ooLow) / Math.Min(eeLow, ooLow) < 12.0,
              "...while both carry real energy down at F1, where they nearly agree");

        // A fricative is the same resonators on noise, so it must not be periodic the
        // way a vowel is. Compared at the pitch, which a voiced buffer is full of.
        var sh = SpeechSource.Fricative(new Formants(400, 1800, 2600), 0.30, Rate, Levels);
        Check(Energy(sh, SpeechSource.DefaultPitchHz, Rate) < Energy(ee, SpeechSource.DefaultPitchHz, Rate),
              "an unvoiced fricative carries less energy at the pitch than a vowel");

        // Deterministic, for the same reason the poly counters are.
        var again = SpeechSource.Fricative(new Formants(400, 1800, 2600), 0.30, Rate, Levels);
        bool same = sh.Length == again.Length;
        for (int i = 0; same && i < sh.Length; i++) same &= sh[i] == again[i];
        Check(same, "the noise source is seeded, so the same call gives the same buffer");
    }

    // --- Phoneme utterances ------------------------------------------------

    /// <summary>
    /// Arbitrary utterances: the phoneme table, the transition model, and the claim
    /// that a phoneme's identity survives all the way to rendered audio.
    ///
    /// The last of those is the one worth measuring. Everything else in this path could
    /// be right -- the table parsed, the frames built, the buffer the correct length --
    /// while the synthesiser quietly ignored the frame stream and produced one long
    /// neutral vowel, and nothing structural would notice.
    /// </summary>
    private static void Utterances()
    {
        const double Rate = 8000.0;
        const int Levels = PokeyChip.AudcVolumeMask;

        // Stress digits stripped, so a pronouncing dictionary's output parses as-is.
        var stressed = Phonemes.Parse("HH AH0 L OW1", out var none);
        Check(stressed.Count == 4 && none.Count == 0,
              $"\"HH AH0 L OW1\" parses to {stressed.Count} phonemes with no unknowns");

        // An unrecognised token is a reported gap, not an exception.
        var typo = Phonemes.Parse("HH QQ L OW", out var unknown);
        Check(typo.Count == 4 && unknown.Count == 1 && unknown[0] == "QQ",
              "an unknown token becomes silence and is reported by name");

        // Duration comes from the table, and the buffer follows it.
        var hello = Phonemes.Parse("HH EH L OW", out _);
        double expectedMs = Phonemes.DurationMs(hello);
        var spoken = SpeechSource.Speak("HH EH L OW", Rate, Levels, out _);
        Check(spoken.Length == (int)(expectedMs / 1000.0 * Rate),
              $"\"HH EH L OW\" is {expectedMs:0}ms, rendered as {spoken.Length} samples");

        // Transitions exist: across "oo" to "ee" the second formant must actually
        // travel, rather than the frames holding one value and jumping at the join.
        var glide = FrameStream.Build(Phonemes.Parse("UW IY", out _));
        double lowF2 = double.MaxValue, highF2 = double.MinValue, steps = 0;
        for (int i = 0; i < glide.Frames.Length; i++)
        {
            double f2 = glide.Frames[i].Shape.F2;
            lowF2 = Math.Min(lowF2, f2);
            highF2 = Math.Max(highF2, f2);
            if (i > 0 && Math.Abs(f2 - glide.Frames[i - 1].Shape.F2) > 1.0) steps++;
        }
        Check(highF2 - lowF2 > 1200.0, $"F2 travels {highF2 - lowF2:0}Hz across \"UW IY\"");
        Check(steps > glide.Frames.Length * 0.2,
              $"...gradually, over {steps:0} frames, rather than in one jump");

        // The payoff: two utterances differing only in their vowel must differ in the
        // rendered audio, at the frequency that vowel is defined by. Measured out of
        // the 4-bit buffer, not read back off the parameters.
        //
        // Bare vowels rather than "S IY" against "S UW", which is what this check
        // compared first and which measured almost nothing (1.3x). A fricative is
        // broadband, so half the buffer was /s/ energy sitting on top of the very
        // frequency being measured -- the vowels did differ, the measurement just could
        // not see past the consonant. Testing the claim needs the claim on its own.
        var ee = SpeechSource.Speak("IY", Rate, Levels, out _);
        var oo = SpeechSource.Speak("UW", Rate, Levels, out _);
        double eeHigh = Energy(ee, 2290.0, Rate), ooHigh = Energy(oo, 2290.0, Rate);
        Check(eeHigh > ooHigh * 4.0,
              $"\"IY\" carries far more energy at its own F2 than \"UW\" ({eeHigh / Math.Max(ooHigh, 1e-9):0.0}x)");

        // Loudness balance, checked inside one utterance because it can only be checked
        // there: Quantise normalises each buffer to its own extremes, so two separately
        // rendered sounds always come back equally loud no matter how they were driven.
        // Within a buffer it is a real property, and getting it wrong squashes vowels.
        var vowelThenS = SpeechSource.Speak("AA S", Rate, Levels, out _);
        var parts = Phonemes.Parse("AA S", out _);
        int boundary = (int)(parts[0].DurationMs / Phonemes.DurationMs(parts) * vowelThenS.Length);
        double vowelRms = Rms(vowelThenS, boundary / 4, boundary * 3 / 4);
        double fricRms = Rms(vowelThenS, boundary + (vowelThenS.Length - boundary) / 4,
                             vowelThenS.Length - (vowelThenS.Length - boundary) / 4);
        Check(vowelRms > fricRms,
              $"a vowel stays louder than a fricative beside it ({vowelRms:0.0} against {fricRms:0.0})");

        // A stop's closure has to be silent, or it is not a stop. Checked on the frames
        // rather than the audio, where the resonators' ring blurs the boundary.
        var stop = FrameStream.Build(Phonemes.Parse("P AA", out _));
        bool anySilent = false;
        foreach (var frame in stop.Frames) anySilent |= frame.Voicing < 0.01 && frame.Noise < 0.01;
        Check(anySilent, "a stop opens with a genuinely silent closure");

        // Every phoneme in the table must render without producing a NaN -- a resonator
        // tuned above Nyquist would, and /s/ asks for 3800Hz at rates down to 4000.
        bool finite = true;
        foreach (var phoneme in Phonemes.All)
        {
            var one = FormantSynth.Render(FrameStream.Build(new[] { phoneme }), 4000.0);
            foreach (double v in one) finite &= !double.IsNaN(v) && !double.IsInfinity(v);
        }
        Check(finite, $"all {Phonemes.All.Count} phonemes render finite audio even at 4kHz");

        // Every shipped example must parse cleanly. A demo with a typo speaks a silent
        // gap where a phoneme should be, and the example is the first thing anyone
        // tries -- so this is the one place a spelling mistake is worth failing over.
        var bad = new System.Collections.Generic.List<string>();
        foreach (var (label, _, text, _) in SpeechSource.Examples)
        {
            Phonemes.Parse(text, out var wrong);
            if (wrong.Count > 0) bad.Add($"{label}: {string.Join(" ", wrong)}");
        }
        Check(bad.Count == 0,
              bad.Count == 0
                  ? $"all {SpeechSource.Examples.Length} shipped examples parse with no unknown phonemes"
                  : $"shipped examples contain unknown phonemes -- {string.Join("; ", bad)}");
    }

    // --- Coarticulation ----------------------------------------------------

    /// <summary>
    /// Stage 2: the three mechanisms that separate connected speech from a list of
    /// sounds -- resistance, stop loci, and the nasal anti-resonance.
    ///
    /// All three are the kind of change that is easy to write, impossible to hear as
    /// wrong, and worth exactly nothing if silently misapplied. Each is checked against
    /// the frames or the audio rather than against the parameters that produced them.
    /// </summary>
    private static void Coarticulation()
    {
        const double Rate = 8000.0;

        // Resistance: a stop must sit on its target for more of its own span than a
        // vowel does. Measured as how many frames are within 5% of the target value,
        // which is the property "Hold" is supposed to control.
        Phonemes.TryGet("T", out var t);
        Phonemes.TryGet("AA", out var aa);
        Check(t.Hold > aa.Hold, $"a stop resists more than a vowel ({t.Hold:0.00} against {aa.Hold:0.00})");

        // Measured in context, between two vowels, and with two sounds identical except
        // for Hold. Measuring a phoneme on its own says nothing: with no neighbours the
        // frame builder pins both ends to its own target and every frame reads as on
        // target whatever Hold says -- which is what the first version of this check
        // did, reporting a confident 1.00 against 1.00.
        var shape = new Formants(400, 1500, 2500);
        var stiff = new Phoneme("X", "test", shape, shape, 1.0, 0.0, 100, 0.0, 0.75);
        var loose = stiff with { Hold = 0.20 };

        double from = aa.DurationMs, to = from + 100.0;
        double stiffFlat = OnTarget(FrameStream.Build(new[] { aa, stiff, aa }), from, to, shape.F2);
        double looseFlat = OnTarget(FrameStream.Build(new[] { aa, loose, aa }), from, to, shape.F2);
        Check(stiffFlat > looseFlat + 0.2,
              $"...and it reaches the frames: a resistant sound holds its target for {stiffFlat:0.00} "
              + $"of its span against {looseFlat:0.00} for a yielding one");

        // Stop loci: the three places of articulation must point F2 in three different
        // directions, or /b/ /d/ /g/ are the same consonant with different bursts.
        Phonemes.TryGet("B", out var b);
        Phonemes.TryGet("D", out var d);
        Phonemes.TryGet("G", out var g);
        Check(b.Start.F2 < d.Start.F2 && d.Start.F2 < g.Start.F2,
              $"labial, alveolar and velar loci are ordered and distinct "
              + $"({b.Start.F2:0} / {d.Start.F2:0} / {g.Start.F2:0} Hz)");

        // And the transition out of them actually moves: "AA B AA" must sweep F2 down
        // toward the labial locus and back, rather than sitting at the vowel's value.
        var abaFrames = FrameStream.Build(Phonemes.Parse("AA B AA", out _)).Frames;
        double lowest = double.MaxValue;
        foreach (var frame in abaFrames) lowest = Math.Min(lowest, frame.Shape.F2);
        Check(lowest < aa.Start.F2 - 150.0,
              $"F2 dips to {lowest:0}Hz through a /b/, away from the vowel's {aa.Start.F2:0}Hz");

        // The nasal anti-resonance: a nasal must have a hole where its zero is. Compared
        // against the same murmur rendered with the notch disabled, so the only
        // difference is the anti-resonance itself.
        Phonemes.TryGet("M", out var m);
        var withNotch = FormantSynth.Render(FrameStream.Build(new[] { m }), Rate);
        var withoutNotch = FormantSynth.Render(FrameStream.Build(new[] { m with { AntiHz = 0.0 } }), Rate);

        double notched = Energy(FormantSynth.Quantise(withNotch, 255), m.AntiHz, Rate);
        double open = Energy(FormantSynth.Quantise(withoutNotch, 255), m.AntiHz, Rate);
        Check(notched < open * 0.75,
              $"/m/ loses energy at its own {m.AntiHz:0}Hz anti-resonance ({notched / Math.Max(open, 1e-9):0.00} of the un-notched level)");

        // The three nasals must differ from each other in the rendered audio. Before the
        // zeros existed they were three flavours of the same weak vowel.
        double mAt = NasalEnergy("M", 1900.0, Rate);
        double nAt = NasalEnergy("N", 1900.0, Rate);
        Check(Math.Abs(mAt - nAt) / Math.Max(Math.Max(mAt, nAt), 1e-9) > 0.15,
              $"/m/ and /n/ differ measurably at 1900Hz ({mAt:0.000} against {nAt:0.000})");
    }

    // --- Prosody -----------------------------------------------------------

    /// <summary>
    /// Stress, and the three things that follow from it.
    ///
    /// The one that matters most is the compatibility rule: an unmarked phoneme is
    /// stressed. Getting that backwards would silently reduce every vowel in every
    /// phoneme string anyone had ever typed, and would look like the synthesiser having
    /// gone muddy rather than like a prosody bug.
    /// </summary>
    private static void Stress()
    {
        const double Rate = 8000.0;
        const int Levels = PokeyChip.AudcVolumeMask;

        var plain = Phonemes.Parse("HH EH L OW", out _);
        bool allStressed = true;
        foreach (var p in plain) if (p.Syllabic) allStressed &= p.Stress == Phoneme.Stressed;
        Check(allStressed, "an unmarked phoneme string is stressed throughout, as it always was");

        // Digits read rather than discarded, in both directions.
        var marked = Phonemes.Parse("G AA1 R D AH0 N", out _);
        Check(marked[1].Stress == Phoneme.Stressed && marked[4].Stress == Phoneme.Unstressed,
              "stress digits parse into the phonemes that carry them");
        Check(Phonemes.Format(marked) == "G AA1 R D AH0 N",
              $"...and write back out unchanged: {Phonemes.Format(marked)}");

        // Only syllabic phonemes get a mark, because only they mean anything by it.
        Check(!Phonemes.Format(marked).Contains("R1") && !Phonemes.Format(marked).Contains("G1"),
              "consonants are written without stress marks");

        // Assignment: first syllable by default, shifted by -tion.
        string garden = Phonemes.Format(Prosody.Apply(Phonemes.Parse("G AA R D EH N", out _)));
        Check(garden == "G AA1 R D AX0 N",
              $"\"garden\" takes first-syllable stress and reduces the rest: {garden}");

        string nation = Phonemes.Format(Prosody.Apply(Phonemes.Parse("N EY SH AH N", out _)));
        Check(nation.Contains("EY1"), $"-tion pulls stress onto the syllable before it: {nation}");

        // Reduction is selective. A long vowel keeps its identity unstressed -- the IY
        // ending "city" is unstressed and is still an IY -- or every word turns to mush.
        string city = Phonemes.Format(Prosody.Apply(Phonemes.Parse("S IH T IY", out _)));
        Check(city.Contains("IY0"), $"an unstressed long vowel loses its stress but not its identity: {city}");

        // Duration: an unstressed syllable is shorter.
        double stressed = FrameStream.Build(Phonemes.Parse("B AA1 B AA1", out _)).TotalMs;
        double reduced = FrameStream.Build(Phonemes.Parse("B AA1 B AA0", out _)).TotalMs;
        Check(reduced < stressed,
              $"an unstressed syllable is shorter ({reduced:0}ms against {stressed:0}ms)");

        // Pitch: the contour falls for a statement and turns up for a question.
        Check(Prosody.Contour(Intonation.Falling, 1.0) < Prosody.Contour(Intonation.Falling, 0.0),
              "a statement's pitch declines across it");
        Check(Prosody.Contour(Intonation.Rising, 1.0) > Prosody.Contour(Intonation.Rising, 0.5),
              "a question falls through the middle and then rises at the end");
        Check(Prosody.Contour(Intonation.Rising, 0.5) < Prosody.Contour(Intonation.Rising, 0.0),
              "...which is a late turn, not a rise across the whole utterance");

        Check(SpeechSource.IntonationOf("Would you like to play a game?") == Intonation.Rising
              && SpeechSource.IntonationOf("Good morning, Dave.") == Intonation.Falling,
              "a question mark is what selects the rising contour");

        // And it reaches the audio: the same words asked and stated must differ.
        var asked = SpeechSource.Speak("HH EH L OW", Rate, Levels, out _,
                                       null, 1.0, Intonation.Rising);
        var told = SpeechSource.Speak("HH EH L OW", Rate, Levels, out _,
                                      null, 1.0, Intonation.Falling);
        bool differs = false;
        for (int i = 0; i < Math.Min(asked.Length, told.Length); i++) differs |= asked[i] != told[i];
        Check(differs, "and the two contours produce genuinely different audio");

        // The whole path, from English: prosody must survive translation.
        string spoken = LetterToSound.Translate("garden market problem", out _);
        Check(spoken.Contains("0") && spoken.Contains("1"),
              $"English translated through the rules arrives with stress marked: {spoken}");
    }

    // --- Voice profiles ----------------------------------------------------

    /// <summary>
    /// The speaker parameters, and the one that matters most: that the default is
    /// unchanged.
    ///
    /// Pulling constants out into a record is the kind of refactor that silently moves
    /// a value nobody notices until every measurement in the project is quietly off a
    /// few percent. The default's fields are asserted literally against what the
    /// constants were.
    /// </summary>
    private static void Voices()
    {
        const double Rate = 8000.0;
        const int Levels = PokeyChip.AudcVolumeMask;

        var d = VoiceProfile.Default;
        Check(d.PitchHz == 120.0 && d.Speed == 1.0 && d.AccentScale == 1.0,
              "the default voice still has the pitch, speed and accent the constants had");
        Check(d.F1Scale == 1.0 && d.F2Scale == 1.0 && d.F3Scale == 1.0,
              "...unscaled formants");
        Check(d.Bandwidth1 == 60.0 && d.Bandwidth2 == 90.0 && d.Bandwidth3 == 150.0
              && d.GlottalTiltHz == 500.0,
              "...and the bandwidths and glottal corner it was written with");

        // A profile has to actually reach the audio, in both directions.
        var flat = SpeechSource.Speak("M AA1 M AA1", Rate, Levels, out _, VoiceProfile.Default);
        var low = SpeechSource.Speak("M AA1 M AA1", Rate, Levels, out _, VoiceProfile.Deliberate);
        var high = SpeechSource.Speak("M AA1 M AA1", Rate, Levels, out _, VoiceProfile.Bright);

        double atDefault = Energy(flat, 120.0, Rate);
        double atLow = Energy(low, 91.0, Rate);
        Check(Energy(low, 91.0, Rate) > Energy(low, 120.0, Rate),
              $"the fitted voice has more energy at its own 91Hz than at the default's 120Hz "
              + $"({atLow:0.00} against {Energy(low, 120.0, Rate):0.00})");
        Check(atDefault > Energy(flat, 91.0, Rate),
              "...and the default the other way round, so the pitch is really the profile's");

        // The fitted voice is slower, because its measured reference was.
        Check(low.Length > flat.Length,
              $"the fitted voice is slower than the default ({low.Length} samples against {flat.Length})");
        Check(high.Length < flat.Length, "and the bright one faster");

        // A caller's speed beats the profile's suggestion, or a speed control that
        // appears not to work is the result.
        var forced = SpeechSource.Speak("M AA1 M AA1", Rate, Levels, out _,
                                        VoiceProfile.Deliberate, 1.0);
        Check(forced.Length == flat.Length,
              "an explicit speed overrides the voice's own, so one control owns the pace");

        // Formant scaling reaches the resonators. The bright voice scales all three up,
        // so its spectrum must sit higher.
        var vowelLow = SpeechSource.Speak("AA1", Rate, Levels, out _, VoiceProfile.Deliberate);
        var vowelHigh = SpeechSource.Speak("AA1", Rate, Levels, out _,
                                           VoiceProfile.Default with { F3Scale = 1.3 });
        Check(Energy(vowelHigh, 3000.0, Rate) > Energy(vowelLow, 3000.0, Rate),
              "scaling F3 up moves energy up with it");

        // Every shipped profile has to be usable: a zeroed field would be a silent
        // filter with no bandwidth, which renders as nothing rather than as an error.
        foreach (var profile in VoiceProfile.All)
        {
            var audio = SpeechSource.Speak("HH EH1 L OW0", Rate, Levels, out _, profile);
            bool sane = audio.Length > 0;
            byte lo = 15, hi = 0;
            foreach (byte s in audio) { lo = Math.Min(lo, s); hi = Math.Max(hi, s); }
            Check(sane && hi > lo, $"\"{profile.Name}\" renders real audio ({lo}..{hi})");
        }
    }

    // --- Singing -----------------------------------------------------------

    /// <summary>
    /// Melody over lyric.
    ///
    /// The tuning is the part worth measuring rather than eyeballing: an octave is a
    /// doubling and a semitone is the twelfth root of two, and a synthesiser that is a
    /// few cents out is out of tune with itself in a way no listener can diagnose and
    /// every listener can hear.
    /// </summary>
    private static void Singing()
    {
        Check(Math.Abs(Song.NoteHz("A4") - 440.0) < 0.01, $"A4 is {Song.NoteHz("A4"):0.00} Hz");
        Check(Math.Abs(Song.NoteHz("C4") - 261.63) < 0.01, $"middle C is {Song.NoteHz("C4"):0.00} Hz");
        Check(Math.Abs(Song.NoteHz("A5") / Song.NoteHz("A4") - 2.0) < 1e-9,
              "an octave is exactly a doubling");
        Check(Math.Abs(Song.NoteHz("A#4") / Song.NoteHz("A4") - Math.Pow(2, 1.0 / 12)) < 1e-9,
              "a semitone is the twelfth root of two");
        Check(Math.Abs(Song.NoteHz("Bb4") - Song.NoteHz("A#4")) < 1e-9,
              "B flat and A sharp are the same note, which the parser has to not confuse with B");
        Check(Math.Abs(Song.NoteHz("B4") - 493.88) < 0.01, $"...while plain B4 is {Song.NoteHz("B4"):0.00} Hz");
        Check(Song.NoteHz("H4") == 0.0 && Song.NoteHz("C") == 0.0,
              "an unparseable note is reported as zero rather than guessed at");

        // Tempo and beats.
        var notes = Song.Parse("90: C4 E4/2 R G4", out double tempo, out var bad);
        Check(tempo == 90.0 && notes.Count == 4 && bad.Count == 0,
              $"\"90: C4 E4/2 R G4\" is {notes.Count} notes at {tempo:0}bpm");
        Check(notes[1].Beats == 2.0 && notes[2].Hz == 0.0,
              "...with a two-beat note and a rest");

        // The alignment: one note per syllable, and the note sets the length.
        var lyric = Phonemes.Parse("M AA M AA M AA", out _);
        var sung = Song.Sing(lyric, "120: C4 E4 G4", out string report);
        Check(report.Contains("3 syllables on 3 notes"), $"three syllables take three notes: {report}");

        double c4 = Song.NoteHz("C4"), g4 = Song.NoteHz("G4");
        Check(Math.Abs(sung[1].NoteHz - c4) < 0.01 && Math.Abs(sung[5].NoteHz - g4) < 0.01,
              "the notes land on the syllables in order");

        // Consonants sing their syllable's note, so a note starts when its syllable does
        // rather than when the vowel finally arrives.
        Check(Math.Abs(sung[0].NoteHz - c4) < 0.01,
              "a syllable's consonants are on its note too");

        // The consonants are charged against the note rather than added to it, or the
        // rhythm drifts later with every cluster.
        var plain = Song.Sing(Phonemes.Parse("AA AA", out _), "120: C4 C4", out _);
        var clustered = Song.Sing(Phonemes.Parse("S T AA S T AA", out _), "120: C4 C4", out _);
        double plainMs = FrameStream.Build(plain).TotalMs;
        double clusteredMs = FrameStream.Build(clustered).TotalMs;
        Check(Math.Abs(clusteredMs - plainMs) < plainMs * 0.15,
              $"consonants come out of the note, not on top of it ({clusteredMs:0}ms against {plainMs:0}ms)");

        // A sung note's length is the note's, and nothing else's. Stress shortens a
        // spoken syllable by 30%, and for a while it was shortening sung ones too --
        // silently, because a slightly rushed song still sounds like a song.
        double even = FrameStream.Build(Song.Sing(Phonemes.Parse("M AA1 M AA1", out _),
                                                  "120: C4 C4", out _)).TotalMs;
        double mixed = FrameStream.Build(Song.Sing(Phonemes.Parse("M AA1 M AA0", out _),
                                                   "120: C4 C4", out _)).TotalMs;
        Check(Math.Abs(even - mixed) < 1.0,
              $"a stress mark does not shorten a sung note ({even:0}ms against {mixed:0}ms)");

        // And the duration really is the melody's: two beats at 120bpm is one second.
        Check(Math.Abs(even - 1000.0) < 20.0,
              $"two one-beat notes at 120bpm last {even:0}ms");

        // The speed control leaves singing alone too, for the same reason -- tempo is
        // the speed control for a song, and having two would fight.
        double quick = FrameStream.Build(Song.Sing(Phonemes.Parse("M AA1 M AA1", out _),
                                                   "120: C4 C4", out _),
                                         FrameStream.DefaultFrameRateHz, 2.0).TotalMs;
        Check(Math.Abs(quick - even) < 1.0, "and the speed control does not either");

        // Mismatches are reported, not thrown on -- notes and syllables rarely line up
        // first time and the count is what tells you which way to fix it.
        Song.Sing(Phonemes.Parse("M AA M AA M AA", out _), "C4", out string tooFew);
        Check(tooFew.Contains("2 syllables past the end"), $"too few notes is described: {tooFew}");
        Song.Sing(Phonemes.Parse("M AA", out _), "C4 E4 G4", out string tooMany);
        Check(tooMany.Contains("2 notes with no syllable"), $"and so is too many: {tooMany}");

        // A note overrides the speaking pitch outright. Multiplying instead would put a
        // tune out of key by however far the sentence had declined.
        const double Rate = 8000.0;
        const int Levels = PokeyChip.AudcVolumeMask;
        var spoken = SpeechSource.Speak("M AA M AA", Rate, Levels, out _);
        var melodic = SpeechSource.Speak("M AA M AA", Rate, Levels, out _,
                                         null, 1.0, Intonation.Falling,
                                         "120: C5 C5");
        bool differs = false;
        for (int i = 0; i < Math.Min(spoken.Length, melodic.Length); i++) differs |= spoken[i] != melodic[i];
        Check(differs, "singing a line produces different audio from speaking it");

        // And the pitch really is the note: measured out of the rendered buffer.
        var held = SpeechSource.Speak("M AA", Rate, Levels, out _, null,
                                      1.0, Intonation.Falling, "60: A4/2");
        double atNote = Energy(held, 440.0, Rate);
        double atOther = Energy(held, 330.0, Rate);
        Check(atNote > atOther,
              $"a syllable sung on A4 has more energy at 440Hz than at 330Hz ({atNote:0.00} against {atOther:0.00})");

        // Every shipped melody must parse, for the same reason every shipped example
        // must: it is the first thing anyone tries.
        var broken = new System.Collections.Generic.List<string>();
        foreach (var (label, _, _, melody) in SpeechSource.Examples)
        {
            if (melody.Length == 0) continue;
            Song.Parse(melody, out _, out var wrong);
            if (wrong.Count > 0) broken.Add($"{label}: {string.Join(" ", wrong)}");
        }
        Check(broken.Count == 0,
              broken.Count == 0
                  ? "every shipped melody parses"
                  : $"shipped melodies contain bad notes -- {string.Join("; ", broken)}");

        // And every shipped melody must have exactly as many notes as its lyric has
        // syllables. Nothing else catches this: a melody one note short still sings,
        // holding its last note through the final syllable, which sounds like a stylistic
        // choice rather than an off-by-one -- and counting 51 of anything by eye is how
        // off-by-ones survive.
        var misaligned = new System.Collections.Generic.List<string>();
        foreach (var (label, _, phonemes, melody) in SpeechSource.Examples)
        {
            if (melody.Length == 0) continue;

            Song.Sing(Phonemes.Parse(phonemes, out _), melody, out string aligned);
            if (!aligned.Contains(" syllables on ")) misaligned.Add($"{label}: {aligned}");
        }
        Check(misaligned.Count == 0,
              misaligned.Count == 0
                  ? "and every one has exactly one note per syllable"
                  : $"melody and lyric disagree -- {string.Join("; ", misaligned)}");
    }

    // --- Letter to sound ---------------------------------------------------

    /// <summary>
    /// English spelling to phonemes, <b>scored rather than asserted</b>.
    ///
    /// A rule set like this is never right; it is some percentage right, and the only
    /// honest thing to do is measure the percentage against known pronunciations and
    /// hold a floor under it. Checking a handful of words it happens to get right would
    /// prove nothing and would quietly rot the first time a rule was reordered.
    ///
    /// The expected forms are CMU pronouncing dictionary entries with the stress digits
    /// dropped. The words are chosen to spread across the patterns the rules claim to
    /// handle -- silent E, digraphs, soft C and G, vowel+R, plurals, -TION, silent
    /// letters -- rather than to flatter the score.
    /// </summary>
    private static void Spelling()
    {
        (string Word, string Expected)[] cases =
        {
            // Regular, and the rules should get these
            ("CAT", "K AE T"),          ("DOG", "D AA G"),        ("MAKE", "M EY K"),
            ("TIME", "T AY M"),         ("HOPE", "HH OW P"),      ("SING", "S IH NG"),
            ("SHIP", "SH IH P"),        ("PHONE", "F OW N"),      ("CITY", "S IH T IY"),
            ("GEM", "JH EH M"),         ("GATE", "G EY T"),       ("BOOK", "B UH K"),
            ("MOON", "M UW N"),         ("RAIN", "R EY N"),       ("BOAT", "B OW T"),
            ("HOUSE", "HH AW S"),       ("BOY", "B OY"),          ("CAR", "K AA R"),
            ("NATION", "N EY SH AH N"), ("KNIFE", "N AY F"),      ("WRITE", "R AY T"),
            ("THINK", "TH IH NG K"),    ("BANK", "B AE NG K"),    ("DOGS", "D AA G Z"),
            ("SMOKE", "S M OW K"),      ("TRAIN", "T R EY N"),    ("SPEAK", "S P IY K"),
            ("STOP", "S T AA P"),       ("PLAY", "P L EY"),       ("GREEN", "G R IY N"),
        };

        // A second set, chosen after the rules were written and never used to add a rule
        // for a specific word. It scored 55% the first time it ran.
        //
        // <b>It is not pristine any more, and saying so matters more than the number.</b>
        // Its misses exposed two genuine defects -- the suffix class was matching
        // mid-word, and DGE was simply absent -- and both were fixed, which is why it now
        // reports higher. Fixing a broken matcher is not the same as tuning to a test,
        // but it does mean this figure is optimistic for words neither set has met.
        //
        // It has moved twice since. Its first misses were dominated by the schwa -- five
        // words wanting an unstressed AH and getting a full vowel -- and stress
        // reduction fixed four of them, which is the clearest evidence available that
        // the prosody work was worth doing: a number measured on words nobody tuned
        // against went up because of it.
        //
        // The five that remain are five different problems, which is usually the sign
        // that the cheap wins are gone: RIVER and FLOWER are undecidable from spelling
        // at all (against DRIVER and LOWER); SUNSET is a compound whose second element
        // keeps its own stress and gets reduced by the first-syllable rule; MUSIC needs
        // an open-syllable U rule; PENCIL is the one surviving schwa, an IH, which is
        // left unreduced on purpose because reducing IH breaks MUSIC and others.
        (string Word, string Expected)[] heldOut =
        {
            ("FISH", "F IH SH"),        ("LAMP", "L AE M P"),     ("DESK", "D EH S K"),
            ("JUMP", "JH AH M P"),      ("QUICK", "K W IH K"),    ("CHURCH", "CH ER CH"),
            ("YELLOW", "Y EH L OW"),    ("SILVER", "S IH L V ER"),("WINTER", "W IH N T ER"),
            ("RIVER", "R IH V ER"),     ("GARDEN", "G AA R D AH N"),
            ("HAMMER", "HH AE M ER"),   ("SUNSET", "S AH N S EH T"),
            ("PROBLEM", "P R AA B L AH M"),                       ("MUSIC", "M Y UW Z IH K"),
            ("SEVEN", "S EH V AH N"),   ("PENCIL", "P EH N S AH L"),
            ("MARKET", "M AA R K AH T"),("BRIDGE", "B R IH JH"),  ("FLOWER", "F L AW ER"),
        };

        double tuned = Score(cases, out var tunedMisses);
        Check(tuned >= 0.90,
              $"the set the rules were written against still scores {tuned:P0} ({cases.Length} words)");
        Report(tunedMisses);

        double honest = Score(heldOut, out var heldOutMisses);
        Check(honest >= 0.60,
              $"held out: 55% at first run, 60% after two fixes, {honest:P0} of {heldOut.Length} "
              + "once stress reduction landed");
        Report(heldOutMisses);

        static double Score((string Word, string Expected)[] words,
                            out System.Collections.Generic.List<string> misses)
        {
            int right = 0;
            misses = new System.Collections.Generic.List<string>();

            foreach (var (word, expected) in words)
            {
                string got = Bare(LetterToSound.Translate(word, out _));
                if (got == expected) right++;
                else misses.Add($"{word}→{got} (want {expected})");
            }
            return right / (double)words.Length;
        }

        // Compared without stress, which is not a dodge: this section tests whether the
        // rules pick the right *sounds*, and where the stress lands is the prosody
        // section's job. Once the digits are gone AX and AH denote the same thing --
        // the schwa -- because the digit was the only thing distinguishing this
        // project's neutral AX from the dictionary's AH0. Keeping them apart here would
        // fail correct words on a notational difference.
        static string Bare(string phonemes)
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (string token in phonemes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string code = token.TrimEnd('0', '1', '2');
                parts.Add(code == "AX" ? "AH" : code);
            }
            return string.Join(" ", parts);
        }

        // Printed rather than failed on. Seeing which words miss is the whole value of a
        // score -- a bare percentage says nothing about what to fix next.
        static void Report(System.Collections.Generic.List<string> misses)
        {
            if (misses.Count > 0) Console.WriteLine($"        misses: {string.Join(", ", misses)}");
        }

        // The exception dictionary must actually win over the rules, or the irregular
        // words it exists for would be quietly re-derived and mangled.
        Check(Bare(LetterToSound.Translate("ONE", out _)) == "W AH N",
              "the exception dictionary beats the rules for irregular words");
        Check(LetterToSound.Translate("ONE", out _).Contains("AH1"),
              "...and its entries still get stress marked, so they are not rhythmically flat");
        Check(Bare(LetterToSound.Translate("COMPUTER", out _)) == "K AH M P Y UW T ER",
              "...while never being reduced, which would overrule vowels their author chose");
        Check(LetterToSound.IsKnown("the") && !LetterToSound.IsKnown("zzyzx"),
              "and reports which words it actually knows");

        // A sentence has to survive the whole path: tokenising, exceptions, rules, and
        // then parsing back into real phonemes.
        string sentence = LetterToSound.Translate("Good morning, Dave.", out var guessed);
        Phonemes.Parse(sentence, out var unknown);
        Check(unknown.Count == 0,
              $"\"Good morning, Dave.\" translates to phonemes that all parse: {sentence}");
        Check(sentence.Contains("-"), "...with clause punctuation becoming a pause");
        Check(guessed.Count == 0, "...and every word of it coming from the dictionary, not guessed");

        // Everything the rules can emit must be a real phoneme. A typo in the table is
        // otherwise invisible until someone types the word that triggers it.
        var alphabet = new System.Collections.Generic.List<string>();
        foreach (char c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ")
            alphabet.Add(LetterToSound.Translate($"{c}A {c}E {c}I {c}O {c}U A{c} E{c} I{c} O{c} U{c}", out _));

        Phonemes.Parse(string.Join(" ", alphabet), out var bogus);
        Check(bogus.Count == 0,
              bogus.Count == 0
                  ? $"every phoneme the {LetterToSound.RuleCount} rules can emit is in the inventory"
                  : $"rules emit phonemes that do not exist: {string.Join(" ", bogus)}");

        // Same for the dictionary -- a typo there is just as invisible.
        var dictionaryOutput = new System.Collections.Generic.List<string>();
        foreach (var (_, english, _, _) in SpeechSource.Examples)
            if (english.Length > 0) dictionaryOutput.Add(LetterToSound.Translate(english, out _));

        Phonemes.Parse(string.Join(" ", dictionaryOutput), out var badDictionary);
        Check(badDictionary.Count == 0,
              badDictionary.Count == 0
                  ? $"and every example's English translates into real phonemes ({LetterToSound.ExceptionCount} dictionary entries)"
                  : $"dictionary emits phonemes that do not exist: {string.Join(" ", badDictionary)}");
    }

    // --- Speed -------------------------------------------------------------

    /// <summary>
    /// The speaking-rate control, and the one interesting thing about it: it is not a
    /// uniform scale.
    /// </summary>
    private static void Speed()
    {
        const double Rate = 8000.0;
        const int Levels = PokeyChip.AudcVolumeMask;

        var normal = SpeechSource.Speak("HH EH L OW", Rate, Levels, out _);
        var quick = SpeechSource.Speak("HH EH L OW", Rate, Levels, out _, null, 2.0);
        var slow = SpeechSource.Speak("HH EH L OW", Rate, Levels, out _, null, 0.6);

        Check(quick.Length < normal.Length && normal.Length < slow.Length,
              $"speed orders the durations: {quick.Length} / {normal.Length} / {slow.Length} samples");

        // Speed 1.0 must be exactly the table, or every duration quoted anywhere in the
        // project silently stops meaning what it says.
        var explicitOne = SpeechSource.Speak("HH EH L OW", Rate, Levels, out _,
                                             null, 1.0);
        Check(explicitOne.Length == normal.Length,
              "speed 1.0 is exactly the table's own durations");

        // The part that makes it sound like speech rather than a tape transport: at the
        // same speed setting, vowels must compress more than constricted sounds do.
        double vowels = Ratio("AA AA AA", 2.0);
        double fricatives = Ratio("S S S", 2.0);
        Check(fricatives > vowels + 0.05,
              $"at 2x, vowels compress to {vowels:0.00} of their length but fricatives only to {fricatives:0.00}");

        Phonemes.TryGet("AA", out var aa);
        Phonemes.TryGet("S", out var s);
        Check(!aa.IsConstricted && s.IsConstricted,
              "a vowel is unconstricted and a fricative is not, which is what drives that");

        // Out-of-range values must clamp rather than produce a zero-length or enormous
        // buffer -- this is a UI-driven number and nothing else guards it.
        var absurd = SpeechSource.Speak("AA", Rate, Levels, out _, null, 500.0);
        Check(absurd.Length > 0, $"an absurd speed clamps rather than emptying the buffer ({absurd.Length} samples)");

        static double Ratio(string text, double speed)
        {
            var phonemes = Phonemes.Parse(text, out _);
            double fast = FrameStream.Build(phonemes, FrameStream.DefaultFrameRateHz, speed).TotalMs;
            double normal = FrameStream.Build(phonemes).TotalMs;
            return fast / normal;
        }
    }

    /// <summary>Fraction of the frames inside a time window sitting within 5% of a
    /// target F2 -- how much of a sound is "on target" rather than travelling to or
    /// from its neighbours.</summary>
    private static double OnTarget(Utterance utterance, double fromMs, double toMs, double targetF2)
    {
        int on = 0, total = 0;
        for (int i = 0; i < utterance.Frames.Length; i++)
        {
            double ms = i / utterance.FrameRateHz * 1000.0;
            if (ms < fromMs || ms > toMs) continue;

            total++;
            if (Math.Abs(utterance.Frames[i].Shape.F2 - targetF2) < targetF2 * 0.05) on++;
        }
        return total == 0 ? 0.0 : on / (double)total;
    }

    private static double NasalEnergy(string code, double hz, double rate)
    {
        Phonemes.TryGet(code, out var phoneme);
        var audio = FormantSynth.Render(FrameStream.Build(new[] { phoneme }), rate);
        return Energy(FormantSynth.Quantise(audio, 255), hz, rate);
    }

    /// <summary>Level of a slice, mean removed -- the samples are unipolar, so without
    /// that the offset would dominate and every slice would measure the same.</summary>
    private static double Rms(byte[] samples, int from, int to)
    {
        if (to <= from) return 0.0;

        double mean = 0.0;
        for (int i = from; i < to; i++) mean += samples[i];
        mean /= to - from;

        double sum = 0.0;
        for (int i = from; i < to; i++) { double d = samples[i] - mean; sum += d * d; }
        return Math.Sqrt(sum / (to - from));
    }

    /// <summary>
    /// Energy at one frequency, by Goertzel.
    ///
    /// The samples are unipolar 0-15, so the mean is removed first -- left in, the DC
    /// term would swamp everything and every vowel would measure the same.
    /// </summary>
    private static double Energy(byte[] samples, double hz, double sampleRate)
    {
        double mean = 0.0;
        foreach (byte sample in samples) mean += sample;
        mean /= Math.Max(1, samples.Length);

        double coefficient = 2.0 * Math.Cos(2.0 * Math.PI * hz / sampleRate);
        double s1 = 0.0, s2 = 0.0;

        foreach (byte sample in samples)
        {
            double s = sample - mean + coefficient * s1 - s2;
            s2 = s1;
            s1 = s;
        }

        return Math.Sqrt(Math.Max(0.0, s1 * s1 + s2 * s2 - coefficient * s1 * s2)) / samples.Length;
    }

    // --- Poly inspection ---------------------------------------------------

    /// <summary>
    /// The sequences the poly page draws must be the ones the audio path runs.
    ///
    /// They are generated by running a real counter rather than by a closed form
    /// precisely so the picture cannot drift from the sound -- this asserts that
    /// holds, by checking each drawn sequence actually repeats at its stated period.
    /// A display built on a second, "equivalent" implementation is how a tool ends up
    /// teaching something the chip does not do.
    /// </summary>
    private static void PolyInspection()
    {
        foreach (var kind in new[] { PolyCounters.Kind.Four, PolyCounters.Kind.Five,
                                     PolyCounters.Kind.Nine })
        {
            int period = PolyCounters.Period(kind);
            var bits = PolyCounters.Sequence(kind, period * 2 + 8);

            bool repeats = true;
            for (int i = 0; i + period < bits.Length; i++) repeats &= bits[i] == bits[i + period];
            Check(repeats, $"the {kind} counter's drawn sequence repeats at {period}");

            // ...and not sooner, or the stated period is a lie and the "repeats here"
            // marker points at the wrong cell.
            bool sooner = false;
            for (int candidate = 1; candidate < period; candidate++)
            {
                bool all = true;
                for (int i = 0; i + candidate < bits.Length && all; i++)
                    all &= bits[i] == bits[i + candidate];
                sooner |= all;
            }
            Check(!sooner, $"...and never sooner, so {period} is the real period");
        }

        // The gating table has to agree with the chip, or the page highlights the
        // wrong counter while the audio consults another.
        Check(PolyCounters.GatesFor(5, false).Length == 0
              && PolyCounters.GatesFor(7, false).Length == 0,
              "pure tone consults no counter at all");
        Check(PolyCounters.GatesFor(4, false)[0] == PolyCounters.Kind.Seventeen
              && PolyCounters.GatesFor(4, true)[0] == PolyCounters.Kind.Nine,
              "the long counter follows AUDCTL's 9-bit bit");
        Check(PolyCounters.GatesFor(0, false).Length == 2,
              "distortion 0 is gated by two counters together");
    }

    // --- Scope -------------------------------------------------------------

    /// <summary>
    /// The scope's triggering, which is the difference between a readable trace and a
    /// smear.
    ///
    /// Audio blocks and frames have no shared rate, so drawing the newest N samples
    /// each frame slides the waveform sideways at the beat frequency between them --
    /// and it does not merely look untidy, it looks like the chip is doing something
    /// it is not. Triggering on a rising zero crossing is what pins it. That failure
    /// is invisible in a still screenshot and obvious in motion, which is exactly the
    /// wrong way round for catching it by eye.
    /// </summary>
    private static void Scope()
    {
        var scope = new ScopeTrace();
        var window = new float[256];

        // Nothing pushed yet: must be safe and blank rather than showing stale memory.
        scope.Fill(window);
        bool blank = true;
        foreach (float v in window) blank &= v == 0f;
        Check(blank && !scope.Ready(256), "an unfed scope reads blank rather than stale");

        // A steady tone, pushed in blocks whose size shares no factor with its period,
        // which is the situation that makes an untriggered trace slide.
        const int period = 37;
        var block = new float[100];
        int phase = 0;
        for (int b = 0; b < 40; b++)
        {
            for (int i = 0; i < block.Length; i++, phase++)
                block[i] = (phase % period) < period / 2 ? 0.8f : -0.8f;
            scope.Push(block, block.Length);
        }

        Check(scope.Ready(256), "...and is ready once enough has been rendered");

        // Every fill must start at the same point in the wave, or the trace walks.
        var first = new float[256];
        scope.Fill(first);
        bool stable = true;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            scope.Fill(window);
            for (int i = 0; i < window.Length; i++) stable &= window[i] == first[i];
        }
        Check(stable, "repeated fills return the identical window, so the trace holds still");

        // And that point is a rising edge, not wherever the buffer happened to end.
        Check(first[0] > 0f, "...triggered on a rising edge rather than free-running");
    }

    // --- Envelope ----------------------------------------------------------

    /// <summary>
    /// The step player, against its own rules and against real rendered time.
    ///
    /// Envelope timing is the classic "sounds about right" bug: quantise a sweep to
    /// the audio block size and it still sweeps, still lasts roughly the right time,
    /// and is wrong in a way no amount of listening will localise. So the duration is
    /// measured out of rendered audio rather than trusted.
    /// </summary>
    /// <summary>
    /// The design layer, checked against the thing it is supposed to be able to express.
    ///
    /// <b>The first assertion is the whole argument for this API.</b> Describe the lasers
    /// sample's classic zap the way a person would -- about thirty-nine hundred hertz down
    /// to under three hundred, seventy-three milliseconds, pure tone, fairly loud -- and the
    /// compiler must produce the four numbers that zap was written with by hand. Not close
    /// to them; them. If it ever cannot, the friendly layer has started being a second
    /// engine rather than a way of writing down the first one.
    ///
    /// (This used to be Tempest's player shot and the ROM's bytes. Those moved out of the
    /// library to a private bank; a hand-written voice is the same test of independence.)
    /// </summary>
    /// <summary>
    /// The WAV writer, read back the way a player reads it rather than compared with
    /// itself: the header fields at their offsets, the sizes a player trusts, and the
    /// samples after them.
    /// </summary>
    private static void WavFiles()
    {
        var samples = SoundRender.Render(StepEnvelope.ZapAudf, StepEnvelope.ZapAudc,
                                         220, PokeyClocks.RimWalkerHz, 0.5f, 22050);
        byte[] wav = SoundRender.ToWav(samples, 22050);

        string Tag(int at) => System.Text.Encoding.ASCII.GetString(wav, at, 4);
        int Int(int at) => BitConverter.ToInt32(wav, at);
        int Short(int at) => BitConverter.ToInt16(wav, at);

        Check(Tag(0) == "RIFF" && Tag(8) == "WAVE" && Tag(12) == "fmt " && Tag(36) == "data",
              "the four chunk tags sit where a player looks for them");
        Check(Int(4) == wav.Length - 8, $"RIFF's size counts everything after itself ({Int(4)})");
        Check(Int(40) == samples.Length * 2 && wav.Length == 44 + samples.Length * 2,
              $"data's size is the samples alone: {samples.Length} samples, {Int(40)} bytes");
        Check(Short(20) == 1 && Short(22) == 1 && Short(34) == 16,
              "PCM, mono, 16-bit");
        Check(Int(24) == 22050 && Int(28) == 22050 * 2 && Short(32) == 2,
              "the sample rate, byte rate and block align agree with each other");

        // The samples are ToPcm16's, byte for byte, little-endian.
        byte[] pcm = SoundRender.ToPcm16(samples);
        bool same = true;
        for (int i = 0; i < pcm.Length; i++) same &= wav[44 + i] == pcm[i];
        Check(same, "the body is exactly the packed samples");

        // A caller with shorts in hand gets the same file.
        byte[] fromShorts = SoundRender.ToWav(SoundRender.ToPcm16Samples(samples), 22050);
        Check(fromShorts.AsSpan().SequenceEqual(wav), "...and the short[] overload writes the identical file");
    }

    private static void Designs()
    {
        var shot = new SoundDesign
        {
            FromHz = 3857,
            ToHz = 278,
            Seconds = 0.073,
            Timbre = Timbre.Tone,
            Level = 9 / 15.0,
        }.Compile();

        Check(shot.Audf == new StepEnvelope.Step(6, 1, +6, 16),
              $"the zap's AUDF compiles to the hand-written (6, 1, +6, 16) -- got {shot.Audf}");
        Check(shot.Audc == new StepEnvelope.Step(169, 16, 0, 1),
              $"...and its AUDC to (169, 16, 0, 1) -- got {shot.Audc}");

        // What it reports having achieved has to be the truth, not the request.
        Check(Math.Abs(shot.StartHz - 3857) < 2.0, $"...starting at {shot.StartHz:0.0} Hz");
        Check(Math.Abs(shot.Seconds - 16 / 220.0) < 0.001, $"...lasting {shot.Seconds:0.000} s");

        // A tremolo is the AUDC trick: a delta of -8 flips the volume between two levels.
        var rasp = new SoundDesign
        {
            FromHz = 1400, ToHz = 140, Seconds = 0.136, Timbre = Timbre.Tone,
            Level = 0.2, Move = LevelMove.Tremolo, TremoloHz = 110,
        }.Compile();
        Check(rasp.Audc.Delta == -8 && rasp.Audc.Inner == 1,
              $"a 110 Hz tremolo at a 220 Hz tick compiles to a -8 every tick -- got {rasp.Audc}");

        // --- A slow move has to widen inner rather than round the delta to nothing ---
        //
        // Five dividers over a hundred ticks cannot step every tick: an integer delta
        // would round to zero and the pitch would sit still.
        var drift = new SoundDesign { FromHz = 200, ToHz = 190, Seconds = 1.0, TickHz = 100 }.Compile();
        Check(drift.Audf.Delta != 0, "a slow sweep still moves rather than rounding its delta to zero");
        Check(drift.Audf.Inner > 1, "...by holding each value longer instead of stepping every tick");
        Check(drift.EndHz < drift.StartHz, "...and it ends below where it started");

        // --- A fade lands on silence rather than wrapping past it into loud ---
        var fade = new SoundDesign { Level = 1.0, Move = LevelMove.FadeOut, Seconds = 0.5 }.Compile();
        int last = StepEnvelope.ValueAt(new[] { fade.Audc }, StepEnvelope.Span(fade.Audc), true);
        Check((last & PokeyChip.AudcVolumeMask) == 0,
              $"a fade-out ends silent rather than wrapping -- ended at volume {last & 0x0f}");

        // --- The packed byte is assembled, not asserted ---
        var noise = new SoundDesign { Timbre = Timbre.Noise, Level = 10 / 15.0 }.Compile();
        Check(noise.Distortion == 4 && noise.Volume == 10,
              $"a design's timbre and level pack into one byte -- got distortion {noise.Distortion}, volume {noise.Volume}");
        Check(noise.Audc.Value == 4 * 32 + 10, "...which is distortion x 32 + volume, as ever");

        // --- Hold means hold ---
        var steady = new SoundDesign { FromHz = 440, ToHz = 440, Seconds = 0.25 }.Compile();
        Check(steady.Audf.Delta == 0, "a steady note compiles to no movement at all");
        Check(Math.Abs(steady.StartHz - steady.EndHz) < 0.001, "...and does not drift");
    }

    private static void Envelope()
    {
        // The lasers sample's rasp: a falling sweep with a -8 tremolo on the volume.
        var raspAudf = new[] { new StepEnvelope.Step(18, 1, +6, 30) };
        var raspAudc = new[] { new StepEnvelope.Step(163, 1, -8, 30) };

        // The sweep endpoints, independent of any clock.
        //
        // It closes at 192, not 198. The ISR's outer counter loads with `outer` and the
        // delta fires only on the decrements that leave it non-zero -- the one reaching
        // zero advances the step instead -- so a step applies outer-1 deltas: 18 + 6 x 29.
        // Tempest's own shot is where this was found: read off its ISR, its sweep ended
        // one delta short of the figure a summary of it gave.
        Check(StepEnvelope.ValueAt(raspAudf, 0, false) == 18, "the rasp opens at AUDF 18");
        Check(StepEnvelope.ValueAt(raspAudf, 30, false) == 192,
              "...and closes at AUDF 192, being outer-1 deltas rather than outer");

        // AUDC's delta is masked to the low nibble, so the volume alternates rather
        // than decaying -- 3, 11, 3, 11. That is a tremolo, and it is most of why a
        // rasp reads as one. Sweeping the whole byte would instead walk the distortion
        // field and change the timbre.
        int v0 = StepEnvelope.ValueAt(raspAudc, 0, true);
        int v1 = StepEnvelope.ValueAt(raspAudc, 1, true);
        int v2 = StepEnvelope.ValueAt(raspAudc, 2, true);
        Check((v0 & 0x0f) == 3 && (v1 & 0x0f) == 11 && (v2 & 0x0f) == 3,
              $"AUDC volume alternates 3, 11, 3 -- a tremolo, not a decay");
        Check((v0 & 0xf0) == (v1 & 0xf0) && (v1 & 0xf0) == (v2 & 0xf0),
              "...and the distortion nibble never moves");

        // --- What a delta is worth on AUDC ---
        //
        // Because the delta is masked into four bits, only delta mod 16 does anything:
        // -8 and +8 are the same edit, and every whole multiple of sixteen is a no-op.
        // The envelope panel annotates the cell with this, so it has to be true.
        Check(StepEnvelope.VolumeDelta(-8) == StepEnvelope.VolumeDelta(8),
              "on AUDC a delta of -8 and +8 are the same edit");
        Check(StepEnvelope.VolumeDelta(-8).Cycle == 2,
              "...and 8 gives the tightest cycle there is, which is the rasp's buzz");
        Check(StepEnvelope.VolumeDelta(16).Effective == 0
              && StepEnvelope.VolumeDelta(-64).Effective == 0,
              "a delta that is a whole multiple of 16 moves the volume not at all");
        Check(StepEnvelope.VolumeDelta(-1).Cycle == 16,
              "an odd delta walks all sixteen levels before repeating");

        // The claim that matters is not that the arithmetic above is self-consistent but
        // that it agrees with the emulator. So walk every delta the grid will accept
        // through ValueAt and measure the volume's real period.
        bool cyclesAgree = true, worstDelta = false;
        for (int delta = -64; delta <= 64 && !worstDelta; delta++)
        {
            // Outer is large so the step never runs out of deltas before the ring closes.
            var probe = new[] { new StepEnvelope.Step(0xa0, 1, delta, 200) };
            int start = StepEnvelope.ValueAt(probe, 0, true) & 0x0f;

            int period = 0;
            for (int tick = 1; tick <= 16 && period == 0; tick++)
                if ((StepEnvelope.ValueAt(probe, tick, true) & 0x0f) == start) period = tick;

            if (period != StepEnvelope.VolumeDelta(delta).Cycle) { cyclesAgree = false; worstDelta = true; }
        }
        Check(cyclesAgree, "...and every delta from -64 to 64 cycles exactly as VolumeDelta claims");

        // --- An independent restatement of the walk, against this one ---
        //
        // RimWalker's Core/Sfx.cs once carried a second implementation of the step walk,
        // written before this library existed. This restates that formula, rather than
        // calling the library, and asserts the two agree for every tick of a spread of
        // voices -- a slow inner, a wrap off the bottom of AUDF, a volume walking to zero,
        // a swell, the tremolo. It is the guard that the walk cannot silently change.
        var shipped = new (string Name, StepEnvelope.Step Audf, StepEnvelope.Step Audc)[]
        {
            ("zap",         new(20,  1, +6,  30),  new(0xa8, 4, -1,  8)),
            ("rasp",        new(18,  1, +6,  30),  new(0xa3, 1, -8, 30)),
            ("slow climb",  new(120, 6, -1,  80),  new(0xa7, 60, 0,  8)),
            ("wrap down",   new(180, 3, -2, 150),  new(0x24, 3,  0, 150)),
            ("swell",       new(110, 2, -15,  6),  new(0xa2, 2, +1,  6)),
            ("fade out",    new(4,   2, +3,  10),  new(0x0c, 2, -1, 10)),
            ("blip",        new(60,  1,  0,   3),  new(0x85, 1, -2,  3)),
        };

        // The formula as Sfx.cs had it, restated rather than called, so this is an
        // independent check and not the same code agreeing with itself.
        static int OldDeltas(int tick, int inner, int outer)
            => Math.Min(tick / Math.Max(1, inner), Math.Max(0, outer - 1));

        static int OldAudfAt(in StepEnvelope.Step s, int tick)
        {
            int value = s.Value;
            for (int i = 0; i < OldDeltas(tick, s.Inner, s.Outer); i++) value = (value + s.Delta) & 0xff;
            return value;
        }

        static int OldAudcAt(in StepEnvelope.Step s, int tick)
        {
            int value = s.Value;
            for (int i = 0; i < OldDeltas(tick, s.Inner, s.Outer); i++)
                value = (value & 0xf0) | ((value + s.Delta) & 0x0f);
            return value;
        }

        bool audfAgrees = true, audcAgrees = true;
        string firstMiss = "";

        foreach (var (name, audf, audc) in shipped)
        {
            var fChain = new[] { audf };
            var cChain = new[] { audc };

            for (int tick = 0; tick <= StepEnvelope.Span(audf); tick++)
                if (StepEnvelope.ValueAt(fChain, tick, false) != OldAudfAt(audf, tick))
                {
                    if (audfAgrees) firstMiss = $"{name} AUDF at tick {tick}";
                    audfAgrees = false;
                }

            for (int tick = 0; tick <= StepEnvelope.Span(audc); tick++)
                if (StepEnvelope.ValueAt(cChain, tick, true) != OldAudcAt(audc, tick))
                {
                    if (audcAgrees) firstMiss = $"{name} AUDC at tick {tick}";
                    audcAgrees = false;
                }
        }

        Check(audfAgrees, "the game's AUDF walk and this one agree on every shipped voice"
                          + (audfAgrees ? "" : $" -- first differs at {firstMiss}"));
        Check(audcAgrees, "...and so do their AUDC walks, nibble mask included"
                          + (audcAgrees ? "" : $" -- first differs at {firstMiss}"));

        // --- Chains ---
        //
        // An eight-step flourish: a flat note, then seven swoops. A player that stopped
        // after the first step would render an eighth of it and still sound like
        // something, which is exactly why this is asserted rather than listened to.
        var lives = new StepEnvelope.Step[]
        {
            new(70, 1, 0, 1), new(70, 1, -1, 70), new(50, 1, -1, 50), new(36, 1, -1, 36),
            new(28, 1, -1, 28), new(22, 1, -1, 22), new(19, 1, -1, 19), new(17, 1, -1, 17),
        };
        Check(lives.Length == 8, "the flourish is eight steps");

        int expectedSpan = 0;
        foreach (var step in lives) expectedSpan += step.Inner * step.Outer;
        Check(StepEnvelope.Span(lives) == expectedSpan,
              $"a chain spans the sum of its steps ({expectedSpan} ticks)");

        // Each step walks the divider down toward 1, and a falling divider is a
        // rising pitch -- four ascending swoops, not one long slide.
        Check(StepEnvelope.ValueAt(lives, 0, false) == 70, "...opening at AUDF 70");
        Check(StepEnvelope.ValueAt(lives, 1, false) == 70, "...still 70 as the second step begins");
        Check(StepEnvelope.ValueAt(lives, 70, false) == 1, "...reaching 1 by the end of step two");
        Check(StepEnvelope.ValueAt(lives, 71, false) == 50, "...then jumping to 50 to start step three");

        // Past the end a chain holds its last value, as POKEY does when nothing
        // writes to it.
        int last = StepEnvelope.ValueAt(lives, expectedSpan + 500, false);
        Check(last == StepEnvelope.ValueAt(lives, expectedSpan, false),
              "...and holds its final value past the end rather than wrapping");

        // Duration, measured from rendered audio rather than from TotalTicks/TickHz.
        var chip = new PokeyChip { BaseClockHz = PokeyClocks.TempestHz };
        var envelope = new StepEnvelope { TickHz = 220.0, Channel = 0 };
        envelope.Trigger(chip);

        const double sampleRate = 44100.0;
        int total = (int)(sampleRate * 0.5);
        var buffer = new float[total];
        envelope.Render(chip, buffer, 0, total, sampleRate);

        Check(!envelope.Playing, "the envelope stops on its own rather than running forever");

        // Where the tone actually stops, found by its LAST ZERO CROSSING rather than
        // by an amplitude threshold.
        //
        // The obvious version -- last sample above some level -- overshoots by about
        // 49ms, and that is not an envelope error: it is Render's DC blocker decaying.
        // Its time constant is 1/(2*pi*8Hz), near 20ms, so falling below a small
        // threshold takes a couple of those regardless of when the chip went quiet.
        // A tone oscillates and a decaying DC tail does not, so the last crossing
        // marks the end of the sound and ignores the filter entirely.
        int lastCrossing = 0;
        for (int i = 1; i < total; i++)
            if ((buffer[i - 1] <= 0f) != (buffer[i] <= 0f)) lastCrossing = i;

        double measured = lastCrossing / sampleRate;
        double expected = envelope.TotalTicks / envelope.TickHz;

        // A little over one period of the closing pitch, which is the resolution a
        // zero crossing can offer.
        Check(Math.Abs(measured - expected) < 0.015,
              $"the tone lasts {measured:0.000}s against {expected:0.000}s expected");

        // Block-size independence: the whole point of slicing at tick boundaries.
        var a = Fresh(); var whole = new float[8192];
        a.Env.Render(a.Chip, whole, 0, whole.Length, sampleRate);

        var b = Fresh(); var pieces = new float[8192];
        for (int at = 0; at < pieces.Length; at += 137)
            b.Env.Render(b.Chip, pieces, at, Math.Min(137, pieces.Length - at), sampleRate);

        bool identical = true;
        for (int i = 0; i < whole.Length; i++) identical &= whole[i] == pieces[i];
        Check(identical, "an envelope renders the same in 137-sample blocks as in one go");

        static (PokeyChip Chip, StepEnvelope Env) Fresh()
        {
            var chip = new PokeyChip { BaseClockHz = PokeyClocks.TempestHz };
            var env = new StepEnvelope { TickHz = 220.0, Channel = 0 };
            env.Trigger(chip);
            return (chip, env);
        }
    }

    // --- Measurement -------------------------------------------------------

    /// <summary>Fundamental of a rendered buffer, by counting zero crossings. Crude,
    /// but these are square waves with no noise, which is the one case where it is
    /// also exact.</summary>
    private static double MeasureHz(PokeyChip chip, double sampleRate, double seconds)
    {
        int count = (int)(sampleRate * seconds);
        var buffer = new float[count];
        chip.Render(buffer, 0, count, sampleRate);

        // Skip the head while the DC blocker settles.
        int start = count / 4;
        int crossings = 0;
        int first = -1, last = -1;

        for (int i = start + 1; i < count; i++)
        {
            if ((buffer[i - 1] <= 0f) == (buffer[i] <= 0f)) continue;
            if (first < 0) first = i;
            last = i;
            crossings++;
        }

        if (crossings < 2 || last <= first) return 0.0;

        // Two crossings per cycle, measured between the first and last so partial
        // cycles at either end cannot bias it.
        double cycles = (crossings - 1) / 2.0;
        double span = (last - first) / sampleRate;
        return cycles / span;
    }
}
