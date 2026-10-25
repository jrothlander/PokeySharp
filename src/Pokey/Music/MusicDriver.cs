using System;
using System.Collections.Generic;
using Pokey;

namespace Pokey.Music;

/// <summary>
/// An Atari 800 music driver: once every vertical blank, write nine bytes to the POKEY.
///
/// <b>This is how music was played on the machine.</b> The CPU was busy running the game,
/// so the music ran in the vertical blank interrupt — sixty times a second, whatever the
/// game was doing — and each time it looked up where every channel was in its note and
/// its instrument, and wrote AUDF and AUDC for all four channels, then AUDCTL. Between
/// those writes the chip simply holds what it was told. There is no other mechanism: every
/// drum, every attack, every vibrato and every chord is those nine bytes changing once a
/// frame.
///
/// <b>One chip, all four channels.</b> Unlike the sound pages, which render each effect on a
/// chip of its own, everything here goes through a single <see cref="PokeyChip"/>, so the
/// channels share the mix, the headroom and the polynomial counters exactly as they did on
/// the hardware.
///
/// Free of MonoGame, like <see cref="SongLibrary"/>: it turns a song into floats, and the
/// page decides what plays them.
/// </summary>
public sealed class MusicDriver
{
    public const int SampleRate = 44100;
    public const int Channels = PokeyChip.Channels;

    /// <summary>
    /// An NTSC Atari's frame rate: 262 scanlines of 114 machine cycles, off the same
    /// crystal as the POKEY. Which makes it 59.92Hz rather than 60 — and 114 is also the
    /// divider behind POKEY's "15kHz" clock, which is the scanline rate for the same reason.
    /// </summary>
    public static readonly double FrameHz
        = PokeyClocks.Atari800NtscHz / (PokeyClocks.VerySlowDivider * 262.0);

    /// <summary>
    /// Output gain. Measured, not derived.
    ///
    /// <see cref="PokeyChip.Mix"/> already scales by all four channels at full volume, so
    /// unlike a lone sound effect — which <see cref="SoundRender"/> multiplies by 8 — a
    /// song using every channel needs very little. This one peaks at 0.75 before the gain,
    /// on the frames where every channel's volume jumps at once; 1.25 puts that at 0.94
    /// and clips nothing. The harness that set it renders the whole song and counts.
    /// </summary>
    public float Gain { get; init; } = 1.25f;

    /// <summary>What a channel is in the middle of. The driver's own RAM, in effect —
    /// which note, which instrument, how many frames into it.</summary>
    private sealed class Channel
    {
        public Instrument Instrument;
        public int[] Notes = Array.Empty<int>();
        public int Age;
        public bool Released;
        public int Volume;
    }

    /// <summary>
    /// What the driver wrote on one frame, and where in the output that frame starts.
    ///
    /// <b>Kept so the page can show what is being heard rather than what is being
    /// rendered.</b> Audio is queued a few blocks ahead of the speaker, so the driver is
    /// always slightly in the future; the page asks for the frame under the sample that is
    /// playing now.
    /// </summary>
    public sealed class Frame
    {
        /// <summary>The song this frame belongs to. Carried per frame because a song
        /// change reaches the speaker a few blocks after the driver makes it, and the
        /// page must read the frames still queued against the song they came from.</summary>
        public Tune Tune;

        public long StartSample;
        public long Number;
        public int Line, Row;
        public int Audctl;

        /// <summary>True once a song that ends has played its last line — the frame's
        /// channels are only fading now, and no more rows will be read.</summary>
        public bool Ended;
        public readonly int[] Audf = new int[Channels];
        public readonly int[] Audc = new int[Channels];

        /// <summary>The whole divider each channel is counting: AUDF for one channel, both
        /// bytes for the high half of a pair, and -1 for a low half, which has none of its
        /// own.</summary>
        public readonly int[] Divider = new int[Channels];

        /// <summary>The note each channel is on, or -1: a drum has none, and neither does
        /// silence.</summary>
        public readonly int[] Note = new int[Channels];

        /// <summary>The instrument each channel is playing, or null.</summary>
        public readonly Instrument[] Instrument = new Instrument[Channels];

