using System.Collections.Generic;
using Speech;

namespace Demo1;

/// <summary>
/// One way of speaking: which voice, at what rate, through how many levels.
///
/// <b>The three fields are the whole demonstration.</b> A game picks one of these and
/// never thinks about it again, but lined up next to each other they show what each knob
/// actually costs — the profile changes who is talking, the rate changes how much of the
/// spectrum survives, and the level count changes whether it sounds like 1982.
/// </summary>
public readonly record struct VoiceSetting(
    string Name,
    VoiceProfile Profile,
    double DacRateHz,
    int MaxLevel,
    double? Speed,
    float Amplitude,
    string Note)
{
    /// <summary>
    /// The most levels the synthesiser can hand back, because
    /// <see cref="FormantSynth.Quantise"/> returns a <c>byte</c> per sample.
    ///
    /// <b>Worth knowing rather than discovering.</b> Its <c>maxLevel</c> is an int and it
    /// clamps to that value before the cast, so asking for 32767 does not give 16-bit
    /// audio and does not fail either — it truncates every sample modulo 256 and returns
    /// something that sounds like loud noise. A byte is generous for a library written to
    /// feed a 4-bit DAC, and 8 bits is a real, historical step up from 4, so this is a
    /// ceiling rather than a limitation.
    /// </summary>
    public const int MaxUsableLevel = 255;

    public static readonly VoiceSetting[] All =
    {
        new("POKEY 4-BIT, 8 KHZ", VoiceProfile.Default, 8000.0, 15, null, 0.7f,
            "Four bits is all a POKEY's volume nibble holds, and it was the machine's only "
            + "sample path. This is how S.A.M. spoke on an Atari in 1982."),

        new("POKEY 4-BIT, 4 KHZ", VoiceProfile.Default, 4000.0, 15, null, 0.7f,
            "The same voice on a slower interrupt. Everything above 2 kHz is gone, which "
            + "takes the third formant and most of the consonants with it."),

        new("8-BIT, 22 KHZ", VoiceProfile.Default, 22050.0, MaxUsableLevel, null, 0.6f,
            "The same synthesiser, same words, sixteen times the resolution and nearly "
            + "three times the rate. One argument's difference in the call - which is how "
            + "much of the robot was the chip rather than the synthesis."),

        new("DELIBERATE, 8-BIT", VoiceProfile.Deliberate, 22050.0, MaxUsableLevel, null, 0.6f,
            "Lower and slower. This profile was fitted to a reference recording by "
            + "measurement rather than by ear, which moved two of its five numbers the "
            + "opposite way to the guess."),

        new("BRIGHT, 8-BIT", VoiceProfile.Bright, 22050.0, MaxUsableLevel, null, 0.6f,
            "Formants scaled up, which reads as a smaller head and a younger speaker. "
            + "Nothing about the words changed."),
    };
}

/// <summary>One thing to say. <see cref="Melody"/> is empty for everything spoken
/// normally.</summary>
public readonly record struct VoiceLine(
    string Category,
    string English,
    string Phonemes,
    string Melody,
    IReadOnlyList<string> Guessed,
    bool ByRule,
    string Note);

/// <summary>
/// A shelf of things a game might say.
///
/// <b>Deliberately free of MonoGame</b>, like <see cref="SoundLibrary"/> — it is text, so
/// a headless test can check that every line parses without an audio device. A shipped
/// example with a typo speaks a silent gap where a phoneme should be, and the first thing
/// anyone tries is the example.
///
/// Most lines are written as English and run through <see cref="LetterToSound"/> at
/// startup, because that is what a game would do: you want "EXTRA LIFE" in your source,
/// not <c>EH1 K S T R AH0 L AY1 F</c>.
///
/// <b>The quotations are taken from <see cref="SpeechSource.Examples"/> by label rather
/// than copied.</b> Those entries are hand-tuned — corrected phonemes, and in one case a
/// hand-written melody that gives a question the rise the contour cannot produce on its
/// own — and they are tuned in <c>PokeyLabForms</c>, which reads that same table. Copying
/// them here would mean every improvement made in the lab had to be pasted across a second
/// time or silently not apply, which is exactly the kind of drift a demo is worst at
/// showing. Referencing them means tuning a line in the lab, saving it to
/// <c>Examples</c>, and hearing it here with no further work.
/// </summary>
public static class VoiceLibrary
{
    public static readonly VoiceLine[] All = Build();

