using System;
using System.Globalization;
using System.IO;
using Pokey;
using Step = Pokey.StepEnvelope.Step;

namespace Lasers;

/// <summary>
/// The Pokey library applied to ten laser sounds, one layer at a time.
///
/// <b>Read this top to bottom and you have the whole API.</b> <see cref="Sweep"/> drives
/// the bare chip with no envelope at all; <see cref="Render"/> is the one call a game
/// actually needs; <see cref="Design"/> is the same sound asked for in hertz and seconds
/// rather than in register values. Everything else here is printing and WAV headers.
///
/// <b>Nothing is asserted that is not also measured.</b> The table in LaserSounds.cs
/// claims pitches and durations; <see cref="Describe"/> reads them back off the compiled
/// chains rather than repeating the comment, and <see cref="Check"/> fails the run if a
/// register wrapped where the recipe did not say it would.
/// </summary>
internal static class Program
{
    private const int SampleRate = 44100;

    private static int Main(string[] args)
    {
        string mode = args.Length > 0 && args[0].StartsWith("--") ? args[0] : null;
        // Beside the repo's other audio rather than in a folder of its own at the root.
        string outDir = args.Length > 0 && mode == null ? args[0] : Path.Combine("sounds", "lasers");

        switch (mode)
        {
            case "--list": List(); return Check();
            case "--csharp": CSharp(); return Check();
            case "--design": Design(); return 0;
            case "--sweep": Sweep(); return 0;
            // Into banks/ by default, which is where PokeyLabForms, Demo1 and RimWalker
            // all look. Written there rather than beside the WAVs because a bank is not
            // an output of this tool so much as an input to those three.
            case "--bank": Bank(args.Length > 1 ? args[1] : "banks"); return Check();
            case null: break;
            default:
                Console.WriteLine("lasers [outputDirectory] | --list | --csharp | --design | --sweep | --bank");
                return 1;
        }

        List();
        int problems = Check();
        RenderAll(outDir);
        return problems;
    }

    // --- Layer 1: the chip on its own --------------------------------------

    /// <summary>
    /// A laser with no envelope, no library help, and nothing but register writes.
    ///
    /// <b>This is all a POKEY does.</b> AUDF is a countdown divider and AUDC packs the
    /// timbre and the volume into one byte; the chip holds whatever it was last given
    /// until something writes again. The falling pitch of a laser is not a feature of the
    /// chip -- it is this loop, writing a slightly larger divider every few milliseconds,
    /// which is exactly what Tempest's interrupt routine did and what
    /// <see cref="StepEnvelope"/> exists to stop you writing by hand.
    /// </summary>
    private static void Sweep()
    {
        var chip = new PokeyChip { BaseClockHz = LaserSounds.ClockHz };
        chip.SetControl(0);                       // AUDCTL: no joins, no filters

        var samples = new float[(int)(SampleRate * 0.073)];
        int written = 0;
        int perTick = (int)(SampleRate / LaserSounds.TickHz);

        Console.WriteLine("A sweep written by hand, tick by tick:");
        for (int tick = 0; tick < 16 && written + perTick <= samples.Length; tick++)
        {
            int audf = 6 + 6 * tick;              // the divider climbs, so the pitch falls
            chip.SetAudf(0, audf);
            chip.SetAudc(0, 5 * 32 + 9);          // distortion 5 (pure tone), volume 9

            // Render only the slice this register value is meant to cover. Rendering the
            // whole buffer once per tick would play the last value for the entire sound.
            chip.Render(samples, written, perTick, SampleRate);
            written += perTick;

            Console.WriteLine($"  tick {tick,2}  AUDF {audf,3}  -> {chip.PitchHz(0),8:0.0} Hz");
        }

        // One driven channel is a quarter of the mix, and the DC blocker halves the swing
        // of a unipolar signal, so raw chip output is eight times quieter than it should
        // be. SoundRender applies this for you; here it is the one thing you have to
        // remember, which is most of the argument for not doing it by hand.
        for (int i = 0; i < samples.Length; i++)
            samples[i] = Math.Clamp(samples[i] * 2f * PokeyChip.Channels, -1f, 1f) * 0.45f;

        string path = Path.Combine("sounds", "lasers", "00-by-hand.wav");
        Write(path, samples);
        Console.WriteLine($"\nThat is 'Classic zap' with no envelope class involved, and it is the same sound. Wrote {path}");
    }

    // --- Layer 2: the one call a game needs --------------------------------

