using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PokeyLabForms.Views;

/// <summary>
/// Shared plumbing for the three black-panel displays.
///
/// Nothing about what any of them draws -- only the two things all three need and
/// would otherwise each get subtly wrong. <b>Double buffering</b>, because these
/// repaint thirty times a second and an unbuffered panel flickers, which on a black
/// background looks like the signal rather than the window. And <b>a monospaced
/// label font</b>, so a number redrawn every frame does not shuffle sideways as its
/// digits change width -- the same reason the arcade's own readouts were fixed-pitch.
/// </summary>
public class InstrumentView : Panel
{
    protected readonly Font LabelFont = new("Consolas", 8.25f, FontStyle.Regular);
    protected readonly Font SmallFont = new("Consolas", 7.5f, FontStyle.Regular);

    public InstrumentView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw, true);

        BackColor = LabColors.Background;
        ForeColor = LabColors.Ink;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // Lines here are diagonal more often than not -- a waveform, a sweep curve --
        // and a vector monitor had no pixel grid to alias against, so smoothing them is
        // closer to the original than leaving them hard-edged would be.
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        base.OnPaint(e);
    }

    /// <summary>Draws a label at a point, in the display's own type.</summary>
    protected void Label(Graphics g, string text, float x, float y, Color colour, Font font = null)
    {
        using var brush = new SolidBrush(colour);
        g.DrawString(text, font ?? LabelFont, brush, x, y);
    }

    protected void LabelRight(Graphics g, string text, float right, float y, Color colour,
                              Font font = null)
    {
        font ??= LabelFont;
        float width = g.MeasureString(text, font).Width;
        Label(g, text, right - width, y, colour, font);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            LabelFont.Dispose();
            SmallFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
