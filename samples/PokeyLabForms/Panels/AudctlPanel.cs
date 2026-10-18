using System;
using System.Drawing;
using System.Windows.Forms;
using Pokey;

namespace PokeyLabForms.Panels;

/// <summary>
/// AUDCTL, the base clock, and mute.
///
/// The eight AUDCTL bits are the settings that make one channel's registers mean
/// something different from another's -- joining a pair into a 16-bit divider, taking
/// a channel off the slow clock, shortening the long poly -- so they sit beside the
/// channel strips rather than under a menu. Each is one checkbox and one bit, in bit
/// order, with the raw byte shown beneath: a lab should let you read the register you
/// are assembling.
/// </summary>
public sealed class AudctlPanel : Panel
{
    /// <summary>Bit order, low to high. The order they appear in the byte, not the
    /// order that would group them by topic -- the byte is the thing being taught.</summary>
    private static readonly string[] BitNames =
    {
        "15 kHz clock (base / 114)",
        // Bit 1 is the channel-1 filter and bit 2 the channel-2 one, not the other way
        // round. The panel lists them in bit order, so it had them swapped too for as
        // long as PokeyChip did.
        "Filter ch1 by ch3",
        "Filter ch2 by ch4",
        "Join ch3 and ch4 (16 bit)",
        "Join ch1 and ch2 (16 bit)",
        "Ch3 on base clock",
        "Ch1 on base clock",
        "9 bit poly (shorten the 17)",
    };

    private readonly ChipSession _session;
    private readonly CheckBox[] _bits = new CheckBox[8];
    private readonly Label _raw;
    private readonly ComboBox _clock;
    private readonly Label _slow;
    private readonly Label _verySlow;
    private readonly Button _mute;

    private bool _syncing;