        /// <summary>
        /// The notes each channel is holding down on this frame - all of a chord, not the one
        /// note of the arpeggio <see cref="Note"/> shows - and empty once a note is released,
        /// has faded to nothing, is a drum, or is muted. With <see cref="Struck"/>, what another
        /// voice needs to play the song's notes as a keyboard would: which keys are down, and
        /// when each was pressed.
        /// </summary>
        public readonly int[][] Held = new int[Channels][];

        /// <summary>Whether a note started on this channel this frame - a key pressed, even
        /// the same key again.</summary>
        public readonly bool[] Struck = new bool[Channels];
    }

    private readonly PokeyChip _chip = new() { BaseClockHz = PokeyClocks.Atari800NtscHz };
    private readonly Channel[] _channels = new Channel[Channels];

    private int _line, _row, _frameInRow;
    private long _frameNumber;
    private double _samplesToNextFrame;
    private int _seekTo = -1;
    private Tune _loading, _edited;
    private bool _ended;

    // The last second or so of frames, and of output, so the page can look back to the
    // one being heard. Preallocated and reused: this runs on every frame, and a demo that
    // allocates at sixty hertz to draw a table is a poor advertisement for a chip that
    // did all this in 256 bytes.
    private readonly Frame[] _history = new Frame[64];
    private int _historyNext, _historyCount;
    private readonly float[] _scope = new float[16384];

    /// <summary>The song being played, or about to be — see <see cref="Load"/>.</summary>
    public Tune Tune => _loading ?? _edited ?? _playing;
    private Tune _playing;

    /// <summary>A muted channel still runs — the driver keeps its place — but is written
    /// with volume zero, which is all muting ever was on this chip.</summary>
    public bool[] Muted { get; } = new bool[Channels];

    /// <summary>A channel the chip leaves silent while it is still played: its notes are read,
    /// stepped and reported in each frame's <see cref="Frame.Held"/> as usual, for another
    /// voice - a sampled piano - to sound them instead. Unlike <see cref="Muted"/>, the
    /// notes still count as played.</summary>
    public bool[] Voiceless { get; } = new bool[Channels];

    /// <summary>
    /// Reads the row it is on once, then stays there: the notes play on by their own
    /// instruments and no further row is read. For auditioning a note in an editor - the
    /// note typed, sounding as the song would sound it, without the rows after it.
    /// </summary>
    public bool Hold { get; set; }

    /// <summary>How many samples have come out of <see cref="Render"/>, ever.</summary>
    public long SamplesRendered { get; private set; }

    public MusicDriver(Tune tune)
    {
        _playing = tune;
        for (int c = 0; c < Channels; c++) _channels[c] = new Channel();
        for (int i = 0; i < _history.Length; i++) _history[i] = new Frame();
    }

    /// <summary>
    /// Switches song at the next frame, from its first line.
    ///
    /// <b>A hard cut, not a fade.</b> Every channel is cleared, because the two songs need
    /// not agree on what a channel is: a note left fading on channel 4 of a song where 3 and
    /// 4 are one counter would be written into the wrong register. Mutes are cleared too —
    /// they were set against the old song's channels.
    /// </summary>
    public void Load(Tune tune)
    {
        _loading = tune;
        _edited = null;
        _seekTo = 0;
        _seekRow = 0;
        Array.Clear(Muted);
    }

    /// <summary>
    /// Swaps in an edited copy of the song being played at the next frame, and keeps the
    /// place: the line, the row, the frame within it, every note still sounding, and the
    /// mutes.
    ///
    /// <b>The opposite of <see cref="Load"/>, and for the opposite case.</b> Load is a new
    /// song, so it cuts everything and starts at the top. This is the same song with a cell
    /// changed, which is what an editor makes on every keystroke. Restarting the line on
    /// each one would make it impossible to hear an edit in context, and a note that was
    /// sounding when the edit landed plays out rather than stopping.
    ///
    /// Nothing is cleared, because nothing is inconsistent: a note left sounding keeps the
    /// instrument it started with until its channel's next cell, and a note whose width no
    /// longer suits the line's AUDCTL is cut by <see cref="Write"/>, as it is at any change
    /// of AUDCTL. A place the edit removed - a line past the new end - goes back to the
    /// loop line.
    /// </summary>
    public void Edit(Tune tune)
    {
        if (_loading != null) _loading = tune;
        else _edited = tune;
    }

