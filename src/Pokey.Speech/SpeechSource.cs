using System;
using System.Collections.Generic;

namespace Speech;

/// <summary>
/// The front door: short helpers that produce a ready-to-play sample buffer.
///
/// Everything here is a thin arrangement of <see cref="Phonemes"/>,
/// <see cref="FrameStream"/> and <see cref="FormantSynth"/>. It exists so a caller that
/// just wants a vowel, or just wants to say something, does not have to assemble three
/// objects to get it.
///
/// <b>Levels are the caller's, not this library's.</b> Nothing here mentions four bits
/// or POKEY -- <paramref name="maxLevel"/> is passed in, because the synthesiser has no
/// business knowing what it is being played through. That is the whole point of this
/// living in its own project.
/// </summary>
public static class SpeechSource
{
    public const double DefaultPitchHz = FormantSynth.DefaultPitchHz;

    /// <summary>
    /// A spread of vowels for demonstration, taken from the phoneme inventory rather
    /// than listed again -- one copy of Peterson and Barney's numbers, not two.
    ///
    /// The order is chosen so the pair that matters sits at index 0 and 4: "ee" and
    /// "oo" have nearly equal F1 and opposite F2, which makes them the test of whether
    /// a synthesis path can carry the second formant at all.
    /// </summary>
    public static readonly (string Label, Formants Shape)[] Vowels = BuildVowels();

    private static (string Label, Formants Shape)[] BuildVowels()
    {
        (string Code, string Label)[] chosen =
        {
            ("IY", "ee  as in heed"),
            ("EH", "eh  as in head"),
            ("AA", "ah  as in hod"),
            ("AO", "aw  as in hawed"),
            ("UW", "oo  as in who'd"),
            ("ER", "er  as in heard"),
        };

        var result = new (string, Formants)[chosen.Length];
        for (int i = 0; i < chosen.Length; i++)
        {
            Phonemes.TryGet(chosen[i].Code, out var phoneme);
            result[i] = (chosen[i].Label, phoneme.Start);
        }
        return result;
    }

