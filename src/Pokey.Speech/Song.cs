using System;
using System.Collections.Generic;
using System.Globalization;

namespace Speech;

/// <summary>
/// Singing: a melody laid over a lyric.
///
/// <b>The difference between speech and song is where the pitch comes from.</b>
/// Speaking, it is relative -- a stressed syllable is a little above its neighbours, and
/// the whole utterance declines. Singing, it is absolute: the syllable is on a note, and
/// nothing about the sentence moves it. That is the entire change, and it is why this
/// sits on top of the existing synthesiser rather than beside it. Everything else --
/// formants, coarticulation, the nasal zero, the 4-bit nibble -- is unaltered.
///
/// <b>Notes map onto syllables, one each, in order.</b> That is how singing is written
/// and it is the only alignment anyone wants to type: the lyric says what, the melody
/// says how high and how long. Consonants take the note of the syllable they belong to,
/// so a note begins where its syllable begins rather than when the vowel finally
/// arrives.
///
/// <b>The historical note this exists for.</b> The first computer to sing was an IBM 704
/// at Bell Labs in 1961 -- John Kelly and Carol Lochbaum on the voice, Max Mathews on
/// the accompaniment -- and what it sang was "Daisy Bell". Arthur C. Clarke heard it
/// while visiting John Pierce there, which is why HAL sings it while being shut down.
/// That is a vocal-tract model doing what this one does, twenty years earlier and on a
/// machine that filled a room.
/// </summary>
public static class Song
{
    /// <summary>One note: a frequency and a length in beats. <see cref="Hz"/> of zero is
    /// a rest.</summary>
    public readonly record struct Note(double Hz, double Beats);

    public const double DefaultTempo = 120.0;

    /// <summary>Semitone offsets within an octave, from C.</summary>
    private static readonly Dictionary<char, int> Naturals = new()
    {
        ['C'] = 0, ['D'] = 2, ['E'] = 4, ['F'] = 5, ['G'] = 7, ['A'] = 9, ['B'] = 11,
    };

    /// <summary>
    /// A note name to a frequency: <c>C4</c>, <c>F#3</c>, <c>Bb4</c>. Returns 0 for
    /// anything unparseable, which the caller reports rather than throwing on.
    ///
    /// Equal temperament from A4 = 440Hz, via MIDI numbering. Middle C is C4 and comes
    /// out at 261.63Hz, which is the number to check this against.
    /// </summary>
    public static double NoteHz(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return 0.0;

        name = name.Trim().ToUpperInvariant();
        if (!Naturals.TryGetValue(name[0], out int semitone)) return 0.0;

        int at = 1;
        if (at < name.Length && (name[at] == '#' || name[at] == 'B'))
        {
            // A lone "B" is the note; "Bb" is B flat. The difference is whether an
            // octave digit follows, which is why this cannot just look at the character.
            if (name[at] == '#') { semitone++; at++; }
            else if (at + 1 < name.Length) { semitone--; at++; }
        }

        if (at >= name.Length) return 0.0;
        if (!int.TryParse(name.Substring(at), NumberStyles.Integer, CultureInfo.InvariantCulture,
                          out int octave)) return 0.0;

        int midi = (octave + 1) * 12 + semitone;
        return 440.0 * Math.Pow(2.0, (midi - 69) / 12.0);
    }

