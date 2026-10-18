using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Pokey;

// System.Windows.Forms is deliberately NOT imported: its Keys enum collides with XNA's,
// which this file uses on every line of input handling. The two dialog types are spelled
// out where they are needed instead.

namespace Demo1;

/// <summary>
/// A browser for <see cref="SoundLibrary"/>: pick a sound, hear it, and see the numbers
/// that make it.
///
/// The sounds themselves are <see cref="Sounds"/>; this only presses the buttons and
/// reports what it pressed.
/// </summary>
public sealed class SoundPage : IPage
{
    // Text sizes are cap heights in pixels. These are all a size or two larger than they
    // first were: the glow spreads by a fixed multiple of the stroke, so a stroke font
    // needs more pixels per glyph than a bitmap one before it reads cleanly.
    private const float StepSize = 12f, NoteSize = 13f, PlotSize = 11f;
    // The bank header sits between the shell's rule and the list, so both columns start
    // lower than they did when there was only ever one set of sounds.
    private const float BankY = 106f, BankRuleY = 126f;
    private const float ListX = 40f, ListWidth = 480f, ListTop = 138f, DetailTop = 138f;
    private const float DetailX = 592f, DetailWidth = 648f;
    private const float PlotWidth = 258f, PlotHeight = 66f, PlotLabels = 44f;

    // The two pictures sit at fixed heights rather than after however many step rows the
    // selected sound happens to have. Arrowing down a list is much easier to read when
    // only the contents of a panel change and not its shape.
    private const float PlotTop = 540f, WaveTop = 656f, WaveHeight = 100f;

    private readonly Sounds _blips;
    private readonly IntPtr _windowHandle;

    /// <summary>True for the frame after the file dialog closed. The game loop was blocked
    /// while it was open, so MonoGame is about to see one enormous elapsed time and try to
    /// catch up by running Update several times; <see cref="Game1"/> reads this and resets
    /// the clock instead.</summary>
    private bool _blocked;

    /// <summary>Reports whether the loop was blocked since the last call, and clears it.
    /// Read-and-reset rather than a plain flag so it cannot be acted on twice.</summary>
    public bool ClearBlocked()
    {
        bool was = _blocked;
        _blocked = false;
        return was;
    }

    // The banks on offer, and where in them we are. Discovered once at construction plus
    // anything opened since, so cycling with B reaches everything without the dialog.
    private readonly List<EnvelopeBank> _banks = new();
    private int _bank;

    private Sounds _sounds;
    private ListView _list;

    /// <summary>Set for a few seconds after loading a file, so a bank that arrived with
    /// problems says so instead of just looking short.</summary>
    private string _message;
    private double _messageLeft;

    // Which sound is sounding, and for how much longer. Used only to draw the progress
    // bar -- MonoGame will not tell us when a SoundEffect finishes, but the envelope's own
    // length is known exactly, so counting it down here is both simpler and accurate.
    private int _playing = -1;
    private double _playRemaining, _playLength;

    // Everything about the selected sound that costs more than a lookup to work out:
    // emulating the chip for its waveform, and expanding both chains to one value per
    // tick. None of it can change while the selection stands, so it is computed when the
    // selection moves rather than sixty times a second.
    private Detail _detail;

    private readonly record struct Detail(int Of, int Ticks, int[] Audf, int[] Audc, float[] Wave);

    /// <summary>The bank currently being browsed. Everything drawn reads through this
    /// rather than through <see cref="SoundLibrary"/>'s statics, which is what makes a
    /// loaded file behave exactly like the built-in set.</summary>
    private EnvelopeBank Bank => _banks[_bank];

    public string Name => "SOUNDS";

    public string Hint =>
        "UP DOWN SOUND    LEFT RIGHT CATEGORY    ENTER PLAY    B BANK    O OPEN A FILE";

