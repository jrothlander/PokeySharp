using System;
using System.Collections.Generic;

namespace Speech;

/// <summary>How an utterance's pitch moves across it. English marks a yes/no question
/// by rising at the end and everything else by falling.</summary>
public enum Intonation { Falling, Rising }

/// <summary>
/// Stress, and what follows from it.
///
/// <b>Stress is the keystone of the whole prosody problem</b>, which is why it is worth
/// one small class rather than three scattered ones. Once you know which syllable is
/// stressed you get three things that otherwise need separate machinery: the unstressed
/// vowels reduce to a schwa, the stressed syllable is longer, and the pitch rises on it.
/// Before this existed every vowel was full, every syllable the same length and the
/// pitch a straight line, which is most of what made the output sound like a machine
/// reading a list rather than a person saying a word.
///
/// <b>The assignment heuristic is crude and it is the weak part.</b> English stress
/// genuinely depends on word class, origin and morphology -- RECord the noun against
/// reCORD the verb -- and none of that is available here. First-syllable stress is right
/// for most English content words and wrong for a real minority, and when it is wrong
/// the damage is bounded: the wrong vowel reduces, which is a duller word rather than a
/// different one.
/// </summary>
public static class Prosody
{
    /// <summary>
    /// Vowels that collapse to a schwa when unstressed.
    ///
    /// <b>Deliberately not all of them.</b> The long vowels and diphthongs keep their
    /// identity unstressed -- the IY at the end of "city" is unstressed and is still an
    /// IY -- and reducing them would turn every word to mush. These four are the short
    /// full vowels, which are the ones English actually neutralises. IH is left out too,
    /// though it is borderline: it reduces in "pencil" and does not in "music", and
    /// leaving it alone is the error that costs less.
    /// </summary>
    private static readonly HashSet<string> Reducible = new(StringComparer.Ordinal)
    {
        "EH", "AE", "AA", "AO",
    };

    /// <summary>Suffixes that pull the stress onto the syllable before them rather than
    /// leaving it at the front. A very short list: these are the ones common enough to
    /// be worth the check.</summary>
    private static readonly string[][] StressShifting =
    {
        new[] { "SH", "AH", "N" },   // -tion, -sion: NAtion, staTION... the vowel before
        new[] { "ZH", "AH", "N" },
    };

    /// <summary>
    /// Marks stress across one word and, if asked, reduces what the stress leaves
    /// unstressed.
    ///
    /// <b>The two halves are separable because their risks are not the same.</b> Marking
    /// stress only changes duration and pitch, so a wrong guess is a word with odd
    /// rhythm -- recognisably the right word. Reducing a vowel changes which vowel it
    /// is, so a wrong guess is a different word. That is why hand-written dictionary
    /// entries are marked but never reduced: their author chose those vowels, and
    /// first-syllable stress has no business overruling that, while leaving them
    /// entirely unmarked would make every common word in a sentence rhythmically flat.
    ///
    /// Returns a new list; the input is not modified, and neither is the phoneme table
    /// these were copied from.
    /// </summary>
    public static List<Phoneme> Apply(IReadOnlyList<Phoneme> word, bool reduce = true)
    {
        var result = new List<Phoneme>(word);

        var nuclei = new List<int>();
        for (int i = 0; i < result.Count; i++)
            if (result[i].Syllabic) nuclei.Add(i);

        if (nuclei.Count == 0) return result;

        int stressed = ChooseStressed(result, nuclei);

        for (int n = 0; n < nuclei.Count; n++)
        {
            int at = nuclei[n];
            if (n == stressed) { result[at] = result[at] with { Stress = Phoneme.Stressed }; continue; }

            result[at] = reduce
                ? Reduce(result[at])
                : result[at] with { Stress = Phoneme.Unstressed };
        }

        return result;
    }

    /// <summary>
    /// Marks every syllable of a word unstressed, without touching its vowels.
    ///
    /// For function words, which have no sentence accent. Not reduced: "the" is already
    /// written with the vowel it should have, and the point here is only that it should
    /// be shorter and lower than the words around it, not that it should become
    /// something else.
    /// </summary>
    public static List<Phoneme> Unstress(IReadOnlyList<Phoneme> word)
    {
        var result = new List<Phoneme>(word);

        for (int i = 0; i < result.Count; i++)
            if (result[i].Syllabic) result[i] = result[i] with { Stress = Phoneme.Unstressed };

        return result;
    }