    /// <summary>
    /// Ready-made utterances, as phoneme strings.
    ///
    /// <b>They live here rather than in a front end so the self-test can prove they
    /// parse.</b> A shipped example with a typo in it is worse than no example: it
    /// speaks a silent gap where a phoneme should be, and the first thing anyone tries
    /// is the example.
    ///
    /// The phrases are deliberately period pieces -- HAL is 1968, WarGames 1983, and
    /// S.A.M. itself 1982 -- and each also happens to exercise something. "Good
    /// morning Dave" has all three nasals in one word; the WarGames line is a question,
    /// which is the one thing the pitch contour cannot yet do; "she sells" is sibilants,
    /// which is what a low interrupt rate destroys first.
    ///
    /// Hyphens are phrase pauses, placed where a speaker would breathe rather than
    /// between every word -- word gaps everywhere sounds like a station announcement.
    /// </summary>
    /// <remarks>
    /// Each carries its English as well as its phonemes. The two are kept side by side
    /// rather than deriving one from the other, and deliberately: the phoneme strings
    /// here are hand-checked, while <see cref="LetterToSound"/> is a rule set that is
    /// only mostly right, so an example is a demonstration of what the synthesiser can
    /// do rather than a demonstration of what the rules happen to produce today. The
    /// English is what the text box shows, so you can see both and compare.
    /// </remarks>
    public static readonly (string Label, string English, string Phonemes, string Melody)[] Examples =
    {
        (
            "Hello world",
            "Hello world.",
            "HH EH L OW - W ER L D",
            ""
            ),
        (
            "Good morning Dave", "Good morning, Dave.",
            "G UH D - M AO R N IH NG - D EY V",
            ""
            ),
        (
            "Shall we play a game?",
            "Shall we play a game?",
            "SH AE0 L W IY1 P L EY1 EY G EY1 M",
            "250: C2/1.5 C2/1.5 F2/1.5 E2/.8 A1/1.5"
            ),
        (
            "My name is Stephen Hawking. -- I am an astro physicist",
            "My name is Stephen Hawking. -- I am an astro physicist",
            "M AY1 N EY1 M IH0 Z S T IY1 V AH0 N HH AO1 K IH0 NG - - - AY1 AE0 M AE0 N AE1 S T R OW0 F IH1 S IH0 S IH0 S T",
            ""
            ),
        (
            "I am afraid I cannot do that", 
            "I am afraid I cannot do that.",
            "AY1 AE0 M AH1 F R EY0 D AY1 K AE1 N AA0 T D UW0 DH AE1 T", 
            ""
            ),
        (
            "Open the pod bay doors", 
            "Open the pod bay doors.",
            "OW P AH N - DH AH - P AA D B EY - D AO R Z", 
            ""
            ),
        (
            "She sells sea shells", 
            "She sells sea shells.", 
            "SH IY - S EH L Z - S IY - SH EH L Z", 
            ""
            ),

        // --- Sung ----------------------------------------------------------
        //
        // Daisy Bell, Harry Dacre 1892, long out of copyright. Sung by an IBM 704 at
        // Bell Labs in 1961 -- the first computer to sing anything -- which is why HAL
        // sings it in 2001, Arthur C. Clarke having been in the room.
        //
        // <b>The lyrics are right and the tune is from memory.</b> The words are a
        // matter of record; these notes and rhythms are a recollection of a very
        // familiar melody and have not been checked against a score. The shape is right
        // -- it opens on a descending arpeggio -- but individual notes may not be, and
        // the rhythm is a guess at a waltz. It is a text box for exactly this reason:
        // if it sounds wrong to you, it probably is, and it is one edit to fix.
        ("♪ Daisy Bell — opening", "Daisy, Daisy, give me your answer do!",
            "D EY1 Z IY0 - D EY1 Z IY0 - G IH1 V M IY0 Y AO1 R AE1 N S ER0 D UW1",
            "150: C5 A4 F4/2 C4/2 D4 E4 F4 D4 C4 F4/2"),

        // The whole chorus: 51 syllables on 51 notes. The six phrases are 10, 10, 8, 7,
        // 8 and 8 syllables, and the first two being equal is not a coincidence -- they
        // are musically parallel and carry the same tune, which is the one part of this
        // transcription there is structural reason to trust.
        ("♪ Daisy Bell — full chorus",
            "Daisy, Daisy, give me your answer do! I'm half crazy, all for the love of you! "
            + "It won't be a stylish marriage, I can't afford a carriage. "
            + "But you'll look sweet upon the seat of a bicycle made for two.",

            "D EY1 Z IY0 - D EY1 Z IY0 - G IH1 V M IY0 Y AO1 R AE1 N S ER0 D UW1 - "
            + "AY1 M HH AE1 F - K R EY1 Z IY0 - AO1 L F AO0 R DH AH0 L AH1 V AH0 V Y UW1 - "
            + "IH1 T W OW1 N T B IY1 AH0 - S T AY1 L IH0 SH M EH1 R IH0 JH - "
            + "AY1 K AE1 N T AH0 F AO1 R D AH0 - K EH1 R IH0 JH - "
            + "B AH1 T Y UW1 L L UH1 K S W IY1 T - AH0 P AA1 N DH AH0 S IY1 T - "
            + "AH0 V AH0 - B AY1 S IH0 K AH0 L - M EY1 D F AO0 R T UW1",

            "150: C5 A4 F4/2 C4/2 D4 E4 F4 D4 C4 F4/2 "
            + "C5 A4 F4/2 C4/2 D4 E4 F4 D4 C4 F4/2 "
            + "G4 A4 A4 A4 Bb4 A4 G4 F4 "
            + "F4 G4 G4 G4 A4 G4 F4/2 "
            + "A4 A4 C5 C5 Bb4 A4 G4 A4 "
            + "C4 C4 F4 A4 C5 A4 F4/2 C4/2"),
        // Lavender's Blue, traditional English, in print by about 1672 as "Diddle
        // Diddle, or The Kind Country Lovers" -- so long out of copyright. The refrain
        // was "diddle diddle" then and became "dilly dilly" later. <b>Note the 1948
        // Burl Ives / Disney version is a copyrighted arrangement with added verses;
        // this is only the traditional couplet, which is not.</b>
        //
        // Both lines are twelve syllables, which is the scansion doing its job -- if a
        // transcription of this comes out uneven, the transcription is wrong.
        ("♪ Lavender's Blue (dilly dilly)",
            "Lavender's blue, dilly dilly, lavender's green. "
            + "When I am king, dilly dilly, you shall be queen.",

            "L AE1 V AH0 N D ER0 Z B L UW1 - D IH1 L IY0 D IH1 L IY0 - "
            + "L AE1 V AH0 N D ER0 Z G R IY1 N - "
            + "W EH1 N AY1 AE1 M K IH1 NG - D IH1 L IY0 D IH1 L IY0 - "
            + "Y UW1 SH AE1 L B IY1 K W IY1 N",

            // The two lines share a tune and differ only in where they land: the first
            // ends on D, unresolved, and the second on C, which is what makes it a
            // couplet rather than two lines.
            "132: G4 G4 A4 G4/2 E4 E4 C4 C4 D4 D4 E4 D4/2 "
            + "G4 G4 A4 G4/2 E4 E4 C4 C4 D4 D4 E4 C4/2"),

        // Solfège: the tuning check that says what it is as it goes. Eight syllables,
        // eight notes, one major scale -- and unlike a scale on a neutral "ma", a wrong
        // note is identifiable by name rather than merely audible as wrong.
        //
        // "Re" is /reɪ/ and "ti" is /tiː/; "ti" replaced the older "si" so that no two
        // syllables begin with the same letter.
        ("♪ Do re mi — one octave up", "",
            "D OW R EY M IY F AA S OW L AA T IY D OW",
            "140: C4 D4 E4 F4 G4 A4 B4 C5"),
        ("♪ Do re mi — and back down", "",
            "D OW R EY M IY F AA S OW L AA T IY D OW "
            + "T IY L AA S OW F AA M IY R EY D OW",
            "140: C4 D4 E4 F4 G4 A4 B4 C5/2 B4 A4 G4 F4 E4 D4 C4/2"),

        // Single sounds and minimal pairs, marked with a dot so they read as a second
        // group. No separator entry: a combo box cannot make one unselectable, and an
        // item that does something harmless-but-surprising when picked is worse than
        // no divider at all.
        // No English for these: they are not words, and inventing a spelling that
        // happens to produce them would be a lie about what the rules can do.
        ("· ee (heed)", "", "IY", ""),
        ("· oo (who'd)", "", "UW", ""),
        ("· oo to ee — the F2 sweep", "", "UW IY", ""),
        ("· m n ng — the three nasals", "", "AA M AA - AA N AA - AA NG AA", ""),
        ("· b d g — the three stop loci", "", "AA B AA - AA D AA - AA G AA", ""),
        ("· s sh — sibilants, lost first", "", "AA S AA - AA SH AA", ""),
    };