    /// <summary>
    /// A recipe to samples.
    ///
    /// <b>One call, and everything the caller does not decide is not a parameter.</b>
    /// <see cref="SoundRender.Render"/> creates the chip, walks the envelope in step with
    /// the audio (so a sweep never quantises to a buffer size), applies the gain the
    /// hardware implies and fades the last 10ms so the cut does not click. What is left
    /// is the recipe, the tick rate, the clock and how loud you want it.
    /// </summary>
    private static float[] Render(in Recipe recipe)
        => SoundRender.Render(recipe.Audf, recipe.Audc,
                              LaserSounds.TickHz, LaserSounds.ClockHz,
                              recipe.Amplitude, SampleRate);

    private static void RenderAll(string outDir)
    {
        Directory.CreateDirectory(outDir);

        for (int i = 0; i < LaserSounds.All.Length; i++)
        {
            var recipe = LaserSounds.All[i];
            float[] samples = Render(recipe);

            string file = Path.Combine(outDir, $"{i + 1:00}-{Slug(recipe.Name)}.wav");
            Write(file, samples);
            Console.WriteLine($"  {file,-44} {samples.Length / (double)SampleRate * 1000,4:0} ms");
        }

        Console.WriteLine($"\n{LaserSounds.All.Length} lasers written to {Path.GetFullPath(outDir)}");
    }

    // --- Layer 3: hertz and seconds ----------------------------------------

    /// <summary>
    /// The same sounds asked for the way somebody designing one thinks about them.
    ///
    /// <b>SoundDesign compiles to steps and nothing else</b>, so what comes back is the
    /// same <c>Step</c> pair the table holds and can be pasted straight into it. Use this
    /// to find a shape, then keep the numbers -- the compiled form is the one the lab can
    /// open and the table can be compared against.
    ///
    /// <b>It reports what it achieved, not what it was asked for.</b> Dividers are
    /// integers and ticks are whole, so at the top of the range the nearest divider can be
    /// most of a semitone away. That gap is the instrument, and printing both sides of it
    /// is how you tell a design that landed from one that quantised badly.
    /// </summary>
    private static void Design()
    {
        var designs = new (string Name, SoundDesign Design)[]
        {
            ("Zap",           new SoundDesign { FromHz = 3800, ToHz = 280,  Seconds = 0.07, Timbre = Timbre.Tone,   Level = 0.6 }),
            ("Blaster",       new SoundDesign { FromHz = 660,  ToHz = 125,  Seconds = 0.12, Timbre = Timbre.Growl,  Level = 0.7, Move = LevelMove.FadeOut }),
            ("Buzz cannon",   new SoundDesign { FromHz = 2000, ToHz = 150,  Seconds = 0.08, Timbre = Timbre.Rasp,   Level = 0.6 }),
            ("Ion beam",      new SoundDesign { FromHz = 870,  ToHz = 155,  Seconds = 0.27, Timbre = Timbre.Noise,  Level = 0.8, Move = LevelMove.FadeOut }),
            ("Siege cannon",  new SoundDesign { FromHz = 440,  ToHz = 108,  Seconds = 0.20, Timbre = Timbre.Grind,  Level = 0.7, Move = LevelMove.FadeOut }),
            ("Rasp",          new SoundDesign { FromHz = 1400, ToHz = 140,  Seconds = 0.136, Timbre = Timbre.Tone,  Level = 0.2,  Move = LevelMove.Tremolo, TremoloHz = 110 }),
        };

        Console.WriteLine("Asked for                                   Got                        Compiles to");
        Console.WriteLine(new string('-', 118));

        foreach (var (name, design) in designs)
        {
            var voice = design.Compile();

            Console.WriteLine(
                $"{name,-13} {design.FromHz,6:0}->{design.ToHz,-5:0}Hz {design.Seconds * 1000,4:0}ms  " +
                $"{voice.StartHz,6:0}->{voice.EndHz,-5:0}Hz {voice.Seconds * 1000,4:0}ms  {voice}");

            Write(Path.Combine("sounds", "lasers", $"design-{Slug(name)}.wav"), voice.Render(0.5f, SampleRate));
        }

        Console.WriteLine("\nThe last row is the rasp described in plain terms. It compiles to");
        Console.WriteLine("Step(18, 1, +6, 30) / Step(163, 1, -8, 30) -- table entry 8's own four numbers, which is");
        Console.WriteLine("the check that this layer is a front end to the engine and not a second engine.");
    }

    // --- Reading the table back --------------------------------------------

    private static void List()
    {
        Console.WriteLine($"Ten lasers at {LaserSounds.ClockHz / 1e6:0.000} MHz, {LaserSounds.TickHz:0} Hz tick");
        Console.WriteLine($"(divider clock {LaserSounds.ClockHz / PokeyClocks.SlowDivider:0} Hz -- every pitch below scales with it)\n");
        Console.WriteLine("  #  Name                   ms    pitch          timbre        volume");
        Console.WriteLine("  " + new string('-', 74));

        for (int i = 0; i < LaserSounds.All.Length; i++)
            Console.WriteLine($"  {i + 1,2}  {Describe(LaserSounds.All[i])}");

        Console.WriteLine();
    }