    /// <summary>
    /// Labels asked for that <see cref="SpeechSource.Examples"/> did not have.
    ///
    /// A missing label drops a line rather than throwing: a demo that will not start
    /// because a quotation was renamed is worse than one that is a line short. The check
    /// in <c>tools</c> asserts this is empty, so it fails somewhere it can be read rather
    /// than somewhere it cannot be heard.
    /// </summary>
    public static readonly List<string> Missing = new();

    private static VoiceLine[] Build()
    {
        var lines = new List<VoiceLine>();

        void Rules(string category, string english, string note)
        {
            string phonemes = LetterToSound.Translate(english, out List<string> guessed);
            lines.Add(new VoiceLine(category, english, phonemes, "", guessed, true, note));
        }

        // Pulls one entry out of the shared table by its label, bringing whatever phonemes
        // and melody it currently carries. The Example's English is used as the display
        // text rather than its label, because labels there are free to contain characters
        // the vector font has no glyph for -- a music note, an em dash.
        void Example(string category, string label, string note)
        {
            foreach (var (exampleLabel, english, phonemes, melody) in SpeechSource.Examples)
            {
                if (exampleLabel != label) continue;

                lines.Add(new VoiceLine(category, english.Length > 0 ? english : label,
                                        phonemes, melody, new List<string>(), false, note));
                return;
            }

            Missing.Add(label);
        }

        // --- Announcer -----------------------------------------------------
        Rules("Announcer", "Player one, ready.",
              "A comma is a pause, not a word. The rules turn clause punctuation into a gap "
              + "and leave the rest alone, because a gap at every word boundary sounds like "
              + "a station announcement.");
        Rules("Announcer", "Game over.",
              "Two words, both in the exception dictionary. This is the easy case and it is "
              + "also the most common one.");
        Rules("Announcer", "Level complete.", "");
        Rules("Announcer", "Extra life.", "");
        Rules("Announcer", "New high score.", "");

        // --- Warnings ------------------------------------------------------
        Rules("Warnings", "Warning. Shields down.",
              "A full stop between two short sentences, which is where the contour resets. "
              + "Without the pause it declines across both and the second sounds like an "
              + "afterthought.");
        Rules("Warnings", "Fuel critical.", "");
        Rules("Warnings", "Enemy approaching.", "");
        Rules("Warnings", "Are you still there?",
              "A question mark, and the contour rises instead of falling. Punctuation is the "
              + "only signal used - English marks questions syntactically too, but a rising "
              + "contour is wrong for many of those anyway.");

        // --- Interface -----------------------------------------------------
        Rules("Interface", "Insert coin.", "");
        Rules("Interface", "Press start.", "");
        Rules("Interface", "Select your ship.", "");

        // --- Quotations ----------------------------------------------------
        // Period pieces on purpose: HAL is 1968, WarGames 1983, and S.A.M. itself 1982.
        // Every one of these is looked up in the shared table, so what plays here is
        // whatever the lab last tuned.
        Example("Quotations", "Shall we play a game?",
                "The only spoken line here with a melody, and it is there because the pitch "
                + "contour cannot ask a question on its own. Five notes for five syllables, "
                + "written by hand in the lab. Notes override prosody entirely.");
        Example("Quotations", "I am afraid I cannot do that", "");
        Example("Quotations", "Good morning Dave",
                "All three nasals in one word - the M, the N and the NG of MORNING. Each is "
                + "an anti-resonance rather than a resonance, which is the one thing a "
                + "three-formant synthesiser has to add a fourth filter to do.");
        Example("Quotations", "Open the pod bay doors", "");

        // --- Singing -------------------------------------------------------
        // The melody feature at full strength rather than as an intonation fix. Daisy Bell
        // was sung by an IBM 704 at Bell Labs in 1961, the first computer to sing anything,
        // which is why HAL sings it in 2001 - Arthur C. Clarke having been in the room.
        Example("Singing", "♪ Daisy Bell — opening",
                "Ten syllables on ten notes. A sung syllable takes its pitch and its length "
                + "from its note, so the tune decides the rhythm too.");

        return lines.ToArray();
    }
}
