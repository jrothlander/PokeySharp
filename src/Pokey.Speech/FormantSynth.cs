using System;

namespace Speech;

/// <summary>
/// The synthesiser: control frames in, samples out.
///
/// A source shaped by three resonances -- the model behind S.A.M., the Votrax SC-01,
/// and every formant synthesiser since Klatt. Nothing here knows what a phoneme is;
/// it is handed a stream of formant targets and amplitudes and follows it.
///
/// <b>Frame-driven rather than segment-driven</b>, which is the change that made
/// arbitrary utterances possible. The earlier version took one formant pair and
/// interpolated across the whole buffer, which is exactly enough for a single vowel or
/// a glide between two and cannot express anything longer. The resonators were always
/// retuned per sample, though, so following a frame stream instead cost nothing at the
/// bottom -- all the new work is above, in deciding what the frames should say.
/// </summary>
public static class FormantSynth
{
    /// <summary>
    /// One two-pole resonator: a single formant.
    ///
    /// <b>This is the thing a square-wave chip cannot do</b>, and the reason speech has
    /// to be synthesised in software and poured through a DAC rather than played on
    /// POKEY's own oscillators. A formant is a resonance -- a broad peak with a
    /// bandwidth -- and an oscillator has neither.
    /// </summary>
    private struct Resonator
    {
        private double _a, _b, _c, _y1, _y2;

        public void Tune(double hz, double bandwidthHz, double sampleRate)
        {
            // Clamped below Nyquist, and this is load-bearing rather than defensive.
            // /s/ asks for a third formant at 3800Hz; at an 8kHz period sample rate
            // that is only just representable, and at 4kHz it is nearly twice Nyquist.
            // Unclamped, the pole angle wraps past pi and the filter resonates at a
            // mirrored frequency -- a plausible-sounding, entirely wrong one. Clamping
            // instead makes a rate too low to carry a formant simply fail to carry it,
            // which is the true statement and the one the rate control exists to
            // demonstrate.
            hz = Math.Clamp(hz, 50.0, sampleRate * 0.45);

            double r = Math.Exp(-Math.PI * bandwidthHz / sampleRate);
            double theta = 2.0 * Math.PI * hz / sampleRate;
            _b = 2.0 * r * Math.Cos(theta);
            _c = -r * r;
            _a = 1.0 - _b - _c;   // unity DC gain, so a low formant is not louder for free
        }

        public double Step(double x)
        {
            double y = _a * x + _b * _y1 + _c * _y2;
            _y2 = _y1;
            _y1 = y;
            return y;
        }
    }

    /// <summary>
    /// A two-zero section: an anti-resonance, the opposite of a formant.
    ///
    /// <b>This is what makes a nasal a nasal.</b> A resonator adds a peak; this cuts a
    /// hole. When the soft palate opens, the nasal cavity hangs off the vocal tract as
    /// a side branch and swallows a band of the spectrum -- and since the murmur's
    /// poles are much the same for /m/, /n/ and /ng/, it is largely *where the hole is*
    /// that tells them apart. Without it all three came out as the same weak vowel,
    /// which is exactly how they sounded.
    ///
    /// Zeros rather than poles, so it is an FIR section and cannot ring or blow up --
    /// it holds past inputs, not past outputs.
    /// </summary>
    private struct AntiResonator
    {
        private double _a, _b, _c, _x1, _x2;

        public void Tune(double hz, double bandwidthHz, double sampleRate)
        {
            hz = Math.Clamp(hz, 50.0, sampleRate * 0.45);

            double r = Math.Exp(-Math.PI * bandwidthHz / sampleRate);
            double theta = 2.0 * Math.PI * hz / sampleRate;
            double b = -2.0 * r * Math.Cos(theta);
            double c = r * r;

            // Normalised to unity at DC, so opening the notch does not also change how
            // loud the sound is -- otherwise every nasal would duck the whole mix.
            // Guarded because the normaliser collapses toward zero as the notch nears
            // DC, which would turn a quiet nasal into a very loud one.
            double gain = 1.0 + b + c;
            gain = Math.Abs(gain) < 0.05 ? 20.0 : 1.0 / gain;

            _a = gain;
            _b = b * gain;
            _c = c * gain;
        }

        public double Step(double x)
        {
            double y = _a * x + _b * _x1 + _c * _x2;
            _x2 = _x1;
            _x1 = x;
            return y;
        }
    }

    // Formant bandwidths, the formant scale and the glottal rolloff now live on
    // VoiceProfile. They are properties of a speaker rather than of the language, and
    // as constants they meant the synthesiser could only ever be one person. They are
    // still not varied per phoneme -- that variation is real but small next to the
    // formant frequencies, and modelling it would be inventing precision.

