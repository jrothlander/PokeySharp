using System;
using System.Collections.Generic;
using System.Threading;

namespace Pokey.Music;

/// <summary>
/// A song rendered for somewhere that plays audio files: the two files a game needs - the
/// intro, played once, and the loop, played forever after it - or, for a song that ends,
/// the one file.
///
/// <b>One copy, for everything that exports.</b> It was written inside tools/soundexport;
/// PokeyTracker's File > Export audio made it the second caller, so it moved here, the way
/// the song model and the driver did. It renders through <see cref="MusicDriver"/> and
/// nothing else, so an exported song is the samples every player of it hears.
///
/// <b>The loop is cut from the second time round, not the first.</b> The first pass begins
/// with whatever the intro left ringing; every later pass begins with the loop's own tail,
/// which is what the loop file must start with if its end is to run back into its
/// beginning without a seam. So the song is rendered once through and then round twice
/// more, and the loop is the stretch between the second and third arrivals at the loop
/// line - whose end is, sample for sample, what really preceded the third.
///
/// What cannot be made identical is measured instead. The chip never stops: its dividers
/// and noise counters free-run, so each time round a note starts at a different point in
/// its cycle and a noise drum is a slightly different texture. Compared sample for sample,
/// two passes therefore differ by as much as the music itself - two square waves a
/// fraction of a cycle apart - while sounding the same. So the seam is judged the way it
/// is heard: by the size of the jump at the join, against the jumps the waveform makes on
/// its own; and by whether the same notes are sounding at the same loudness either side,
/// compared as 10ms loudness envelopes that do not care where in its cycle a wave is.
/// </summary>
public sealed class SongExport
{
    public const int SampleRate = MusicDriver.SampleRate;

    /// <summary>The intro, played once before the loop; null when the song loops from its
    /// first line, and for a song that ends.</summary>
    public float[] Intro { get; private init; }

    /// <summary>The loop, cut to run back into its own start; null for a song that ends.</summary>
    public float[] Loop { get; private init; }

    /// <summary>The whole of a song that ends, trimmed after its last audible sample; null
    /// for a song that loops.</summary>
    public float[] Once { get; private init; }

    /// <summary>A song that loops, played once through from its first line to the end of its
    /// last, as if it ended there - its last notes left to ring out and the silence after
    /// them trimmed. The version to listen to, rather than to loop. Null unless asked for,
    /// and for a song that ends, whose <see cref="Once"/> is already the whole song.</summary>
    public float[] Full { get; private init; }

    /// <summary>How far past the loop line the cut was made, in samples - see
    /// <see cref="BestCut"/>.</summary>
    public int Cut { get; private init; }

    /// <summary>The jump from the loop's last sample to its first, as the file plays it.</summary>
    public float Join { get; private init; }

    /// <summary>The jump the song itself made at that moment, playing straight through
    /// into its third pass. The loop line usually opens on an attack, so this is often
    /// large; a join no bigger adds nothing the song did not already do.</summary>
    public float SongJump { get; private init; }

    /// <summary>The sample-to-sample jump the loop exceeds only one time in a thousand -
    /// the size of the square-wave edges the chip makes constantly.</summary>
    public float Edges { get; private init; }

    /// <summary>How far the first 200ms of the next pass differs in loudness from the loop
    /// file's own start, as a share of it.</summary>
    public double Seam { get; private init; }

    /// <summary>The same for the first pass, after the intro; 0 when there is no intro.</summary>
    public double Entry { get; private init; }

    /// <summary>How much two passes differ halfway through anyway, far from any cut - the
    /// floor <see cref="Seam"/> and <see cref="Entry"/> should be judged against.</summary>
    public double Floor { get; private init; }

    /// <summary>The files this export is, named from <paramref name="stem"/>:
    /// <c>stem-intro.wav</c> and <c>stem-loop.wav</c>, only the loop for a song that loops
    /// from its first line, <c>stem-full.wav</c> when the full version was rendered, and
    /// <c>stem.wav</c> for a song that ends.</summary>
    public IEnumerable<(string FileName, float[] Samples)> Files(string stem)
    {
        if (Once != null) { yield return ($"{stem}.wav", Once); yield break; }
        if (Intro != null) yield return ($"{stem}-intro.wav", Intro);
        if (Loop != null) yield return ($"{stem}-loop.wav", Loop);
        if (Full != null) yield return ($"{stem}-full.wav", Full);
    }

    /// <summary>A song's name as a file name: "THE ENTERTAINER" to <c>the-entertainer</c>.</summary>
    public static string Slug(string name)
        => name.ToLowerInvariant().Replace(' ', '-').Replace(",", "").Replace("'", "");