    /// <summary><paramref name="blips"/> is <see cref="Game1"/>'s interface bank, kept
    /// separate from the browsable one — a loaded file need not contain a menu click, and
    /// the shell going silent because of what you opened would be a strange thing to
    /// debug.</summary>
    /// <param name="windowHandle">
    /// The game window, used to own the file dialog.
    ///
    /// <b>Not optional in practice.</b> An unowned common dialog is a top-level window of
    /// its own: it can open behind a full-screen game, and while it is up the game's main
    /// thread is blocked, so Windows paints the usual "not responding" ghost over it.
    /// Between the two, pressing O looks exactly like a hang. Owning it puts it in front
    /// and keeps it modal to the thing that asked.
    /// </param>
    public SoundPage(Sounds blips, IntPtr windowHandle)
    {
        _blips = blips;
        _windowHandle = windowHandle;

        // Built in first, then whatever is beside the executable. The built-in set is
        // always present, so there is never a state with nothing to show.
        _banks.Add(SoundLibrary.BuiltIn);
        _banks.AddRange(EnvelopeBank.Discover(BankDirectory));

        Load(0);
    }

    /// <summary>Where shipped banks live. Beside the executable rather than in the source
    /// tree, because that is what gets copied next to a published build.</summary>
    public static string BankDirectory => Path.Combine(AppContext.BaseDirectory, "banks");

    public void Update(Input input, double elapsedSeconds)
    {
        if (input.Pressed(Keys.Down) && _list.Move(+1)) Blip();
        if (input.Pressed(Keys.Up) && _list.Move(-1)) Blip();
        if (input.Pressed(Keys.Right) && _list.MoveCategory(+1)) Blip();
        if (input.Pressed(Keys.Left) && _list.MoveCategory(-1)) Blip();
        if (input.Pressed(Keys.Enter) || input.Pressed(Keys.Space)) Play(_list.Selected);

        // B cycles what is already found, O goes looking. Both, because reaching for a file
        // dialog to hear the four sets sitting right there would be tedious, and being
        // limited to those four would make the format pointless.
        if (input.Pressed(Keys.B)) Load((_bank + 1) % _banks.Count);
        if (input.Pressed(Keys.O)) OpenFile();

        if (input.Clicked)
        {
            int hit = _list.HitTest(input.MouseX, input.MouseY);
            if (hit >= 0) Play(hit);
        }

        _playRemaining = Math.Max(0.0, _playRemaining - elapsedSeconds);
        _messageLeft = Math.Max(0.0, _messageLeft - elapsedSeconds);
    }

    // --- Banks ------------------------------------------------------------

    /// <summary>
    /// Switches to a bank, rendering all of its voices.
    ///
    /// Everything derived from the old bank goes with it: the list, the built effects, the
    /// selection and the cached detail. Rebuilding rather than patching is what stops a
    /// stale waveform from a different game surviving the switch.
    /// </summary>
    private void Load(int index)
    {
        _bank = Math.Clamp(index, 0, _banks.Count - 1);
        var bank = _banks[_bank];

        _sounds?.Dispose();
        _sounds = new Sounds(bank);

        var rows = new List<ListView.Row>();
        foreach (var recipe in bank.Recipes)
            rows.Add(new ListView.Row(recipe.Category, recipe.Name,
                                      $"{bank.Seconds(recipe) * 1000.0:0} MS"));

        _list = new ListView(rows, ListX, ListWidth, ListTop);
        _detail = default;
        _playing = -1;
        _playRemaining = 0.0;

        _message = bank.Problems.Count > 0
            ? $"{bank.Recipes.Length} LOADED, {bank.Problems.Count} PROBLEMS - {bank.Problems[0]}"
            : null;
        _messageLeft = _message == null ? 0.0 : 8.0;
    }

    /// <summary>Asks for a bank file and loads it. The dialog, and why it runs where it
    /// does, is <see cref="FileDialog"/>.</summary>
    private void OpenFile()
    {
        string picked = FileDialog.Open(_windowHandle, "Open a sound bank", "Sound banks",
                                        EnvelopeBank.Extension, BankDirectory, out string failure);

        // However long the dialog was up, the game loop was not running for it.
        _blocked = true;

        // Reported, not swallowed. A dialog that cannot open is worth one line on screen --
        // the alternative is a key that silently does nothing, which is how this went wrong
        // the first time.
        if (failure != null)
        {
            _message = $"COULD NOT OPEN THE FILE DIALOG - {failure}";
            _messageLeft = 8.0;
        }

        if (picked == null) return;

        // Replaced rather than appended when the same file is opened twice, so re-opening
        // after an edit shows the edit instead of adding a second stale copy.
        var loaded = EnvelopeBank.Load(picked);
        int existing = _banks.FindIndex(b => string.Equals(b.Source, picked,
                                                           StringComparison.OrdinalIgnoreCase));

        if (existing >= 0) _banks[existing] = loaded;
        else { _banks.Add(loaded); existing = _banks.Count - 1; }

        Load(existing);
    }