    /// <summary>Relative formant amplitudes, roughly the spectral tilt of a real vowel.
    /// Summed with alternating signs, the standard parallel-branch trick for
    /// approximating the spectral valleys a cascade produces between its peaks.</summary>
    private const double Amplitude1 = 1.0, Amplitude2 = 0.6, Amplitude3 = 0.3;

    /// <summary>Typical adult male pitch. Speech is intelligible across a wide range of
    /// this -- the formants carry the words, not the pitch.</summary>
    public const double DefaultPitchHz = 120.0;

    /// <summary>
    /// How hard the noise source is driven relative to voicing.
    ///
    /// See the excitation sum in <see cref="Render"/> for why the two cannot simply be
    /// given equal amplitudes. <b>The value was measured to, not reasoned to</b>, and
    /// the arithmetic was badly wrong: an estimate of the two sources' RMS said 0.12
    /// would balance them, and at 0.12 the fricative was still four times the vowel.
    /// The relationship is not linear, because the quantiser normalises to whichever
    /// sound is loudest -- so making the fricative quieter also makes the vowel louder.
    /// 0.015 puts a vowel about 1.6x above a fricative beside it, which the self-test
    /// asserts. Real speech is more extreme still; this stops short of that so /s/
    /// stays clearly audible.
    /// </summary>
    private const double NoiseScale = 0.015;