    /// <summary>The line the driver is on, or has been told to jump to. A few blocks ahead
    /// of what is being heard; <see cref="At"/> is for that.</summary>
    public int Line => _seekTo >= 0 ? _seekTo : _line;

    /// <summary>True once a song that ends has played through and not been sent anywhere
    /// since. Like <see cref="Line"/>, this is the driver's position, a few blocks ahead
    /// of what is being heard — a page shows <see cref="Frame.Ended"/> instead.</summary>
    public bool Ended => _ended && _seekTo < 0 && _loading == null;

    /// <summary>Jumps to a line of the order list at the next frame - to its first row, or
    /// to <paramref name="row"/>, which is how an editor plays from its cursor. Deferred
    /// rather than immediate so it cannot land halfway through writing a frame.</summary>
    public void Seek(int line, int row = 0)
    {
        _seekTo = ((line % Tune.Lines) + Tune.Lines) % Tune.Lines;
        _seekRow = Math.Clamp(row, 0, Tune.RowsPerLine - 1);
    }

    private int _seekRow;

    /// <summary>
    /// Renders the next <paramref name="count"/> samples.
    ///
    /// <b>The block is cut at frame boundaries</b>, for the same reason
    /// <see cref="StepEnvelope.Render"/> cuts at tick boundaries: writing the registers once
    /// per block would tie the music's timing to the audio buffer size.
    /// </summary>
    public void Render(float[] buffer, int count)
    {
        double samplesPerFrame = SampleRate / FrameHz;
        int done = 0;

        while (done < count)
        {
            if (_samplesToNextFrame <= 0.0)
            {
                Vblank(SamplesRendered + done);
                _samplesToNextFrame += samplesPerFrame;
            }

            int slice = Math.Min(count - done, Math.Max(1, (int)_samplesToNextFrame));
            _chip.Render(buffer, done, slice, SampleRate);
            _samplesToNextFrame -= slice;
            done += slice;
        }

        for (int i = 0; i < count; i++)
        {
            buffer[i] = Math.Clamp(buffer[i] * Gain, -1f, 1f);
            _scope[(SamplesRendered + i) % _scope.Length] = buffer[i];
        }

        SamplesRendered += count;
    }