    /// <summary>
    /// Renders a song for export. Check <see cref="Tune.Problems"/> first: every problem is
    /// a cell or a line the reader skipped, so a song with any would export as a song that
    /// is not the one written.
    /// </summary>
    /// <param name="progress">Told how far through the render is, 0 to 1, now and then.</param>
    /// <param name="cancel">Checked between blocks; a cancelled render throws
    /// <see cref="OperationCanceledException"/>.</param>
    /// <param name="loop">Render the intro and the loop. A song that ends ignores this and
    /// the next: it is always its one file.</param>
    /// <param name="full">Render <see cref="Full"/>, the song once through.</param>
    public static SongExport Render(Tune tune, Action<double> progress = null, CancellationToken cancel = default,
                                    bool loop = true, bool full = false)
    {
        tune = tune.Heard();   // lines and channels switched off are not in the files
        if (tune.Lines == 0) throw new ArgumentException("The song has no lines to play.", nameof(tune));

        if (tune.Ends) return new SongExport { Once = RenderToEnd(tune, progress, cancel) };

        // The progress bar's share for each render, by how much sound each makes: the loop
        // render plays the song and then the loop twice more, the full one the song once.
        var (introSeconds, loopSeconds) = Seconds(tune);
        double loopWork = loop ? introSeconds + 2 * loopSeconds : 0, fullWork = full ? introSeconds + loopSeconds : 0;
        double split = loopWork / Math.Max(1e-9, loopWork + fullWork);

        float[] Whole()
        {
            if (!full) return null;
            // The same song read back from its own text with its ending changed - a copy, so
            // the caller's song is not touched. Written text reads back as the same samples;
            // the harness asserts that for every song.
            var once = SongFile.Parse(SongFile.Write(tune).Split('\n'), tune.Source ?? "full.song");
            once.Ends = true;
            return RenderToEnd(once, progress == null ? null : f => progress(split + f * (1 - split)), cancel);
        }
        if (!loop) return new SongExport { Full = Whole() };

        var (song, starts) = RenderLoopStarts(tune, passes: 3, progress == null ? null : f => progress(f * split), cancel);
        var whole = Whole();

        // Cut a few milliseconds into the loop line rather than exactly on it, at whichever
        // sample makes both joins smallest. Every cut an exact loop-length apart loops as
        // well as any other, so this costs nothing but those few milliseconds moving from
        // the start of the loop to the end of the intro.
        int shift = BestCut(song, starts);
        int intro = starts[0] + shift, loopFrom = starts[1] + shift, loopTo = starts[2] + shift;
        if (starts[0] == 0) intro = 0;

        var cut = song.GetRange(loopFrom, loopTo - loopFrom).ToArray();
        return new SongExport
        {
            Intro = intro > 0 ? song.GetRange(0, intro).ToArray() : null,
            Loop = cut,
            Full = whole,
            Cut = shift,
            Join = Math.Abs(cut[^1] - cut[0]),
            SongJump = Math.Abs(song[loopTo] - song[loopTo - 1]),
            Edges = EdgePercentile(cut, 0.999),
            // What really came next, the third time round, against what the file starts with.
            Seam = EnvelopeMismatch(song, loopTo, loopFrom),
            Entry = intro > 0 ? EnvelopeMismatch(song, intro, loopFrom) : 0,
            Floor = EnvelopeMismatch(song, loopFrom + cut.Length / 2, starts[0] + cut.Length / 2),
        };
    }

    /// <summary>How long the song takes to reach its loop line, and how long a pass round
    /// the loop is, from its lines' speeds - what a progress bar needs before any sample
    /// has been rendered.</summary>
    public static (double Intro, double Loop) Seconds(Tune tune)
    {
        tune = tune.Heard();
        double intro = 0, loop = 0;
        for (int line = 0; line < tune.Lines; line++)
        {
            double s = tune.FramesPerRowAt(line) * tune.RowsPerLine / MusicDriver.FrameHz;
            if (!tune.Ends && line >= tune.LoopLine) loop += s;
            else intro += s;
        }
        return (intro, loop);
    }

    /// <summary>
    /// How far past the loop line to cut, within 5ms: the sample where the worse of the two
    /// joins is smallest - the loop's end into its start, and the intro's end into the
    /// loop's start. Measured against what really followed each, so it prefers the cut
    /// where the file's joins look most like the song's own continuation.
    /// </summary>
    private static int BestCut(List<float> song, List<int> starts)
    {
        int best = 0;
        float bestJoin = float.MaxValue;

        for (int k = 0; k < SampleRate / 200; k++)
        {
            float start = song[starts[1] + k];
            float wrap = Math.Abs(song[starts[2] + k - 1] - start);
            float entry = starts[0] > 0 ? Math.Abs(song[starts[0] + k - 1] - start) : 0f;
            float worst = Math.Max(wrap, entry);

            if (worst < bestJoin) { bestJoin = worst; best = k; }
        }

        return best;
    }

    /// <summary>The sample-to-sample jump a waveform exceeds only this rarely - the size of
    /// its own edges, which a square-wave chip makes constantly.</summary>
    private static float EdgePercentile(float[] samples, double percentile)
    {
        var steps = new float[samples.Length - 1];
        for (int i = 1; i < samples.Length; i++) steps[i - 1] = Math.Abs(samples[i] - samples[i - 1]);
        Array.Sort(steps);
        return steps[(int)(percentile * (steps.Length - 1))];
    }

