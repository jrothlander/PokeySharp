using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Pokey;

namespace PokeyLabForms.Panels;

/// <summary>
/// Arcade-style envelopes, played on the emulated chip.
///
/// <b>POKEY has no envelopes.</b> It holds whatever was last written until something
/// writes again. What Tempest has is an interrupt routine that rewrites AUDF and AUDC
/// on a timer, walking a table of four-byte steps -- and that table is what this panel
/// edits. The voices come from the <c>.sound</c> banks beside the executable; Tempest's
/// own are in a bank of their own, kept outside this repository because they are the
/// ROM's numbers. Copy one into <c>banks/</c> beside the lab to audition it here.
///
/// <b>Both chains are shown in full, which PokeyLab cannot do.</b> There a chain is
/// edited one step at a time through an "editing step 3 of 8" row, because a caret-
/// driven list has nowhere to put eight steps at once. Most of Tempest's sounds are a
/// single step and lose nothing by it -- but its extra-life flourish is eight, and
/// seeing a chain as rows of values is the difference between reading the table and
/// inferring it.
/// </summary>
public sealed class EnvelopePanel : Panel
{
    /// <summary>
    /// One entry in the Sound list.
    ///
    /// <b>The two clock fields are what makes a bank sound different from the built-in
    /// one.</b> The built-in voice carries no tick rate, so it plays at whatever the form
    /// is already set to. A voice out of a <c>.sound</c> file was designed
    /// against the clock and tick rate written in that file, and every pitch in it scales
    /// with the clock -- so loading one without them plays the right numbers in the wrong
    /// key, which reads as a badly designed sound rather than as a missing setting.
    /// </summary>
    private readonly record struct Voice(
        string Label, StepEnvelope.Step[] Audf, StepEnvelope.Step[] Audc,
        double? TickHz, double? ClockHz);

    /// <summary>The library's own starting voice. It stays first in the list and stays
    /// compiled in, so the list is never empty even with no banks on disk.</summary>
    private static readonly (string Label, StepEnvelope.Step[] Audf, StepEnvelope.Step[] Audc)[] Presets =
    {
        ("Zap", StepEnvelope.ZapAudf, StepEnvelope.ZapAudc),
    };

    /// <summary>Where the <c>.sound</c> files are: beside the executable, put there by the
    /// csproj. Beside rather than embedded, because the whole point of the format is that
    /// a file can be edited and reopened.</summary>
    private static string BankDirectory => Path.Combine(AppContext.BaseDirectory, "banks");

    /// <summary>Column clamps, matching PokeyLab's. Inner and outer are counts and
    /// cannot be zero; delta is signed and small.</summary>
    private static readonly (int Low, int High)[] Limits =
    {
        (0, 255),     // value
        (1, 255),     // inner
        (-64, 64),    // delta
        (1, 255),     // outer
    };

    private readonly ChipSession _session;

    /// <summary>Everything the Sound list offers, in list order: the built-in voice,
    /// then every sound in every bank found beside the executable.</summary>
    private readonly List<Voice> _voices = new();

    private readonly ComboBox _preset;
    private readonly ComboBox _channel;
    private readonly NumericUpDown _tickHz;
    private readonly CheckBox _loop;
    private readonly Button _play;
    private readonly Button _stop;
    private Button _copy;

    private readonly DataGridView _audfGrid;
    private readonly DataGridView _audcGrid;
    private readonly ComboBox _audcDistortion;
    private readonly NumericUpDown _audcVolume;
    private readonly Label _notes;

    private bool _loading;

