using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Pokey;
using Speech;

namespace PokeyLabForms.Panels;

/// <summary>
/// The speech page: 4-bit volume-only DAC, driven by a formant synthesiser.
///
/// <b>This is the page that turns the rest of the window off.</b> Everything else here
/// is about what POKEY does -- dividers, poly gating, the distortion field. Speaking
/// uses none of it: the channel goes into volume-only mode, which cuts the divider and
/// the poly out entirely, and the sound arrives already made from the <c>Speech</c>
/// library. That is exactly what S.A.M. did in 1982.
///
/// <b>Its own tab, because it outgrew a column.</b> It began as a 316px panel wedged
/// between the scope and the poly trains, which was fine when it played one vowel and
/// impossible once it needed an utterance box, an example list and a phoneme reference.
/// The chip's own registers stay visible above at all times; it is only the bottom-right
/// explainer that swaps, so an A/B against a tone running on another channel still works
/// without leaving the tab.
///
/// <b>Phonemes, not English.</b> Type <c>HH EH L OW</c> and it says it; type "hello" and
/// it does not. Letter-to-sound rules are a layer above this one. S.A.M. shipped a
/// phoneme mode alongside its text mode for the same two reasons: it is what you debug
/// the synthesiser with, and it is the only way to say a word the rules get wrong.
/// </summary>
public sealed class VoicePanel : Panel
{
    /// <summary>
    /// Interrupt rates worth comparing.
    ///
    /// The range is what a 1MHz 6502 could sustain while also doing the synthesis, and
    /// the differences are audible rather than academic: at 4kHz nothing above 1800Hz
    /// survives, which collapses /ng/ onto /n/ and takes the sibilants with it. 11025 is
    /// the modern reference point -- more than the period really had.
    /// </summary>
    private static readonly double[] Rates = { 4000, 6000, 8000, 11025 };

    /// <summary>
    /// Speaking rates, as multipliers on the phoneme table's own durations.
    ///
    /// <b>1.0 is the table and stays the default</b>, because it is the baseline every
    /// duration quoted anywhere in this project refers to, and the self-test asserts
    /// against it. But the table is slow -- around 2.5 words a second against a normal
    /// 4 -- so 1.5 is marked as where ordinary speech actually sits, and is probably
    /// where anyone listening rather than measuring wants to be.
    /// </summary>
    private static readonly (string Label, double Value)[] Speeds =
    {
        ("0.6× slow", 0.6),
        ("0.7× measured", 0.7),
        ("0.8×", 0.8),
        ("1.0× table", 1.0),
        ("1.25×", 1.25),
        ("1.5× natural", 1.5),
        ("2.0× fast", 2.0),
    };

    private const int DefaultSpeedIndex = 3;

    /// <summary>Moves the speed combo to the nearest entry to a voice's own pace.
    /// Nearest rather than exact, so a profile can carry any number without the list
    /// having to contain it.</summary>
    private void ApplySuggestedSpeed(double wanted)
    {
        int best = 0;
        for (int i = 1; i < Speeds.Length; i++)
            if (Math.Abs(Speeds[i].Value - wanted) < Math.Abs(Speeds[best].Value - wanted)) best = i;

        _loading = true;
        _speed.SelectedIndex = best;
        _loading = false;
    }

    /// <summary>Levels this front end plays through. POKEY's volume nibble, and the one
    /// place the chip's arithmetic reaches the speech library.</summary>
    private const int Levels = PokeyChip.AudcVolumeMask;

    private readonly ChipSession _session;

    // Not readonly: assigned in the two Build methods rather than inline, because this
    // page has enough controls that building it in one constructor body stopped being
    // readable. The two halves are independent and each is built in one place.
    private ComboBox _example;
    private TextBox _english;
    private TextBox _phonemes;
    private TextBox _melody;
    private string _derivedNote = "";
    private ComboBox _rate;
    private ComboBox _speed;
    private ComboBox _voice;
    private ComboBox _channel;
    private CheckBox _loop;
    private Button _play;
    private Button _stop;
    private Label _readout;
    private ListView _reference;

    private bool _loading = true;