    /// <summary>
    /// What a recipe actually does, walked out of the chains rather than read off its
    /// note -- pitch at the first and last tick, its real duration, and the volume range
    /// its AUDC chain visits.
    /// </summary>
    private static string Describe(in Recipe recipe)
    {
        int ticks = LaserSounds.Bank.TotalTicks(recipe);
        double first = PitchAt(recipe, 0), last = PitchAt(recipe, ticks - 1);
        double peak = first, trough = first;

        int loud = 0, quiet = 15;
        for (int tick = 0; tick < ticks; tick++)
        {
            double hz = PitchAt(recipe, tick);
            peak = Math.Max(peak, hz);
            trough = Math.Min(trough, hz);

            int volume = StepEnvelope.ValueAt(recipe.Audc, tick, true) & PokeyChip.AudcVolumeMask;
            loud = Math.Max(loud, volume);
            quiet = Math.Min(quiet, volume);
        }

        // A sweep that turns around visits pitches neither end reports, so say so.
        string turn = peak > Math.Max(first, last) + 1 ? $" ^{peak:0}" :
                      trough < Math.Min(first, last) - 1 ? $" v{trough:0}" : "";

        int audc = recipe.Audc[0].Value;
        return $"{recipe.Name,-20} {ticks / LaserSounds.TickHz * 1000,4:0}  " +
               $"{first,6:0}->{last,-5:0}Hz{turn,-7} {TimbreName(audc >> 5),-12}  {quiet}-{loud}";
    }

    private static double PitchAt(in Recipe recipe, int tick)
    {
        int audf = StepEnvelope.ValueAt(recipe.Audf, tick, false);
        return LaserSounds.ClockHz / PokeyClocks.SlowDivider / (2.0 * (audf + 1));
    }

    /// <summary>The eight distortion codes are six sounds: the field's low bit bypasses a
    /// counter stage, so 1 and 3 are the same and 5 and 7 are the same.</summary>
    private static string TimbreName(int distortion) => distortion switch
    {
        0 => "gritty",
        1 or 3 => "5-bit growl",
        2 => "grind",
        4 => "noise",
        5 or 7 => "pure tone",
        _ => "4-bit buzz",
    };

    // --- The checks --------------------------------------------------------

    /// <summary>
    /// The two ways one of these tables fails silently.
    ///
    /// <b>Both registers wrap rather than clamping</b>, so a sweep that runs off the end
    /// of a byte reappears at the other end, and a fade that overshoots zero by one comes
    /// back at full volume. Neither throws, neither is silent, and both still play -- just
    /// not the sound that was designed. The arcade uses wrapping deliberately, which is
    /// why a recipe can declare <c>AllowWrap</c> rather than the check simply forbidding it.
    /// </summary>
    private static int Check()
    {
        int problems = 0;

        foreach (var recipe in LaserSounds.All)
        {
            if (recipe.AllowWrap) continue;

            foreach (string fault in Wraps(recipe.Audf, 0, 255, "AUDF"))
            {
                Console.WriteLine($"  ! {recipe.Name}: {fault}");
                problems++;
            }

            foreach (string fault in Wraps(recipe.Audc, 0, 15, "volume", PokeyChip.AudcVolumeMask))
            {
                Console.WriteLine($"  ! {recipe.Name}: {fault}");
                problems++;
            }
        }

        Console.WriteLine(problems == 0
            ? "  All ten stay inside their registers.\n"
            : $"  {problems} recipe(s) wrap without saying so.\n");

        return problems;
    }

    /// <summary>
    /// Walks a chain in plain arithmetic -- no masking -- so an excursion past the end of
    /// the register is visible instead of being folded back in. A step applies
    /// <c>Outer - 1</c> deltas, not <c>Outer</c>: the decrement that reaches zero advances
    /// to the next step rather than adding again.
    /// </summary>
    private static System.Collections.Generic.List<string> Wraps(
        Step[] chain, int low, int high, string what, int mask = 0xff)
    {
        var faults = new System.Collections.Generic.List<string>();

        foreach (var step in chain)
        {
            int value = step.Value & mask;
            for (int i = 0; i < Math.Max(0, step.Outer - 1); i++)
            {
                value += step.Delta;
                if (value < low || value > high)
                {
                    faults.Add($"{what} reaches {value} (outside {low}..{high}) on step " +
                               $"({step.Value}, {step.Inner}, {step.Delta:+0;-0;0}, {step.Outer})");
                    break;
                }
            }
        }

        return faults;
    }

    // --- Getting a voice out of here ---------------------------------------

