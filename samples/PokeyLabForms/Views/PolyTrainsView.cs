using System;
using System.Drawing;
using System.Windows.Forms;
using Pokey;

namespace PokeyLabForms.Views;

/// <summary>
/// The polynomial counters, drawn as the bit trains they are.
///
/// <b>This is the display for the idea the chip is named after.</b> POKEY's "noise" is
/// not noise: it is three shift registers free-running at the base clock, and a channel
/// that is not on pure tone toggles its output only when the bits it is watching say it
/// may. The channel strips tell you a channel is on distortion 4; the scope shows you
/// the result; neither shows the mechanism in between, which is what this does.
///
/// Two facts do all the explaining, and both are visible here rather than asserted:
///
/// <list type="bullet">
/// <item><b>Width decides character.</b> The 4-bit counter repeats every fifteen steps,
/// so it buzzes at a pitch of its own; the 17-bit repeats every 131071, which is longer
/// than anyone waits and so reads as noise. Same mechanism, opposite sound, and the
/// only difference is how wide the register is. The repeat marker is the whole
/// argument.</item>
/// <item><b>The counters free-run; the channel samples.</b> They advance at the base
/// clock regardless of what any channel is doing, and a channel looks at them once per
/// divider period. That is why POKEY noise has pitch at all -- you are hearing a fixed
/// pattern strobed at whatever rate AUDF picks, and turning the divider moves the ticks
/// beneath the train without changing a single cell above them.</item>
/// </list>
///
/// <b>Nothing here is a control.</b> The trains are each counter's own fixed output
/// from reset -- one seed and one rule give one sequence, identical every run -- and
/// the distortion and AUDF that decide the highlighting are read from whichever channel
/// the panel points at.
/// </summary>
public sealed class PolyTrainsView : InstrumentView
{
    /// <summary>Bits shown per train. Enough that the 4- and 5-bit counters visibly
    /// repeat more than once while the long ones plainly do not.</summary>
    private const int Bits = 64;

    private static readonly PolyCounters.Kind[] Shown =
    {
        PolyCounters.Kind.Four, PolyCounters.Kind.Five,
        PolyCounters.Kind.Nine, PolyCounters.Kind.Seventeen,
    };

    public static readonly string[] Names = { "4 BIT", "5 BIT", "9 BIT", "17 BIT" };

    /// <summary>The sequences are deterministic, so they are generated once and kept.
    /// Regenerating four LFSR runs on every repaint would work and would be wasteful;
    /// more to the point, caching makes it obvious they cannot change.</summary>
    private static readonly bool[][] Sequences = BuildSequences();

    private static bool[][] BuildSequences()
    {
        var all = new bool[Shown.Length][];
        for (int i = 0; i < Shown.Length; i++) all[i] = PolyCounters.Sequence(Shown[i], Bits);
        return all;
    }

    /// <summary>Display name for a counter. Deliberately not called <c>Name</c>: this
    /// is a <see cref="Control"/>, and that would hide the inherited property.</summary>
    public static string CounterName(PolyCounters.Kind kind) => Names[Array.IndexOf(Shown, kind)];

    private int _audf;
    private PolyCounters.Kind[] _gates = Array.Empty<PolyCounters.Kind>();

    public void Show(int audf, PolyCounters.Kind[] gates)
    {
        _audf = Math.Max(0, audf);
        _gates = gates ?? Array.Empty<PolyCounters.Kind>();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;

        float labelLeft = 8f;
        float trainLeft = 84f;
        float trainRight = Width - 10f;
        if (trainRight <= trainLeft) return;

        float spacing = (Height - 12f) / Shown.Length;
        float cell = (trainRight - trainLeft) / Bits;
        float height = Math.Min(26f, spacing - 30f);
        if (height <= 2f) return;

        using var ruleP = new Pen(LabColors.Rule, 1f);
        using var badP = new Pen(LabColors.Bad, 1.8f);
        using var coolP = new Pen(LabColors.Cool, 1.4f);
        using var accentB = new SolidBrush(LabColors.Accent);
        using var faintB = new SolidBrush(LabColors.InkFaint);

        for (int index = 0; index < Shown.Length; index++)
        {
            var kind = Shown[index];
            bool gating = Array.IndexOf(_gates, kind) >= 0;
            float y = 8f + index * spacing;

            Label(g, Names[index], labelLeft, y, gating ? LabColors.Accent : LabColors.InkFaint);
            Label(g, $"EVERY {PolyCounters.Period(kind)}", labelLeft, y + 14f,
                  LabColors.InkFaint, SmallFont);
            if (gating) Label(g, "IN USE", labelLeft, y + 27f, LabColors.Good, SmallFont);

            var bits = Sequences[index];
            for (int i = 0; i < Bits; i++)
            {
                var box = new RectangleF(trainLeft + i * cell + 1f, y, cell - 2f, height);

                // A set bit is a filled cell, a clear one an outline. Gating counters
                // fill in the accent so the eye lands on the one actually consulted.
                if (bits[i]) g.FillRectangle(gating ? accentB : faintB, box);
                else g.DrawRectangle(ruleP, box.X, box.Y, box.Width, box.Height);
            }

            // Where the pattern starts over. The 4- and 5-bit counters wear this
            // plainly; the 9- and 17-bit ones repeat far off the right-hand edge, which
            // is exactly why they sound like noise instead of a tone.
            int period = PolyCounters.Period(kind);
            if (period < Bits)
            {
                float x = trainLeft + period * cell;
                g.DrawLine(badP, x, y - 4f, x, y + height + 5f);
                Label(g, "REPEATS", x + 4f, y + height + 3f, LabColors.Bad, SmallFont);
            }

            // The channel's own sampling rate, ticked beneath: one every AUDF + 1
            // steps. Turning AUDF moves these without touching a cell above them.
            for (int i = 0; i < Bits; i += _audf + 1)
            {
                float x = trainLeft + i * cell + cell / 2f;
                g.DrawLine(coolP, x, y + height + 2f, x, y + height + 9f);
            }
        }
    }
}