    /// <summary>
    /// One vertical blank: read the row if one starts here, then write every channel.
    ///
    /// Rows are read only on the frame they start, and instruments are stepped on every
    /// frame. That split is the whole design of a tracker driver — the pattern says when a
    /// note starts, the instrument says what happens to it frame by frame after that.
    /// </summary>
    private void Vblank(long startSample)
    {
        if (_loading != null)
        {
            _playing = _loading;
            _loading = null;
            foreach (var channel in _channels)
            {
                channel.Instrument = null;
                channel.Notes = Array.Empty<int>();
                channel.Volume = 0;
            }
        }

        if (_edited != null)
        {
            _playing = _edited;
            _edited = null;

            if (_line >= _playing.Lines)
            {
                _line = Math.Min(_playing.LoopLine, _playing.Lines - 1);
                _row = _frameInRow = 0;
                _ended = false;
            }
            if (_row >= _playing.RowsPerLine) _row = _playing.RowsPerLine - 1;
            if (_ended && !_playing.Ends) _ended = false;
        }

        if (_seekTo >= 0)
        {
            _line = _seekTo;
            _row = Math.Min(_seekRow, _playing.RowsPerLine - 1);
            _frameInRow = 0;
            _seekTo = -1;
            _seekRow = 0;
            _ended = false;
            foreach (var channel in _channels) channel.Released = true;
        }

        var tune = _playing;

        // A line switched off ("skip") is jumped over, however it was reached - stepping on,
        // looping, a seek, or an edit switching off the line being played: on to the next line
        // that plays, round to the loop if the rest are off, and to the end of a song that
        // ends - or of one with nothing left to play at all.
        if (!_ended && tune.Lines > 0 && !tune.Plays(_line))
        {
            int next = tune.NextPlaying(_line);
            if (next < 0 && !tune.Ends) next = tune.NextPlaying(tune.LoopLine);
            if (next < 0 && !tune.Ends) next = tune.NextPlaying(0);
            if (next < 0)
            {
                _ended = true;
                _row = tune.RowsPerLine - 1;
                foreach (var channel in _channels) channel.Released = true;
            }
            else
            {
                _line = next;
                _row = _frameInRow = 0;
            }
        }

        if (_frameInRow == 0 && !_ended)
        {
            // HeardAt: a channel switched off on this line is silent.
            for (int c = 0; c < Channels; c++) Trigger(_channels[c], tune.HeardAt(c, _line, _row));
        }

        var frame = _history[_historyNext];
        _historyNext = (_historyNext + 1) % _history.Length;
        _historyCount = Math.Min(_historyCount + 1, _history.Length);

        frame.Tune = tune;
        frame.StartSample = startSample;
        frame.Number = _frameNumber;
        frame.Line = _line;
        frame.Row = _row;
        frame.Ended = _ended;

        // AUDCTL first, so a pair is joined before its bytes arrive. Written every frame,
        // from the line's own setting: a real driver did the same, because a game's sound
        // effects could have changed it in between — and it is what lets a song join two
        // channels for one section and give them back for the next.
        frame.Audctl = tune.AudctlAt(_line);
        _chip.SetControl(frame.Audctl);

        for (int c = 0; c < Channels; c++)
        {
            if (tune.IsLowHalf(c, _line)) Quiet(c, frame);
            else Write(c, frame, joined: c > 0 && tune.IsLowHalf(c - 1, _line));
        }

        _frameNumber++;

        // An ended song still steps every channel — its last notes are fading by their own
        // release rates — but its position stays on the last row, and no row is read again.
        if (_ended) return;

        // Held, the row has been read and is not read again: stay past its first frame.
        if (Hold) { _frameInRow = 1; return; }

        if (++_frameInRow < tune.FramesPerRowAt(_line)) return;

        _frameInRow = 0;
        if (++_row < tune.RowsPerLine) return;

        if (_line + 1 < tune.Lines)
        {
            _row = 0;
            _line++;
        }
        else if (tune.Ends)
        {
            // The last row has played its full length. Release everything, as a note-off
            // on every channel would, so a held chord fades rather than being cut — and
            // rather than ringing forever, which is what a held note does with nothing
            // left to stop it.
            _ended = true;
            _row = tune.RowsPerLine - 1;
            foreach (var channel in _channels) channel.Released = true;
        }
        else
        {
            _row = 0;
            _line = tune.LoopLine;
        }
    }

    /// <summary>The low half of a pair: volume zero, and AUDF zero until its partner writes
    /// the low byte over it a moment later — which it does only while it has a note, so a
    /// silent pair is not left counting on a stale byte. The chip ignores this channel's
    /// AUDC once joined; zeroing it anyway means unjoining can never leave it droning.</summary>
    private void Quiet(int c, Frame frame)
    {
        // Whatever the channel was playing before it was given away is over, not paused:
        // left in place, a chord from the section before would resume the moment the pair
        // came apart again.
        _channels[c].Instrument = null;

        _chip.SetAudf(c, 0);
        _chip.SetAudc(c, 0);
        frame.Audf[c] = 0;
        frame.Audc[c] = 0;
        frame.Divider[c] = -1;
        frame.Note[c] = -1;
        frame.Instrument[c] = null;
        frame.Held[c] = Array.Empty<int>();
        frame.Struck[c] = false;
    }

    private static void Trigger(Channel channel, Cell cell)
    {
        switch (cell.Kind)
        {
            case CellKind.Off:
                channel.Released = true;
                break;

            case CellKind.Play:
                channel.Instrument = cell.Instrument;
                channel.Notes = cell.Notes;
                channel.Age = 0;
                channel.Released = false;
                break;
        }
    }

