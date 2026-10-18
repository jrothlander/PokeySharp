using System;
using System.Drawing;
using System.Windows.Forms;
using Pokey;

namespace PokeyLabForms.Panels;

/// <summary>
/// One channel's registers, as controls.
///
/// <b>Laid out as the hardware is, not as a synthesiser would be.</b> There is no
/// "note" and no "waveform" here, because POKEY has neither -- it has a divider, a
/// distortion field and a volume nibble, and the sound is whatever those three do
/// together. Presenting it any other way would teach the wrong model.
///
/// The derived pitch sits in the group's own caption, beside the channel number,
/// because the relationship between the divider and the pitch is the single least
/// intuitive thing about the chip: the divider is an <i>inverse</i>, so equal steps in
/// AUDF are wildly unequal steps in pitch. Dragging the slider and watching the Hz
/// move is the fastest way to internalise that -- and is the clearest thing this front
/// end buys over PokeyLab's, where the same value is reached one keypress at a time.
/// </summary>
public sealed class ChannelStrip : GroupBox
{
    private readonly ChipSession _session;
    private readonly int _channel;

    private readonly NumericUpDown _audf;
    private readonly TrackBar _audfSlider;
    private readonly ComboBox _distortion;
    private readonly NumericUpDown _volume;
    private readonly TrackBar _volumeSlider;
    private readonly CheckBox _volumeOnly;

    /// <summary>
    /// Set while writing values in from the chip, so the resulting change events do
    /// not turn round and write them straight back.
    ///
    /// It matters here beyond the usual reason. The envelope rewrites AUDF and AUDC on
    /// its own channel several hundred times a second while it plays, so this panel is
    /// genuinely a display of something moving -- not just an editor that occasionally
    /// reloads.
    /// </summary>
    private bool _syncing;

