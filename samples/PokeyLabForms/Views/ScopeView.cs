using System;
using System.Drawing;
using System.Windows.Forms;

namespace PokeyLabForms.Views;

/// <summary>
/// The chip's actual output, drawn as a waveform.
///
/// <b>This is the view that makes the rest of the window mean something.</b> A POKEY
/// channel's output is one flip-flop -- high or low, nothing between -- and every
/// sound it makes is that bit toggling, or not toggling, at moments the divider picks.
/// The channel strips set when those moments are and what gates them; this shows the
/// result, and the two together are the whole chip.
///
/// Worth watching while turning a knob rather than after: a pure tone is a square, the
/// 5-bit poly is a short repeating pattern you can almost count, and the 17-bit poly
/// never visibly repeats at all. Those three pictures explain the distortion field
/// faster than the label naming it does.
/// </summary>
public sealed class ScopeView : InstrumentView
{
    /// <summary>Window drawn, in samples. About 7ms at 44.1kHz -- a handful of cycles
    /// of a mid-range tone, which is enough to see the shape without the trace turning
    /// into a solid block.</summary>
    public const int Window = 320;

    private readonly float[] _trace = new float[Window];
    private bool _ready;
    private float _gain = 4f;
    private int _span = Window;

    /// <summary>
    /// Vertical scale, as a multiplier on full scale.
    ///
    /// <b>x4 is the default because it is the reciprocal of the one-channel ceiling, not
    /// because it looks nice.</b> The mix sums four channels and divides by all four at
    /// full volume -- <c>4 x 15 = 60</c> -- so one voice alone can never exceed a quarter
    /// of full scale. Drawn against full scale it therefore uses a quarter of the height
    /// however loud it is, which is the honest picture and a waste of the panel. At x4 a
    /// single channel at volume 15 fills the frame exactly, and x1 is still there for
    /// when all four are playing.
    ///
    /// <b>Deliberately manual rather than auto-fitting.</b> A scope that rescales itself
    /// to whatever it is showing is easier to read and tells you nothing: the trace height
    /// stops meaning amplitude, and turning a volume down would leave the picture
    /// identical. Fixed steps keep the height meaningful, which is what a volts-per-division
    /// control has always been for.
    /// </summary>
    public float Gain
    {
        get => _gain;
        set
        {
            float wanted = Math.Clamp(value, 1f, 16f);
            if (Math.Abs(_gain - wanted) < 0.001f) return;
            _gain = wanted;
            Invalidate();
        }
    }

    /// <summary>
    /// How many of the captured samples are drawn, stretched across the full width.
    ///
    /// The capture is always <see cref="Window"/> samples; this only narrows what is
    /// plotted, so zooming costs nothing upstream and <c>ScopeTrace</c> -- which is shared
    /// with the other front end -- does not have to know the scope has a zoom at all.
    /// </summary>
    public int Span
    {
        get => _span;
        set
        {
            int wanted = Math.Clamp(value, 16, Window);
            if (_span == wanted) return;
            _span = wanted;
            Invalidate();
        }
    }

    /// <summary>Sample rate, for the time readout only. A plain number handed in, so this
    /// view still knows nothing about the chip or the session.</summary>
    public int SampleRate { get; set; } = 44100;

    /// <summary>Hands in a freshly triggered window. Called from the UI thread's
    /// refresh, so the array is never read by the painter while being written.</summary>
    public void Show(float[] trace, bool ready)
    {
        Array.Copy(trace, _trace, Window);
        _ready = ready;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;

        float left = 8f, right = Width - 8f;
        float top = 8f, bottom = Height - 8f;
        float midY = (top + bottom) / 2f;

        using (var rule = new Pen(LabColors.Rule, 1.2f))
            g.DrawRectangle(rule, left, top, right - left, bottom - top);

        // Silence sits on the centre line rather than at the bottom, because the chip
        // is DC-blocked before it reaches here.
        using (var centre = new Pen(LabColors.SurfaceDim, 1f))
            g.DrawLine(centre, left, midY, right, midY);

        if (!_ready)
        {
            Label(g, "NO AUDIO - NOTHING TO TRACE", left + 12f, midY - 8f, LabColors.InkFaint);
            return;
        }

        float halfHeight = (bottom - top) / 2f - 6f;
        float step = (right - left) / (_span - 1);

        // Clamped after the gain, so a trace driven past full scale flattens against the
        // frame instead of being drawn outside it. That flattening is information: it says
        // the picture is now bigger than the scale can show.
        var points = new PointF[_span];
        for (int i = 0; i < _span; i++)
            points[i] = new PointF(left + i * step,
                                   midY - Math.Clamp(_trace[i] * _gain, -1f, 1f) * halfHeight);

        using var trace = new Pen(LabColors.Accent, 1.6f);
        g.DrawLines(trace, points);

        // The scale, in the corner, the way an instrument states its own settings -- and
        // for the same reason the sweep graph labels its axes: a picture whose scale is
        // only knowable from a control somewhere else is a picture you can misread.
        string scale = $"×{_gain:0.#}   {_span * 1000.0 / Math.Max(1, SampleRate):0.0} MS";
        LabelRight(g, scale, right - 6f, bottom - 16f, LabColors.InkFaint, SmallFont);
    }
}
