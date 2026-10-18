using System;
using System.Drawing;
using System.Windows.Forms;
using Pokey;
using PokeyLabForms.Panels;
using PokeyLabForms.Views;

namespace PokeyLabForms;

/// <summary>
/// The lab: one live POKEY, every register, and a continuous stream of whatever the
/// chip is currently doing -- all on one window.
///
/// <b>Why one window rather than PokeyLab's three pages.</b> There, registers,
/// envelopes and poly counters are separate screens, which is the right shape for a
/// keyboard-driven tool where only one thing can hold the caret. But the three are not
/// independent: an envelope writes registers, and registers consult counters. Turning a
/// divider ought to move a pitch, move the sampling ticks under a poly train, and
/// change the shape on the scope, and being able to watch all three happen at once is
/// the thing this front end is actually for. Paging between them shows the same facts
/// and hides the relationship.
///
/// The cost is a large minimum window, which is why this is a second front end and not
/// a replacement.
/// </summary>
public sealed class MainForm : Form
{
    /// <summary>
    /// How often the display is rebuilt from the chip.
    ///
    /// The chip does not run on this clock -- it is ticked inside the audio render, on
    /// the feeder thread -- so this is purely how often the window catches up. About
    /// thirty a second: fast enough that a slider feels connected to the pitch beside
    /// it, slow enough that a form full of controls is not being rewritten constantly.
    /// </summary>
    private const int RefreshMs = 33;

    /// <summary>
    /// Points sampled along the sweep for the graph.
    ///
    /// Comfortably more than the pixels available, and bounded -- see
    /// <see cref="SweepGraphView.Show"/> for why a point per envelope tick is not an
    /// option once a chain can span a million of them.
    /// </summary>
    private const int GraphSamples = 400;

    private readonly ChipSession _session = new();

    private readonly ChannelStrip[] _strips = new ChannelStrip[PokeyChip.Channels];
    private readonly AudctlPanel _audctl;
    private readonly EnvelopePanel _envelope;
    private readonly MeasuredPanel _measured;
    private readonly VoicePanel _voice;
    private readonly PolyPanel _poly;
    private readonly ScopeView _scope;
    private readonly SweepGraphView _sweep;
    private readonly CheckBox _showMeasured;
    private readonly RegisterBitsView _bits;

    private readonly Timer _refresh;
    private readonly StatusStrip _status;
    private readonly ToolStripStatusLabel _audioState;

    private readonly float[] _trace = new float[ScopeView.Window];
    private readonly double[] _pitch = new double[GraphSamples];
    private readonly ChannelRegisters[] _registers = new ChannelRegisters[PokeyChip.Channels];
    private int _control;

