using System;
using System.Collections.Generic;
using System.Text;

namespace Speech;

/// <summary>Three formant frequencies, in Hz. F1 and F2 carry nearly all of a vowel's
/// identity; F3 mostly carries speaker identity, and is the first thing a low sample
/// rate loses.</summary>
public readonly record struct Formants(double F1, double F2, double F3);

/// <summary>
/// One speech sound, as the parameters a formant synthesiser needs to make it.
///
/// <b>Two formant targets, not one.</b> A steady vowel has them equal, but a diphthong
/// is defined by movement -- "bite" is not a vowel, it is a journey from "ah" toward
/// "ee" -- and so are the glides. Collapsing that to a single target would make AY, OY
/// and OW indistinguishable from the vowels they start on.
///
/// <b>Voicing and noise are separate amplitudes rather than one flag.</b> That is not
/// generality for its own sake: voiced fricatives are the reason. /z/ and /v/ are
/// periodic and turbulent at the same time, and no boolean can say so.
/// </summary>
public readonly record struct Phoneme(
    string Code,
    string Example,
    Formants Start,
    Formants End,
    double Voicing,
    double Noise,
    double DurationMs,
    double ClosureMs = 0.0,
    double Hold = 0.30,
    double AntiHz = 0.0,
    bool Syllabic = false,
    int Stress = Phoneme.Stressed,
    double NoteHz = 0.0)
{
    /// <summary>
    /// Stress levels, in the ARPABET convention: 0 unstressed, 1 primary, 2 secondary.
    ///
    /// <b>Unmarked defaults to stressed, and that is a compatibility decision.</b>
    /// Stress only reaches a phoneme by being written on it, so a hand-typed string like
    /// <c>HH EH L OW</c> sounds exactly as it did before any of this existed. Defaulting
    /// the other way would silently reduce every vowel anyone had ever typed to a schwa.
    /// </summary>
    public const int Unstressed = 0, Stressed = 1, Secondary = 2;

    /// <summary>A stop consonant, identified by having a closure. The silence is most
    /// of what makes /p/ read as a stop rather than a puff of noise.</summary>
    public bool IsStop => ClosureMs > 0.0;

    /// <summary>Whether this sound is shaped by an anti-resonance -- true only for the
    /// nasals.</summary>
    public bool IsNasal => AntiHz > 0.0;

    /// <summary>
    /// Whether this sound is made with a constriction -- turbulence, a closure, or the
    /// nose -- rather than an open vocal tract.
    ///
    /// Derived rather than stored, because the existing fields already say it: a vowel
    /// or glide has no noise, no closure and no nasal zero, and everything else has at
    /// least one. Used only by the speed control, where it matters that these compress
    /// less than vowels do.
    /// </summary>
    public bool IsConstricted => IsStop || IsNasal || Noise > 0.0;
}

/// <summary>
/// The phoneme inventory: ARPABET, the set CMU's pronouncing dictionary uses.
///
/// <b>Why ARPABET.</b> It is the machine-readable convention for American English, it
/// is what any letter-to-sound rule set would emit, and it is what a pronunciation
/// dictionary would hand over -- so building against it now means a text front end
/// later is a new layer rather than a rewrite of this one.
///
/// <b>Where the numbers come from, honestly.</b> The vowels are Peterson and Barney's
/// 1952 adult-male averages, the same measured data the rest of this project already
/// uses, and they are solid. <b>Everything else is conventional textbook values
/// adjusted by ear</b> -- consonant targets, durations, hold fractions and
/// anti-resonance frequencies alike. They should be read as a starting point rather
/// than as evidence.
///
/// <b>Three parameters here exist for coarticulation</b>, and they are what separates
/// this from a table of isolated sounds:
///
/// <list type="bullet">
/// <item><b>Hold</b> -- how much of its own span a phoneme spends sitting on its
/// target rather than travelling to or from its neighbours. This is coarticulation
/// resistance: a vowel yields and is largely transition, a stop or fricative holds its
/// place and gives way only at the edges. Everything used to be a flat 0.30, which made
/// a /t/ as mushy as an /aa/.</item>
/// <item><b>Locus</b> -- a stop's formant targets are the place of articulation it is
/// made at, so the transition out of it points the following vowel's F2 in a direction
/// that identifies it. This is the cue that actually distinguishes /b/ from /d/ from
/// /g/; the burst alone barely does.</item>
/// <item><b>AntiHz</b> -- the nasal anti-resonance. See <c>FormantSynth</c>: a nasal is
/// not a weak vowel, it is a sound with a hole in its spectrum, and the hole is what
/// tells /m/ from /n/ from /ng/.</item>
/// </list>
/// </summary>
public static class Phonemes
{
    /// <summary>Silence. Also what an unrecognised token becomes, so a typo is a gap
    /// rather than a crash.</summary>
    public const string Silence = "SIL";