    /// <summary>
    /// Works out one channel's two bytes for this frame and writes them — or three, for the
    /// high half of a pair, whose divider's low byte goes to the channel below.
    /// </summary>
    /// <param name="joined">Whether this channel is the high half of a pair on this line.</param>
    private void Write(int c, Frame frame, bool joined)
    {
        var channel = _channels[c];

        // A note whose width does not match the channel's current state is cut, never
        // played: a 16-bit divider on an unjoined channel, or an 8-bit one read as the top
        // of a 16-bit counter, is a wrong pitch that still sounds like music. It happens
        // only across a line where AUDCTL changes, on a note left fading from the section
        // before; anywhere else Tune.Check has already reported it.
        if (channel.Instrument?.Table != null && channel.Instrument.Table.Is16Bit != joined)
            channel.Instrument = null;

        var instrument = channel.Instrument;
        int audf = 0, audc = 0, note = -1;
        bool struck = false;
        bool wide = instrument?.Table?.Is16Bit == true;

        if (instrument != null)
        {
            int age = channel.Age++;
            struck = age == 0 && !channel.Released;

            // A released note fades from wherever it had got to, rather than jumping to the
            // instrument's own tail: a note-off halfway through an attack should not get
            // louder on the way out.
            channel.Volume = channel.Released
                ? Math.Max(0, channel.Volume - instrument.Release)
                : Instrument.At(instrument.Volume, age);

            if (instrument.Audf != null)
            {
                audf = Instrument.At(instrument.Audf, age);
            }
            else if (channel.Notes.Length > 0)
            {
                // One note a frame, in turn. For a single note this is just that note; for
                // a chord it is the arpeggio.
                note = channel.Notes[age % channel.Notes.Length];
                audf = instrument.Table.Audf(note);

                if (instrument.Vibrato != null && age >= instrument.VibratoDelay)
                    audf += instrument.Vibrato[(age - instrument.VibratoDelay) % instrument.Vibrato.Length];
            }

            int volume = Muted[c] || Voiceless[c] ? 0 : channel.Volume;
            audc = Instrument.At(instrument.Distortion, age) | volume;
            if (channel.Volume == 0) note = -1;
        }

        audf = Math.Clamp(audf, 0, wide ? instrument.Table.Max : 255);
        frame.Divider[c] = audf;

        if (wide && c > 0)
        {
            // Low byte first, into the partner. The chip reads both on its next reload, so
            // the order within the frame does not matter to the sound — only that both
            // land before the frame is rendered.
            _chip.SetAudf(c - 1, audf & 0xff);
            frame.Audf[c - 1] = audf & 0xff;
            audf >>= 8;
        }

        _chip.SetAudf(c, audf);
        _chip.SetAudc(c, audc);

        frame.Audf[c] = audf;
        frame.Audc[c] = audc;
        frame.Note[c] = note;
        frame.Instrument[c] = channel.Volume > 0 ? instrument : null;

        bool holding = instrument != null && instrument.Audf == null && !channel.Released && channel.Volume > 0 && !Muted[c];
        frame.Held[c] = holding ? channel.Notes : Array.Empty<int>();
        frame.Struck[c] = struck && holding;
    }

    /// <summary>The frame that was being played at <paramref name="sample"/>: the newest
    /// one that had started by then. Null before anything has been rendered.</summary>
    /// <summary>The frames that started at or after <paramref name="sample"/>, oldest first,
    /// into <paramref name="into"/> - for a voice following the driver block by block. The
    /// frames are the driver's own and are reused: read them before rendering again.</summary>
    public void FramesFrom(long sample, List<Frame> into)
    {
        into.Clear();
        for (int i = _historyCount; i >= 1; i--)
        {
            var frame = _history[(_historyNext - i + _history.Length) % _history.Length];
            if (frame.StartSample >= sample) into.Add(frame);
        }
    }

    public Frame At(long sample)
    {
        for (int i = 1; i <= _historyCount; i++)
        {
            var frame = _history[(_historyNext - i + _history.Length) % _history.Length];
            if (frame.StartSample <= sample) return frame;
        }

        return _historyCount == 0
            ? null
            : _history[(_historyNext - _historyCount + _history.Length) % _history.Length];
    }

    /// <summary>The output samples ending at <paramref name="end"/>, oldest first. Silence
    /// for anything not rendered yet or too old to still be held.</summary>
    public void CopyOutput(long end, float[] into)
    {
        long start = end - into.Length;
        for (int i = 0; i < into.Length; i++)
        {
            long at = start + i;
            bool held = at >= 0 && at < SamplesRendered && at >= SamplesRendered - _scope.Length;
            into[i] = held ? _scope[at % _scope.Length] : 0f;
        }
    }
}
