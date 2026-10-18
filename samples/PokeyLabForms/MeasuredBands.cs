namespace PokeyLabForms;

/// <summary>
/// What the reference captures measured, for the player's shot.
///
/// From <c>docs/ARCADE-SOURCE.md</c> section 9: a matched filter over candidate sweeps
/// and a frame-by-frame ridge trace, agreeing across both fire clips. <b>The bands are
/// wider than the scatter on purpose</b> -- lossy video captures with cabinet rumble
/// cannot adjudicate the last few Hz, but they catch an error of a factor, which is the
/// error that was actually there.
///
/// Shared by the sweep graph, which draws them as a target box, and the readout, which
/// judges four numbers against them. One copy so the box and the verdicts can never
/// disagree about where the answer is.
/// </summary>
public static class MeasuredBands
{
    public static readonly (float Low, float High) Start = (900f, 1100f);
    public static readonly (float Low, float High) End = (140f, 195f);
    public static readonly (float Low, float High) Seconds = (0.136f, 0.184f);
    public static readonly (float Low, float High) Fall = (5.7f, 6.6f);

    public static bool Holds((float Low, float High) band, double value)
        => value >= band.Low && value <= band.High;
}