    /// <summary>Each recipe as the call a game pastes -- the same shape PokeyLabForms'
    /// <i>Copy as C#</i> button emits, so a voice tuned in the lab and one written here
    /// are the same text.</summary>
    private static void CSharp()
    {
        foreach (var recipe in LaserSounds.All)
        {
            int audc = recipe.Audc[0].Value;
            Console.WriteLine($"// \"{recipe.Name}\" -- AUDC {audc} = distortion {audc >> 5} " +
                              $"({TimbreName(audc >> 5)}), volume {audc & PokeyChip.AudcVolumeMask}");
            Console.WriteLine("ChipSound.Build(");
            Console.WriteLine($"    audf: new[] {{ {string.Join(", ", Array.ConvertAll(recipe.Audf, Print))} }},");
            Console.WriteLine($"    audc: new[] {{ {string.Join(", ", Array.ConvertAll(recipe.Audc, Print))} }},");
            Console.WriteLine($"    tickHz: {LaserSounds.TickHz:0}, baseClockHz: PokeyClocks.TempestHz, " +
                              $"amplitude: {recipe.Amplitude.ToString("0.00", CultureInfo.InvariantCulture)}f);");
            Console.WriteLine();
        }
    }

    private static string Print(Step step)
        => $"new Step({step.Value}, {step.Inner}, {step.Delta:+0;-0;0}, {step.Outer})";

    /// <summary>The set as a <c>.sound</c> bank, which PokeyLabForms and RimWalker both
    /// read. One line per step, in the order <i>Copy as C#</i> prints them.</summary>
    private static void Bank(string outDir)
    {
        Directory.CreateDirectory(outDir);
        string path = Path.Combine(outDir, "lasers.sound");

        using (var writer = new StreamWriter(path))
        {
            writer.WriteLine("# Ten laser shots. Rendered by tools/lasers.");
            writer.WriteLine($"bank   {LaserSounds.Bank.Name}");
            writer.WriteLine($"clock  {LaserSounds.ClockHz:0}");
            writer.WriteLine($"tick   {LaserSounds.TickHz:0}");
            writer.WriteLine();
            writer.WriteLine("category Lasers");

            foreach (var recipe in LaserSounds.All)
            {
                writer.WriteLine();
                writer.WriteLine($"sound {recipe.Name}");
                foreach (var step in recipe.Audf)
                    writer.WriteLine($"audf  {step.Value} {step.Inner} {step.Delta:+0;-0;0} {step.Outer}");
                foreach (var step in recipe.Audc)
                    writer.WriteLine($"audc  {step.Value} {step.Inner} {step.Delta:+0;-0;0} {step.Outer}");
                writer.WriteLine($"amp   {recipe.Amplitude.ToString("0.00", CultureInfo.InvariantCulture)}");
                if (recipe.AllowWrap) writer.WriteLine("wrap");
                writer.WriteLine($"note  {recipe.Note}");
            }
        }

        Console.WriteLine($"Wrote {Path.GetFullPath(path)} -- open it in PokeyLabForms to tune these by ear.");

        // Read it straight back, because the parser never throws: a line it cannot
        // understand is skipped and recorded, so a writer with a bad format string loses
        // one sound silently. Showing Problems is the whole reason they are collected.
        var reloaded = EnvelopeBank.Load(path);
        Console.WriteLine($"Reads back as {reloaded.Recipes.Length} of {LaserSounds.All.Length} sounds, " +
                          $"{reloaded.Problems.Count} problem(s).");
        foreach (string problem in reloaded.Problems) Console.WriteLine($"  ! {problem}");
    }

    // --- WAV ---------------------------------------------------------------

    /// <summary>
    /// 16-bit mono PCM, little-endian.
    ///
    /// <see cref="SoundRender.ToPcm16"/> does the packing; the only thing left is the
    /// 44-byte header. Byte order is the one thing to get right here -- backwards gives
    /// noise that is almost your sound, which is a horrible thing to debug.
    /// </summary>
    private static void Write(string path, float[] samples)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));

        byte[] pcm = SoundRender.ToPcm16(samples);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        writer.Write(new[] { 'R', 'I', 'F', 'F' });
        writer.Write(36 + pcm.Length);
        writer.Write(new[] { 'W', 'A', 'V', 'E' });
        writer.Write(new[] { 'f', 'm', 't', ' ' });
        writer.Write(16);
        writer.Write((short)1);                  // PCM
        writer.Write((short)1);                  // mono
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);            // bytes per second
        writer.Write((short)2);                  // block align
        writer.Write((short)16);                 // bits
        writer.Write(new[] { 'd', 'a', 't', 'a' });
        writer.Write(pcm.Length);
        writer.Write(pcm);
    }

    private static string Slug(string name)
        => name.ToLowerInvariant().Replace(' ', '-').Replace(",", "");
}