    /// <summary>
    /// Parses a melody: an optional <c>tempo:</c> then notes, each optionally with
    /// <c>/beats</c>.
    ///
    /// <c>140: C5 A4 F4/2 C4/2</c> -- four notes at 140bpm, the last two twice as long.
    /// A bare note is one beat. <c>R</c> is a rest.
    /// </summary>
    public static List<Note> Parse(string melody, out double tempo, out List<string> unknown)
    {
        var notes = new List<Note>();
        tempo = DefaultTempo;
        unknown = new List<string>();
        if (string.IsNullOrWhiteSpace(melody)) return notes;

        foreach (string raw in melody.Split(new[] { ' ', '\t', '\r', '\n', ',' },
                                            StringSplitOptions.RemoveEmptyEntries))
        {
            string token = raw;

            // A leading "140:" sets the tempo. Written into the melody rather than made
            // a separate control because tempo is part of a tune, not a preference --
            // copying a melody somewhere else should carry its speed with it.
            int colon = token.IndexOf(':');
            if (colon >= 0)
            {
                string head = token.Substring(0, colon);
                if (double.TryParse(head, NumberStyles.Float, CultureInfo.InvariantCulture, out double bpm)
                    && bpm > 0)
                    tempo = Math.Clamp(bpm, 20.0, 400.0);
                else
                    unknown.Add(head);

                token = token.Substring(colon + 1);
                if (token.Length == 0) continue;
            }

            double beats = 1.0;
            int slash = token.IndexOf('/');
            if (slash >= 0)
            {
                string tail = token.Substring(slash + 1);
                if (double.TryParse(tail, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                    && parsed > 0)
                    beats = parsed;
                token = token.Substring(0, slash);
            }

            if (token.Equals("R", StringComparison.OrdinalIgnoreCase))
            {
                notes.Add(new Note(0.0, beats));
                continue;
            }

            double hz = NoteHz(token);
            if (hz <= 0.0) { unknown.Add(raw); continue; }

            notes.Add(new Note(hz, beats));
        }

        return notes;
    }

    /// <summary>
    /// Lays a melody over a phoneme sequence.
    ///
    /// Each syllable takes the next note, and the note's length becomes the syllable's.
    /// <b>The consonants are charged against the note, not added to it</b> -- a syllable
    /// on a half-second note lasts half a second whether it starts with a vowel or with
    /// "str", so the vowel is shortened by whatever its consonants cost. Without that
    /// the rhythm drifts later with every consonant cluster, which is audible within a
    /// bar and unfixable by ear afterwards.
    ///
    /// Notes run out before syllables do, or the other way about, more often than not.
    /// Neither is an error: leftover syllables hold the last note, leftover notes are
    /// dropped, and <paramref name="report"/> says which happened.
    /// </summary>
    public static List<Phoneme> Sing(IReadOnlyList<Phoneme> phonemes, string melody, out string report)
    {
        var result = new List<Phoneme>(phonemes);
        var notes = Parse(melody, out double tempo, out var unknown);

        if (notes.Count == 0)
        {
            report = unknown.Count > 0 ? $"not a note: {string.Join(" ", unknown)}" : "";
            return result;
        }

        var nuclei = new List<int>();
        for (int i = 0; i < result.Count; i++)
            if (result[i].Syllabic) nuclei.Add(i);

        if (nuclei.Count == 0) { report = "nothing to sing -- the lyric has no vowels"; return result; }

        double beatMs = 60000.0 / tempo;

        // Which syllable each phoneme belongs to: the nearest nucleus. Onset consonants
        // therefore join the syllable ahead of them and coda consonants the one behind,
        // which is close enough to real syllabification for the purpose here, which is
        // only deciding whose note a consonant sings.
        var owner = new int[result.Count];
        for (int i = 0; i < result.Count; i++) owner[i] = NearestNucleus(nuclei, i);

        // What each syllable's consonants already cost, so the note can pay for them.
        var consonantMs = new double[nuclei.Count];
        for (int i = 0; i < result.Count; i++)
        {
            if (result[i].Syllabic) continue;
            consonantMs[owner[i]] += result[i].ClosureMs + result[i].DurationMs;
        }

        for (int n = 0; n < nuclei.Count; n++)
        {
            var note = notes[Math.Min(n, notes.Count - 1)];
            double wholeMs = note.Beats * beatMs;

            // At least a short vowel, however greedy the consonants were: a syllable
            // whose note is entirely consumed by its own consonants should sound rushed,
            // not silent.
            double vowelMs = Math.Max(50.0, wholeMs - consonantMs[n]);

            int at = nuclei[n];
            result[at] = result[at] with { DurationMs = vowelMs, NoteHz = note.Hz };
        }

        // Consonants sing their syllable's note too, so it starts when the syllable does.
        for (int i = 0; i < result.Count; i++)
        {
            if (result[i].Syllabic) continue;
            result[i] = result[i] with { NoteHz = notes[Math.Min(owner[i], notes.Count - 1)].Hz };
        }

        report = Describe(nuclei.Count, notes.Count, unknown);
        return result;
    }

    private static int NearestNucleus(List<int> nuclei, int at)
    {
        int best = 0, bestDistance = int.MaxValue;

        for (int n = 0; n < nuclei.Count; n++)
        {
            int distance = Math.Abs(nuclei[n] - at);
            if (distance >= bestDistance) continue;

            best = n;
            bestDistance = distance;
        }
        return best;
    }

    private static string Describe(int syllables, int notes, List<string> unknown)
    {
        var parts = new List<string>();

        if (unknown.Count > 0) parts.Add($"not a note: {string.Join(" ", unknown)}");
        if (notes < syllables) parts.Add($"{syllables - notes} syllables past the end of the melody");
        else if (notes > syllables) parts.Add($"{notes - syllables} notes with no syllable to sing them");
        else parts.Add($"{syllables} syllables on {notes} notes");

        return string.Join("; ", parts);
    }
}
