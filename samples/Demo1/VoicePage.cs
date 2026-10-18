using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Input;
using Speech;

namespace Demo1;

/// <summary>
/// Makes the demo talk, and shows what it cost.
///
/// <b>The point is the smallness.</b> A game that wants to say "extra life" out loud does
/// not need a voice actor, a recording session, or a single audio file — it needs the
/// three calls in <see cref="Voice"/> and the word written in its source. Everything on
/// this page is the same pipeline with one argument changed, which is why the settings sit
/// on the left and right arrows rather than buried somewhere.
///
/// The one genuinely interesting knob is the level count. Pass 15 and you get four bits,
/// which is all a POKEY's volume nibble could hold and exactly how S.A.M. spoke on an
/// Atari in 1982. Pass 32767 and it is the same synthesiser, unquantised. Hearing those
/// two next to each other is the fastest way to understand what the chip was and was not
/// contributing.
/// </summary>
public sealed class VoicePage : IPage, IDisposable
{
    private const float ListX = 40f, ListWidth = 480f, ListTop = 122f;
    private const float DetailX = 592f, DetailWidth = 648f;
    private const float BodySize = 13f, LabelSize = 12f;

    // The last two blocks sit at fixed heights rather than after however many wrapped
    // lines the selected phrase happens to need, so arrowing down the list changes the
    // contents of the panel and not its shape.
    private const float PipelineTop = 528f, HowTop = 664f;

    private readonly Voice _voice = new();
    private readonly ListView _list;
    private readonly int[] _phonemeCount;
    private readonly Sounds _blips;

    private int _setting;

    // What is currently being said, and for how much longer -- MonoGame will not tell us
    // when a SoundEffect finishes, but its own Duration is exact.
    private int _speaking = -1;
    private double _speakRemaining, _speakLength;

    // The built utterance for whatever is selected. Rebuilt when the line or the setting
    // changes, in Update rather than while drawing, so a frame never does synthesis
    // halfway through painting.
    private SoundEffect _current;
    private (int Line, int Setting) _currentOf = (-1, -1);

    public string Name => "VOICE";
    public string Hint => "UP DOWN LINE    LEFT RIGHT VOICE    ENTER SPEAK    CLICK A LINE";

    /// <summary><paramref name="blips"/> is <see cref="Game1"/>'s, shared with the sounds
    /// page — this one only wants the menu blip out of it.</summary>
    public VoicePage(Sounds blips)
    {
        var rows = new List<ListView.Row>();
        _phonemeCount = new int[VoiceLibrary.All.Length];

        for (int i = 0; i < VoiceLibrary.All.Length; i++)
        {
            var line = VoiceLibrary.All[i];
            _phonemeCount[i] = Phonemes.Parse(line.Phonemes, out _).Count;
            rows.Add(new ListView.Row(line.Category, Shorten(line.English),
                                      $"{_phonemeCount[i]} PH"));
        }

        _list = new ListView(rows, ListX, ListWidth, ListTop);
        _blips = blips;
    }

    /// <summary>The list column is narrower than a sentence. Thirty characters is what fits
    /// beside the phoneme count at this size; the full text is the first thing the detail
    /// panel shows, so trimming past that loses nothing.</summary>
    private static string Shorten(string english)
        => english.Length <= 30 ? english : english.Substring(0, 28) + "..";

    public void Update(Input input, double elapsedSeconds)
    {
        if (input.Pressed(Keys.Down) && _list.Move(+1)) Blip();
        if (input.Pressed(Keys.Up) && _list.Move(-1)) Blip();

        // Left and right change the voice rather than jumping a category, which is what
        // they do on the sounds page. Comparing settings is this page's whole job, and the
        // list is short enough not to need the jump.
        if (input.Pressed(Keys.Right)) Setting(+1);
        if (input.Pressed(Keys.Left)) Setting(-1);

        if (input.Pressed(Keys.Enter) || input.Pressed(Keys.Space)) Speak(_list.Selected);

        if (input.Clicked)
        {
            int hit = _list.HitTest(input.MouseX, input.MouseY);
            if (hit >= 0) Speak(hit);
        }

        Build();
        _speakRemaining = Math.Max(0.0, _speakRemaining - elapsedSeconds);
    }

