using System;
using System.Collections.Generic;

namespace Speech;

/// <summary>
/// One slice of synthesiser control parameters. What the resonators are set to and how
/// hard they are being driven, at one instant.
///
/// <paramref name="Nasality"/> and <paramref name="AntiHz"/> are carried separately
/// rather than folded into one "zero frequency, or nothing" field, and that split is
/// what makes a nasal blend into its neighbours instead of switching on. Interpolating
/// a frequency toward "none" would sweep the notch down through the whole spectrum on
/// the way out of every /m/; fading the amount while the frequency drifts gently is the
/// only version that does not audibly whistle.
/// </summary>
public readonly record struct Frame(Formants Shape, double Voicing, double Noise,
                                    double Nasality, double AntiHz, double Pitch, double NoteHz);

/// <summary>An utterance as the synthesiser wants it: control frames, their rate, and
/// how long the whole thing lasts.</summary>
public sealed class Utterance
{
    public Frame[] Frames { get; }
    public double FrameRateHz { get; }
    public double TotalMs { get; }

    public Utterance(Frame[] frames, double frameRateHz, double totalMs)
    {
        Frames = frames;
        FrameRateHz = frameRateHz;
        TotalMs = totalMs;
    }
}

/// <summary>
/// Turns a phoneme sequence into a stream of control frames.
///
/// <b>This is the layer that decides whether it sounds like speech or like a list of
/// sounds.</b> Butting phoneme targets up against each other and stepping between them
/// produces something no mouth could make: real articulators have mass, so a formant
/// leaving one target for the next takes time getting there, and that movement is
/// itself a cue -- most of what tells /b/ from /d/ is in the transition, not in the
/// burst.
///
/// <b>The model here is targets and interpolation</b>, the oldest and simplest thing
/// that works. Each phoneme plants its target in the middle of its own span rather than
/// filling it, and everything between two targets is a straight line. That means every
/// boundary automatically gets a transition whose length is set by the two phonemes
/// around it, with no boundary rules to write and nothing to keep in step.
///
/// <b>How much each sound resists being moved is its own property</b>, not a constant.
/// A vowel gives way and spends most of its length travelling; a stop holds its locus
/// and releases fast. That is <see cref="Phoneme.Hold"/>, and it is the difference
/// between articulation and mush -- with one flat value for everything, a /t/ was as
/// slurred as an /aa/.
/// </summary>
public static class FrameStream
{
    /// <summary>Five-millisecond frames. Finer than the 10ms a period synthesiser would
    /// have used, because it is free here and because the shortest thing in the
    /// inventory -- a 30ms stop closure -- is only six frames even at this rate.</summary>
    public const double DefaultFrameRateHz = 200.0;

    /// <summary>
    /// Where in its own span a phoneme reaches its target, from how much it holds.
    ///
    /// Everything outside these two points is transition. A hold of 0.30 puts the
    /// targets at 0.35 and 0.65 -- the middle third held, the rest travelling. A hold
    /// of 0.75 puts them at 0.125 and 0.875, so the sound is on its target almost
    /// immediately and leaves it late, which is what a released stop does.
    /// </summary>
    private static (double In, double Out) Targets(double hold)
    {
        double clamped = Math.Clamp(hold, 0.0, 0.98);
        double lead = (1.0 - clamped) / 2.0;
        return (lead, 1.0 - lead);
    }

    /// <summary>Where the notch sits when nothing is nasal. Neutral, and never heard --
    /// it exists only so the frequency has somewhere sensible to be while the amount is
    /// zero, rather than lurching from 0Hz on every nasal onset.</summary>
    private const double RestingAntiHz = 1500.0;

    private readonly record struct Point(double TimeMs, Formants Shape, double Voicing, double Noise,
                                         double Nasality, double AntiHz, double Pitch, double NoteHz);

    /// <summary>Range the speed control is clamped to. Past these the durations stop
    /// meaning anything: everything blurs into one gesture at the top, and the frame
    /// count grows without the sound improving at the bottom.</summary>
    public const double MinSpeed = 0.4, MaxSpeed = 3.0;

    /// <summary>
    /// How much of a speed change a constricted sound takes, against a vowel taking all
    /// of it.
    ///
    /// <b>Speaking faster is not playing a tape faster</b>, and this is the difference.
    /// When people speed up, vowels compress hard while stops, fricatives and nasals
    /// barely move -- a closure still has to happen, a fricative still needs enough
    /// turbulence to be identified. Scaling everything uniformly is the obvious version
    /// and it sounds exactly like what it is: a recording played at the wrong rate,
    /// with the pitch left behind as the giveaway that it is not a person.
    ///
    /// 0.6 is a rule of thumb, not a measurement. The effect is real and well
    /// established; this particular number was chosen because it is audibly better than
    /// 1.0 and not so low that fast speech becomes all consonants.
    /// </summary>
    private const double ConstrictedElasticity = 0.6;