    public AudctlPanel(ChipSession session)
    {
        _session = session;
        Dock = DockStyle.Fill;

        // Every single-column panel here states its column style. Left to the default
        // a column is AutoSize, which sizes to the widest child and stretches the rest
        // to match -- see the note in EnvelopePanel for what that did there.
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));

        // --- AUDCTL -------------------------------------------------------

        // No top margin, so this caption lines up with the envelope's, the channel strips'
        // and the sweep's -- see the note in MainForm on why the columns disagreed.
        var ctlBox = new GroupBox
        {
            Text = "AUDCTL",
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 4, 8, 6),
            Margin = new Padding(0, 0, 0, 3),
        };
        // Eight switches, then the slack, then the byte they add up to.
        //
        // <b>The spacer goes between them, not after.</b> With the slack last, the raw
        // value sat immediately under the checkboxes and left a tall empty region below it
        // -- a group box with a hole in it, which reads as a layout that went wrong rather
        // than one that was chosen. Anchoring the value to the bottom edge turns the same
        // pixels into breathing room, and puts the summary where a summary belongs.
        var ctlGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 10 };
        ctlGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        // 22 rather than 24: eight fixed rows plus the value row is the tallest fixed
        // block in this column, and at 24 it did not fit the box at the window's minimum
        // height -- the last checkbox overran the group and the value drew on top of it.
        for (int i = 0; i < 8; i++) ctlGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        ctlGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        ctlGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));

        for (int bit = 0; bit < 8; bit++)
        {
            int captured = bit;
            _bits[bit] = new CheckBox
            {
                Text = BitNames[bit],
                Dock = DockStyle.Fill,
                AutoSize = false,
                Margin = new Padding(3, 0, 3, 0),
            };
            _bits[bit].CheckedChanged += (_, _) =>
            {
                if (_syncing) return;
                lock (_session.Gate)
                {
                    int mask = 1 << captured;
                    int control = _session.Chip.Control;
                    _session.Chip.SetControl(_bits[captured].Checked ? control | mask
                                                                     : control & ~mask);
                }
            };
            ctlGrid.Controls.Add(_bits[bit], 0, bit);
        }

        _raw = new Label { Dock = DockStyle.Fill, AutoSize = false, ForeColor = SystemColors.GrayText };
        ctlGrid.Controls.Add(_raw, 0, 9);
        ctlBox.Controls.Add(ctlGrid);
        layout.Controls.Add(ctlBox, 0, 0);

        // --- Base clock ---------------------------------------------------

        var clockBox = new GroupBox
        {
            Text = "Base clock",
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 4, 8, 8),
        };
        var clockGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        // AutoSize is wanted here -- the box should be as tall as its three lines of
        // note and no taller -- so the column is Absolute rather than Percent: a
        // Percent column has no preferred width for AutoSize to ask about.
        clockGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 228f));

        _clock = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var (label, _, _) in ChipSession.Clocks) _clock.Items.Add(label);
        _clock.SelectedIndex = 0;
        _clock.SelectedIndexChanged += (_, _) =>
        {
            if (_syncing) return;
            lock (_session.Gate)
                _session.Chip.BaseClockHz = ChipSession.Clocks[_clock.SelectedIndex].Hz;
        };
        clockGrid.Controls.Add(_clock, 0, 0);

        // The two derived clocks, because every pitch on the left depends on them and
        // neither is the round number its name suggests.
        _slow = Note();
        _verySlow = Note();
        clockGrid.Controls.Add(_slow, 0, 1);
        clockGrid.Controls.Add(_verySlow, 0, 2);

        clockGrid.Controls.Add(new Label
        {
            Text = "The \"64 kHz\" clock is the base over 28, so it is never 64000 "
                   + "and it moves with the machine.",
            Dock = DockStyle.Fill,
            AutoSize = true,
            MaximumSize = new Size(230, 0),
            ForeColor = SystemColors.GrayText,
        }, 0, 3);

        clockBox.Controls.Add(clockGrid);
        layout.Controls.Add(clockBox, 0, 1);

        // --- Mute ---------------------------------------------------------

        _mute = new Button { Text = "Mute all", Dock = DockStyle.Fill, Margin = new Padding(3, 6, 3, 3) };
        _mute.Click += (_, _) => _session.ToggleMute();
        layout.Controls.Add(_mute, 0, 2);

        Controls.Add(layout);
    }

    private static Label Note() => new()
    {
        Dock = DockStyle.Fill,
        AutoSize = true,
        ForeColor = SystemColors.GrayText,
    };

    /// <summary>Caller holds <see cref="ChipSession.Gate"/>.</summary>
    public void SyncFromChip()
    {
        var chip = _session.Chip;
        _syncing = true;

        int control = chip.Control;
        for (int bit = 0; bit < 8; bit++)
        {
            bool on = (control & (1 << bit)) != 0;
            if (!_bits[bit].Focused && _bits[bit].Checked != on) _bits[bit].Checked = on;
        }

        _raw.Text = $"AUDCTL  0x{control:X2}   ({control})";

        _slow.Text = $"Slow (÷28):  {PokeyClocks.Slow(chip.BaseClockHz):0} Hz";
        _verySlow.Text = $"Very slow (÷114):  {PokeyClocks.VerySlow(chip.BaseClockHz):0} Hz";

        // The combo edits the clock, but it is no longer the only thing that sets it:
        // loading a sound out of a .sound bank sets the clock that bank was designed
        // against, and a combo left showing the old machine while the chip runs at a new
        // one is a readout that lies. The chip is the authority; this follows it.
        if (!_clock.Focused)
        {
            for (int i = 0; i < ChipSession.Clocks.Length; i++)
            {
                if (Math.Abs(ChipSession.Clocks[i].Hz - chip.BaseClockHz) > 0.5) continue;
                if (_clock.SelectedIndex != i) _clock.SelectedIndex = i;
                break;
            }
        }

        // The mute state has to be visible. A silent chip and a muted chip look
        // identical on the channel strips -- both read volume 0 -- so without this the
        // only way to tell why nothing is coming out is to press the button and find
        // out.
        string wanted = _session.Muted ? "Muted — click to restore" : "Mute all";
        if (_mute.Text != wanted) _mute.Text = wanted;
        _mute.ForeColor = _session.Muted ? Color.Firebrick : SystemColors.ControlText;

        _syncing = false;
    }
}