    private void Blip() => _blips.Play("Menu blip");

    private void Play(int index)
    {
        _list.Select(index);
        _playing = index;
        _playLength = Bank.Seconds(Bank.Recipes[index]);
        _playRemaining = _playLength;
        _sounds.Play(index);
    }

    // --- Drawing ----------------------------------------------------------

    public void Draw(Screen screen)
    {
        DrawBankHeader(screen);

        // An empty bank is a real outcome -- a file can parse cleanly and contain nothing.
        // The header and its problem line are the whole answer in that case, so there is
        // nothing else to draw and nothing to index into.
        if (Bank.Recipes.Length == 0) return;

        _list.Draw(screen, _playRemaining > 0.0 ? _playing : -1);
        DrawDetail(screen, Bank.Recipes[_list.Selected]);
    }

    /// <summary>
    /// Which bank is showing, where it came from, and what it costs.
    ///
    /// <b>The clock is on screen because it is the thing that silently differs.</b> Two
    /// banks with identical numbers in them sound like different games if they were
    /// designed against different clocks, and nothing else on the page would say so.
    /// </summary>
    private void DrawBankHeader(Screen screen)
    {
        screen.Text($"{Bank.Name}   -   {Bank.Recipes.Length} SOUNDS   -   "
                    + $"BANK {_bank + 1} OF {_banks.Count}",
                    ListX, BankY, 13f, Screen.Heading);

        // The message takes the right-hand slot rather than a row of its own, so a bank
        // that loads with problems does not shift everything below it down by a line and
        // then back up again eight seconds later.
        bool saying = _messageLeft > 0.0 && _message != null;

        screen.TextRight(saying
                             ? _message
                             : $"{Bank.ClockHz / 1000.0:0.###} KHZ CLOCK   {Bank.TickHz:0} HZ TICK",
                         DetailX + DetailWidth, BankY, saying ? 11f : 13f,
                         saying ? Screen.Live : Screen.Body);

        screen.Rule(ListX, BankRuleY, DetailX + DetailWidth - ListX);
    }

    private void DrawDetail(Screen screen, Recipe recipe)
    {
        var detail = Detailed(recipe);
        float y = DetailTop;

        screen.Text(recipe.Name, DetailX, y, 26f, Screen.Heading);
        y += 40f;

        screen.Text(recipe.Category, DetailX, y, 12f, Screen.Body);
        screen.TextRight($"{detail.Ticks} TICKS AT {Bank.TickHz:0} HZ",
                         DetailX + DetailWidth, y, 12f, Screen.Body);
        y += 24f;

        DrawPlayBar(screen, y);
        y += 22f;

        // --- The numbers ---------------------------------------------------
        y = Section(screen, y, "AUDF   DIVIDER");
        foreach (var step in recipe.Audf)
        {
            screen.Text(StepText(step), DetailX + 12f, y, StepSize, Screen.Number);
            y += 20f;
        }

        // The distortion goes on the heading, not on every row.
        //
        // It is a property of the whole sound rather than of a step: the arcade's envelope
        // code masks the delta so only the volume nibble moves, which is what keeps a
        // sound's timbre fixed for its life. Repeating it down the rows also collided with
        // the step text on the widest chain, so this both reads truer and fits.
        y += 14f;
        int timbre = recipe.Audc[0].Value >> 5;
        y = Section(screen, y, $"AUDC   {PokeyChip.DistortionName(timbre).ToUpperInvariant()}");

        foreach (var step in recipe.Audc)
        {
            screen.Text(StepText(step), DetailX + 12f, y, StepSize, Screen.Number);

            // Named again only if this step disagrees with the heading. No recipe does
            // today, but a chain is free to write a different high nibble, and a heading
            // that quietly lied about it would be worse than a slightly crowded row.
            int here = step.Value >> 5;
            string volume = here == timbre
                ? $"VOL {step.Value & 0x0f}"
                : $"{PokeyChip.DistortionName(here).ToUpperInvariant()}, VOL {step.Value & 0x0f}";

            screen.TextRight(volume, DetailX + DetailWidth, y, StepSize, Screen.Body);
            y += 20f;
        }

        // --- Why a designer would reach for it -----------------------------
        y += 18f;
        foreach (string wrapped in screen.Wrap(recipe.Note, DetailWidth, NoteSize))
        {
            screen.Text(wrapped, DetailX, y, NoteSize, Screen.Body);
            y += 19f;
        }

        // --- What those numbers do over time -------------------------------
        DrawPlot(screen, DetailX, PlotTop, "PITCH, HZ", detail.Audf, Pitch);
        DrawPlot(screen, DetailX + DetailWidth - PlotWidth - PlotLabels, PlotTop, "VOLUME",
                 detail.Audc, Volume);

        DrawWave(screen, recipe, detail.Wave);
    }