    public EnvelopePanel(ChipSession session)
    {
        _session = session;
        Dock = DockStyle.Fill;

        var box = new GroupBox
        {
            Text = "Envelope",
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 4, 8, 6),
        };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8 };

        // Stated rather than left to the default, and this is not tidiness. A
        // TableLayoutPanel given a ColumnCount but no ColumnStyle makes every column
        // AutoSize, so the column takes the width of its widest child and every other
        // child is stretched to match -- which is how the combo boxes here ended up 396
        // wide inside a 452-wide panel, clipped past their own dropdown arrows, and how
        // the step grids lost their Outer column off the right-hand edge. A single
        // column that is meant to fill its parent has to say so.
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // <b>Everything fixed is squeezed so the two grids get the rest.</b> A chain can
        // be eight steps -- the extra-life flourish is -- and a grid showing one row at a
        // time cannot tell you whether there is a second, which is most of why both
        // chains are shown in full here rather than one step at a time like PokeyLab.
        // Two structural savings do the work, rather than shaving pixels off everything:
        // the settings block puts Channel and Tick Hz on one line instead of two, and
        // each chain's Add/Remove buttons share a row with its heading instead of taking
        // one underneath. That is 74px back, and it goes straight into the grids.
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));       // settings
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));       // transport + loop
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));       // AUDF heading + buttons
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));        // AUDF grid
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));       // AUDC heading + buttons
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));        // AUDC grid
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));       // AUDC composer
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));       // notes

        // --- Settings -----------------------------------------------------

        // No AutoSize: it is the other half of the clipping described above. AutoSize
        // asks a panel for its preferred width, and a Percent column has no preferred
        // width to give -- the pair produces a size that fits nothing.
        // <b>Margin zeroed, rows raised to 28.</b> A combo box and a spinner both refuse to
        // shrink past about 23px, and the default 3px margin on this panel plus 3px on
        // each control left a 26px row with 20px of usable height -- so both controls
        // overflowed and the row below cut their bottom borders off. The panel's own
        // margin was the wasteful part: every other row here already insets nothing and
        // lets its children do it, so this now matches and the rows get the height back
        // without taking a pixel from the grids.
        var settings = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            Margin = new Padding(0),
        };
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62f));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56f));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64f));
        for (int i = 0; i < 2; i++) settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));

        settings.Controls.Add(Caption("Sound"), 0, 0);
        _preset = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(3, 2, 3, 2),
        };
        _voices.AddRange(CollectVoices(out string banksNote));
        foreach (var voice in _voices) _preset.Items.Add(voice.Label);
        _preset.SelectedIndex = 0;
        _preset.SelectedIndexChanged += (_, _) => { if (!_loading) LoadPreset(); };

        // The banks report on themselves in a tooltip rather than a label, because this
        // panel has no spare row -- the grids get whatever the fixed rows do not, and a
        // chain can be eight steps. A parse problem still has to be *shown*: the format
        // never throws, so a mistyped line is skipped and recorded, and a caller that
        // swallows Problems turns one typo into one silently missing sound.
        new ToolTip().SetToolTip(_preset, banksNote);
        settings.Controls.Add(_preset, 1, 0);
        settings.SetColumnSpan(_preset, 3);

        // Not something PokeyLab exposes -- it pins the envelope to channel 1. Offered
        // here because the interesting comparison is a voice against a drone: park a
        // tone on channel 2 and fire the shot over it, which needs the two on different
        // channels and costs one combo box.
        settings.Controls.Add(Caption("Channel"), 0, 1);
        _channel = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(3, 2, 3, 2),
        };
        for (int i = 0; i < PokeyChip.Channels; i++) _channel.Items.Add($"Channel {i + 1}");
        _channel.SelectedIndex = 0;
        _channel.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            lock (_session.Gate)
            {
                // Silence the channel being left, or it holds whatever AUDC it was last
                // written and drones for the rest of the session.
                _session.Envelope.Stop(_session.Chip);
                _session.Envelope.Channel = _channel.SelectedIndex;
            }
        };
        settings.Controls.Add(_channel, 1, 1);

        settings.Controls.Add(Caption("Tick Hz"), 2, 1);
        _tickHz = new NumericUpDown
        {
            Minimum = 20,
            Maximum = 2000,
            Increment = 5,
            Value = 220,
            Dock = DockStyle.Fill,
            Margin = new Padding(3, 2, 3, 2),
            TextAlign = HorizontalAlignment.Right,
        };
        // How often the ISR rewrites the registers. Sets duration and nothing else --
        // pitch comes from the divider and the clock.
        _tickHz.ValueChanged += (_, _) =>
        {
            if (_loading) return;
            lock (_session.Gate) _session.Envelope.TickHz = (double)_tickHz.Value;
        };
        settings.Controls.Add(_tickHz, 3, 1);

        layout.Controls.Add(settings, 0, 0);

        // --- Transport ----------------------------------------------------

        // Loop sits here rather than up in the settings block: it modifies playback, so it
        // belongs beside the transport, and moving it off a line of its own is a whole row
        // back for the grids.
        var transport = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 2, 0, 2),
        };
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        // Wide enough for the whole caption: the checkbox has AutoSize off so that it
        // fills its cell vertically, which also means a short column truncates the text
        // instead of wrapping it.
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 176f));

        // Through the session rather than straight to the envelope: starting this has
        // to take the chip back from the sample voice, and that handover lives in one
        // place so the two panels cannot disagree about who is playing.
        _play = new Button { Text = "Play", Dock = DockStyle.Fill };
        _play.Click += (_, _) => _session.PlayEnvelope();
        transport.Controls.Add(_play, 0, 0);

        _stop = new Button { Text = "Stop", Dock = DockStyle.Fill };
        _stop.Click += (_, _) =>
        {
            lock (_session.Gate) _session.Envelope.Stop(_session.Chip);
        };
        transport.Controls.Add(_stop, 1, 0);

        _loop = new CheckBox
        {
            Text = "Loop (hold the sound)",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(8, 0, 3, 0),
        };
        // Not something the arcade does; a sound you can hold is far easier to compare
        // against a reference than one you have to keep re-firing.
        _loop.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            lock (_session.Gate) _session.Envelope.Looping = _loop.Checked;
        };
        transport.Controls.Add(_loop, 2, 0);

        layout.Controls.Add(transport, 0, 1);

        // --- Chains -------------------------------------------------------

        layout.Controls.Add(ChainHeader("AUDF", audf: true), 0, 2);
        _audfGrid = ChainGrid();
        _audfGrid.CellEndEdit += (_, e) => CommitCell(_audfGrid, e.RowIndex, e.ColumnIndex, audf: true);
        layout.Controls.Add(_audfGrid, 0, 3);

        layout.Controls.Add(ChainHeader("AUDC", audf: false), 0, 4);
        _audcGrid = ChainGrid();
        _audcGrid.CellEndEdit += (_, e) => CommitCell(_audcGrid, e.RowIndex, e.ColumnIndex, audf: false);
        _audcGrid.SelectionChanged += (_, _) => UpdateNotes();
        layout.Controls.Add(_audcGrid, 0, 5);

        // --- The two fields that make up the selected AUDC byte ------------
        //
        // <b>AUDC's Value column is the one field on this panel nobody can read at a
        // glance.</b> AUDF's value is a divider and 16 means sixteen; AUDC's 162 means
        // distortion 5 and volume 2, which is only obvious once you already know where
        // the field boundary falls. Editing the fields and watching the byte assemble
        // itself in the grid demonstrates the arithmetic instead of asserting it -- the
        // same job the coloured cells do for the live chip in the register strip.

        var composer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0, 2, 0, 0),
        };
        // Wide enough for the captions themselves: these are Labels with AutoSize off, so
        // a column a few pixels short does not wrap or ellipsise, it silently cuts the
        // word off mid-letter.
        composer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72f));
        composer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        composer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56f));
        composer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54f));
        composer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        composer.Controls.Add(Caption("Distortion"), 0, 0);

        _audcDistortion = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(3, 1, 3, 1),
        };
        // Every code, including the duplicates, for the same reason the channel strips
        // list them all: eight codes is what the field holds, and a byte off a real
        // machine contains whichever one was written.
        for (int code = 0; code < 8; code++)
            _audcDistortion.Items.Add($"{code}  {PokeyChip.DistortionName(code)}");
        _audcDistortion.SelectedIndexChanged += (_, _) => ComposeAudc();
        composer.Controls.Add(_audcDistortion, 1, 0);

        composer.Controls.Add(Caption("Volume"), 2, 0);

        _audcVolume = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 15,
            Dock = DockStyle.Fill,
            Margin = new Padding(3, 1, 3, 1),
            TextAlign = HorizontalAlignment.Right,
        };
        _audcVolume.ValueChanged += (_, _) => ComposeAudc();
        composer.Controls.Add(_audcVolume, 3, 0);

        layout.Controls.Add(composer, 0, 6);

        // AutoSize off so the sentence wraps to two lines instead of running off the
        // right edge -- a Label only wraps when it is not sizing itself to its text.
        _notes = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(3, 4, 3, 0),
        };
        layout.Controls.Add(_notes, 0, 7);

        box.Controls.Add(layout);
        Controls.Add(box);

        LoadPreset();
    }

    // --- Construction helpers ---------------------------------------------

    private static Label Caption(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoSize = false,
    };

    private static Label Heading(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
        // Centred, not bottom-aligned: the heading now shares its row with that chain's
        // buttons rather than sitting on a line of its own above the grid.
        TextAlign = ContentAlignment.MiddleLeft,
        AutoSize = false,
        Margin = new Padding(3, 0, 3, 0),
    };

    /// <summary>
    /// A chain's heading and its Add/Remove buttons on one row.
    ///
    /// They used to be two rows, a 20px heading above the grid and a 28px button strip
    /// below it, which spent 48px per chain on two things that each need about half a
    /// line. Sharing a row gives 40px back across the two chains, and it goes into the
    /// grids -- where it is the difference between seeing one step and seeing four.
    /// </summary>
    private Control ChainHeader(string text, bool audf)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 2, 0, 0),
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        row.Controls.Add(Heading(text), 0, 0);
        row.Controls.Add(ChainButtons(audf), 1, 0);
        return row;
    }

    private static DataGridView ChainGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AllowUserToOrderColumns = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersWidth = 44,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
            BackgroundColor = SystemColors.Window,

            // Tighter than the default 22, which buys most of another visible step in the
            // same height. The grids are read far more often than they are typed into.
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 21,
        };
        grid.RowTemplate.Height = 20;

        // A step is (value, inner, delta, outer) -- the order the ISR fetches them in
        // at Lcbd1-6,y / -5 / -4 / -3, rather than an order chosen for the display.
        foreach (string name in new[] { "Value", "Inner", "Delta", "Outer" })
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = name,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight },
            });

        return grid;
    }

    private Control ChainButtons(bool audf)
    {
        // AutoSize so the AutoSize column it sits in takes exactly the buttons' width and
        // the heading keeps the rest.
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
        };

        var add = new Button { Text = "Add step", Width = 74, Height = 23, Margin = new Padding(2, 0, 0, 0) };
        add.Click += (_, _) => ResizeChain(audf, +1);
        row.Controls.Add(add);

        var remove = new Button { Text = "Remove step", Width = 88, Height = 23, Margin = new Padding(2, 0, 0, 0) };
        remove.Click += (_, _) => ResizeChain(audf, -1);
        row.Controls.Add(remove);

        // The copy button sits on the AUDC row because it is the lower of the two, and
        // it copies *both* chains -- putting it under the last thing you edited reads
        // better than attaching it to one chain as though it belonged to it.
        if (!audf)
        {
            _copy = new Button { Text = "Copy C#", Width = 72, Height = 23, Margin = new Padding(2, 0, 0, 0) };
            _copy.Click += (_, _) => CopyAsCSharp();
            row.Controls.Add(_copy);

            new ToolTip().SetToolTip(_copy,
                "Copies this whole voice to the clipboard as C# you can paste into a game:\n"
                + "both step chains, the tick rate and the base clock.\n"
                + "See docs/SOUND-RECIPE.md for what to paste it into.");
        }

        return row;
    }

    /// <summary>
    /// Puts the current voice on the clipboard as pasteable C#.
    ///
    /// <b>This exists because the alternative is transcribing sixteen integers by
    /// hand.</b> That step used to be the only place in the whole design-to-game loop
    /// where you could silently get it wrong: a transposed digit still plays, just not
    /// the sound you built, and a forgotten tick rate or base clock gives the right
    /// shape in the wrong key — which reads as a bad design decision rather than a typo.
    ///
    /// The emitted snippet carries everything that defines the voice, including the two
    /// values that live outside the grids and are the easiest to forget.
    /// </summary>
    private void CopyAsCSharp()
    {
        StepEnvelope.Step[] audf, audc;
        double tickHz, clockHz;

        lock (_session.Gate)
        {
            audf = (StepEnvelope.Step[])_session.Envelope.AudfChain.Clone();
            audc = (StepEnvelope.Step[])_session.Envelope.AudcChain.Clone();
            tickHz = _session.Envelope.TickHz;
            clockHz = _session.Chip.BaseClockHz;
        }

        string clockSymbol = $"{clockHz:0}";
        string clockLabel = $"{clockHz:0} Hz";
        foreach (var (label, hz, symbol) in ChipSession.Clocks)
        {
            if (Math.Abs(hz - clockHz) > 0.5) continue;
            clockSymbol = $"PokeyClocks.{symbol}";
            clockLabel = label;
            break;
        }

        int distortion = (audc[0].Value >> 5) & 7;

        var text = new StringBuilder();
        text.AppendLine($"// PokeyLab — \"{_preset.Text}\", {clockLabel}, {tickHz:0} Hz tick");
        text.AppendLine("// Needs: using Pokey;");
        text.AppendLine($"// AUDC {audc[0].Value} = distortion {distortion} "
                        + $"({PokeyChip.DistortionName(distortion)}), volume {audc[0].Value & 0x0f}");
        text.AppendLine("ChipSound.Build(");
        text.AppendLine($"    audf: {Chain(audf)},");
        text.AppendLine($"    audc: {Chain(audc)},");
        text.AppendLine($"    tickHz: {tickHz:0.##}, baseClockHz: {clockSymbol}, amplitude: 0.5f);");

        try
        {
            Clipboard.SetText(text.ToString());
            Flash("Copied");
        }
        catch (Exception)
        {
            // Another process can hold the clipboard open, and SetText throws when it
            // does. Nothing here is worth taking the app down for, and a button that
            // says so is more use than a stack trace.
            Flash("Clipboard busy");
        }
    }

    /// <summary>
    /// One chain as a C# array literal.
    ///
    /// Deltas carry an explicit sign, because the sign is the field people misread — and
    /// on AUDF it is the one that decides whether the pitch rises or falls.
    ///
    /// Long chains break across lines. The extra-life flourish is eight steps and runs
    /// past 180 characters on one line, which pastes fine and reads like a wall; the
    /// threshold is where a line stops fitting on a screen rather than anything
    /// principled.
    /// </summary>
    private static string Chain(StepEnvelope.Step[] chain)
    {
        const int Wrap = 3;
        bool multiline = chain.Length > Wrap;

        // Environment.NewLine, not "\n". The rest of the snippet is built with
        // AppendLine, which emits CRLF on Windows, and mixing the two gave a clipboard
        // payload with both -- which renders with the indentation visibly wrong and
        // pastes unpredictably depending on what is receiving it.
        string nl = Environment.NewLine;
        var parts = new StringBuilder(multiline ? $"new[]{nl}    {{{nl}" : "new[] { ");

        for (int i = 0; i < chain.Length; i++)
        {
            if (multiline) parts.Append("        ");
            else if (i > 0) parts.Append(", ");

            // Qualified, because Step is nested inside StepEnvelope and a bare "new Step"
            // does not resolve from `using Pokey;` alone. This emitted it unqualified for
            // as long as the button has existed, so every pasted voice needed a using
            // alias added by hand before it would build -- a small thing that undermines
            // the whole point of a copy button.
            parts.Append($"new StepEnvelope.Step({chain[i].Value}, {chain[i].Inner}, "
                         + $"{chain[i].Delta:+0;-0;0}, {chain[i].Outer})");

            if (multiline) parts.Append(i < chain.Length - 1 ? $",{nl}" : nl);
        }

        return parts.Append(multiline ? "    }" : " }").ToString();
    }

    /// <summary>Says something on the button for a moment, then puts it back. Cheaper
    /// than a dialog for something that either worked or did not.</summary>
    private void Flash(string message)
    {
        _copy.Text = message;

        var restore = new Timer { Interval = 1400 };
        restore.Tick += (_, _) =>
        {
            _copy.Text = "Copy C#";
            restore.Stop();
            restore.Dispose();
        };
        restore.Start();
    }

    // --- Chain editing ----------------------------------------------------

    /// <summary>
    /// The Sound list: the built-in voice, then every sound in every bank on disk.
    ///
    /// <b>The banks are read once, at construction.</b> Rereading them on each selection
    /// would make the list change under the mouse; a file edited while the lab is open is
    /// picked up by restarting it, which is the same deal PokeyLab's own presets offer and
    /// costs a second.
    ///
    /// <b>Nothing here throws.</b> <see cref="EnvelopeBank.Discover"/> returns an empty
    /// list for a missing folder and a bank carrying <see cref="EnvelopeBank.Problems"/>
    /// for a file it could not fully read, so a bad line costs one sound rather than the
    /// window. <paramref name="note"/> is where those problems go.
    /// </summary>
    private static List<Voice> CollectVoices(out string note)
    {
        var voices = new List<Voice>();

        // The built-in voice carries Tempest's clock for the same reason a bank voice
        // carries its own: it was designed against it, and once *anything* in this list
        // can change the clock, a voice that leaves it alone inherits whichever machine
        // the last selection chose, and plays a fifth out with nothing saying why.
        //
        // The tick rate is left alone deliberately -- it already runs at the 220 Hz this
        // app starts at, and the tick is a knob the guide asks you to turn.
        foreach (var (label, audf, audc) in Presets)
            voices.Add(new Voice(label, audf, audc, null, PokeyClocks.TempestHz));

        var problems = new List<string>();
        int banks = 0;

        foreach (var bank in EnvelopeBank.Discover(BankDirectory))
        {
            banks++;
            foreach (string problem in bank.Problems) problems.Add($"{bank.Name}: {problem}");

            foreach (var recipe in bank.Recipes)
            {
                // Qualified by bank, because two games' banks routinely name a sound the
                // same thing and the list is flat.
                voices.Add(new Voice($"{bank.Name}: {recipe.Name}",
                                     recipe.Audf, recipe.Audc, bank.TickHz, bank.ClockHz));
            }
        }

        var text = new StringBuilder();
        text.Append($"{Presets.Length} built-in voice, ");
        text.Append($"{voices.Count - Presets.Length} from {banks} bank file(s) in {BankDirectory}.");
        text.Append(Environment.NewLine);
        text.Append("Selecting a sound sets the base clock it was designed against; a bank sound sets Tick Hz too.");

        foreach (string problem in problems)
            text.Append(Environment.NewLine).Append("Skipped -- ").Append(problem);

        note = text.ToString();
        return voices;
    }

    /// <summary>
    /// Copies the selected voice in.
    ///
    /// <b>Copied, not referenced</b> -- the built-in voice is a static array shared with
    /// every other caller of the library, and editing it in place would quietly rewrite
    /// it for the rest of the session.
    ///
    /// <b>A bank voice brings its clock and tick rate with it</b>, and both are applied
    /// here. The chip is the authority on the clock and every panel reads it back, so
    /// setting it is enough -- the Base clock combo follows on the next sync rather than
    /// having to be told. What is deliberately <i>not</i> applied is the recipe's
    /// amplitude: this lab has no output volume, and the chip's own volume nibble is
    /// inside AUDC where it can be seen.
    /// </summary>
    private void LoadPreset()
    {
        var voice = _voices[Math.Clamp(_preset.SelectedIndex, 0, _voices.Count - 1)];

        lock (_session.Gate)
        {
            _session.Envelope.AudfChain = (StepEnvelope.Step[])voice.Audf.Clone();
            _session.Envelope.AudcChain = (StepEnvelope.Step[])voice.Audc.Clone();

            if (voice.TickHz is double tick) _session.Envelope.TickHz = tick;
            if (voice.ClockHz is double clock) _session.Chip.BaseClockHz = clock;
        }

        // The spinner is the tick rate's editor, so it has to be told; the flag stops it
        // writing the value it was just given back to the envelope.
        if (voice.TickHz is double shown)
        {
            bool wasLoading = _loading;
            _loading = true;
            _tickHz.Value = (decimal)Math.Clamp(shown, (double)_tickHz.Minimum, (double)_tickHz.Maximum);
            _loading = wasLoading;
        }

        RefreshChains();
    }

    /// <summary>
    /// Grows or shrinks a chain, keeping the existing steps.
    ///
    /// A step added at the end copies the last one rather than arriving as zeroes,
    /// which would be a silent terminator and make the sound shorter instead of longer.
    ///
    /// Named for what it returns rather than the obvious <c>Resize</c>: this is a
    /// <see cref="Control"/>, and that name would hide the inherited event.
    /// </summary>
    private static StepEnvelope.Step[] WithLength(StepEnvelope.Step[] chain, int count)
    {
        count = Math.Clamp(count, 1, 16);
        var next = new StepEnvelope.Step[count];
        for (int i = 0; i < count; i++)
            next[i] = i < chain.Length ? chain[i] : chain[chain.Length - 1];
        return next;
    }

    private void ResizeChain(bool audf, int by)
    {
        lock (_session.Gate)
        {
            var envelope = _session.Envelope;
            if (audf) envelope.AudfChain = WithLength(envelope.AudfChain, envelope.AudfChain.Length + by);
            else envelope.AudcChain = WithLength(envelope.AudcChain, envelope.AudcChain.Length + by);
        }
        RefreshChains();
    }

    /// <summary>Reads one edited cell back into the chain, clamped, and re-displays
    /// whatever actually landed there -- so a rejected or clamped entry is visible
    /// rather than silently different from what was typed.</summary>
    private void CommitCell(DataGridView grid, int row, int column, bool audf)
    {
        if (_loading || row < 0 || column < 0) return;

        var (low, high) = Limits[column];
        string text = grid.Rows[row].Cells[column].Value?.ToString();

        lock (_session.Gate)
        {
            var chain = audf ? _session.Envelope.AudfChain : _session.Envelope.AudcChain;
            if (row >= chain.Length) return;

            var step = chain[row];
            // An unparseable entry keeps the existing value rather than becoming zero,
            // which for inner or outer would be an invalid step.
            int parsed = int.TryParse(text, out int typed)
                ? Math.Clamp(typed, low, high)
                : column switch { 0 => step.Value, 1 => step.Inner, 2 => step.Delta, _ => step.Outer };

            chain[row] = column switch
            {
                0 => step with { Value = parsed },
                1 => step with { Inner = parsed },
                2 => step with { Delta = parsed },
                _ => step with { Outer = parsed },
            };
        }

        RefreshChains();
        UpdateNotes();
    }

    /// <summary>Rewrites both grids from the envelope. Cheap -- at most sixteen rows a
    /// chain -- so it runs after any structural change rather than trying to patch
    /// individual cells.</summary>
    private void RefreshChains()
    {
        _loading = true;
        lock (_session.Gate)
        {
            Fill(_audfGrid, _session.Envelope.AudfChain, audc: false);
            Fill(_audcGrid, _session.Envelope.AudcChain, audc: true);
        }
        _loading = false;
        UpdateNotes();
    }

    /// <summary>
    /// Rewrites one grid.
    ///
    /// <b>The cells keep the table's own numbers.</b> An AUDC delta of -8 is displayed as
    /// -8 even though it lands as +8, because that is the byte in the table and it is
    /// what "Copy as C#" has to emit. The interpretation goes in the tooltip and the note
    /// underneath, where it cannot be mistaken for the data.
    /// </summary>
    private static void Fill(DataGridView grid, StepEnvelope.Step[] chain, bool audc)
    {
        while (grid.Rows.Count > chain.Length) grid.Rows.RemoveAt(grid.Rows.Count - 1);
        while (grid.Rows.Count < chain.Length) grid.Rows.Add();

        for (int i = 0; i < chain.Length; i++)
        {
            var row = grid.Rows[i];
            row.HeaderCell.Value = $"{i + 1}";
            row.Cells[0].Value = chain[i].Value;
            row.Cells[1].Value = chain[i].Inner;
            row.Cells[2].Value = chain[i].Delta;
            row.Cells[3].Value = chain[i].Outer;

            if (!audc) continue;

            var delta = row.Cells[2];
            delta.ToolTipText = DeltaNote(chain[i]);

            // A delta that is a whole multiple of sixteen is the one case that reads as a
            // broken control: you type a large number, press enter, and nothing about the
            // sound changes. Colouring it says the field took the edit and the edit has
            // no effect, which is different from the edit being rejected.
            //
            // Both colours are needed, and that is not belt and braces: the grid is
            // FullRowSelect, so the row holding the cell you just edited is almost always
            // the selected one, and a selected cell draws from SelectionForeColor and
            // ignores ForeColor entirely. Setting only the latter marks the case in the
            // one state you are least likely to be looking at it in. Firebrick reads as a
            // warning on the window background and turns to mud on the selection blue,
            // hence the second colour rather than the same one twice.
            bool noEffect = StepEnvelope.VolumeDelta(chain[i].Delta).Effective == 0;
            delta.Style.ForeColor = noEffect ? Color.Firebrick : SystemColors.ControlText;
            delta.Style.SelectionForeColor = noEffect ? Color.Gold : SystemColors.HighlightText;
        }
    }

    /// <summary>
    /// One sentence on what a step's delta does to the volume nibble, with the first few
    /// levels it actually visits.
    ///
    /// The levels come from <see cref="StepEnvelope.ValueAt"/> rather than being worked
    /// out here, so the masking and the <c>outer - 1</c> rule are applied by the same
    /// code that feeds the chip. A step whose <c>Outer</c> ends before the ring closes
    /// therefore shows what really happens rather than the cycle in the abstract.
    /// </summary>
    private static string DeltaNote(StepEnvelope.Step step)
    {
        var (effective, cycle) = StepEnvelope.VolumeDelta(step.Delta);

        // The equivalent with the smallest magnitude, which is what a reader recognises:
        // 8 rather than 8, but -7 rather than 9.
        int signed = effective <= 8 ? effective : effective - 16;
        string lands = signed == step.Delta
            ? $"Delta {step.Delta:+0;-0;0}"
            : $"Delta {step.Delta:+0;-0;0} lands as {signed:+0;-0;0} (the nibble wraps at 16)";

        // A delta of literally zero is not a surprise and does not need the mod-16
        // explanation; a delta of 16 or -32, which looks like it should do something, is
        // exactly what that sentence is for.
        if (effective == 0)
            return step.Delta == 0
                ? $"{lands}: the volume holds for the whole step."
                : $"{lands}: the volume never moves — every whole multiple of 16 is a no-op.";

        var levels = new List<string>();
        var one = new[] { step };
        int shown = Math.Min(cycle, 4);
        for (int i = 0; i <= shown; i++)
            levels.Add((StepEnvelope.ValueAt(one, i * Math.Max(1, step.Inner), true) & 0x0f).ToString());

        string tail = cycle > shown ? " …" : "";
        return $"{lands}: volume walks {string.Join(" → ", levels)}{tail}, "
               + $"repeating every {cycle} tick{(cycle == 1 ? "" : "s")}.";
    }

    /// <summary>
    /// What the selected AUDC byte actually means.
    ///
    /// The nibbles do two unrelated jobs and the delta only touches one of them: the
    /// ISR masks it so the distortion nibble survives, which is what keeps a sound's
    /// timbre fixed for its whole life.
    /// </summary>
    /// <summary>
    /// Writes the two fields back into the selected step's byte.
    ///
    /// <b>Bit 4 is carried through rather than exposed.</b> Volume-only turns the channel
    /// into a four-bit DAC, which is the sample voice's business and not an envelope's --
    /// but a step that arrives with it set keeps it through any edit made here, and the
    /// note underneath says so out loud rather than letting it be a silent passenger.
    /// </summary>
    private void ComposeAudc()
    {
        if (_loading) return;

        int index = _audcGrid.CurrentRow?.Index ?? 0;

        lock (_session.Gate)
        {
            var chain = _session.Envelope.AudcChain;
            if (chain.Length == 0) return;

            index = Math.Clamp(index, 0, chain.Length - 1);
            var step = chain[index];

            chain[index] = step with
            {
                Value = ((_audcDistortion.SelectedIndex & 7) << 5)
                        | (step.Value & PokeyChip.AudcVolumeOnly)
                        | ((int)_audcVolume.Value & PokeyChip.AudcVolumeMask),
            };
        }

        RefreshChains();
    }

    private void UpdateNotes()
    {
        int index = _audcGrid.CurrentRow?.Index ?? 0;
        StepEnvelope.Step step;

        lock (_session.Gate)
        {
            var chain = _session.Envelope.AudcChain;
            if (chain.Length == 0) return;
            step = chain[Math.Clamp(index, 0, chain.Length - 1)];
        }

        int distortion = (step.Value >> 5) & 7;
        int volume = step.Value & PokeyChip.AudcVolumeMask;
        bool volumeOnly = (step.Value & PokeyChip.AudcVolumeOnly) != 0;

        // Saved and restored rather than simply cleared: this runs from inside
        // RefreshChains as well as on its own, and clearing the flag here would let the
        // rest of that refresh write to the chip.
        bool wasLoading = _loading;
        _loading = true;
        _audcDistortion.SelectedIndex = distortion;
        _audcVolume.Value = volume;
        _loading = wasLoading;

        // The sum written out, because the whole point of the two controls above is that
        // the byte in the grid is those two fields added together.
        string sum = volumeOnly
            ? $"{distortion} × 32 + 16 volume-only + {volume}"
            : $"{distortion} × 32 + {volume}";

        _notes.Text = $"AUDC {step.Value} = {sum} = ${step.Value:X2}, "
                      + $"distortion {distortion} {PokeyChip.DistortionName(distortion)}."
                      + $"  {DeltaNote(step)}";
    }

    /// <summary>Caller holds <see cref="ChipSession.Gate"/>.</summary>
    public void SyncFromChip()
    {
        bool playing = _session.Envelope.Playing;
        string wanted = playing ? "Retrigger" : "Play";
        if (_play.Text != wanted) _play.Text = wanted;
        _stop.Enabled = playing;
    }
}