    private static readonly Formants Neutral = new(500, 1500, 2500);

    // --- Coarticulation resistance ------------------------------------------
    //
    // How much of its span each class of sound holds its target for. Higher is more
    // resistant: crisper articulation, faster transitions at the edges. Lower yields
    // more to whatever is on either side.

    /// <summary>Vowels give way. They are long, and most of their length is spent
    /// travelling between the consonants around them.</summary>
    private const double HoldVowel = 0.30;

    /// <summary>Glides are movement by definition -- /w/ and /y/ are barely more than a
    /// transition with a direction -- so they resist least of all.</summary>
    private const double HoldGlide = 0.20;

    /// <summary>A fricative is a sustained posture; it holds.</summary>
    private const double HoldFricative = 0.70;

    /// <summary>A stop holds its locus and then releases fast. This is the number that
    /// makes a release sound like a release.</summary>
    private const double HoldStop = 0.75;

    private const double HoldNasal = 0.60;

    /// <summary>Bandwidth of the nasal anti-resonance. Wide, because a nasal zero is a
    /// broad spectral hollow rather than a notch.</summary>
    public const double AntiBandwidthHz = 200.0;

    private static readonly Phoneme[] Table =
    {
        // --- Vowels (Peterson and Barney 1952, adult male) -----------------
        Vowel("IY", "beet", new Formants(270, 2290, 3010), 120),
        Vowel("IH", "bit", new Formants(390, 1990, 2550), 90),
        Vowel("EH", "bet", new Formants(530, 1840, 2480), 110),
        Vowel("AE", "bat", new Formants(660, 1720, 2410), 130),
        Vowel("AA", "father", new Formants(730, 1090, 2440), 130),
        Vowel("AO", "bought", new Formants(570, 840, 2410), 130),
        Vowel("UH", "book", new Formants(440, 1020, 2240), 90),
        Vowel("UW", "boot", new Formants(300, 870, 2240), 120),
        Vowel("AH", "but", new Formants(640, 1190, 2390), 90),
        Vowel("ER", "bird", new Formants(490, 1350, 1690), 130),
        Vowel("AX", "about", Neutral, 70),

        // --- Diphthongs: a start and a destination -------------------------
        Diphthong("EY", "bait", new Formants(530, 1840, 2480), new Formants(270, 2290, 3010), 170),
        Diphthong("AY", "bite", new Formants(730, 1090, 2440), new Formants(270, 2290, 3010), 180),
        Diphthong("OY", "boy", new Formants(570, 840, 2410), new Formants(270, 2290, 3010), 190),
        Diphthong("AW", "bout", new Formants(730, 1090, 2440), new Formants(300, 870, 2240), 180),
        Diphthong("OW", "boat", new Formants(570, 840, 2410), new Formants(300, 870, 2240), 160),

        // --- Glides and liquids --------------------------------------------
        Glide("W", "we", new Formants(300, 800, 2200), 70),
        Glide("Y", "yes", new Formants(270, 2300, 3000), 70),
        Glide("L", "let", new Formants(400, 1200, 2600), 80),
        // The low third formant is the whole signature of American /r/, and it is the
        // one consonant here whose F3 matters more than its F2.
        Glide("R", "red", new Formants(350, 1050, 1600), 80),

        // --- Nasals: a low murmur with a hole punched in it ------------------
        // The poles are similar across all three -- the nasal cavity dominates the
        // murmur -- so it is largely the zero that tells them apart, which is why they
        // sounded like three flavours of the same weak vowel before it existed.
        Nasal("M", "me", new Formants(250, 1100, 2200), 1000),
        Nasal("N", "no", new Formants(250, 1700, 2600), 1900),
        Nasal("NG", "sing", new Formants(250, 2300, 2750), 3000),

        // --- Fricatives ----------------------------------------------------
        // The sibilants ask for energy far above where the low sample rates can put it
        // -- real /s/ peaks above 5kHz -- so their third formant is set as high as the
        // model can carry and the resonator clamps it to whatever the rate allows.
        Fricative("F", "fee", new Formants(400, 1100, 2200), 0.0, 0.50, 110),
        Fricative("V", "vee", new Formants(400, 1100, 2200), 0.6, 0.30, 80),
        Fricative("TH", "thin", new Formants(400, 1400, 2200), 0.0, 0.40, 100),
        Fricative("DH", "then", new Formants(400, 1400, 2200), 0.6, 0.25, 70),
        Fricative("S", "see", new Formants(400, 1400, 3800), 0.0, 0.90, 120),
        Fricative("Z", "zoo", new Formants(400, 1400, 3800), 0.5, 0.60, 90),
        Fricative("SH", "she", new Formants(400, 1800, 2500), 0.0, 0.90, 120),
        Fricative("ZH", "measure", new Formants(400, 1800, 2500), 0.5, 0.60, 90),
        Fricative("HH", "he", Neutral, 0.0, 0.40, 70),

        // --- Stops: a closure, a burst, and a locus -------------------------
        // The formants here are places of articulation, not sounds. Labials point F2
        // low, alveolars to the middle, velars high with F3 pulled down to meet them --
        // the "velar pinch". The following vowel's transition away from these is what
        // identifies the consonant.
        Stop("P", "pea", new Formants(400, 800, 2100), 0.0, 0.50),
        Stop("B", "bee", new Formants(400, 800, 2100), 0.5, 0.30),
        Stop("T", "tea", new Formants(400, 1800, 2600), 0.0, 0.60),
        Stop("D", "dee", new Formants(400, 1800, 2600), 0.5, 0.30),
        Stop("K", "key", new Formants(400, 2300, 2500), 0.0, 0.60),
        Stop("G", "green", new Formants(400, 2300, 2500), 0.5, 0.30),

        // --- Affricates: a stop released into a fricative --------------------
        new Phoneme("CH", "cheese", new Formants(400, 1800, 2500), new Formants(400, 1800, 2500),
                    0.0, 0.80, 110, 40, HoldStop),
        new Phoneme("JH", "gee", new Formants(400, 1800, 2500), new Formants(400, 1800, 2500),
                    0.5, 0.50, 90, 30, HoldStop),

        // --- Silence -------------------------------------------------------
        new Phoneme(Silence, "pause", Neutral, Neutral, 0.0, 0.0, 80, 0.0, 1.0),
    };