    /// <summary>Volume is AUDC's low nibble; the distortion in the high bits never moves,
    /// because the arcade's own envelope code masks the delta so a sound's timbre survives
    /// its fade.</summary>
    private static double Volume(int audc) => audc & 0x0f;

    /// <summary>
    /// The samples themselves, as a min-max envelope one pixel column wide.
    ///
    /// <b>Not a redraw of the pitch plot at higher resolution.</b> The registers say what
    /// the chip was told; this says what came out — so the difference between a tone and a
    /// poly is visible as texture rather than having to be taken on trust, and a sound that
    /// renders quieter than its neighbours shows it here before it is noticed by ear.
    ///
    /// Column min and max rather than a single sampled value, because at forty-four
    /// thousand samples across six hundred columns anything else aliases: picking one
    /// sample per column would draw a plausible waveform that is not this one.
    /// </summary>
    private void DrawWave(Screen screen, Recipe recipe, float[] samples)
    {
        float middle = WaveTop + 16f + WaveHeight / 2f;

        screen.Text("WAVEFORM", DetailX, WaveTop, PlotSize, Screen.Body);
        screen.TextRight($"{Bank.Seconds(recipe) * 1000.0:0} MS",
                         DetailX + DetailWidth, WaveTop, PlotSize, Screen.Body);
        screen.Rule(DetailX, middle, DetailWidth);

        int columns = (int)DetailWidth;
        for (int c = 0; c < columns; c++)
        {
            int from = (int)((long)c * samples.Length / columns);
            int to = Math.Max(from + 1, (int)((long)(c + 1) * samples.Length / columns));

            float low = 0f, high = 0f;
            for (int i = from; i < to && i < samples.Length; i++)
            {
                low = Math.Min(low, samples[i]);
                high = Math.Max(high, samples[i]);
            }

            float x = DetailX + c;
            screen.Line(new Vector2(x, middle - high * WaveHeight / 2f),
                        new Vector2(x, middle - low * WaveHeight / 2f), Screen.Number, 1f);
        }
    }

    /// <summary>Works out the selected sound's expensive parts, once per selection.</summary>
    private Detail Detailed(Recipe recipe)
    {
        if (_detail.Of == _list.Selected && _detail.Wave != null) return _detail;

        int ticks = Bank.TotalTicks(recipe);
        _detail = new Detail(
            _list.Selected, ticks,
            SoundLibrary.Trace(recipe.Audf, ticks),
            SoundLibrary.Trace(recipe.Audc, ticks),
            ChipSound.Render(recipe.Audf, recipe.Audc, Bank.TickHz, Bank.ClockHz,
                             recipe.Amplitude));
        return _detail;
    }