    public VoicePanel(ChipSession session)
    {
        _session = session;
        Dock = DockStyle.Fill;

        var box = new GroupBox
        {
            Text = "Voice",
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 4, 8, 6),
        };

        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62f));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38f));

        split.Controls.Add(BuildControls(), 0, 0);
        split.Controls.Add(BuildReference(), 1, 0);

        box.Controls.Add(split);
        Controls.Add(box);

        lock (_session.Gate) _session.Voice.Channel = _channel.SelectedIndex;
        _loading = false;
        Rebuild();
    }

    // --- Left: what to say and how ----------------------------------------

    private Control BuildControls()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 10 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));   // example
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));   // English
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));   // phonemes
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));   // melody
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));   // transport
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));   // rate
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));   // channel
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));   // loop
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));   // readout
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // explanation

        // --- Examples -----------------------------------------------------
        //
        // Selecting one *fills the box* rather than switching to a canned mode. That is
        // the whole point: an example is a starting position you can then edit, and the
        // box stays the single source of truth for what actually gets spoken.

        layout.Controls.Add(Caption("Example"), 0, 0);
        _example = Picker();
        foreach (var (label, _, _, _) in SpeechSource.Examples) _example.Items.Add(label);
        // Selected while _loading is still set, so it shows which example the boxes
        // already contain without the handler firing to put it there again. A blank
        // combo above a full text box reads as though the two are unrelated.
        _example.SelectedIndex = 0;
        _example.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _example.SelectedIndex < 0) return;

            var (_, english, phonemes, melody) = SpeechSource.Examples[_example.SelectedIndex];

            // Every box is set from the example, and the phonemes are the example's own
            // hand-checked ones rather than whatever the rules would derive from its
            // English. The example demonstrates the synthesiser; the rules get
            // demonstrated by typing into the English box yourself.
            //
            // The melody is set even when empty, which is the point: picking a spoken
            // example after a sung one has to clear the tune, or the next sentence comes
            // out sung to the previous one's notes.
            _loading = true;
            _english.Text = english;
            _melody.Text = melody;
            _loading = false;

            _phonemes.Text = phonemes;
            Rebuild();
        };
        layout.Controls.Add(_example, 1, 0);

        // --- English ------------------------------------------------------
        //
        // The English box *derives* the phoneme box; the phoneme box is what actually
        // gets spoken. That order matters and is the honest one: the rules are only
        // mostly right, so their output has to land somewhere you can correct it rather
        // than going straight to the synthesiser. Editing the English again overwrites
        // your phoneme edits, which is the cost of having a source of truth at all.

        layout.Controls.Add(Caption("English"), 0, 1);
        _english = new TextBox
        {
            Text = SpeechSource.Examples[0].English,
            Dock = DockStyle.Fill,
            Margin = new Padding(3, 3, 3, 3),
        };
        _english.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            Derive();
            _session.PlaySample();
        };
        _english.Leave += (_, _) => Derive();
        layout.Controls.Add(_english, 1, 1);

        _phonemes = new TextBox
        {
            Text = SpeechSource.Examples[0].Phonemes,
            Dock = DockStyle.Fill,
            Margin = new Padding(3, 2, 3, 2),
            Font = new Font("Consolas", 9.75f),
        };
        // Enter speaks. Typing an utterance and pressing return is the interaction this
        // page exists for, and reaching for the mouse between every edit would make
        // comparing two pronunciations tedious enough to stop doing.
        _phonemes.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;   // no ding from the unhandled Enter
            Rebuild();
            _session.PlaySample();
        };
        _phonemes.Leave += (_, _) => Rebuild();
        layout.Controls.Add(Caption("Phonemes"), 0, 2);
        layout.Controls.Add(_phonemes, 1, 2);

        // A tooltip rather than a line of text under the panel: the melody row took the
        // space that line used to have, and these are facts you want at the moment you
        // are editing the box rather than permanently on screen.
        new ToolTip().SetToolTip(_phonemes,
            "ARPABET. Double-click the list on the right to insert a code.\n"
            + "A hyphen is a pause. A digit marks stress: AA1 strong, AX0 reduced.\n"
            + "A \"?\" in either box makes the pitch rise at the end.");

        // --- Melody -------------------------------------------------------
        //
        // Empty means speak. Anything in it means sing, and the notes take over pitch
        // and rhythm entirely -- a sung syllable's length comes from its note, not from
        // the phoneme table or the speed control.

        layout.Controls.Add(Caption("Melody"), 0, 3);
        _melody = new TextBox
        {
            Text = SpeechSource.Examples[0].Melody,
            Dock = DockStyle.Fill,
            Margin = new Padding(3, 2, 3, 2),
            Font = new Font("Consolas", 9.75f),
        };
        _melody.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            Rebuild();
            _session.PlaySample();
        };
        _melody.Leave += (_, _) => Rebuild();
        layout.Controls.Add(_melody, 1, 3);

        new ToolTip().SetToolTip(_melody,
            "One note per syllable, in order. Empty means speak instead of sing.\n"
            + "C4 is middle C; C5 an octave up; F#3 and Bb4 work. R is a rest.\n"
            + "\"/2\" makes a note two beats. A leading \"112:\" sets the tempo.");

        // --- Transport ----------------------------------------------------

        var transport = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 3, 0, 3),
        };
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35f));

        _play = new Button { Text = "Speak", Dock = DockStyle.Fill };
        _play.Click += (_, _) => { Rebuild(); _session.PlaySample(); };
        transport.Controls.Add(_play, 0, 0);

        _stop = new Button { Text = "Stop", Dock = DockStyle.Fill };
        _stop.Click += (_, _) => { lock (_session.Gate) _session.Voice.Stop(_session.Chip); };
        transport.Controls.Add(_stop, 1, 0);

        // The control condition: what the nibble costs with no speech in the way.
        var tone = new Button { Text = "Reference tone", Dock = DockStyle.Fill };
        tone.Click += (_, _) =>
        {
            double rate = Rates[_rate.SelectedIndex];
            var sine = SpeechSource.Sine(440.0, 0.5, rate, Levels);
            lock (_session.Gate)
            {
                _session.Voice.Samples = sine;
                _session.Voice.SampleRateHz = rate;
            }
            _readout.Text = $"sine 440 Hz · {sine.Length} samples · 4 bits — the quantisation on its own";
            _readout.ForeColor = SystemColors.GrayText;
            _session.PlaySample();
        };
        transport.Controls.Add(tone, 2, 0);

        layout.Controls.Add(transport, 0, 4);
        layout.SetColumnSpan(transport, 2);

        // --- Settings -----------------------------------------------------

        // Rate and speed share a row. They are the two timing controls and they belong
        // together, and the row they would otherwise each take is the space the
        // explanation at the bottom needs.
        layout.Controls.Add(Caption("Timing"), 0, 5);

        var timing = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0) };
        timing.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46f));
        timing.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52f));
        timing.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54f));

        _rate = Picker();
        foreach (double rate in Rates) _rate.Items.Add($"{rate:0} Hz");
        _rate.SelectedIndex = 2;   // 8kHz, the middle of what the period could sustain
        _rate.SelectedIndexChanged += (_, _) => Rebuild();
        timing.Controls.Add(_rate, 0, 0);

        timing.Controls.Add(new Label
        {
            Text = "Speed",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
        }, 1, 0);

        _speed = Picker();
        foreach (var (label, _) in Speeds) _speed.Items.Add(label);
        _speed.SelectedIndex = DefaultSpeedIndex;
        _speed.SelectedIndexChanged += (_, _) => Rebuild();
        timing.Controls.Add(_speed, 2, 0);

        layout.Controls.Add(timing, 1, 5);

        // The two things the shortened labels no longer say. Both matter and neither
        // fits on a combo box that now has to share its row.
        var hints = new ToolTip();
        hints.SetToolTip(_rate, "The interrupt rate the 6502 rewrites the volume nibble at.\n"
                                + "Not the output rate, and not the chip's clock.");
        hints.SetToolTip(_speed, "Scales every phoneme's duration.\n"
                                 + "Constricted sounds compress less than vowels, as they do in real speech.");

        // Voice and channel share a row, the same way rate and speed do. Voice leads
        // because it is the one that changes what you hear rather than where.
        layout.Controls.Add(Caption("Voice"), 0, 6);

        var who = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0) };
        who.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52f));
        who.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58f));
        who.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48f));

        _voice = Picker();
        foreach (var profile in VoiceProfile.All) _voice.Items.Add(profile.Name);
        _voice.SelectedIndex = 0;
        _voice.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;

            // Selecting a voice moves the speed combo to that voice's pace. It seeds the
            // control rather than overriding it: after this the speed control is what
            // decides, so the two never disagree about which one is in charge.
            ApplySuggestedSpeed(VoiceProfile.All[_voice.SelectedIndex].Speed);
            Rebuild();
        };
        who.Controls.Add(_voice, 0, 0);

        who.Controls.Add(new Label
        {
            Text = "Channel",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
        }, 1, 0);

        hints.SetToolTip(_voice,
            "Pitch, formant scale, bandwidths and glottal rolloff.\n"
            + "\"Low and deliberate\" is fitted to a measured reference, not chosen by ear.\n"
            + "Picking a voice also sets the speed to its own pace.");

        _channel = Picker();
        for (int i = 0; i < PokeyChip.Channels; i++) _channel.Items.Add($"Channel {i + 1}");
        // Channel 4 by default, so the DAC does not land on channel 1 where the envelope
        // and the opening tone both live -- the A/B wants them coexisting.
        _channel.SelectedIndex = PokeyChip.Channels - 1;
        _channel.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            lock (_session.Gate)
            {
                // Hand the old channel back before moving, or it keeps the volume-only
                // bit and its last nibble as a standing level. Only if it was actually
                // playing: Stop clears the whole AUDC byte, which would wipe a tone the
                // user had set up on that channel by hand.
                if (_session.Voice.Playing) _session.Voice.Stop(_session.Chip);
                _session.Voice.Channel = _channel.SelectedIndex;
            }
        };
        who.Controls.Add(_channel, 2, 0);
        layout.Controls.Add(who, 1, 6);

        _loop = new CheckBox
        {
            Text = "Loop",
            Dock = DockStyle.Fill,
            AutoSize = false,
            Margin = new Padding(3, 2, 3, 2),
        };
        _loop.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            lock (_session.Gate) _session.Voice.Looping = _loop.Checked;
        };
        layout.Controls.Add(_loop, 1, 7);

        _readout = new Label { Dock = DockStyle.Fill, AutoSize = false, ForeColor = SystemColors.GrayText };
        layout.Controls.Add(_readout, 0, 8);
        layout.SetColumnSpan(_readout, 2);

        var explanation = new Label
        {
            // One line's worth. The row only gets what the fixed rows above leave over,
            // and anything longer than about a hundred characters loses its tail --
            // which twice cost this label the very clause that made it worth reading.
            Text = "A hyphen is a pause; a digit marks stress (AA1 strong, AX0 reduced). "
                   + "A \"?\" makes the pitch rise.",
            Dock = DockStyle.Fill,
            AutoSize = false,
            ForeColor = SystemColors.GrayText,
        };
        layout.Controls.Add(explanation, 0, 9);
        layout.SetColumnSpan(explanation, 2);

        return layout;
    }

    // --- Right: what the codes mean ---------------------------------------

    /// <summary>
    /// The phoneme inventory, as a reference.
    ///
    /// Typing ARPABET from memory is not reasonable, and a page that demands it without
    /// saying what the codes sound like is a page you cannot use without a second
    /// window open. Example words come from the inventory itself rather than being
    /// listed again here, so the two cannot drift.
    /// </summary>
    private Control BuildReference()
    {
        _reference = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            Margin = new Padding(6, 3, 3, 3),
        };
        _reference.Columns.Add("Code", 58);
        _reference.Columns.Add("As in", 90);

        foreach (var phoneme in Phonemes.All)
        {
            if (phoneme.Code == Phonemes.Silence) continue;
            _reference.Items.Add(new ListViewItem(new[] { phoneme.Code, phoneme.Example }));
        }

        // Double-click inserts, so the reference is usable rather than merely readable.
        _reference.DoubleClick += (_, _) =>
        {
            if (_reference.SelectedItems.Count == 0) return;

            string code = _reference.SelectedItems[0].SubItems[0].Text;
            string text = _phonemes.Text;
            _phonemes.Text = text.Length == 0 || text.EndsWith(" ") ? text + code + " " : text + " " + code + " ";
            _phonemes.SelectionStart = _phonemes.Text.Length;
            _phonemes.Focus();
            Rebuild();
        };

        return _reference;
    }

    private static Label Caption(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoSize = false,
    };

    private static ComboBox Picker() => new()
    {
        Dock = DockStyle.Fill,
        DropDownStyle = ComboBoxStyle.DropDownList,
        Margin = new Padding(3, 3, 3, 3),
    };

    /// <summary>
    /// Runs the letter-to-sound rules over the English box and puts the result in the
    /// phoneme box.
    ///
    /// Words that came from the rules rather than the exception dictionary are named in
    /// the readout. That is not a warning that they are wrong -- most will be fine --
    /// but they are the ones worth glancing at, and knowing which is the difference
    /// between correcting a transcription and re-reading all of it.
    /// </summary>
    private void Derive()
    {
        if (_loading) return;

        string english = _english.Text;
        if (string.IsNullOrWhiteSpace(english)) { _derivedNote = ""; Rebuild(); return; }

        string phonemes = LetterToSound.Translate(english, out List<string> guessed);
        _phonemes.Text = phonemes;

        _derivedNote = guessed.Count == 0
            ? "   ·   every word from the dictionary"
            : $"   ·   guessed by rule: {string.Join(" ", guessed).ToLowerInvariant()}";

        Rebuild();
    }

    /// <summary>
    /// Regenerates the buffer from whatever is in the box.
    ///
    /// Synthesised at the DAC's rate rather than at some higher rate and resampled,
    /// because the rate is the thing being demonstrated: generating at 44.1kHz and
    /// decimating would land the formants correctly and hide the aliasing that a real
    /// 4kHz interrupt would have produced.
    /// </summary>
    private void Rebuild()
    {
        if (_loading) return;

        double rate = Rates[_rate.SelectedIndex];
        double speed = Speeds[_speed.SelectedIndex].Value;

        // Either box can ask the question. The English is the natural place for a "?",
        // but someone working in phonemes alone still needs a way to say "this rises",
        // and a stray "?" among the phonemes parses as a pause rather than an error.
        var intonation = SpeechSource.IntonationOf(_english.Text + " " + _phonemes.Text);

        // The speed control wins over the profile's own suggestion. Selecting a voice
        // moves the speed combo to that voice's pace, so the two agree at that moment,
        // and after that the control is the one that decides.
        var profile = VoiceProfile.All[_voice.SelectedIndex];

        var samples = SpeechSource.Speak(_phonemes.Text, rate, Levels, out List<string> unknown,
                                         profile, speed, intonation, _melody.Text);

        // What the melody did with the lyric, when there is one. Notes and syllables
        // rarely line up first time, and "3 syllables past the end of the melody" is the
        // difference between fixing it and re-counting the whole thing by hand.
        string sung = "";
        if (!string.IsNullOrWhiteSpace(_melody.Text))
        {
            Song.Sing(Phonemes.Parse(_phonemes.Text, out _), _melody.Text, out sung);
            if (sung.Length > 0) sung = "   ·   " + sung;
        }

        bool wasPlaying;
        lock (_session.Gate)
        {
            wasPlaying = _session.Voice.Playing;
            _session.Voice.Samples = samples;
            _session.Voice.SampleRateHz = rate;
        }

        // Retrigger if it was already speaking, so changing the rate or the text is a
        // live comparison rather than something you have to press Speak again to hear.
        if (wasPlaying) _session.PlaySample();

        // Unknown tokens are named, not counted. "3 unknown" leaves you hunting; the
        // tokens themselves usually make the mistake obvious on sight.
        string note = unknown.Count > 0
            ? $"   ·   not recognised: {string.Join(" ", unknown)}"
            : sung.Length > 0 ? sung : _derivedNote;
        _readout.Text = samples.Length == 0
            ? "nothing to say"
            : $"{samples.Length} samples · {samples.Length / rate:0.000} s · {Nyquist(rate)}{note}";
        _readout.ForeColor = unknown.Count > 0 ? Color.Firebrick : SystemColors.GrayText;
    }

    /// <summary>What the rate can and cannot carry, named rather than left to be worked
    /// out -- it is the whole reason the rate is a control.</summary>
    private static string Nyquist(double rate)
    {
        double limit = rate * 0.45;
        string verdict = limit < 1500 ? "loses F2"
                       : limit < 2600 ? "loses F3"
                       : "keeps all three formants";
        return $"nothing above {limit:0} Hz — {verdict}";
    }

    /// <summary>Caller holds <see cref="ChipSession.Gate"/>.</summary>
    public void SyncFromChip()
    {
        bool playing = _session.Voice.Playing;
        string wanted = playing ? "Speak again" : "Speak";
        if (_play.Text != wanted) _play.Text = wanted;
        _stop.Enabled = playing;
    }
}