    public MainForm()
    {
        // Captions name things; they do not explain them. An appended clause on a group
        // box reads as a tutorial rather than a product, and the explanation belongs in
        // the panel's own content or its tooltip where someone can go looking for it.
        Text = "PokeyLab — POKEY C012294";
        StartPosition = FormStartPosition.CenterScreen;
        // Taller than it was before the register bits took a fixed strip along the
        // bottom: that strip is absolute, so every pixel of it comes out of the two
        // proportional rows above, and the poly trains were already the tightest thing
        // in the window.
        ClientSize = new Size(1540, 940);
        // Wider than it was before the sample voice took 316px of the bottom row: below
        // this the poly trains lose enough width that their 64 cells stop being
        // countable, which is the one thing that display is for.
        // The height is set by the AUDCTL column, which is the one stack of fixed-height
        // content in the window: eight switch rows and the byte they add up to, above a
        // clock box and a button. Below this the group cannot show all of it, and a
        // minimum that does not fit its own content is not a minimum.
        MinimumSize = new Size(1260, 860);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120f));   // the nine bytes
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 63f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 37f));

        // --- Top: the chip, its control byte, and the envelope driving it --

        var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
        // <b>Four equal columns, in percent rather than pixels.</b> They were 352 / 254 /
        // 452 / the rest, which is how they grew: each was set to whatever its contents
        // needed at the time, so the AUDCTL column ended up half the width of the envelope
        // beside it and the form read as four unrelated panels rather than one instrument.
        //
        // Percent also means they stay even when the window is resized, where absolute
        // widths put every extra pixel into the last column. Each column is checked to
        // work at the minimum window width: the envelope is the tightest -- its step grid
        // and its heading-plus-buttons row set the real floor -- and it fits.
        for (int i = 0; i < 4; i++) top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));

        // Margin zeroed here, and on `right` below, so all four column headings line up.
        //
        // The envelope is a Panel whose only child is a docked GroupBox, and a docked
        // child of a plain Panel ignores its own Margin -- so that group sits flush at the
        // cell edge. Every other column nests one level deeper (a TableLayoutPanel or a
        // Panel holding a GroupBox that *does* get a margin), which put their captions 3px
        // lower than the envelope's. Taking the 3px off the outer container lets the inner
        // control's own margin be the only inset, which is what the envelope already had.
        var channels = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = new Padding(0),
        };
        channels.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        for (int i = 0; i < PokeyChip.Channels; i++)
        {
            channels.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));
            _strips[i] = new ChannelStrip(_session, i) { Dock = DockStyle.Fill };
            channels.Controls.Add(_strips[i], 0, i);
        }
        top.Controls.Add(channels, 0, 0);

        _audctl = new AudctlPanel(_session);
        top.Controls.Add(_audctl, 1, 0);

        _envelope = new EnvelopePanel(_session);
        top.Controls.Add(_envelope, 2, 0);

        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
        };
        right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 156f));

        var sweepBox = new GroupBox
        {
            Text = "The sweep",
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 4, 6, 6),
        };

        var sweepLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        sweepLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        sweepLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        sweepLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));

        _sweep = new SweepGraphView { Dock = DockStyle.Fill, Margin = new Padding(0) };
        sweepLayout.Controls.Add(_sweep, 0, 0);

        // The target box is the whole point of the graph when a voice is being checked
        // against the captures, and clutter when one is being designed from scratch --
        // so it is a switch rather than a permanent fixture. Turning it off also releases
        // the axes; see SweepGraphView.ShowMeasured.
        _showMeasured = new CheckBox
        {
            Text = "Show the measured target box",
            Checked = true,
            Dock = DockStyle.Fill,
            AutoSize = false,
            Margin = new Padding(2, 2, 2, 0),
        };
        _showMeasured.CheckedChanged += (_, _) => _sweep.ShowMeasured = _showMeasured.Checked;
        sweepLayout.Controls.Add(_showMeasured, 0, 1);

        new ToolTip().SetToolTip(_showMeasured,
            "The green box marks where reference recordings of a real cabinet say the\n"
            + "player's shot should start, end and finish.\n"
            + "With it shown, the axes are held wide enough to keep it on screen.\n"
            + "With it hidden, they fit whatever sweep is actually loaded.");

        sweepBox.Controls.Add(sweepLayout);
        right.Controls.Add(sweepBox, 0, 0);

        _measured = new MeasuredPanel(_session);
        right.Controls.Add(_measured, 0, 1);

        top.Controls.Add(right, 3, 0);
        root.Controls.Add(top, 0, 1);

        // --- Bottom: what actually came out, and the mechanism behind it ---

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        // One column wide and three wide, matching the row above, so the boundary between
        // the scope and the explainers falls on the same vertical as the one between the
        // channel strips and AUDCTL. The two rows then read as one grid rather than two,
        // and they stay aligned at any window width because both are in percent.
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));   // scope
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75f));   // explainer tabs

        var scopeBox = new GroupBox
        {
            Text = "Output",
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 4, 6, 6),
        };
        var scopeLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        scopeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        scopeLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        scopeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));

        _scope = new ScopeView
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            SampleRate = ChipSession.SampleRate,
        };
        scopeLayout.Controls.Add(_scope, 0, 0);
        scopeLayout.Controls.Add(ScopeControls(), 0, 1);

        scopeBox.Controls.Add(scopeLayout);
        bottom.Controls.Add(scopeBox, 0, 0);

        // Tabs for the two explainers, and only for them.
        //
        // The single-window premise still holds where it earns its keep: every register,
        // the envelope, the sweep and the scope are visible at all times, so turning a
        // divider and watching the pitch, the trace and the verdict all move costs
        // nothing. What swaps is the bottom-right explainer -- the poly trains and the
        // voice are two different subjects that are never consulted at the same instant,
        // and the voice had genuinely outgrown the 316px column it used to share with
        // them. Tabbing those two costs nothing an A/B needs, because a tone left
        // running on another channel keeps sounding whichever tab is open.
        var explainers = new TabControl { Dock = DockStyle.Fill, Margin = new Padding(3, 3, 3, 3) };

        // Poly counters first, and therefore open by default. It is the explainer for the
        // mechanism the rest of the window keeps referring to -- the distortion field on
        // every channel strip selects one of these counters -- so it is the one that
        // should be showing when someone starts turning knobs. The voice is a separate
        // subject you go looking for.
        var polyTab = new TabPage("Poly counters") { UseVisualStyleBackColor = true, Padding = new Padding(3) };
        _poly = new PolyPanel(_session);
        polyTab.Controls.Add(_poly);
        explainers.TabPages.Add(polyTab);

        var voiceTab = new TabPage("Voice") { UseVisualStyleBackColor = true, Padding = new Padding(3) };
        _voice = new VoicePanel(_session);
        voiceTab.Controls.Add(_voice);
        explainers.TabPages.Add(voiceTab);

        bottom.Controls.Add(explainers, 1, 0);

        root.Controls.Add(bottom, 0, 2);

        // --- The registers themselves, along the bottom --------------------
        //
        // Full width and always visible, rather than a third tab beside the two
        // explainers. The whole value of this display is watching a byte change while
        // your hand is on the control that changed it, and a tab you have to go and open
        // cannot do that -- by the time you switch to it, the thing you wanted to see
        // has already happened. It costs 142px of a window that was already large,
        // which is the trade.

        var bitsBox = new GroupBox
        {
            Text = "The nine bytes",
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 4, 6, 6),
        };
        _bits = new RegisterBitsView { Dock = DockStyle.Fill };
        bitsBox.Controls.Add(_bits);
        root.Controls.Add(bitsBox, 0, 0);

        // --- Status --------------------------------------------------------

        _status = new StatusStrip();
        _audioState = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _status.Items.Add(_audioState);

        // The Fill control goes in first: docking is resolved from the end of the
        // collection backwards, so whatever is added first is laid out last and gets
        // the space nothing else claimed.
        Controls.Add(root);
        Controls.Add(_status);

        _session.Start();

        _refresh = new Timer { Interval = RefreshMs };
        _refresh.Tick += (_, _) => Sync();
        _refresh.Start();

        Sync();
    }

    /// <summary>
    /// Rebuilds the whole display from the chip, on the UI thread.
    ///
    /// <b>One gate, taken once.</b> Every panel's <c>SyncFromChip</c> documents that
    /// its caller holds it, which is what lets them read the chip freely without each
    /// taking and dropping the lock a dozen times -- and, more usefully, means the
    /// window always shows one consistent instant of the chip rather than a channel
    /// read before an envelope tick and a poly train read after it.
    ///
    /// The two displays fed from arrays are filled here too, so nothing under
    /// <c>Views/</c> ever touches the chip or needs to know a lock exists.
    /// </summary>
    private void Sync()
    {
        double totalSeconds, playSeconds;
        bool playing;

        lock (_session.Gate)
        {
            foreach (var strip in _strips) strip.SyncFromChip();
            _audctl.SyncFromChip();
            _envelope.SyncFromChip();
            _measured.SyncFromChip();
            _voice.SyncFromChip();
            _poly.SyncFromChip();

            // Snapshotted inside the gate along with everything else, so the bytes on
            // screen are one instant of the chip rather than four channels read either
            // side of an envelope tick -- which is exactly the display where that would
            // show, since a tick rewrites two of these registers at once.
            var chip = _session.Chip;
            for (int i = 0; i < PokeyChip.Channels; i++)
                _registers[i] = new ChannelRegisters(
                    chip.Audf(i), chip.Audc(i),
                    chip.IsLowHalf(i), chip.IsHighHalf(i), chip.VolumeOnly(i), chip.IsFast(i));
            _control = chip.Control;

            var envelope = _session.Envelope;
            int ticks = Math.Max(1, envelope.TotalTicks);
            totalSeconds = envelope.Seconds;
            playing = envelope.Playing;
            playSeconds = Math.Min(envelope.Tick, ticks) / Math.Max(1.0, envelope.TickHz);

            // Sampled from the envelope's own arithmetic -- the same AudfAt that writes
            // the registers -- so the staircase on screen is the one being played
            // rather than an idealisation of it.
            for (int i = 0; i < GraphSamples; i++)
            {
                int tick = (int)Math.Round(i / (double)(GraphSamples - 1) * ticks);
                _pitch[i] = envelope.PitchAt(_session.Chip, tick);
            }
        }

        _sweep.Show(_pitch, totalSeconds, playSeconds, playing);
        _bits.Show(_registers, _control);

        _session.Scope.Fill(_trace);
        _scope.Show(_trace, _session.Scope.Ready(ScopeView.Window));

        UpdateStatus();
    }

    /// <summary>
    /// The scope's two scales, laid out like the instrument's own front panel.
    ///
    /// Both are pure view settings: the capture is unchanged, so neither touches the chip,
    /// the gate or <c>ScopeTrace</c>. See <see cref="ScopeView.Gain"/> for why the vertical
    /// scale is a fixed set of steps rather than an auto-fit.
    /// </summary>
    private Control ScopeControls()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            Margin = new Padding(2, 2, 2, 0),
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38f));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62f));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46f));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76f));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        Label Caption(string text) => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false,
            Margin = new Padding(3, 0, 0, 0),
        };

        row.Controls.Add(Caption("Gain"), 0, 0);

        var gain = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(3, 1, 3, 1),
        };
        float[] gains = { 1f, 2f, 4f, 8f };
        foreach (float step in gains) gain.Items.Add($"×{step:0}");
        gain.SelectedIndex = Array.IndexOf(gains, 4f);
        gain.SelectedIndexChanged += (_, _) => _scope.Gain = gains[gain.SelectedIndex];
        row.Controls.Add(gain, 1, 0);

        row.Controls.Add(Caption("Span"), 2, 0);

        var span = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(3, 1, 3, 1),
        };
        // Fractions of the captured window rather than round times, because the capture
        // length is what actually bounds this.
        int[] spans = { ScopeView.Window, ScopeView.Window / 2, ScopeView.Window / 4, ScopeView.Window / 8 };
        foreach (int samples in spans)
            span.Items.Add($"{samples * 1000.0 / ChipSession.SampleRate:0.0} ms");
        span.SelectedIndex = 0;
        span.SelectedIndexChanged += (_, _) => _scope.Span = spans[span.SelectedIndex];
        row.Controls.Add(span, 3, 0);

        new ToolTip().SetToolTip(gain,
            "Vertical scale. One channel can only reach a quarter of full scale, because\n"
            + "the mix divides by four channels at volume 15 — so ×4 fills the frame with a\n"
            + "single voice, and ×1 is the true full-scale picture for all four.");

        return row;
    }

    private void UpdateStatus()
    {
        if (_session.AudioRunning)
        {
            _audioState.Text = $"Audio running — {ChipSession.SampleRate} Hz mono, waveOut.";
            _audioState.ForeColor = SystemColors.ControlText;
            return;
        }

        // The consequence is kept and the reassurance dropped. Someone looking at a flat
        // scope needs to know the chip is not advancing -- that is a fault report. Being
        // told the registers still work is a lecture, and the panels themselves show it.
        string why = string.IsNullOrEmpty(_session.AudioFault) ? "the device stopped" : _session.AudioFault;
        _audioState.Text = $"No audio ({why}). The chip is not advancing.";
        _audioState.ForeColor = Color.Firebrick;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _refresh.Stop();

        // Before anything else: it stops the feeder thread, and the feeder thread is
        // the one still calling into the chip.
        _session.Dispose();

        base.OnFormClosed(e);
    }
}
