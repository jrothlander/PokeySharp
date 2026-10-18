using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PokeyLabForms.Views;

/// <summary>
/// One channel's pair of registers, plus the three AUDCTL-derived facts about it that
/// change how its bytes should be read.
///
/// A snapshot rather than the channel itself, because <c>Views/</c> never touches the
/// chip -- see <see cref="MainForm.Sync"/> for the rule and why it exists.
/// </summary>
public readonly record struct ChannelRegisters(
    int Audf, int Audc, bool LowHalf, bool HighHalf, bool VolumeOnly, bool Fast);

/// <summary>
/// The nine bytes, as bits.
///
/// <b>Every other display in this window shows an effect; this one shows the cause, in
/// the form the chip actually receives it.</b> The scope shows what came out, the sweep
/// graph shows pitch over time, the poly trains show the mechanism in between. But every
/// control on the form is a human abstraction -- a dropdown reading "distortion 5", a
/// slider reading "volume 2" -- and the chip has never heard of any of them. It is
/// handed <c>$A2</c>. Until this panel there was nowhere in the app you could see the
/// byte.
///
/// Three things fall out of drawing it, and each is otherwise something you can only be
/// told:
///
/// <list type="bullet">
/// <item><b>AUDC's arithmetic stops being a claim.</b> The distortion field is bits 7-5
/// and volume is bits 3-0, so <c>AUDC = distortion x 32 + volume</c> -- set distortion to
/// 5 and watch the top three cells become 101, set volume to 2 and watch the nibble
/// become 0010, and read $A2 off the end of the row. The three fields are coloured
/// separately for exactly this reason.</item>
/// <item><b>A joined pair stops being confusing.</b> Set AUDCTL bit 4 and channel 1's
/// whole row greys out: it is a low byte now, it clocks its partner, and it is not a
/// voice. That is the entire answer to why joining 1+2 silences a sound playing on
/// channel 1, and it takes two seconds instead of a paragraph.</item>
/// <item><b>The envelope's wrap becomes visible.</b> During playback the envelope is
/// rewriting AUDF and AUDC a couple of hundred times a second, and registers wrap rather
/// than clamp -- Tempest's shot subtracts 8 from a volume of 2 every tick and rolls the
/// nibble over repeatedly, which is the buzz you hear. Here you watch the nibble roll.
/// </item>
/// </list>
///
/// <b>It is a read-out, not an input.</b> The cells do not click. Every one of these bits
/// already has a control elsewhere on the form, and a second editing surface for the same
/// state is both a synchronisation problem and a question nobody should have to ask about
/// which one is authoritative. Same standing as the poly trains, and for the same reason.
/// </summary>
public sealed class RegisterBitsView : InstrumentView
{
    private const int Bits = 8;

    /// <summary>Which register a group of cells belongs to, which is the only thing
    /// deciding how its bits are coloured.</summary>
    private enum Reg { Audf, Audc, Audctl }

    /// <summary>
    /// AUDCTL's bit names, most significant first, in two lines because every one of them
    /// is a separate switch and none has a name that fits a cell on one line.
    /// </summary>
    private static readonly string[] CtlTop =
        { "9-BIT", "CH1", "CH3", "JOIN", "JOIN", "FILT", "FILT", "15 KHZ" };

    private static readonly string[] CtlLow =
        { "POLY", "FAST", "FAST", "1+2", "3+4", "CH2/4", "CH1/3", "CLOCK" };

    private readonly ChannelRegisters[] _channels = new ChannelRegisters[4];
    private int _audctl;

    /// <summary>
    /// Which bits moved since the previous frame, per register.
    ///
    /// <b>Sampled, and honestly so.</b> The window catches up thirty times a second while
    /// the envelope ticks at a couple of hundred, so this marks the bits that differ
    /// between two frames roughly seven ticks apart -- not every write. A bit that
    /// toggles twice in that gap reads as unmoved. That is the right trade: repainting a
    /// form full of controls at the envelope's rate would cost far more than it showed,
    /// and marking the movement is what keeps a fast nibble legible as motion rather than
    /// a blur.
    /// </summary>
    private readonly int[] _audfMoved = new int[4];
    private readonly int[] _audcMoved = new int[4];
    private int _ctlMoved;

