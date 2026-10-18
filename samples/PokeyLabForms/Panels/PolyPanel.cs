using System;
using System.Drawing;
using System.Windows.Forms;
using Pokey;
using PokeyLabForms.Views;

namespace PokeyLabForms.Panels;

/// <summary>
/// The poly counters, and which of them the selected channel is consulting.
///
/// <b>This panel reads the live chip rather than holding settings of its own.</b>
/// PokeyLab's poly page used to carry a private distortion and AUDF, which looked
/// exactly like every other page in that tool while quietly describing a channel that
/// did not exist -- set channel 1 to distortion 6 elsewhere, come here, and the numbers
/// disagreed with what you were hearing. The only thing owned here is which channel it
/// is pointed at.
///
/// That failure is much less likely on a single window, where the channel strips are
/// visible a few inches away, but the fix is kept: two representations of one register
/// is a bug whether or not you can see both at once.
/// </summary>
public sealed class PolyPanel : Panel
{
    private readonly ChipSession _session;

    private readonly ComboBox _channel;
    private readonly Label _distortion;
    private readonly Label _audf;
    private readonly Label _gating;
    private readonly PolyTrainsView _trains;

    public PolyPanel(ChipSession session)
    {
        _session = session;
        Dock = DockStyle.Fill;

        var box = new GroupBox
        {
            Text = "Polynomial counters",
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 4, 8, 6),
        };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // --- Left: what is being explained --------------------------------

        var side = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        side.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        side.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _channel = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        for (int i = 0; i < PokeyChip.Channels; i++) _channel.Items.Add($"Channel {i + 1}");
        _channel.SelectedIndex = 0;
        // Updated straight away rather than left to the next refresh tick: changing the
        // channel is the one control here whose whole effect is on this panel, and a
        // frame of staleness reads as the combo not having worked.
        _channel.SelectedIndexChanged += (_, _) => { lock (_session.Gate) SyncFromChip(); };
        side.Controls.Add(_channel, 0, 0);

        // Read-only, and shown as such: these are the channel's real registers, set on
        // its own strip or by an envelope, not knobs belonging to this panel.
        _distortion = Reading();
        _audf = Reading();
        side.Controls.Add(_distortion, 0, 1);
        side.Controls.Add(_audf, 0, 2);

        _gating = new Label { Dock = DockStyle.Fill, AutoSize = false, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) };
        side.Controls.Add(_gating, 0, 3);

        side.Controls.Add(new Label
        {
            Text = "Each row is that counter's own output from reset — fixed, not sampled, "
                   + "not random. One seed and one rule give one sequence, the same every time. "
                   + "The ticks beneath a row are where this channel samples it: turning AUDF "
                   + "moves them without changing a single cell above.",
            Dock = DockStyle.Fill,
            AutoSize = false,
            ForeColor = SystemColors.GrayText,
        }, 0, 4);

        layout.Controls.Add(side, 0, 0);

        // --- Right: the trains --------------------------------------------

        _trains = new PolyTrainsView { Dock = DockStyle.Fill, Margin = new Padding(6, 3, 3, 3) };
        layout.Controls.Add(_trains, 1, 0);

        box.Controls.Add(layout);
        Controls.Add(box);
    }

    private static Label Reading() => new()
    {
        Dock = DockStyle.Fill,
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = SystemColors.GrayText,
    };

    /// <summary>Caller holds <see cref="ChipSession.Gate"/>.</summary>
    public void SyncFromChip()
    {
        int channel = _channel.SelectedIndex;
        var chip = _session.Chip;

        int distortion = chip.Distortion(channel);
        int audf = chip.Audf(channel);
        bool nineBit = (chip.Control & PokeyChip.CtlNineBitPoly) != 0;

        _distortion.Text = $"Distortion:  {distortion}  {PokeyChip.DistortionName(distortion)}";
        _audf.Text = $"AUDF:  {audf}   —   samples every {audf + 1}";

        var gates = PolyCounters.GatesFor(distortion, nineBit);

        // What the current setting actually means, in one line, because the trains are
        // only legible once you know which of them is being consulted.
        _gating.Text = gates.Length switch
        {
            0 => "Pure tone — no counter is consulted.",
            1 => $"Gated by the {PolyTrainsView.CounterName(gates[0])} counter.",
            _ => $"Gated by {PolyTrainsView.CounterName(gates[0])} and {PolyTrainsView.CounterName(gates[1])}"
                 + " — both must be high.",
        };
        _gating.ForeColor = gates.Length == 0 ? SystemColors.GrayText : Color.FromArgb(0x1B, 0x6B, 0x2C);

        _trains.Show(audf, gates);
    }
}