    private void Blip() => _blips.Play("Menu blip");

    private void Setting(int delta)
    {
        int was = _setting;
        _setting = Math.Clamp(_setting + delta, 0, VoiceSetting.All.Length - 1);
        if (_setting != was) Blip();
    }

    /// <summary>Synthesises the selected line under the selected setting, if it is not
    /// already built. <see cref="Voice.Get"/> caches, so this is a dictionary lookup on
    /// every frame but the first after a change.</summary>
    private void Build()
    {
        if (_currentOf == (_list.Selected, _setting)) return;

        _current = _voice.Get(_list.Selected, _setting);
        _currentOf = (_list.Selected, _setting);
    }

    private void Speak(int line)
    {
        _list.Select(line);
        Build();

        _speaking = line;
        _speakLength = _current?.Duration.TotalSeconds ?? 0.0;
        _speakRemaining = _speakLength;
        _voice.Play(line, _setting);
    }

    // --- Drawing ----------------------------------------------------------

    public void Draw(Screen screen)
    {
        _list.Draw(screen, _speakRemaining > 0.0 ? _speaking : -1);
        DrawDetail(screen, VoiceLibrary.All[_list.Selected]);
    }

    private void DrawDetail(Screen screen, VoiceLine line)
    {
        var setting = VoiceSetting.All[_setting];
        float y = ListTop - 6f;

        // The sentence, at whatever size fits. A phrase is a great deal longer than a
        // sound's name, and shrinking it beats truncating the thing the page is about.
        float size = 22f;
        while (size > 13f && screen.Measure(line.English, size) > DetailWidth) size -= 1f;
        screen.Text(line.English, DetailX, y, size, Screen.Heading);
        y += 36f;

        screen.Text(line.Category, DetailX, y, LabelSize, Screen.Body);
        screen.TextRight($"{_phonemeCount[_list.Selected]} PHONEMES",
                         DetailX + DetailWidth, y, LabelSize, Screen.Body);
        y += 24f;

        DrawSpeakBar(screen, y);
        y += 22f;

        // --- Which voice, and what that costs -------------------------------
        y = Section(screen, y, $"VOICE   {_setting + 1} OF {VoiceSetting.All.Length}   "
                               + "- LEFT RIGHT TO CHANGE");
        screen.Text(setting.Name, DetailX + 12f, y, BodySize, Screen.Number);
        y += 21f;

        foreach (string wrapped in screen.Wrap(setting.Note, DetailWidth - 12f, LabelSize))
        {
            screen.Text(wrapped, DetailX + 12f, y, LabelSize, Screen.Body);
            y += 18f;
        }

        // --- What is actually spoken ----------------------------------------
        y += 12f;
        y = Section(screen, y, "PHONEMES");
        foreach (string wrapped in screen.Wrap(line.Phonemes, DetailWidth - 12f, LabelSize))
        {
            screen.Text(wrapped, DetailX + 12f, y, LabelSize, Screen.Number);
            y += 18f;
        }

        y += 4f;
        screen.Text(Provenance(line), DetailX + 12f, y, LabelSize, Screen.Body);
        y += 22f;

        // Only when there is one, which is most of the point: a melody is the exception,
        // reached for when the contour cannot say the line on its own.
        if (line.Melody.Length > 0)
        {
            y = Section(screen, y, "MELODY   ONE NOTE PER SYLLABLE, INSTEAD OF THE CONTOUR");
            foreach (string wrapped in screen.Wrap(line.Melody, DetailWidth - 12f, LabelSize))
            {
                screen.Text(wrapped, DetailX + 12f, y, LabelSize, Screen.Number);
                y += 18f;
            }
            y += 8f;
        }

        if (line.Note.Length > 0)
        {
            foreach (string wrapped in screen.Wrap(line.Note, DetailWidth, LabelSize))
            {
                screen.Text(wrapped, DetailX, y, LabelSize, Screen.Body);
                y += 18f;
            }
        }

        DrawPipeline(screen, line, setting);
        DrawHow(screen);
    }