    // --- Saying things -----------------------------------------------------

    /// <summary>
    /// Speaks a phoneme string: <c>HH EH L OW</c>.
    ///
    /// <b>Phonemes, not English.</b> Letter-to-sound rules are a separate layer and a
    /// separate stage; this is the one underneath it. S.A.M. shipped exactly this
    /// alongside its text mode, and for the same two reasons -- it is what you debug
    /// the synthesiser with, and it is the only way to say a word the rules get wrong.
    ///
    /// <paramref name="unknown"/> collects tokens that matched nothing. They are spoken
    /// as silence rather than throwing, so a typo mid-sentence leaves a gap you can
    /// hear and a message you can read instead of losing the utterance.
    /// </summary>
    /// <param name="speed">Overrides the voice's own suggested speed when given. The
    /// profile carries one so that picking a voice sets a sensible pace, but the caller
    /// still owns it -- two things silently controlling one number is how a speed
    /// control ends up appearing not to work.</param>
    public static byte[] Speak(string phonemeText, double sampleRateHz, int maxLevel,
                               out List<string> unknown, VoiceProfile? voice = null,
                               double? speed = null, Intonation intonation = Intonation.Falling,
                               string melody = null)
    {
        var speaker = voice ?? VoiceProfile.Default;

        var phonemes = Phonemes.Parse(phonemeText, out unknown);
        if (phonemes.Count == 0) return Array.Empty<byte>();

        // A melody, if there is one, replaces the prosody rather than adding to it: a
        // sung syllable's pitch and length come from its note, not from where the stress
        // fell or how far the sentence had declined.
        if (!string.IsNullOrWhiteSpace(melody)) phonemes = Song.Sing(phonemes, melody, out _);

        var utterance = FrameStream.Build(phonemes, FrameStream.DefaultFrameRateHz,
                                          speed ?? speaker.Speed);
        return FormantSynth.Quantise(
            FormantSynth.Render(utterance, sampleRateHz, speaker, 20260830, intonation), maxLevel);
    }