    /// <summary>
    /// How different two stretches sound, ignoring where each wave is in its cycle: the
    /// first 200ms of each as twenty 10ms RMS windows, and the summed difference of those
    /// as a share of the second stretch's own.
    /// </summary>
    private static double EnvelopeMismatch(List<float> samples, int a, int b)
    {
        const int Windows = 20, Window = SampleRate / 100;
        double diff = 0, level = 0;
        for (int w = 0; w < Windows; w++)
        {
            double ea = Rms(samples, a + w * Window, Window), eb = Rms(samples, b + w * Window, Window);
            diff += Math.Abs(ea - eb);
            level += eb;
        }
        return level == 0 ? 0 : diff / level;
    }

    /// <summary>
    /// Renders a song until it has arrived at its loop line <paramref name="passes"/>
    /// times, and returns every sample and the sample each arrival began on.
    ///
    /// Rendered in blocks shorter than a frame, so no frame can start and end between two
    /// looks at the driver - each block brings at most one new frame, and the driver's
    /// history says exactly which sample it began on.
    /// </summary>
    private static (List<float> Song, List<int> Starts) RenderLoopStarts(Tune tune, int passes,
        Action<double> progress, CancellationToken cancel)
    {
        var driver = new MusicDriver(tune);
        var song = new List<float>();
        var starts = new List<int>();
        var block = new float[256];
        long lastFrame = -1;
        int lastLine = -1, lastRow = -1;

        // Past the last arrival by a little, so what follows it can be measured.
        int tail = SampleRate / 2;

        var (introSeconds, loopSeconds) = Seconds(tune);
        double expected = Math.Max(1, (introSeconds + (passes - 1) * loopSeconds) * SampleRate + tail);

        while (starts.Count < passes || song.Count < starts[^1] + tail)
        {
            driver.Render(block, block.Length);
            song.AddRange(block);
            if (song.Count % (SampleRate / 2) < block.Length)
            {
                cancel.ThrowIfCancellationRequested();
                progress?.Invoke(Math.Min(1, song.Count / expected));
            }
            if (starts.Count >= passes) continue;

            var frame = driver.At(driver.SamplesRendered - 1);
            if (frame.Number == lastFrame) continue;

            // The first frame of the loop line: row 0 of it, arrived at from somewhere else.
            bool arrived = frame.Line == tune.LoopLine && frame.Row == 0
                           && !(lastLine == frame.Line && lastRow == 0);
            if (arrived) starts.Add((int)frame.StartSample);

            lastFrame = frame.Number;
            lastLine = frame.Line;
            lastRow = frame.Row;
        }

        progress?.Invoke(1);
        return (song, starts);
    }

    /// <summary>
    /// A song that ends, rendered until it has: through its last line, then on while its
    /// released notes fade, then cut after the last sample anyone could hear.
    ///
    /// <b>Trimmed, not padded.</b> A jingle is played on an event, and silence on the end
    /// of the file is a delay before whatever the game plays next. The cut is 20ms past the
    /// last sample above -54dB, with the final 10ms faded so the cut cannot click.
    /// </summary>
    private static float[] RenderToEnd(Tune tune, Action<double> progress, CancellationToken cancel)
    {
        var driver = new MusicDriver(tune);
        var song = new List<float>();
        var block = new float[1024];
        double expected = Math.Max(1, (Seconds(tune).Intro + 1) * SampleRate);

        // Through the end, and a second more for the fades - longer than any release here
        // takes, and capped so a song whose notes never release cannot run forever.
        int afterEnd = -1;
        while (afterEnd < SampleRate && song.Count < SampleRate * 600)
        {
            driver.Render(block, block.Length);
            song.AddRange(block);
            if (driver.Ended) afterEnd = afterEnd < 0 ? 0 : afterEnd + block.Length;
            if (song.Count % (SampleRate / 2) < block.Length)
            {
                cancel.ThrowIfCancellationRequested();
                progress?.Invoke(Math.Min(1, song.Count / expected));
            }
        }

        int last = song.Count - 1;
        while (last > 0 && Math.Abs(song[last]) < 0.002f) last--;

        int length = Math.Min(song.Count, last + SampleRate / 50);
        int fade = SampleRate / 100;
        var result = song.GetRange(0, length).ToArray();
        for (int i = 0; i < fade && i < result.Length; i++)
            result[result.Length - 1 - i] *= i / (float)fade;
        progress?.Invoke(1);
        return result;
    }

    /// <summary>RMS of <paramref name="count"/> samples from <paramref name="at"/>.</summary>
    private static double Rms(List<float> samples, int at, int count)
    {
        double sum = 0;
        int n = 0;
        for (int i = 0; i < count && at + i < samples.Count; i++, n++)
            sum += samples[at + i] * (double)samples[at + i];
        return n == 0 ? 0 : Math.Sqrt(sum / n);
    }
}