    // Syllabic: true for the vowels and diphthongs and nothing else. It is what
    // syllable counting and stress assignment work from, and it is stated rather than
    // inferred -- every way of deriving it from the other fields (a vowel is the one
    // with no noise and this particular Hold...) couples two things that have no reason
    // to move together.
    private static Phoneme Vowel(string code, string example, Formants shape, double ms)
        => new(code, example, shape, shape, 1.0, 0.0, ms, 0.0, HoldVowel, 0.0, true);

    private static Phoneme Diphthong(string code, string example, Formants from, Formants to, double ms)
        => new(code, example, from, to, 1.0, 0.0, ms, 0.0, HoldVowel, 0.0, true);

    private static Phoneme Glide(string code, string example, Formants shape, double ms)
        => new(code, example, shape, shape, 1.0, 0.0, ms, 0.0, HoldGlide);

    private static Phoneme Nasal(string code, string example, Formants shape, double antiHz)
        => new(code, example, shape, shape, 0.8, 0.0, 80, 0.0, HoldNasal, antiHz);

    private static Phoneme Fricative(string code, string example, Formants shape,
                                     double voicing, double noise, double ms)
        => new(code, example, shape, shape, voicing, noise, ms, 0.0, HoldFricative);

    private static Phoneme Stop(string code, string example, Formants shape, double voicing, double noise)
        => new(code, example, shape, shape, voicing, noise,
               voicing > 0 ? 50 : 60, voicing > 0 ? 30 : 45, HoldStop);