    public ChannelStrip(ChipSession session, int channel)
    {
        _session = session;
        _channel = channel;

        // Tighter top and bottom than the sides. The three register rows each need their
        // controls' full height and the checkbox needs its own, which together come to
        // more than a 113px group has to give -- and the padding is the cheapest place to
        // find the difference, since the group's caption and border already separate one
        // channel from the next.
        Padding = new Padding(8, 2, 8, 2);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 4,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74f));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62f));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // Absolute for the three register rows, not four equal shares.
        //
        // <b>A share of a short strip is smaller than the controls' own minimum height,
        // and that is what breaks the alignment.</b> A NumericUpDown and a TrackBar both
        // refuse to shrink past a floor, and a TableLayoutPanel that cannot fit a control
        // in its cell stops centring it and pins it to the top instead. The two controls
        // then sit at different heights for reasons that have nothing to do with either
        // of them, and the offset changes whenever anything else on the form claims a few
        // pixels -- which is exactly how they drifted apart when the register strip took
        // 142px off the bottom of the window.
        //
        // Giving each register row the height its controls actually need keeps every one
        // of them centred and the row internally consistent at any window size. The
        // checkbox has no such floor, so it takes whatever is left.
        float registerRow = RowControlHeight + 1;
        for (int i = 0; i < 3; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, registerRow));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        // --- AUDF ---------------------------------------------------------
        //
        // A number and a slider over the same value, which is not redundancy: the
        // number is how you land on a divider exactly, and the slider is how you sweep
        // through them and hear where the interesting ones are.

        grid.Controls.Add(Caption("AUDF"), 0, 0);

        _audf = Spinner(0, 255);
        _audf.ValueChanged += (_, _) =>
        {
            if (_syncing) return;
            WriteAudf((int)_audf.Value);
            Mirror(_audfSlider, (int)_audf.Value);
        };
        grid.Controls.Add(_audf, 1, 0);

        _audfSlider = Slider(0, 255);
        _audfSlider.ValueChanged += (_, _) =>
        {
            if (_syncing) return;
            WriteAudf(_audfSlider.Value);
            Mirror(_audf, _audfSlider.Value);
        };
        grid.Controls.Add(_audfSlider, 2, 0);

        // --- Distortion ---------------------------------------------------

        grid.Controls.Add(Caption("Distortion"), 0, 1);

        // Anchored for the same reason as the spinners and sliders -- see AcrossAndCentred.
        // A DropDownList ComboBox has a fixed height too, so docking it would put it on a
        // third line of its own.
        _distortion = new ComboBox
        {
            Anchor = AcrossAndCentred,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(3, 0, 3, 1),
        };
        // Every code listed, including the duplicated behaviours -- 1 and 3 are both
        // plain 5-bit, 5 and 7 are both pure tone. Collapsing them would tidy the list
        // and lie about the register: eight codes is what the field holds, and a byte
        // read back off a real machine will contain whichever one was written.
        for (int code = 0; code < 8; code++)
            _distortion.Items.Add($"{code}  {PokeyChip.DistortionName(code)}");
        _distortion.SelectedIndexChanged += (_, _) =>
        {
            if (_syncing) return;
            _session.SetDistortion(_channel, _distortion.SelectedIndex);
        };
        grid.Controls.Add(_distortion, 1, 1);
        grid.SetColumnSpan(_distortion, 2);

        // --- Volume -------------------------------------------------------

        grid.Controls.Add(Caption("Volume"), 0, 2);

        _volume = Spinner(0, 15);
        _volume.ValueChanged += (_, _) =>
        {
            if (_syncing) return;
            _session.SetVolume(_channel, (int)_volume.Value);
            Mirror(_volumeSlider, (int)_volume.Value);
        };
        grid.Controls.Add(_volume, 1, 2);

        _volumeSlider = Slider(0, 15);
        _volumeSlider.ValueChanged += (_, _) =>
        {
            if (_syncing) return;
            _session.SetVolume(_channel, _volumeSlider.Value);
            Mirror(_volume, _volumeSlider.Value);
        };
        grid.Controls.Add(_volumeSlider, 2, 2);

        // --- Volume only --------------------------------------------------

        _volumeOnly = new CheckBox
        {
            Text = "Volume only (divider and poly cut out)",
            Dock = DockStyle.Fill,
            AutoSize = false,
            Margin = new Padding(3, 1, 3, 1),
        };
        _volumeOnly.CheckedChanged += (_, _) =>
        {
            if (_syncing) return;
            _session.SetVolumeOnly(_channel, _volumeOnly.Checked);
        };
        grid.Controls.Add(_volumeOnly, 0, 3);
        grid.SetColumnSpan(_volumeOnly, 3);

        Controls.Add(grid);
    }

    private void WriteAudf(int value)
    {
        lock (_session.Gate) _session.Chip.SetAudf(_channel, Math.Clamp(value, 0, 255));
    }

    /// <summary>Copies a value into this strip's other control for the same register
    /// without that control writing it back to the chip a second time.</summary>
    private void Mirror(NumericUpDown target, int value)
    {
        _syncing = true;
        target.Value = Math.Clamp(value, (int)target.Minimum, (int)target.Maximum);
        _syncing = false;
    }

    private void Mirror(TrackBar target, int value)
    {
        _syncing = true;
        target.Value = Math.Clamp(value, target.Minimum, target.Maximum);
        _syncing = false;
    }

    private static Label Caption(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoSize = false,
        Margin = new Padding(3, 0, 3, 0),
    };

    /// <summary>
    /// Stretch horizontally, keep your own height, and let the cell centre you.
    ///
    /// <b>Every control on a row has to agree about this or the row stops lining up.</b>
    /// A NumericUpDown, a ComboBox and a TrackBar all have a natural height they will not
    /// shrink below, so <c>Dock.Fill</c> does not actually make them fill -- it pins each
    /// one to the top of its cell at whatever height it insists on, and any difference
    /// between those heights becomes a visible offset. That is exactly what happened when
    /// the register strip took 142px off the bottom of the window: the rows dropped to
    /// 21px, the spinners hit their 19px floor and overflowed downward, the sliders still
    /// fitted and did not, and every slider ended up sitting five pixels above the number
    /// beside it.
    ///
    /// Anchoring left and right instead stretches the width and leaves the height alone,
    /// and a TableLayoutPanel centres a control it is not stretching vertically. The row
    /// can then be any height at all and the three controls stay on one line.
    /// </summary>
    private const AnchorStyles AcrossAndCentred = AnchorStyles.Left | AnchorStyles.Right;

    /// <summary>
    /// The height every control on a register row is given, so a row cannot disagree with
    /// itself.
    ///
    /// <b>Identical boxes is the point, not the particular number.</b> When the two
    /// controls are different heights the layout has to treat them differently — it
    /// centres whichever one fits and clamps whichever one does not to the top of the
    /// cell — so the offset between them changes with the row height and reappears the
    /// next time anything above or below claims a few pixels. Given the same box they get
    /// the same treatment whatever the row does, and a TrackBar draws its thumb centred
    /// in whatever box it is handed.
    ///
    /// Taken from a NumericUpDown because that is the control with an opinion: it refuses
    /// to be shorter than its font needs, and asking it is right at any DPI where a
    /// hardcoded number would not be.
    /// </summary>
    private static readonly int RowControlHeight = MeasureRowHeight();

    private static int MeasureRowHeight()
    {
        using var probe = new NumericUpDown();
        return probe.PreferredHeight;
    }

    private static NumericUpDown Spinner(int minimum, int maximum) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        Anchor = AcrossAndCentred,
        Height = RowControlHeight,
        Margin = new Padding(3, 0, 3, 1),
        TextAlign = HorizontalAlignment.Right,
    };

    private static TrackBar Slider(int minimum, int maximum) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        Anchor = AcrossAndCentred,
        AutoSize = false,
        Height = RowControlHeight,
        // One pixel lower than the spinner beside it, and measured rather than guessed: a
        // TrackBar draws its thumb about 1.5px above the centre of the box it is given,
        // because it reserves the bottom of that box for tick marks it is not drawing.
        // The row is one pixel taller than the controls, so this spends that pixel on the
        // slider without pushing either control past the cell and back into the clamping
        // case described above.
        Margin = new Padding(3, 1, 3, 0),
        TickStyle = TickStyle.None,
    };

    /// <summary>
    /// Writes the chip's current state into the controls.
    ///
    /// <b>A focused control is left alone.</b> Everything here tracks a chip that
    /// something else may be writing -- an envelope on this channel, or the mute
    /// button -- so the display has to follow it. But overwriting a spinner while
    /// someone is typing into it would eat the keystroke, so whichever control has
    /// focus wins for as long as it has focus. The rule is small and it is the whole
    /// difference between a live readout and a fight.
    ///
    /// Caller holds <see cref="ChipSession.Gate"/>.
    /// </summary>
    public void SyncFromChip()
    {
        var chip = _session.Chip;

        bool low = chip.IsLowHalf(_channel);
        bool high = chip.IsHighHalf(_channel);

        string caption = $"Channel {_channel + 1}";
        if (low) caption += "  (low half)";
        else if (high) caption += "  (16 bit)";

        // Blank for a low half, which contributes its count to its partner and nothing
        // to the mix -- there is no pitch of its own to report.
        //
        // Blank for volume-only too, and for a sharper reason: PitchHz would happily
        // report what the divider *would* produce, but in volume-only mode the divider
        // is disconnected and that number describes nothing. A channel being used as a
        // DAC showing a confident pitch is precisely the sort of quietly wrong readout
        // this tool exists to not have.
        if (chip.VolumeOnly(_channel)) caption += "   —   volume only, no pitch";
        else if (!low) caption += $"   —   {chip.PitchHz(_channel):0.0} Hz";
        if (Text != caption) Text = caption;

        _syncing = true;

        int audf = chip.Audf(_channel);
        if (!_audf.Focused) _audf.Value = audf;
        if (!_audfSlider.Focused) _audfSlider.Value = audf;

        if (!_distortion.Focused) _distortion.SelectedIndex = chip.Distortion(_channel);

        int volume = chip.Volume(_channel);
        if (!_volume.Focused) _volume.Value = volume;
        if (!_volumeSlider.Focused) _volumeSlider.Value = volume;

        if (!_volumeOnly.Focused) _volumeOnly.Checked = chip.VolumeOnly(_channel);

        _syncing = false;
    }
}
