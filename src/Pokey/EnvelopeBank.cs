using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Step = Pokey.StepEnvelope.Step;

namespace Pokey;

/// <summary>
/// One sound, as the numbers that make it.
///
/// <see cref="Note"/> is what the sound is *for* rather than what it does — the interesting
/// part of a demo like this is why a designer reached for that setting, not the setting.
/// </summary>
public readonly record struct Recipe(
    string Category,
    string Name,
    Step[] Audf,
    Step[] Audc,
    float Amplitude,
    string Note)
{
    /// <summary>
    /// This sound runs a register past the end on purpose.
    ///
    /// <b>Wrapping is a technique, not only a mistake.</b> Both registers wrap rather than
    /// clamp, and the arcade uses it: Tempest's shot takes a -8 on a volume of 2 every
    /// tick, and the buzz that produces is the sound. Its descend sweep runs AUDF off the
    /// bottom, through zero, and lands on 255 as a low rumble.
    ///
    /// So the check cannot simply forbid it — but it is also exactly how a hand-edited
    /// chain fails silently, which is why the default is that a wrap is a mistake and a
    /// file has to say otherwise. Declaring it puts the intent in the file next to the
    /// numbers.
    /// </summary>
    public bool AllowWrap { get; init; }
}

/// <summary>
/// A named set of sounds, and the clock they were designed against.
///
/// <b>Not called SoundBank, deliberately.</b> MonoGame already has one -- XACT's
/// <c>Microsoft.Xna.Framework.Audio.SoundBank</c> -- and any game file with that namespace
/// imported would silently resolve the unqualified name to theirs. A library meant to be
/// used from a game should not make its host pick. This holds envelopes, so it says so.
///
/// <b>The clock belongs to the bank, not to the app.</b> Every pitch scales with it, so a
/// voice designed at one clock and played at another is in the wrong key — RimWalker's own
/// effects were tuned at an effective 32kHz divider clock while Tempest's ran at 54kHz, and
/// auditioning one at the other's rate is a different sound, not a rounding error. Putting
/// it in the file is what lets banks from different games sit side by side and each be
/// right.
/// </summary>
public sealed class EnvelopeBank
{
    public string Name { get; init; } = "Untitled";

    /// <summary>Where it came from, for the header. A path, or a note that it is built in.</summary>
    public string Source { get; init; } = "built in";

    public double ClockHz { get; init; } = PokeyClocks.TempestHz;
    public double TickHz { get; init; } = 220.0;

    public Recipe[] Recipes { get; init; } = Array.Empty<Recipe>();

    /// <summary>Anything the parser could not use. Surfaced rather than swallowed: a
    /// mistyped line silently dropping a sound is the failure this format is most likely
    /// to have, and the file is meant to be edited by hand.</summary>
    public IReadOnlyList<string> Problems { get; init; } = Array.Empty<string>();

    // --- Reading a recipe without playing it -------------------------------

    /// <summary>How long the sound runs. The two chains are independent and routinely
    /// disagree about length, so this is the longer of them, exactly as the chip is.</summary>
    public double Seconds(Recipe recipe) => Envelope(recipe).Seconds;

    public int TotalTicks(Recipe recipe) => Envelope(recipe).TotalTicks;

    private StepEnvelope Envelope(Recipe recipe) => new()
    {
        AudfChain = recipe.Audf, AudcChain = recipe.Audc, TickHz = TickHz, Channel = 0,
    };

    // --- The file format ---------------------------------------------------

    /// <summary>The extension banks are saved with, and what the open dialog filters on.</summary>
    public const string Extension = ".sound";