    /// <summary>
    /// Which syllable takes the stress.
    ///
    /// First, unless the word ends in one of the shifting suffixes, in which case the
    /// syllable before it. A one-syllable word is stressed by definition -- there is
    /// nothing for it to be unstressed relative to.
    /// </summary>
    private static int ChooseStressed(IReadOnlyList<Phoneme> word, List<int> nuclei)
    {
        if (nuclei.Count == 1) return 0;

        foreach (string[] suffix in StressShifting)
        {
            if (!EndsWith(word, suffix)) continue;

            // The suffix carries a nucleus of its own, so the one before it is the
            // second from the end.
            return Math.Max(0, nuclei.Count - 2);
        }

        return 0;
    }

    private static bool EndsWith(IReadOnlyList<Phoneme> word, string[] codes)
    {
        if (word.Count < codes.Length) return false;

        for (int i = 0; i < codes.Length; i++)
            if (word[word.Count - codes.Length + i].Code != codes[i]) return false;

        return true;
    }

    /// <summary>
    /// Turns one vowel into its unstressed form.
    ///
    /// A reducible vowel becomes a schwa outright -- it takes AX's formants, not AH's,
    /// because AX is the genuinely neutral one and AH is the full vowel of "but". Every
    /// other vowel simply loses its stress mark, which still shortens it and drops its
    /// pitch without changing what it is.
    /// </summary>
    private static Phoneme Reduce(Phoneme vowel)
    {
        if (!Reducible.Contains(vowel.Code) || !Phonemes.TryGet("AX", out var schwa))
            return vowel with { Stress = Phoneme.Unstressed };

        // The schwa's own formants and hold, but this occurrence's identity as an
        // unstressed vowel. Duration is left to the frame builder, which shortens
        // everything unstressed rather than baking it in here.
        return schwa with { Stress = Phoneme.Unstressed };
    }

    // --- What stress buys --------------------------------------------------

    /// <summary>How much shorter an unstressed syllable is. Applied to syllabic
    /// phonemes only: consonants do not lengthen under stress the way vowels do.</summary>
    public const double UnstressedDuration = 0.70;

    /// <summary>Pitch on a stressed syllable against an unstressed one. Small, because
    /// a large one sounds like singing rather than speaking.</summary>
    public const double StressedPitch = 1.08, UnstressedPitch = 0.94;

    /// <summary>Duration multiplier for one phoneme, from its stress.</summary>
    public static double Duration(in Phoneme phoneme)
        => phoneme.Syllabic && phoneme.Stress == Phoneme.Unstressed ? UnstressedDuration : 1.0;

    /// <summary>Pitch multiplier for one phoneme. Consonants sit at 1.0 and let the
    /// contour interpolate through them, which is roughly what pitch does between two
    /// vowels anyway.</summary>
    public static double Pitch(in Phoneme phoneme)
    {
        if (!phoneme.Syllabic) return 1.0;
        return phoneme.Stress == Phoneme.Unstressed ? UnstressedPitch : StressedPitch;
    }

    /// <summary>
    /// The sentence-level pitch contour, as a multiplier at a point through the
    /// utterance.
    ///
    /// <b>Falling is not a stylistic default; it is what a statement does.</b> Pitch
    /// declines across any English declarative, and a level one sounds like a machine
    /// holding a note. A yes/no question inverts only the end: it declines like anything
    /// else and then rises sharply on the last syllable, which is why the rise here is
    /// confined to the final fifth rather than applied across the whole utterance.
    /// </summary>
    public static double Contour(Intonation intonation, double progress)
    {
        const double Declination = 0.85;

        if (intonation == Intonation.Falling)
            return 1.0 + (Declination - 1.0) * progress;

        // Falls more gently, then turns up. The turn has to be late and steep or it
        // reads as a wobble rather than a question.
        const double Turn = 0.80;
        if (progress < Turn) return 1.0 - 0.08 * (progress / Turn);

        double intoRise = (progress - Turn) / (1.0 - Turn);
        return 0.92 + 0.33 * intoRise;
    }
}
