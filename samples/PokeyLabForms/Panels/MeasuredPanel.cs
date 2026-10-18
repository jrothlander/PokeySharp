using System;
using System.Drawing;
using System.Windows.Forms;
using Pokey;

namespace PokeyLabForms.Panels;

/// <summary>
/// The current envelope's four numbers, judged against the reference captures.
///
/// <b>This is the readout that can settle the open question.</b> RimWalker's own sound
/// page tunes an approximation of POKEY; here the same four-byte steps drive a real
/// one, so a disagreement between the table and the captures can no longer be blamed
/// on the synthesis. What is left is the clock, the decode, or the measurement.
///
/// The bands are the shot's. They are shown whatever preset is loaded, and that is
/// deliberate rather than an oversight: only the player's shot was measured, so a
/// green verdict on any other sound means "this happens to land where the shot does",
/// not "this is correct". Hiding the row for other presets would lose the comparison;
/// pretending each preset has its own band would invent evidence.
/// </summary>
public sealed class MeasuredPanel : Panel
{
    private readonly ChipSession _session;

    private readonly Label[] _now = new Label[4];
    private readonly Label[] _verdict = new Label[4];

    private static readonly string[] Titles = { "Starts at", "Ends at", "Lasts", "Fall" };
    private static readonly string[] Units = { "Hz", "Hz", "s", "to 1" };

    public MeasuredPanel(ChipSession session)
    {
        _session = session;
        Dock = DockStyle.Fill;

        var box = new GroupBox
        {
            Text = "Against the reference captures (player's shot)",
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 4, 8, 6),
        };

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 5 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90f));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44f));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        for (int i = 0; i < 4; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));

        grid.Controls.Add(Header(""), 0, 0);
        grid.Controls.Add(Header("Now"), 1, 0);
        grid.Controls.Add(Header("Measured"), 2, 0);
        grid.Controls.Add(Header(""), 3, 0);

        var bands = new[]
        {
            MeasuredBands.Start, MeasuredBands.End, MeasuredBands.Seconds, MeasuredBands.Fall,
        };

        for (int i = 0; i < 4; i++)
        {
            grid.Controls.Add(Cell(Titles[i], SystemColors.ControlText), 0, i + 1);

            // The one number being judged, a step up in weight so the eye lands on it
            // rather than on the band beside it.
            _now[i] = Cell("", SystemColors.ControlText);
            _now[i].Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold);
            grid.Controls.Add(_now[i], 1, i + 1);

            string band = i == 2
                ? $"{bands[i].Low:0.000} – {bands[i].High:0.000} {Units[i]}"
                : $"{bands[i].Low:0.#} – {bands[i].High:0.#} {Units[i]}";
            grid.Controls.Add(Cell(band, SystemColors.GrayText), 2, i + 1);

            _verdict[i] = Cell("", SystemColors.ControlText);
            _verdict[i].Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold);
            grid.Controls.Add(_verdict[i], 3, i + 1);
        }

        box.Controls.Add(grid);
        Controls.Add(box);
    }

    private static Label Header(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        AutoSize = false,
        ForeColor = SystemColors.GrayText,
        TextAlign = ContentAlignment.BottomLeft,
    };

    private static Label Cell(string text, Color colour) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        AutoSize = false,
        ForeColor = colour,
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private static readonly Color Passes = Color.FromArgb(0x1B, 0x6B, 0x2C);
    private static readonly Color Fails = Color.FromArgb(0xA3, 0x2E, 0x24);

    /// <summary>Caller holds <see cref="ChipSession.Gate"/>.</summary>
    public void SyncFromChip()
    {
        var envelope = _session.Envelope;
        var chip = _session.Chip;

        double start = envelope.PitchAt(chip, 0);
        double end = envelope.PitchAt(chip, envelope.TotalTicks);

        // Guarded because a sweep that runs the divider up to its ceiling can finish at
        // a pitch near zero, and a ratio against it is meaningless rather than large.
        double fall = end > 0.01 ? start / end : 0.0;
        double seconds = envelope.Seconds;

        Set(0, $"{start:0} Hz", MeasuredBands.Holds(MeasuredBands.Start, start));
        Set(1, $"{end:0} Hz", MeasuredBands.Holds(MeasuredBands.End, end));
        Set(2, $"{seconds:0.000} s", MeasuredBands.Holds(MeasuredBands.Seconds, seconds));
        Set(3, $"{fall:0.0} to 1", MeasuredBands.Holds(MeasuredBands.Fall, fall));
    }

    private void Set(int index, string value, bool holds)
    {
        _now[index].Text = value;
        _now[index].ForeColor = holds ? Passes : Fails;
        _verdict[index].Text = holds ? "OK" : "no";
        _verdict[index].ForeColor = holds ? Passes : Fails;
    }
}