    /// <summary>
    /// Where this line's phonemes came from.
    ///
    /// Worth saying on screen, because it is the honest limit of the letter-to-sound
    /// layer: the rules are only mostly right, so a word they guessed is the one to listen
    /// to before shipping. It is not a warning that it is wrong — most guesses are fine —
    /// but knowing which is the difference between checking one word and re-reading all of
    /// them.
    /// </summary>
    private static string Provenance(VoiceLine line)
    {
        if (!line.ByRule) return "HAND-WRITTEN PHONEMES";
        if (line.Guessed.Count == 0) return "EVERY WORD FROM THE DICTIONARY";
        return $"GUESSED BY RULE - {string.Join(", ", line.Guessed)}";
    }

    /// <summary>The size of the thing at each stage. Speech is usually imagined as
    /// expensive, and four numbers showing a sentence costing a few thousand bytes of
    /// levels is a quicker argument than a paragraph saying so.</summary>
    private void DrawPipeline(Screen screen, VoiceLine line, VoiceSetting setting)
    {
        float y = Section(screen, PipelineTop, "PIPELINE");

        int words = line.English.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        int levels = (int)((_current?.Duration.TotalSeconds ?? 0.0) * setting.DacRateHz);

        Row(screen, ref y, "ENGLISH", $"{words} WORDS, {line.English.Length} CHARACTERS");
        Row(screen, ref y, "PHONEMES", $"{_phonemeCount[_list.Selected]}");
        Row(screen, ref y, "LEVELS",
            $"{levels} SAMPLES, {setting.MaxLevel + 1} LEVELS, {setting.DacRateHz:0} HZ");
        Row(screen, ref y, "LENGTH",
            $"{(_current?.Duration.TotalMilliseconds ?? 0.0):0} MS");
    }

    private void DrawHow(Screen screen)
    {
        float y = Section(screen, HowTop, "HOW");

        Row(screen, ref y, "LETTERTOSOUND.TRANSLATE", "ENGLISH  -  PHONEMES");
        Row(screen, ref y, "SPEECHSOURCE.SPEAK", "PHONEMES  -  LEVELS");
        Row(screen, ref y, "VOICE.HOLD", "LEVELS  -  16 BIT PCM");

        screen.Text("ALL OF IT IS IN DEMO1/VOICE.CS, AND IT IS ABOUT THIRTY LINES.",
                    DetailX + 12f, y + 4f, LabelSize, Screen.Body);
    }

    private static void Row(Screen screen, ref float y, string label, string value)
    {
        screen.Text(label, DetailX + 12f, y, LabelSize, Screen.Body);
        screen.Text(value, DetailX + 250f, y, LabelSize, Screen.Number);
        y += 19f;
    }

    private void DrawSpeakBar(Screen screen, float y)
    {
        screen.Rule(DetailX, y + 6f, DetailWidth);

        bool sounding = _speaking == _list.Selected && _speakRemaining > 0.0 && _speakLength > 0.0;
        if (!sounding) return;

        float fraction = (float)(1.0 - _speakRemaining / _speakLength);
        screen.Line(new Vector2(DetailX, y + 6f),
                    new Vector2(DetailX + DetailWidth * fraction, y + 6f), Screen.Live, 3f);
    }

    private static float Section(Screen screen, float y, string label)
    {
        screen.Text(label, DetailX, y, LabelSize, Screen.Heading);
        screen.Rule(DetailX, y + 17f, DetailWidth);
        return y + 27f;
    }

    /// <summary>Only the synthesised utterances. The blips belong to <see cref="Game1"/>,
    /// which disposes them.</summary>
    public void Dispose() => _voice?.Dispose();
}
