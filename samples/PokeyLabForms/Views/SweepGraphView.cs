using System;
using System.Drawing;
using System.Windows.Forms;

namespace PokeyLabForms.Views;

/// <summary>
/// The sweep, tick by tick, with the measured answer as a target box.
///
/// <b>Plotted from values the caller took off the envelope itself</b>, not from a
/// formula reproduced here -- the same <c>StepEnvelope.AudfAt</c> arithmetic that
/// drives the registers, so the staircase on screen is the one being played rather
/// than an idealisation of it. That is also why this view holds no reference to the
/// chip or the envelope: it is handed an array of pitches and knows nothing else,
/// which keeps every lock in <see cref="ChipSession"/> and none in a paint handler.
/// </summary>
public sealed class SweepGraphView : InstrumentView
{
    private double[] _pitch = { 0.0 };
    private double _totalSeconds = 1.0;
    private double _playSeconds;
    private bool _playing;
    private bool _showMeasured = true;

    /// <summary>
    /// Whether the measured target box is drawn — and, just as importantly, whether the
    /// axes reserve room for it.
    ///
    /// <b>The two go together.</b> The scaling below deliberately keeps the box on screen
    /// even when the current settings are nowhere near it, because the disagreement is the
    /// thing being looked at. But that costs range: a sound that is quiet and short is
    /// squashed into a corner of a plot sized for Tempest's shot. With the box switched off
    /// there is nothing to keep on screen, so the axes fit the sweep itself and a sound
    /// unrelated to the reference captures gets the whole plot. Hiding the box while
    /// keeping its scaling would give the worst of both.
    /// </summary>
    public bool ShowMeasured
    {
        get => _showMeasured;
        set
        {
            if (_showMeasured == value) return;
            _showMeasured = value;
            Invalidate();
        }
    }

    /// <summary>
    /// Hands in a freshly sampled curve.
    ///
    /// <paramref name="pitch"/> is a run of samples spread evenly across
    /// <paramref name="totalSeconds"/> -- <b>not one entry per envelope tick</b>. Ticks
    /// would be the obvious contract and it is the wrong one: a chain is capped at
    /// sixteen steps but each can span <c>inner x outer</c> up to 65025 ticks, so an
    /// envelope the user is free to build runs to a million of them, and there are only
    /// a few hundred pixels to draw into. The caller subsamples; this only needs to know
    /// how much time the samples cover.
    /// </summary>
    public void Show(double[] pitch, double totalSeconds, double playSeconds, bool playing)
    {
        _pitch = pitch != null && pitch.Length > 0 ? pitch : new double[] { 0.0 };
        _totalSeconds = totalSeconds > 0 ? totalSeconds : 1.0;
        _playSeconds = playSeconds;
        _playing = playing;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;

        float left = 52f, right = Width - 12f;
        float top = 22f, bottom = Height - 26f;
        if (right <= left || bottom <= top) return;

        int samples = _pitch.Length;

        // The curve's own peak, rather than its first sample. They are the same thing for
        // a falling sweep, which is nearly every Tempest voice -- but a rising one puts its
        // highest pitch at the end, and scaling to the start would run it off the top.
        double peakHz = _pitch[0];
        for (int i = 1; i < samples; i++)
            if (_pitch[i] > peakHz) peakHz = _pitch[i];

        // With the target box shown, scaled so it is always on screen even when the
        // current settings are nowhere near it -- the disagreement is the thing being
        // looked at, so it must not be off the edge. With it hidden, scaled to the sweep,
        // which is the whole reason the switch is worth having. See ShowMeasured.
        double maxHz = _showMeasured
            ? Math.Max(peakHz, MeasuredBands.Start.High * 1.15) * 1.08
            : peakHz * 1.08;

        double maxSeconds = _showMeasured
            ? Math.Max(_totalSeconds, MeasuredBands.Seconds.High * 1.15) * 1.08
            : _totalSeconds;

        if (maxHz <= 0) maxHz = 1;
        if (maxSeconds <= 0) maxSeconds = 1;

        PointF At(double t, double hz) => new(
            left + (float)(t / maxSeconds) * (right - left),
            bottom - (float)(hz / maxHz) * (bottom - top));

        using (var rule = new Pen(LabColors.Rule, 1.4f))
        {
            g.DrawLine(rule, left, top, left, bottom);
            g.DrawLine(rule, left, bottom, right, bottom);
        }

        LabelRight(g, $"{maxHz:0} HZ", left - 6f, top - 6f, LabColors.InkFaint, SmallFont);
        LabelRight(g, $"{maxSeconds:0.000} S", right, bottom + 6f, LabColors.InkFaint, SmallFont);

        // The measured box: land the curve's two ends in its two corners.
        if (_showMeasured)
        {
            var topLeft = At(MeasuredBands.Seconds.Low, MeasuredBands.Start.High);
            var bottomRight = At(MeasuredBands.Seconds.High, MeasuredBands.End.Low);
            using (var good = new Pen(Color.FromArgb(190, LabColors.Good), 1.4f))
                g.DrawRectangle(good, topLeft.X, topLeft.Y,
                                bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
            Label(g, "MEASURED", topLeft.X + 4f, topLeft.Y - 15f, LabColors.Good, SmallFont);
        }

        // Drawn as a staircase rather than a curve, because that is what it is: the
        // ISR rewrites the divider on a tick and it holds until the next one.
        if (samples > 1)
        {
            using var accent = new Pen(LabColors.Accent, 2.2f);
            var previous = At(0.0, _pitch[0]);
            for (int i = 1; i < samples; i++)
            {
                var next = At(i / (double)(samples - 1) * _totalSeconds, _pitch[i]);
                var corner = new PointF(next.X, previous.Y);
                g.DrawLine(accent, previous, corner);
                g.DrawLine(accent, corner, next);
                previous = next;
            }
        }

        // A playhead while it sounds, so a long envelope can be followed by eye.
        if (_playing)
        {
            float x = At(Math.Clamp(_playSeconds, 0.0, _totalSeconds), 0.0).X;
            using var bad = new Pen(LabColors.Bad, 1.6f);
            g.DrawLine(bad, x, top, x, bottom);
        }
    }
}