    private static readonly Dictionary<string, Phoneme> ByCode = Build();

    private static Dictionary<string, Phoneme> Build()
    {
        var map = new Dictionary<string, Phoneme>(StringComparer.Ordinal);
        foreach (var phoneme in Table) map[phoneme.Code] = phoneme;
        return map;
    }

    /// <summary>Every code, in table order, for a UI that wants to list them.</summary>
    public static IReadOnlyList<Phoneme> All => Table;

    public static bool TryGet(string code, out Phoneme phoneme) => ByCode.TryGetValue(code, out phoneme);

    /// <summary>
    /// Parses a whitespace-separated phoneme string.
    ///
    /// <b>Stress digits are read, not stripped.</b> <c>AH0</c>, <c>EY1</c> and
    /// <c>IY2</c> resolve to the same phonemes carrying different stress -- the ARPABET
    /// convention, which is what a pronouncing dictionary emits, so text pasted from one
    /// arrives with its prosody intact. An unmarked phoneme is stressed; see
    /// <see cref="Phoneme.Unstressed"/> for why that is the safe default rather than
    /// the tidy one.
    ///
    /// A hyphen, comma or sentence-ending mark is a pause. <b>An unrecognised token
    /// becomes silence and is reported</b> rather than throwing: a typo in the middle of
    /// a sentence should leave a gap you can hear and a message you can read, not lose
    /// the whole utterance.
    /// </summary>
    public static List<Phoneme> Parse(string text, out List<string> unknown)
    {
        var result = new List<Phoneme>();
        unknown = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        foreach (string raw in text.Split(new[] { ' ', '\t', '\r', '\n', '/', '.' },
                                          StringSplitOptions.RemoveEmptyEntries))
        {
            string token = Normalise(raw, out int stress);
            if (token.Length == 0) continue;

            if (token is "-" or "," or "?" or "!" or ";" or ":")
            {
                result.Add(ByCode[Silence]);
                continue;
            }

            if (TryGet(token, out var phoneme))
            {
                result.Add(stress >= 0 ? phoneme with { Stress = stress } : phoneme);
            }
            else
            {
                unknown.Add(raw);
                result.Add(ByCode[Silence]);
            }
        }

        return result;
    }

    /// <summary>Splits a token into its code and its stress digit, if it carries one.
    /// <paramref name="stress"/> is -1 when unmarked, so the phoneme keeps its
    /// default.</summary>
    private static string Normalise(string raw, out int stress)
    {
        stress = -1;
        var builder = new StringBuilder(raw.Length);

        foreach (char c in raw)
        {
            if (char.IsDigit(c)) { stress = c - '0'; continue; }
            builder.Append(char.ToUpperInvariant(c));
        }
        return builder.ToString();
    }

    /// <summary>
    /// Writes a phoneme sequence back out, stress digits included.
    ///
    /// The inverse of <see cref="Parse"/>, and the reason stress is visible in the UI at
    /// all: the text box shows <c>G AA1 R D AH0 N</c>, so what the prosody decided is
    /// something you can read and argue with rather than an invisible property of the
    /// sound. Marks are written only on syllabic phonemes, where they mean anything.
    /// </summary>
    public static string Format(IReadOnlyList<Phoneme> phonemes)
    {
        var parts = new List<string>(phonemes.Count);

        foreach (var phoneme in phonemes)
        {
            if (phoneme.Code == Silence) { parts.Add("-"); continue; }
            parts.Add(phoneme.Syllabic ? $"{phoneme.Code}{phoneme.Stress}" : phoneme.Code);
        }

        return string.Join(" ", parts);
    }

    /// <summary>Total spoken length, closures included, so a caller can size a buffer
    /// before rendering one.</summary>
    public static double DurationMs(IReadOnlyList<Phoneme> phonemes)
    {
        double total = 0.0;
        for (int i = 0; i < phonemes.Count; i++) total += phonemes[i].ClosureMs + phonemes[i].DurationMs;
        return total;
    }
}