    /// <summary>Nothing has moved before the first frame -- without this, every set bit
    /// on the opening chip would be marked as having just changed.</summary>
    private bool _seen;

    public void Show(ChannelRegisters[] channels, int audctl)
    {
        for (int i = 0; i < _channels.Length && i < channels.Length; i++)
        {
            if (_seen)
            {
                _audfMoved[i] = _channels[i].Audf ^ channels[i].Audf;
                _audcMoved[i] = _channels[i].Audc ^ channels[i].Audc;
            }
            _channels[i] = channels[i];
        }

        if (_seen) _ctlMoved = _audctl ^ audctl;
        _audctl = audctl;
        _seen = true;

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;

        // The other displays draw curves; this one draws a grid of small rectangles, and
        // antialiasing only blurs their edges into the black behind them.
        g.SmoothingMode = SmoothingMode.None;

        const float pad = 8f;
        const float chW = 32f;      // "CH1"
        const float regW = 46f;     // "AUDF1"
        const float hexW = 32f;     // "$3C"
        const float noteW = 46f;    // "VOL ONLY"
        const float headerH = 11f;
        const float ctlLabelH = 18f;

        // Everything in a half except the sixteen cells.
        //
        // <b>The note is not in here, and that is what makes the block centre.</b> A note
        // only appears when a channel is doing something unusual, so a column reserved for
        // one sits empty nearly all the time -- and reserved at the outer edge of each
        // half, the empty one on the right read as the whole display being shoved left.
        // Keeping it out of the block's width centres the cells, which are the thing the
        // eye actually lines up on, and the notes then draw into the margin that centring
        // creates. The margin is guaranteed to be at least a note wide because the cell
        // size is chosen with that room already set aside.
        const float halfFixed = chW + 2f * (regW + 6f + hexW);

        // <b>The gutter is sized from the note, not picked.</b> A note draws just past the
        // end of its own half -- which for the right half is the page margin, but for the
        // left half is the space before CH3 begins. At a nominal gap the two collided:
        // channel 1's "VOL ONLY" landed on top of the CH3 label. Deriving the gutter from
        // the note's own width means they cannot overlap however the rest is sized.
        float gutter = noteW + 14f;

        // <b>Four channels stacked in one column, AUDCTL to their right.</b> This mirrors
        // the form's own order -- the channel strips are a vertical stack in the left
        // column and the AUDCTL panel sits beside them -- so the strip reads as the same
        // machine laid out the same way, rather than as an unrelated grid that happens to
        // be nearby. The earlier arrangement paired the channels 1-2 / 3-4 across two
        // halves, which fitted a short strip at the bottom of the window but matched
        // nothing above it.
        //
        // Channels are left-aligned and AUDCTL right-aligned, so both ends are anchored
        // and the slack collects in the middle where it separates two genuinely different
        // things: eight per-channel registers, and the one register that governs the chip.

        const float ctlNoteW = 122f;   // "NOTHING SWITCHED ON"
        float ctlCell = 64f;
        float ctlBlockW = regW + 6f + Bits * ctlCell + 6f + hexW;

        // Sixteen cells to a channel row, not eight -- AUDF and AUDC each get their own
        // eight. Getting this wrong understates the row's width, which leaves the note
        // clip and the AUDCTL placement working off a row edge that is not where the row
        // actually ends.
        float forCells = Width - 2f * pad - gutter - noteW - ctlNoteW - ctlBlockW;
        float cell = (forCells - halfFixed) / (2f * Bits);
        cell = Math.Clamp(cell, 9f, 30f);
        if (cell < 9f || forCells <= 0f) return;

        float channelsW = halfFixed + 2f * Bits * cell;
        float left = pad + 4f;
        float ctlLeft = Math.Max(left + channelsW + gutter, Width - pad - ctlNoteW - ctlBlockW);

        int rows = _channels.Length;
        float rowH = Math.Clamp((Height - 2f * pad - headerH) / rows, 8f, 24f);
        if (rowH < 8f) return;

        // Centred vertically rather than hanging from the top: the strip's height is fixed
        // by the form while the row height is capped, so at most sizes there is slack.
        float contentH = headerH + rows * rowH;
        float top = Math.Max(pad, (Height - contentH) / 2f);

        using var rule = new Pen(LabColors.Rule, 1f);
        using var moved = new Pen(LabColors.Bad, 1.4f);

        float rowsTop = top + headerH;

        // --- Header: the bit numbers, once over each of the three groups ---

        float audfX = left + chW + regW;
        float audcX = audfX + Bits * cell + 6f + hexW + regW;
        float ctlCellsX = ctlLeft + regW + 6f;

        foreach (var (gx, width) in new[] { (audfX, cell), (audcX, cell), (ctlCellsX, ctlCell) })
            for (int i = 0; i < Bits; i++)
                CentreLabel(g, (7 - i).ToString(), gx + i * width, width - 3f, top,
                            LabColors.InkFaint, SmallFont);

        // --- The four channels, stacked ---

        for (int channel = 0; channel < rows; channel++)
        {
            float y = rowsTop + channel * rowH;
            float x = left;

            var c = _channels[channel];

            // A low half is dimmed rather than hidden. It still holds a value, and the
            // value still matters -- it is the bottom eight bits of its partner's
            // divider. What it has stopped being is a voice, and grey says that without
            // pretending the register went away.
            bool dim = c.LowHalf;

            Label(g, $"CH{channel + 1}", x, y + 2f,
                  dim ? LabColors.InkFaint : LabColors.Ink, SmallFont);
            x += chW;

            x = Group(g, x, y, cell, rowH - 4f, regW, hexW, $"AUDF{channel + 1}",
                      c.Audf, _audfMoved[channel], Reg.Audf, dim, rule, moved);

            x = Group(g, x, y, cell, rowH - 4f, regW, hexW, $"AUDC{channel + 1}",
                      c.Audc, _audcMoved[channel], Reg.Audc, dim, rule, moved);

            // Into the gap between the channels and AUDCTL, and clipped against AUDCTL
            // rather than the panel edge, because that is what it would actually run into.
            var (note, tone) = Note(c, channel);
            if (note.Length > 0 && x + 4f + g.MeasureString(note, SmallFont).Width < ctlLeft)
                Label(g, note, x + 4f, y + 2f, tone, SmallFont);
        }

        // --- AUDCTL, beside them ---
        //
        // Wider cells than the channel rows, because each of these bits is its own switch
        // and has to carry its own name. It is deliberately not aligned to the grid to its
        // left: those eight columns are one field read as a number, these eight are eight
        // unrelated flags, and lining them up would imply a relationship that is not there.

        // Its cells and the two lines of names underneath, centred against the four rows.
        float ctlBlockH = rowH + ctlLabelH;
        float ctlY = rowsTop + Math.Max(0f, (rows * rowH - ctlBlockH) / 2f);
        float ctlX = ctlCellsX;

        Label(g, "AUDCTL", ctlLeft, ctlY + 2f, LabColors.Ink, SmallFont);
        float afterCtl = Group(g, ctlLeft, ctlY, ctlCell, rowH - 4f, regW + 6f, hexW,
                               "", _audctl, _ctlMoved, Reg.Audctl, false, rule, moved);

        for (int i = 0; i < Bits; i++)
        {
            int bit = 7 - i;
            bool set = (_audctl & (1 << bit)) != 0;
            var tone = set ? CtlColour(bit) : LabColors.InkFaint;
            float cx = ctlX + i * ctlCell;
            CentreLabel(g, CtlTop[i], cx, ctlCell - 3f, ctlY + rowH - 2f, tone, SmallFont);
            CentreLabel(g, CtlLow[i], cx, ctlCell - 3f, ctlY + rowH + 7f, tone, SmallFont);
        }

        // Zero is not an absence here, it is a finding: every feature this register
        // controls is switched off, which is exactly what Tempest writes and why all of
        // that game's variety has to come from rewriting AUDF and AUDC on a timer.
        if (_audctl == 0)
            Label(g, "NOTHING SWITCHED ON", afterCtl + 6f, ctlY + 2f,
                  LabColors.Good, SmallFont);
    }