    /// <summary>
    /// Parses a bank file.
    ///
    /// <b>A line-based format rather than JSON</b>, because these are written by hand and
    /// pasted into from PokeyLabForms. Every line is a keyword and a value, a chain is one
    /// line per step so the four numbers sit in the order the lab prints them, and a
    /// missing comma cannot take the file with it. It also takes comments, which matters
    /// for a format whose most useful field is the one explaining why a sound is what it
    /// is.
    ///
    /// <b>Nothing here throws on bad input.</b> An unreadable line is recorded in
    /// <see cref="Problems"/> and skipped, so one typo costs one sound rather than the
    /// bank — the same reasoning as the voice settings file, and for the same reason: this
    /// is a file a person edits.
    /// </summary>
    public static EnvelopeBank Parse(IEnumerable<string> lines, string name, string source)
    {
        var recipes = new List<Recipe>();
        var problems = new List<string>();

        double clock = PokeyClocks.TempestHz, tick = 220.0;
        string bankName = name, category = "Sounds";

        // The sound being accumulated. A sound ends when the next one starts or the file
        // does, because a chain is any number of lines and there is no other way to know.
        // The category is captured when a sound STARTS, not when it is flushed. A sound is
        // still pending when the next "category" line arrives -- it ends only at the next
        // "sound" or at the file -- so reading the live variable at flush time files the
        // last sound of every category under the following one.
        string soundName = null, soundCategory = null, note = null;
        var audf = new List<Step>();
        var audc = new List<Step>();
        float amplitude = 0.5f;
        bool allowWrap = false;

        void Flush(int lineNumber)
        {
            if (soundName == null) return;

            if (audf.Count == 0 || audc.Count == 0)
                problems.Add($"line {lineNumber}: \"{soundName}\" has no {(audf.Count == 0 ? "audf" : "audc")}, skipped");
            else
                recipes.Add(new Recipe(soundCategory, soundName, audf.ToArray(), audc.ToArray(),
                                       amplitude, note ?? "") { AllowWrap = allowWrap });

            soundName = null;
            note = null;
            audf = new List<Step>();
            audc = new List<Step>();
            amplitude = 0.5f;
            allowWrap = false;
        }

        int number = 0;
        foreach (string raw in lines)
        {
            number++;
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            int split = line.IndexOfAny(new[] { ' ', '\t' });
            string key = (split < 0 ? line : line.Substring(0, split)).ToLowerInvariant();
            string value = split < 0 ? "" : line.Substring(split + 1).Trim();

            switch (key)
            {
                case "bank":
                    bankName = value;
                    break;

                case "clock":
                    if (!TryDouble(value, out clock)) problems.Add($"line {number}: bad clock \"{value}\"");
                    break;

                case "tick":
                    if (!TryDouble(value, out tick)) problems.Add($"line {number}: bad tick \"{value}\"");
                    break;

                case "category":
                    category = value;
                    break;

                case "sound":
                    Flush(number);
                    soundName = value;
                    soundCategory = category;
                    break;

                case "audf":
                case "audc":
                    if (soundName == null) { problems.Add($"line {number}: {key} before any sound"); break; }
                    if (TryStep(value, out var step)) (key == "audf" ? audf : audc).Add(step);
                    else problems.Add($"line {number}: bad {key} \"{value}\"");
                    break;

                case "amp":
                    if (!TryFloat(value, out amplitude))
                        problems.Add($"line {number}: bad amp \"{value}\"");
                    break;

                case "wrap":
                    // Takes no value. Says this sound runs a register past the end on
                    // purpose -- see Recipe.AllowWrap for why that is a real technique.
                    allowWrap = true;
                    break;

                case "note":
                    // Appended, so a long note can be wrapped across several lines rather
                    // than running off the side of an editor.
                    note = note == null ? value : note + " " + value;
                    break;

                default:
                    problems.Add($"line {number}: unknown keyword \"{key}\"");
                    break;
            }
        }

        Flush(number);

        return new EnvelopeBank
        {
            Name = bankName,
            Source = source,
            ClockHz = clock,
            TickHz = tick,
            Recipes = recipes.ToArray(),
            Problems = problems,
        };
    }

    /// <summary>Reads a bank from disk, or null if it cannot be read at all. A file that
    /// parses to nothing still comes back, carrying its problems — "loaded, and here is
    /// why it is empty" is more use than a bare failure.</summary>
    public static EnvelopeBank Load(string path)
    {
        try
        {
            return Parse(File.ReadAllLines(path), Path.GetFileNameWithoutExtension(path), path);
        }
        catch (Exception e)
        {
            return new EnvelopeBank
            {
                Name = Path.GetFileNameWithoutExtension(path),
                Source = path,
                Problems = new[] { e.Message },
            };
        }
    }

    /// <summary>Every bank file in a directory, alphabetically. A missing or unreadable
    /// directory is an empty list rather than an error: a caller that ships a compiled-in
    /// set has something to fall back on, and one that does not would rather show nothing
    /// than fail to start.</summary>
    public static List<EnvelopeBank> Discover(string directory)
    {
        var found = new List<EnvelopeBank>();

        try
        {
            if (!Directory.Exists(directory)) return found;

            var files = Directory.GetFiles(directory, "*" + Extension);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string file in files) found.Add(Load(file));
        }
        catch (Exception)
        {
            // An app that will not start because a banks folder is unreadable is a worse
            // trade than an app with no banks in it.
        }

        return found;
    }

    // --- Small parsers ------------------------------------------------------

    /// <summary>A chain step: four integers, the order the arcade stores them and the order
    /// <c>Copy as C#</c> prints them. A leading + on the delta is allowed and ignored,
    /// because that is how it reads in the lab and in the source.</summary>
    private static bool TryStep(string text, out Step step)
    {
        step = default;
        var parts = text.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4) return false;

        var values = new int[4];
        for (int i = 0; i < 4; i++)
        {
            string part = parts[i].StartsWith("+") ? parts[i].Substring(1) : parts[i];
            if (!int.TryParse(part, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
                              out values[i]))
                return false;
        }

        step = new Step(values[0], values[1], values[2], values[3]);
        return true;
    }

    private static bool TryDouble(string text, out double value)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static bool TryFloat(string text, out float value)
        => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