    public static double[] Render(Utterance utterance, double sampleRateHz,
                                  VoiceProfile? voice = null, int seed = 20260830,
                                  Intonation intonation = Intonation.Falling)
    {
        // Nullable rather than a defaulted struct: a record struct's `default` is all
        // zeroes, which is a voice with no pitch and no bandwidths rather than a
        // sensible one, and it would fail silently.
        var speaker = voice ?? VoiceProfile.Default;

        int count = Math.Max(1, (int)(utterance.TotalMs / 1000.0 * sampleRateHz));
        var signal = new double[count];

        Resonator f1 = default, f2 = default, f3 = default;
        AntiResonator nasal = default;

        // Deterministic, for the same reason a poly counter is: a source a test can
        // assert on, and an utterance that is the same every time it is spoken.
        var noise = new Random(seed);

        double phase = 0.0, tilt1 = 0.0, tilt2 = 0.0;
        double tilt = GlottalTilt(speaker.GlottalTiltHz, sampleRateHz);

        var frames = utterance.Frames;

        for (int i = 0; i < count; i++)
        {
            double progress = count > 1 ? i / (double)(count - 1) : 0.0;

            // Where we are in the frame stream, interpolated rather than stepped -- a
            // 200Hz frame rate stepped abruptly would put an audible 5ms buzz on every
            // formant move.
            double at = i / sampleRateHz * utterance.FrameRateHz;
            int index = Math.Min((int)at, frames.Length - 1);
            int next = Math.Min(index + 1, frames.Length - 1);
            double blend = at - index;

            var a = frames[index];
            var b = frames[next];

            double shapeF1 = Lerp(a.Shape.F1, b.Shape.F1, blend);
            double shapeF2 = Lerp(a.Shape.F2, b.Shape.F2, blend);
            double shapeF3 = Lerp(a.Shape.F3, b.Shape.F3, blend);
            double voicing = Lerp(a.Voicing, b.Voicing, blend);
            double noiseLevel = Lerp(a.Noise, b.Noise, blend);
            double nasality = Lerp(a.Nasality, b.Nasality, blend);
            double antiHz = Lerp(a.AntiHz, b.AntiHz, blend);
            double accent = Lerp(a.Pitch, b.Pitch, blend);
            double note = Lerp(a.NoteHz, b.NoteHz, blend);

            f1.Tune(shapeF1 * speaker.F1Scale, speaker.Bandwidth1, sampleRateHz);
            f2.Tune(shapeF2 * speaker.F2Scale, speaker.Bandwidth2, sampleRateHz);
            f3.Tune(shapeF3 * speaker.F3Scale, speaker.Bandwidth3, sampleRateHz);
            nasal.Tune(antiHz, Phonemes.AntiBandwidthHz, sampleRateHz);

            // The glottal source: one pulse per cycle, which is physically what excites
            // the vocal tract -- each pulse sets the resonators ringing and the ring is
            // the vowel. A bare impulse train is spectrally flat and sounds like a
            // buzzer, so two one-pole sections stand in for the roughly 12dB per octave
            // a real glottal pulse rolls off at.
            // A note replaces the speaking pitch outright rather than modifying it, and
            // that is the whole difference between singing and speaking. Speech pitch is
            // relative -- an accent above its neighbours, on a line that declines across
            // the sentence -- and a sung note is absolute. Multiplying the two would put
            // the tune out of key by however much the sentence had declined by then.
            //
            // The note is interpolated between frames like everything else, so a
            // syllable change slides rather than steps. That is a small portamento, and
            // it is what a formant synthesiser singing has always sounded like.
            // AccentScale stretches the accent's deviation from flat rather than
            // multiplying the accent itself, so a scale of 0 is a monotone and 2 is
            // twice the movement -- scaling the value directly would raise the whole
            // voice as a side effect of making it more expressive.
            double shaped = 1.0 + (accent - 1.0) * speaker.AccentScale;

            double pitch = note > 0.0
                ? note
                : speaker.PitchHz * Prosody.Contour(intonation, progress) * shaped;

            phase += pitch / sampleRateHz;
            double pulse = 0.0;
            if (phase >= 1.0) { phase -= 1.0; pulse = 1.0; }

            tilt1 += (pulse - tilt1) * tilt;
            tilt2 += (tilt1 - tilt2) * tilt;

            // Voicing and noise together, not either-or. Voiced fricatives are periodic
            // and turbulent at the same moment, and this sum is where that happens.
            //
            // NoiseScale is doing real work. The two sources are not comparable at face
            // value: voicing is a sparse impulse train -- one pulse every 66 samples at
            // 120Hz and 8kHz, near zero the rest of the time -- while noise is
            // continuous, so equal nominal amplitudes leave the noise several times
            // louder in RMS. That is backwards from speech, where a fricative is
            // markedly quieter than a vowel, and it matters beyond taste: Quantise
            // stretches the whole utterance between its own extremes, so one over-loud
            // /s/ compresses every vowel sharing the buffer with it.
            double excitation = voicing * tilt2
                              + noiseLevel * (noise.NextDouble() * 2.0 - 1.0) * NoiseScale;

            double sum = Amplitude1 * f1.Step(excitation)
                       - Amplitude2 * f2.Step(excitation)
                       + Amplitude3 * f3.Step(excitation);

            // The notch is always running -- stepping a filter only when it is wanted
            // would leave it with stale history and make every nasal onset a click --
            // and is mixed in by how nasal this instant is.
            double notched = nasal.Step(sum);
            signal[i] = nasality <= 0.0 ? sum : Lerp(sum, notched, nasality);
        }

        Fade(signal, sampleRateHz);
        return signal;
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>One-pole coefficient for the voice's glottal rolloff corner. Around
    /// 500Hz is the ballpark of a real source; lower is darker and harder.</summary>
    private static double GlottalTilt(double cornerHz, double sampleRateHz)
        => Math.Min(1.0, 2.0 * Math.PI * cornerHz / sampleRateHz);

    /// <summary>Five milliseconds in and out. Without it the buffer starts and ends on a
    /// step, and a step in a coarsely quantised stream is a click loud enough to be
    /// mistaken for the quantisation being worse than it is.</summary>
    public static void Fade(double[] signal, double sampleRateHz)
    {
        int ramp = Math.Min(signal.Length / 2, Math.Max(1, (int)(0.005 * sampleRateHz)));
        for (int i = 0; i < ramp; i++)
        {
            double gain = i / (double)ramp;
            signal[i] *= gain;
            signal[signal.Length - 1 - i] *= gain;
        }
    }

    /// <summary>
    /// Maps a signal onto <paramref name="maxLevel"/> + 1 unipolar levels.
    ///
    /// <b>Unipolar, because the registers this ends up in are.</b> The standing offset
    /// that produces is the caller's problem, and on POKEY it is removed by the DC
    /// blocker in the render path.
    ///
    /// <b>Mapped from the signal's own minimum and maximum, not from its peak
    /// magnitude.</b> A formant synthesiser's output is markedly asymmetric -- impulse
    /// excitation and the parallel branches' alternating signs give it a much larger
    /// positive excursion than negative -- so scaling by the larger of the two and
    /// centring leaves the bottom of the range unreachable. Measured on a vowel at four
    /// bits it was using 2..15: an eighth of the resolution thrown away on a scale that
    /// has none to spare. Stretching min-to-max is still perfectly linear, so the
    /// waveform's shape is untouched; all that changes is an offset.
    /// </summary>
    public static byte[] Quantise(double[] signal, int maxLevel)
    {
        double low = double.MaxValue, high = double.MinValue;
        foreach (double v in signal) { low = Math.Min(low, v); high = Math.Max(high, v); }

        var samples = new byte[signal.Length];
        double span = high - low;
        if (span <= 1e-9) return samples;   // flat input is silence, not a divide by zero

        for (int i = 0; i < signal.Length; i++)
        {
            int level = (int)Math.Round((signal[i] - low) / span * maxLevel);
            samples[i] = (byte)Math.Clamp(level, 0, maxLevel);
        }
        return samples;
    }
}