    public static Utterance Build(IReadOnlyList<Phoneme> phonemes,
                                  double frameRateHz = DefaultFrameRateHz,
                                  double speed = 1.0)
    {
        // A scale, not a rate: speed 2 means everything takes half as long.
        double scale = 1.0 / Math.Clamp(speed, MinSpeed, MaxSpeed);
        double constricted = 1.0 + (scale - 1.0) * ConstrictedElasticity;

        // Two independent multipliers on every duration: the speed control, and stress.
        // They compose rather than competing -- an unstressed syllable is shorter than a
        // stressed one whatever speed the whole utterance is spoken at.
        //
        // <b>Unless it is sung, in which case the note owns the duration outright.</b>
        // Song.Sing already worked out how long the syllable runs, from the tempo and
        // the note's length, and subtracted what its consonants cost. Scaling that
        // afterwards undoes the arithmetic: stress was quietly taking 30% off every
        // syllable marked unstressed, so the Daisy chorus came out at 21.9s against the
        // 25s its 60 beats at 150bpm ask for. Tempo is the speed control for singing,
        // and the only one.
        double Stretch(in Phoneme phoneme)
            => phoneme.NoteHz > 0.0
                ? 1.0
                : (phoneme.IsConstricted ? constricted : scale) * Prosody.Duration(phoneme);

        // Totalled here rather than from Phonemes.DurationMs, which reports the table's
        // own unscaled length and is still what a caller wants when asking how long a
        // sequence is written to be.
        double totalMs = 0.0;
        foreach (var phoneme in phonemes)
            totalMs += (phoneme.ClosureMs + phoneme.DurationMs) * Stretch(phoneme);

        int frameCount = Math.Max(1, (int)Math.Round(totalMs / 1000.0 * frameRateHz));

        if (phonemes.Count == 0)
            return new Utterance(
                new[] { new Frame(new Formants(500, 1500, 2500), 0, 0, 0, RestingAntiHz, 1.0, 0.0) },
                frameRateHz, 0.0);

        var points = new List<Point>(phonemes.Count * 2 + 2);
        double at = 0.0;

        foreach (var phoneme in phonemes)
        {
            double nasality = phoneme.IsNasal ? 1.0 : 0.0;
            double antiHz = phoneme.IsNasal ? phoneme.AntiHz : RestingAntiHz;
            double pitch = Prosody.Pitch(phoneme);
            double note = phoneme.NoteHz;
            var (targetIn, targetOut) = Targets(phoneme.Hold);

            double stretch = Stretch(phoneme);
            double durationMs = phoneme.DurationMs * stretch;
            double closureMs = phoneme.ClosureMs * stretch;

            if (closureMs > 0.0)
            {
                // A stop's silence. Both ends pinned at zero amplitude so the closure
                // is genuinely silent rather than a ramp through it -- and so the burst
                // that follows rises from nothing, which is what makes it a burst.
                //
                // The closure holds the stop's own locus, so the formants are already
                // in place when the burst arrives and the only thing that moves after
                // it is the transition into whatever follows. That transition is the
                // cue that identifies the consonant.
                points.Add(new Point(at, phoneme.Start, 0.0, 0.0, nasality, antiHz, pitch, note));
                points.Add(new Point(at + closureMs, phoneme.Start, 0.0, 0.0, nasality, antiHz, pitch, note));
                at += closureMs;
            }

            points.Add(new Point(at + targetIn * durationMs, phoneme.Start,
                                 phoneme.Voicing, phoneme.Noise, nasality, antiHz, pitch, note));
            points.Add(new Point(at + targetOut * durationMs, phoneme.End,
                                 phoneme.Voicing, phoneme.Noise, nasality, antiHz, pitch, note));
            at += durationMs;
        }

        // Pin both ends by repeating the first and last control values, so the edges are
        // held rather than extrapolated. Without this a single steady phoneme -- whose
        // only two points sit at 35% and 65% -- would have nothing defined outside that
        // middle third.
        points.Insert(0, points[0] with { TimeMs = 0.0 });
        points.Add(points[points.Count - 1] with { TimeMs = totalMs });

        var frames = new Frame[frameCount];
        int cursor = 0;

        for (int i = 0; i < frameCount; i++)
        {
            double timeMs = i / frameRateHz * 1000.0;

            while (cursor < points.Count - 2 && points[cursor + 1].TimeMs < timeMs) cursor++;

            var a = points[cursor];
            var b = points[cursor + 1];
            double span = b.TimeMs - a.TimeMs;
            double t = span > 1e-9 ? Math.Clamp((timeMs - a.TimeMs) / span, 0.0, 1.0) : 0.0;

            frames[i] = new Frame(
                new Formants(Lerp(a.Shape.F1, b.Shape.F1, t),
                             Lerp(a.Shape.F2, b.Shape.F2, t),
                             Lerp(a.Shape.F3, b.Shape.F3, t)),
                Lerp(a.Voicing, b.Voicing, t),
                Lerp(a.Noise, b.Noise, t),
                Lerp(a.Nasality, b.Nasality, t),
                Lerp(a.AntiHz, b.AntiHz, t),
                Lerp(a.Pitch, b.Pitch, t),
                Lerp(a.NoteHz, b.NoteHz, t));
        }

        return new Utterance(frames, frameRateHz, totalMs);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