    /// <summary>
    /// Whether a piece of text asks a question.
    ///
    /// A question mark, and nothing cleverer. English also marks questions
    /// syntactically -- inversion, wh-words -- and a rising contour is wrong for many
    /// of those anyway ("where are you" falls), so punctuation is both the simplest
    /// signal and the more reliable one.
    /// </summary>
    public static Intonation IntonationOf(string text)
        => text != null && text.Contains('?') ? Intonation.Rising : Intonation.Falling;

    // --- Single sounds, for demonstration ----------------------------------

    public static byte[] Vowel(Formants shape, double seconds, double sampleRateHz, int maxLevel,
                               double pitchHz = DefaultPitchHz)
        => One(new Phoneme("", "vowel", shape, shape, 1.0, 0.0, seconds * 1000.0),
               sampleRateHz, maxLevel, pitchHz);

    /// <summary>A glide between two vowel shapes. The clearest demonstration available
    /// of what a synthesis path does with F2, because between "oo" and "ee" the second
    /// formant travels most of its range while the first barely moves.
    ///
    /// Held loosely, so the sweep occupies most of the sound rather than being a quick
    /// move between two long steady halves -- the movement is the thing on show.</summary>
    public static byte[] Glide(Formants from, Formants to, double seconds, double sampleRateHz,
                               int maxLevel, double pitchHz = DefaultPitchHz)
        => One(new Phoneme("", "glide", from, to, 1.0, 0.0, seconds * 1000.0, 0.0, 0.10),
               sampleRateHz, maxLevel, pitchHz);

    /// <summary>An unvoiced fricative: the same resonators driven by noise instead of a
    /// pulse train.</summary>
    public static byte[] Fricative(Formants shape, double seconds, double sampleRateHz, int maxLevel)
        => One(new Phoneme("", "fricative", shape, shape, 0.0, 1.0, seconds * 1000.0),
               sampleRateHz, maxLevel, DefaultPitchHz);

    /// <summary>The single-sound demos take a pitch rather than a whole voice, because a
    /// pitch is all they vary. It becomes the default voice with that one field
    /// changed, which keeps every other synthesis parameter identical to what the rest
    /// of the library uses.</summary>
    private static byte[] One(Phoneme phoneme, double sampleRateHz, int maxLevel, double pitchHz)
    {
        var utterance = FrameStream.Build(new[] { phoneme });
        var voice = VoiceProfile.Default with { PitchHz = pitchHz };
        return FormantSynth.Quantise(FormantSynth.Render(utterance, sampleRateHz, voice), maxLevel);
    }

    /// <summary>A plain sine, for hearing what the quantisation costs by itself with no
    /// speech in the way. The control condition.</summary>
    public static byte[] Sine(double hz, double seconds, double sampleRateHz, int maxLevel)
    {
        int count = Math.Max(1, (int)(seconds * sampleRateHz));
        var signal = new double[count];
        for (int i = 0; i < count; i++) signal[i] = Math.Sin(2.0 * Math.PI * hz * i / sampleRateHz);
        FormantSynth.Fade(signal, sampleRateHz);
        return FormantSynth.Quantise(signal, maxLevel);
    }
}