    /// <summary>A bar that empties over exactly the sound's own length. Feedback that a key
    /// press landed, and a reminder that these voices are measured in tens of
    /// milliseconds.</summary>
    private void DrawPlayBar(Screen screen, float y)
    {
        screen.Rule(DetailX, y + 6f, DetailWidth);

        bool sounding = _playing == _list.Selected && _playRemaining > 0.0 && _playLength > 0.0;
        if (!sounding) return;

        float fraction = (float)(1.0 - _playRemaining / _playLength);
        screen.Line(new Vector2(DetailX, y + 6f),
                    new Vector2(DetailX + DetailWidth * fraction, y + 6f), Screen.Live, 3f);
    }

    private static float Section(Screen screen, float y, string label)
    {
        screen.Text(label, DetailX, y, 12f, Screen.Heading);
        screen.Rule(DetailX, y + 17f, DetailWidth);
        return y + 27f;
    }

    /// <summary>A step as its four fields, in the order the arcade stores them and the
    /// order <c>Copy as C#</c> emits them, so what is on screen can be matched against the
    /// source line by eye. The delta carries its sign even when positive: on AUDF a
    /// positive delta lowers the pitch, and that surprise is worth keeping visible.</summary>
    private static string StepText(StepEnvelope.Step step)
        => $"VALUE {step.Value,3}   INNER {step.Inner,3}   "
           + $"DELTA {step.Delta.ToString("+0;-0; 0"),3}   OUTER {step.Outer,2}";

    /// <summary>
    /// A divider's pitch, as the chip computes it.
    ///
    /// AUDF is a divider, not a note, so the curve this produces is a hyperbola rather than
    /// a line — which is exactly why a sweep with a constant delta sounds like it slows
    /// down as it falls, and worth being able to see.
    /// </summary>
    private double Pitch(int audf)
        => Bank.ClockHz / PokeyClocks.SlowDivider / (2.0 * (audf + 1));

    private static void DrawPlot(Screen screen, float x, float y, string label, int[] trace,
                                 Func<int, double> project)
    {
        screen.Text(label, x, y, PlotSize, Screen.Body);
        float top = y + 16f, bottom = top + PlotHeight;

        screen.Rule(x, bottom, PlotWidth);

        double low = double.MaxValue, high = double.MinValue;
        foreach (int raw in trace)
        {
            double value = project(raw);
            low = Math.Min(low, value);
            high = Math.Max(high, value);
        }

        // A flat trace is a real and common answer -- a held divider is how an engine hum
        // is made, and most of the library holds its volume steady -- so it gets a line
        // through the middle and a single label. Widening it into a fake range instead
        // would divide by zero for a moment and then claim a sweep that is not there.
        if (high - low < 1e-6)
        {
            float centre = top + PlotHeight / 2f;
            screen.Line(new Vector2(x, centre), new Vector2(x + PlotWidth, centre),
                        Screen.Number, 1.8f);
            screen.Text($"{high:0}", x + PlotWidth + 6f, centre - PlotSize / 2f, PlotSize,
                        Screen.Body);
            return;
        }

        // Drawn as a stair, not as a line through the points.
        //
        // A register holds its value for the whole tick and then jumps, so joining tick
        // centres draws a slope where the chip has a step -- which reads as a glide the
        // sound does not contain. Coin, two flat notes a fifth apart, is the case that
        // makes the difference obvious.
        float previousY = 0f;
        for (int i = 0; i < trace.Length; i++)
        {
            float x0 = x + PlotWidth * i / trace.Length;
            float x1 = x + PlotWidth * (i + 1) / trace.Length;
            float py = bottom - (float)((project(trace[i]) - low) / (high - low)) * PlotHeight;

            if (i > 0 && Math.Abs(py - previousY) > 0.5f)
                screen.Line(new Vector2(x0, previousY), new Vector2(x0, py), Screen.Number, 1.8f);

            screen.Line(new Vector2(x0, py), new Vector2(x1, py), Screen.Number, 1.8f);
            previousY = py;
        }

        // Labelled with its own range rather than a shared axis: the pitch of a laser and
        // the pitch of an engine hum are two orders of magnitude apart, and a scale that
        // fits both would flatten every sweep in the library to a straight line.
        screen.Text($"{high:0}", x + PlotWidth + 6f, top - 4f, PlotSize, Screen.Body);
        screen.Text($"{low:0}", x + PlotWidth + 6f, bottom - PlotSize + 2f, PlotSize, Screen.Body);
    }
}