    /// <summary>Draws one register: its name, its eight cells, and its value in hex.
    /// Returns the x the next thing may start at.</summary>
    private float Group(Graphics g, float x, float y, float cell, float h,
                        float regW, float hexW, string name, int value, int movedMask,
                        Reg reg, bool dim, Pen rule, Pen moved)
    {
        if (name.Length > 0)
            Label(g, name, x, y + 2f, dim ? LabColors.InkFaint : LabColors.Ink, SmallFont);
        x += regW;

        for (int i = 0; i < Bits; i++)
        {
            int bit = 7 - i;
            var box = new RectangleF(x + i * cell, y, cell - 3f, h);

            // Set is a filled cell, clear an outline -- the same reading as the poly
            // trains, so the two displays teach one visual language between them.
            if ((value & (1 << bit)) != 0)
            {
                using var brush = new SolidBrush(dim ? LabColors.InkFaint : Colour(reg, bit));
                g.FillRectangle(brush, box);
            }
            else
            {
                g.DrawRectangle(rule, box.X, box.Y, box.Width, box.Height);
            }

            if ((movedMask & (1 << bit)) != 0)
                g.DrawRectangle(moved, box.X - 1.5f, box.Y - 1.5f, box.Width + 3f, box.Height + 3f);
        }

        x += Bits * cell + 6f;
        Label(g, $"${value:X2}", x, y + 2f, dim ? LabColors.InkFaint : LabColors.Accent, SmallFont);
        return x + hexW;
    }

