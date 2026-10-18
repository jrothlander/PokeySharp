namespace Speech;

/// <summary>
/// The settings that make one voice sound different from another.
///
/// <b>These were constants until something needed two of them.</b> Pitch, formant
/// bandwidths, the glottal rolloff and a formant scale are properties of a speaker, not
/// of the language, and hard-coding them meant the synthesiser could only ever be one
/// person. Gathering them into a record costs a parameter on <c>Render</c> and makes
/// "voice" a thing you can name, compare and fit.
///
/// <b>Fitting is the point.</b> <see cref="Deliberate"/> is not a set of numbers anyone
/// chose by ear -- every one of them came off a measured reference through
/// <c>tools/voicedump</c> and the analysis script, the same way ARCADE-SOURCE section 9
/// settled the shot sound. Guessing at a voice is exactly how this project has been
/// wrong before.
/// </summary>
public readonly record struct VoiceProfile(
    string Name,
    double PitchHz,
    double Speed,
    double AccentScale,
    double F1Scale,
    double F2Scale,
    double F3Scale,
    double Bandwidth1,
    double Bandwidth2,
    double Bandwidth3,
    double GlottalTiltHz)
{
    /// <summary>
    /// The voice everything was built against.
    ///
    /// A generic adult male: 120Hz, unscaled formants, and the bandwidths that have
    /// been in <c>FormantSynth</c> since it was written. Kept exactly as it was so that
    /// nothing already tuned or asserted against it moves.
    /// </summary>
    public static readonly VoiceProfile Default = new(
        Name: "Default",
        PitchHz: 120.0,
        Speed: 1.0,
        AccentScale: 1.0,
        F1Scale: 1.0, F2Scale: 1.0, F3Scale: 1.0,
        Bandwidth1: 60.0, Bandwidth2: 90.0, Bandwidth3: 150.0,
        GlottalTiltHz: 500.0);

    /// <summary>
    /// Low, slow and wide: fitted to a measured reference rather than chosen.
    ///
    /// Every figure below is the measurement, with the default it replaces beside it:
    ///
    /// <list type="bullet">
    /// <item><b>Pitch 91Hz</b> (was 120). Median F0 of the reference, once the search
    /// range was narrowed enough to stop autocorrelation reporting third and fourth
    /// harmonics as the fundamental.</item>
    /// <item><b>Speed 0.70</b> (was 1.0). The reference runs 2.7 syllables a second
    /// against our 3.8. <b>This is the opposite of what guessing predicted</b> -- the
    /// synthesiser is slow against conversational English and was still too fast for
    /// this.</item>
    /// <item><b>F3 x0.83</b>. Median F3 of 2054Hz against our 2473. F1 and F2 measured
    /// close enough to leave alone, which is itself worth knowing: the difference is
    /// specifically in the third formant.</item>
    /// <item><b>F2 bandwidth 250Hz</b> (was 90). Ours is far more resonant than the
    /// reference, which measured around 262Hz. <b>Treat this one as a direction, not a
    /// target</b> -- LPC bandwidth estimates are the noisiest thing the analysis
    /// produces.</item>
    /// <item><b>Accent x2.0</b>. The reference's pitch moves three times as much as
    /// ours (30% of mean against 10.6%). Also contrary to the guess, which was that
    /// this voice would be flatter. <b>The least trustworthy of the five</b>: some of
    /// that spread may be residual tracking error rather than a speaker.</item>
    /// </list>
    ///
    /// Named for what it is rather than for what it was fitted to. The reference is a
    /// film soundtrack, and a preset claiming to be it would be claiming more than a
    /// five-parameter fit can deliver.
    /// </summary>
    public static readonly VoiceProfile Deliberate = new(
        Name: "Low and deliberate",
        PitchHz: 91.0,
        Speed: 0.70,
        AccentScale: 2.0,
        F1Scale: 1.0, F2Scale: 1.0, F3Scale: 0.83,
        Bandwidth1: 60.0, Bandwidth2: 250.0, Bandwidth3: 150.0,
        GlottalTiltHz: 500.0);

    /// <summary>A higher, brighter voice. Not fitted to anything -- it exists to show
    /// the profile mechanism does something, and because a formant scale above 1 is the
    /// cheap approximation of a shorter vocal tract.</summary>
    public static readonly VoiceProfile Bright = new(
        Name: "Higher and brighter",
        PitchHz: 175.0,
        Speed: 1.1,
        AccentScale: 1.2,
        F1Scale: 1.08, F2Scale: 1.10, F3Scale: 1.10,
        Bandwidth1: 70.0, Bandwidth2: 110.0, Bandwidth3: 170.0,
        GlottalTiltHz: 700.0);

    public static readonly VoiceProfile[] All = { Default, Deliberate, Bright };
}