    /// <summary>
    /// What a bit is coloured, which is entirely a question of which field it belongs to.
    ///
    /// AUDF is one eight-bit number, so it is one colour. AUDC is three fields sharing a
    /// byte and they are coloured apart, because seeing where distortion stops and volume
    /// starts is most of what this panel is for. AUDCTL is eight unrelated switches
    /// grouped by what they do: clock selection, the 16-bit joins, the filters, and the
    /// poly length.
    /// </summary>
    private static Color Colour(Reg reg, int bit) => reg switch
    {
        Reg.Audf => LabColors.Accent,
        Reg.Audc => bit >= 5 ? LabColors.Accent            // distortion, bits 7-5
                  : bit == 4 ? LabColors.Cool              // volume-only
                  : LabColors.Good,                        // volume, bits 3-0
        _ => CtlColour(bit),
    };

    private static Color CtlColour(int bit) => bit switch
    {
        7 => LabColors.Accent,          // 9-bit poly
        6 or 5 => LabColors.Cool,       // channel 1 / channel 3 straight off the base clock
        4 or 3 => LabColors.Good,       // the two 16-bit joins
        2 or 1 => LabColors.Ink,        // the two high-pass filters
        _ => LabColors.Cool,            // 15 kHz clock, global
    };

    /// <summary>The one thing about a channel that changes how its two bytes should be
    /// read. Ordered by how much it changes them: a low half is not a voice at all, which
    /// outranks everything else that could be said about it.</summary>
    /// <remarks>
    /// Short enough to sit in the margin beside the block rather than in a column inside
    /// it. Which partner a joined channel belongs to is dropped from the text because the
    /// AUDCTL row below is already showing JOIN 1+2 or JOIN 3+4 lit, and the greyed row is
    /// adjacent to the channel it pairs with.
    /// </remarks>
    private static (string, Color) Note(in ChannelRegisters c, int channel) =>
          c.LowHalf    ? ("LOW BYTE", LabColors.InkFaint)
        : c.HighHalf   ? ("16-BIT", LabColors.Good)
        : c.VolumeOnly ? ("VOL ONLY", LabColors.Cool)
        : c.Fast       ? ("FAST CLK", LabColors.Cool)
        : ("", LabColors.Ink);

    /// <summary>Centres a label over a cell, so a bit number sits on its column however
    /// wide the column worked out.</summary>
    private void CentreLabel(Graphics g, string text, float x, float width, float y,
                             Color colour, Font font)
    {
        float w = g.MeasureString(text, font).Width;
        Label(g, text, x + (width - w) / 2f, y, colour, font);
    }
}
