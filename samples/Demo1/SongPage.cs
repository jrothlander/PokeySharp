using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Input;
using Pokey;
using Pokey.Music;

namespace Demo1;

/// <summary>
/// A song played the way an Atari 800 played one: four channels, each with a job, driven
/// by a routine that runs once a frame and writes nine bytes.
///
/// <b>Streamed, not pre-rendered</b>, unlike every sound on the other two pages. The chip
/// runs live, the way PokeyLab's does, so a channel can be muted while the song is going
/// and the registers on screen are the ones being written — which is the whole reason to
/// look at this page rather than just listen to a file.
///
/// The left half is the song as a tracker shows it; the right half is the chip as the
/// driver leaves it, frame by frame.
/// </summary>
public sealed class SongPage : IPage, IDisposable
{
    /// <summary>About 23ms a block, three queued: enough to ride out a slow frame, little
    /// enough that a mute lands while you are still looking at the key you pressed.</summary>
    private const int BlockSamples = 1024, QueueDepth = 3;

    private const float LeftX = 40f, LeftWidth = 560f;
    private const float RowNumberX = 58f, ColumnX = 104f, ColumnWidth = 124f;
    private const float HeaderY = 106f, HeaderRuleY = 126f;
    private const float ColumnsY = 138f, TracksY = 160f, RowsTop = 184f, RowHeight = 20f;

    /// <summary>Odd, so the row being played sits in the middle with as much song
    /// showing after it as before.</summary>
    private const int VisibleRows = 17;

    private const float NotesTop = 548f;
    private const float RightX = 660f, RightWidth = 580f, RightEdge = RightX + RightWidth;
    private const float ChannelsTop = 138f, ChannelHeight = 78f;
    private const float BytesTop = 460f, ScopeTop = 548f, ScopeHeight = 70f, HowTop = 662f;

    /// <summary>For the "---" of an empty cell: present enough to keep the grid, quiet
    /// enough that the notes read as the only thing in it.</summary>
    private static readonly Color Faint = new(64, 69, 78);

    private readonly MusicDriver _driver;
    private readonly Sounds _blips;
    private readonly IntPtr _windowHandle;
    private readonly System.Collections.Generic.Dictionary<Tune, string[][]> _tuning = new();

    /// <summary>The songs on offer: the built-in one, replaced by any file of the same
    /// name, then every file in <see cref="SongDirectory"/>, then anything opened with O.
    /// Never empty — the built-in song is always there.</summary>
    private readonly System.Collections.Generic.List<Tune> _songs;
    private int _song;

    /// <summary>Set for a few seconds after loading, so a file that arrived with problems
    /// — or could not be played at all — says so in the header.</summary>
    private string _message;
    private double _messageLeft;

    /// <summary>True for the frame after the file dialog closed; see
    /// <see cref="IPage.ClearBlocked"/>.</summary>
    private bool _blocked;

    /// <summary>The song the frame being heard belongs to — which, for a few blocks after
    /// <c>N</c>, is still the old one. Everything drawn reads through this, and every
    /// control through <see cref="Tune"/>.</summary>
    private Tune _shown;

    private DynamicSoundEffectInstance _stream;
    private readonly float[] _block = new float[BlockSamples];
    private readonly byte[] _pcm = new byte[BlockSamples * 2];
    private readonly float[] _scope = new float[1470];

    private bool _audio, _started, _playing;
    private long _submitted;
    private double _silentSamples;
    private int _selected;

    private Tune Tune => _driver.Tune;

    public string Name => "SONG";

    public string Hint =>
        "ENTER PLAY PAUSE    N SONG    O OPEN    LEFT RIGHT LINE    UP DOWN CHANNEL    M MUTE    S SOLO";

    /// <summary>Where shipped song files live: beside the executable, copied there from
    /// this project's own <c>songs/</c>, the demo's chosen set.</summary>
    public static string SongDirectory => System.IO.Path.Combine(AppContext.BaseDirectory, "songs");

    /// <summary><paramref name="blips"/> is <see cref="Game1"/>'s interface bank, for the
    /// menu click when the channel selection moves; <paramref name="windowHandle"/> owns
    /// the file dialog, for the reasons <see cref="FileDialog.Open"/> gives.</summary>
    public SongPage(Sounds blips, IntPtr windowHandle)
    {
        _blips = blips;
        _windowHandle = windowHandle;

        _songs = SongFile.Catalog(SongDirectory, out var skipped);
        if (skipped.Count > 0) Say($"{skipped.Count} SONG FILE(S) SKIPPED - {skipped[0]}");

        _driver = new MusicDriver(_songs[0]);
        _shown = Tune;
    }

    private void Say(string message)
    {
        _message = message;
        _messageLeft = 8.0;
    }

    public bool ClearBlocked()
    {
        bool was = _blocked;
        _blocked = false;
        return was;
    }

    /// <summary>The tuning lines for each channel of a song, worked out the first time that
    /// song is shown.</summary>
    private string[] TuningLines(Tune tune, int channel)
    {
        if (!_tuning.TryGetValue(tune, out var lines))
        {
            lines = new string[MusicDriver.Channels][];
            for (int c = 0; c < lines.Length; c++) lines[c] = Tuning(tune.Parts[c]);
            _tuning[tune] = lines;
        }
        return lines[channel];
    }

    /// <summary>
    /// How well a channel's tables serve the notes it is asked for — one line per table,
    /// since a channel that changes instrument between sections can use more than one.
    ///
    /// Worked out from the song rather than stated, so if a track is rewritten into a key
    /// the table handles badly, this line is where it shows.
    /// </summary>
    private static string[] Tuning(Part part)
    {
        if (part.Kind == PartKind.Pair) return new[] { "NO NOTES - IT IS HALF OF THE BASS'S DIVIDER" };
        if (part.Kind == PartKind.Drums) return new[] { "NO NOTE TABLE - EACH DRUM CARRIES ITS OWN DIVIDERS" };

        var lines = new System.Collections.Generic.List<string>();
        foreach (var (table, used) in part.NotesUsed())
        {
            int worst = -1;
            foreach (int midi in used)
                if (worst < 0 || Math.Abs(table.Cents(midi)) > Math.Abs(table.Cents(worst)))
                    worst = midi;

            if (worst >= 0)
                lines.Add($"{used.Count} NOTES ON {table.Name}, WORST "
                          + $"{NoteTable.NoteName(worst)} AT {Signed(table.Cents(worst))} CENTS");
        }
        return lines.ToArray();
    }

    /// <summary>Whole cents with a sign, and a plain 0 for anything that rounds to it —
    /// the 16-bit table lands within a tenth of a cent, and "-0" reads as a defect.</summary>
    private static string Signed(double cents) => Math.Round(cents).ToString("+0;-0;0");

    public void Update(Input input, double elapsedSeconds)
    {
        if (input.Pressed(Keys.Enter) || input.Pressed(Keys.Space))
        {
            // A jingle that has finished plays again from the top rather than un-pausing
            // into silence, which is what Enter would otherwise do.
            if (_driver.Ended)
            {
                _driver.Seek(0);
                if (!_playing) Toggle();
            }
            else Toggle();
        }
        if (input.Pressed(Keys.N)) NextSong();
        if (input.Pressed(Keys.O)) OpenFile();

        // Relative to where the driver is rather than to what is being heard: the driver
        // is a few blocks ahead, and reading the heard line would make two quick presses
        // land on the same line.
        if (input.Pressed(Keys.Right)) Jump(+1);
        if (input.Pressed(Keys.Left)) Jump(-1);

        if (input.Pressed(Keys.Down)) Select(_selected + 1);
        if (input.Pressed(Keys.Up)) Select(_selected - 1);
        // The low half of a pair has no sound to mute, so these act on the channel it
        // counts for — the one you would expect to go quiet.
        int target = Tune.IsLowHalf(_selected, _driver.Line) ? _selected + 1 : _selected;
        if (input.Pressed(Keys.M)) _driver.Muted[target] = !_driver.Muted[target];
        if (input.Pressed(Keys.S)) Solo(target);

        if (input.Clicked)
        {
            int hit = HitTest(input.MouseX, input.MouseY);
            if (hit >= 0) Select(hit);
        }

        _messageLeft = Math.Max(0.0, _messageLeft - elapsedSeconds);
        Pump(elapsedSeconds);
    }

    /// <summary>Pauses when the page is left. The stream is only fed from this page's
    /// Update, so a song left running behind another page would starve and stutter.</summary>
    public void Leave()
    {
        if (!_playing) return;
        _playing = false;
        _stream?.Pause();
    }

    private void Select(int channel)
    {
        int was = _selected;
        _selected = Math.Clamp(channel, 0, MusicDriver.Channels - 1);
        if (_selected != was) _blips.Play("Menu blip");
    }

    /// <summary>Everything but this channel off — or, if it is already the only one on,
    /// everything back on, so the same key undoes itself.</summary>
    private void Solo(int channel)
    {
        // The low half of a pair is left out both ways: it has nothing to mute. A channel
        // that is a low byte only in some sections still counts — it has a voice in the
        // others.
        bool Counts(int c) => c != channel && Tune.Parts[c].Kind != PartKind.Pair;

        bool alone = !_driver.Muted[channel];
        for (int c = 0; c < MusicDriver.Channels; c++)
            if (Counts(c) && !_driver.Muted[c]) alone = false;

        for (int c = 0; c < MusicDriver.Channels; c++)
            _driver.Muted[c] = !alone && Counts(c);
    }

    private void Jump(int delta)
    {
        _driver.Seek(_driver.Line + delta);
        if (!_playing) Toggle();
    }

    /// <summary>The next song, from its start, playing — choosing a song is asking to
    /// hear it.</summary>
    private void NextSong() => Play((_song + 1) % _songs.Count);

    private void Play(int index)
    {
        _song = index;
        _driver.Load(_songs[_song]);
        if (!_playing) Toggle();
    }

    /// <summary>
    /// Asks for a song file and plays it.
    ///
    /// <b>The stream is paused first.</b> The game loop stops while the dialog is open, so
    /// nothing feeds the stream; left playing it would run dry and stutter on the way.
    ///
    /// Replaced rather than appended when the same file is opened again — so re-opening
    /// after an edit plays the edit instead of adding a stale second copy — and a file of
    /// a song already listed replaces it by name, as the catalog does at startup.
    /// </summary>
    private void OpenFile()
    {
        bool was = _playing;
        if (_playing) { _playing = false; _stream?.Pause(); }

        string picked = FileDialog.Open(_windowHandle, "Open a song", "Songs", SongFile.Extension,
                                        SongDirectory, out string failure);
        _blocked = true;

        if (failure != null) Say($"COULD NOT OPEN THE FILE DIALOG - {failure}");

        if (picked == null)
        {
            if (was) Toggle();
            return;
        }

        var loaded = SongFile.Load(picked);
        if (loaded.Lines == 0)
        {
            // Nothing a driver could play. Said, and the song that was playing carries on.
            Say(loaded.Problems.Count > 0 ? loaded.Problems[^1] : $"{System.IO.Path.GetFileName(picked)}: NOTHING TO PLAY");
            if (was) Toggle();
            return;
        }

        int existing = _songs.FindIndex(s =>
            string.Equals(s.Source, picked, StringComparison.OrdinalIgnoreCase)
            || string.Equals(s.Name, loaded.Name, StringComparison.OrdinalIgnoreCase));

        if (existing >= 0) _songs[existing] = loaded;
        else { _songs.Add(loaded); existing = _songs.Count - 1; }

        if (loaded.Problems.Count > 0) Say($"{loaded.Problems.Count} PROBLEMS - {loaded.Problems[0]}");
        Play(existing);
    }

    private void Toggle()
    {
        if (!_started)
        {
            _started = true;
            StartAudio();
        }

        _playing = !_playing;
        if (_stream == null) return;

        try
        {
            if (!_playing) _stream.Pause();
            else if (_stream.State == SoundState.Paused) _stream.Resume();
            else _stream.Play();
        }
        catch (Exception)
        {
            _audio = false;
        }
    }

    /// <summary>
    /// Opens the stream, or does without one.
    ///
    /// Opened on the first play rather than at load, so the page costs nothing until it is
    /// used. No device is a reason to be silent, not to fail: the song still runs on a
    /// clock of its own and the page still shows it, which is most of what it is for.
    /// </summary>
    private void StartAudio()
    {
        try
        {
            _stream = new DynamicSoundEffectInstance(MusicDriver.SampleRate, AudioChannels.Mono);
            _audio = true;
        }
        catch (Exception)
        {
            _stream = null;
            _audio = false;
        }
    }

    /// <summary>
    /// Keeps the queue topped up. <b>This is what makes time pass for the song</b> — the
    /// driver only moves when it is asked for samples.
    ///
    /// Bounded to the queue depth per frame, as PokeyLab's is: a device that stops
    /// consuming would otherwise spin this loop forever inside Update.
    /// </summary>
    private void Pump(double elapsedSeconds)
    {
        if (!_playing) return;

        if (!_audio)
        {
            _silentSamples += elapsedSeconds * MusicDriver.SampleRate;
            while (_silentSamples >= BlockSamples)
            {
                _driver.Render(_block, BlockSamples);
                _submitted += BlockSamples;
                _silentSamples -= BlockSamples;
            }
            return;
        }

        try
        {
            for (int b = 0; b < QueueDepth && _stream.PendingBufferCount < QueueDepth; b++)
            {
                _driver.Render(_block, BlockSamples);

                for (int i = 0; i < BlockSamples; i++)
                {
                    short sample = (short)(_block[i] * short.MaxValue);
                    _pcm[i * 2] = (byte)(sample & 0xff);
                    _pcm[i * 2 + 1] = (byte)((sample >> 8) & 0xff);
                }

                _stream.SubmitBuffer(_pcm);
                _submitted += BlockSamples;
            }
        }
        catch (Exception)
        {
            // Device lost mid-song. Same rule as everywhere else in the demo: go quiet,
            // and let the page carry on without it.
            _audio = false;
        }
    }

    /// <summary>The sample leaving the speaker now: everything submitted, less what is
    /// still queued. Good to within a block, which is well under a frame of the song.</summary>
    private long Heard
    {
        get
        {
            if (!_audio || _stream == null) return _submitted;
            try { return Math.Max(0, _submitted - (long)_stream.PendingBufferCount * BlockSamples); }
            catch (Exception) { return _submitted; }
        }
    }

    private int HitTest(int x, int y)
    {
        if (y >= ColumnsY - 4 && y < TracksY + 16 && x >= ColumnX)
        {
            int column = (int)((x - ColumnX) / ColumnWidth);
            if (column < MusicDriver.Channels) return column;
        }

        if (x >= RightX && x <= RightEdge && y >= ChannelsTop - 4)
        {
            int block = (int)((y - ChannelsTop + 4) / ChannelHeight);
            if (block < MusicDriver.Channels) return block;
        }

        return -1;
    }

    // --- Drawing ----------------------------------------------------------

    public void Draw(Screen screen)
    {
        var now = _driver.At(Heard);
        _shown = now?.Tune ?? Tune;

        DrawHeader(screen, now);
        DrawTracker(screen, now);
        DrawPart(screen);
        DrawChannels(screen, now);
        DrawBytes(screen, now);
        DrawScope(screen);
        DrawHow(screen);
    }

    private void DrawHeader(Screen screen, MusicDriver.Frame now)
    {
        var tune = _shown;
        int number = _songs.IndexOf(tune) + 1;
        int line = now?.Line ?? 0;

        // The section in place of the key when the song has them, and the tempo of the line
        // being heard: both change partway through a suite.
        string title = $"{(number > 0 ? number.ToString() : "-")}/{_songs.Count}   {tune.Name} - {tune.SectionAt(line) ?? tune.Key} - "
                       + $"{tune.Bpm(MusicDriver.FrameHz, line):0} BPM";
        screen.Text(title, LeftX, HeaderY, 13f, Screen.Heading);
        float titleEnd = LeftX + screen.Measure(title, 13f) + 24f;

        string status;
        Color colour = Screen.Body;

        if (_messageLeft > 0.0 && _message != null)
        {
            status = _message;
            colour = Screen.Live;
        }
        else if (tune.Problems.Count > 0)
        {
            status = $"{tune.Problems.Count} PROBLEMS - {tune.Problems[0]}";
            colour = Screen.Live;
        }
        else if (!_started)
        {
            status = "PRESS ENTER TO PLAY";
            colour = Screen.Live;
        }
        else if (now?.Ended == true)
        {
            // From the frame being heard, not the driver's position, so it appears as the
            // last note finishes rather than a few blocks before.
            status = "ENDED - ENTER TO PLAY IT AGAIN";
            colour = Screen.Live;
        }
        else
        {
            string state = !_playing ? "PAUSED   " : !_audio ? "NO AUDIO DEVICE   " : "";
            string where = $"LINE {line + 1:00}/{tune.Lines}";
            string speed = $"{tune.FramesPerRowAt(line)} FRAMES A ROW";

            // A long name and section can meet the status in the middle, so the status
            // gives up its parts, least useful first, until it fits: the frame rate (the
            // same for every song), the frame count, then the speed. The line and the
            // state are never dropped.
            string[] choices =
            {
                $"{state}{where}   FRAME {now?.Number ?? 0}   {speed} AT {MusicDriver.FrameHz:0.00} HZ",
                $"{state}{where}   FRAME {now?.Number ?? 0}   {speed}",
                $"{state}{where}   {speed}",
                $"{state}{where}",
            };
            status = choices[^1];
            foreach (string choice in choices)
                if (RightEdge - screen.Measure(choice, 13f) >= titleEnd) { status = choice; break; }
        }

        // Messages and problems are long; smaller, as the sounds page shows them, so they do
        // not run into the song's name on the left.
        screen.TextRight(status, RightEdge, HeaderY, colour == Screen.Live && _started ? 11f : 13f, colour);
        screen.Rule(LeftX, HeaderRuleY, RightEdge - LeftX);
    }

    /// <summary>
    /// The song as a tracker shows it: a row per step of the meter, a column per channel, and the
    /// row being played held in the middle while the song scrolls through it.
    /// </summary>
    private void DrawTracker(Screen screen, MusicDriver.Frame now)
    {
        var tune = _shown;
        int line = now?.Line ?? 0, row = now?.Row ?? 0;

        screen.Text("ROW", LeftX, ColumnsY, 12f, Screen.Dim);

        for (int c = 0; c < MusicDriver.Channels; c++)
        {
            float x = ColumnX + c * ColumnWidth;
            string label = $"{c + 1} {tune.Parts[c].Role}";
            bool muted = _driver.Muted[c];

            screen.Text(label, x, ColumnsY, 13f,
                        c == _selected ? Screen.Heading : muted ? Screen.Dim : Screen.Body);
            if (c == _selected) screen.Rule(x, ColumnsY + 18f, screen.Measure(label, 13f), Screen.Heading);

            string track = muted ? "MUTED"
                         : tune.IsLowHalf(c, line) ? "PAIRED"
                         : tune.TrackAt(c, line) ?? "SILENT";
            screen.Text(track, x, TracksY + 2f, 11f, muted ? Screen.Heading : Screen.Dim);
        }

        for (int i = 0; i < VisibleRows; i++)
        {
            int offset = i - VisibleRows / 2;
            if (!Offset(line, row, offset, out int showLine, out int showRow)) continue;

            float y = RowsTop + i * RowHeight;
            bool current = offset == 0;

            // A bar line between one line of the order list and the next, which is where
            // the tracks change underneath the rows.
            if (showRow == 0 && i > 0) screen.Rule(LeftX, y - 5f, LeftWidth, Screen.RuleColour);

            if (current) screen.Text(">", LeftX, y, 13f, Screen.Heading);
            screen.Text($"{showRow:00}", RowNumberX, y, 12f,
                        current ? Screen.Heading : showRow % tune.RowsPerBeat == 0 ? Screen.Body : Screen.Dim);

            for (int c = 0; c < MusicDriver.Channels; c++)
            {
                var cell = tune.CellAt(c, showLine, showRow);
                screen.Text(cell.Label, ColumnX + c * ColumnWidth, y, 13f, CellColour(cell, c, current));
            }
        }
    }

    private Color CellColour(Cell cell, int channel, bool current)
    {
        if (_driver.Muted[channel]) return cell.Kind == CellKind.Play ? Screen.Dim : Faint;
        return cell.Kind switch
        {
            CellKind.Play => current ? Screen.Live : Screen.Number,
            CellKind.Off => current ? Screen.Chosen : Screen.Dim,
            _ => current ? Screen.Dim : Faint,
        };
    }

    /// <summary>The row <paramref name="offset"/> rows from a given one, following the
    /// order list forward — including the jump back to the loop — and stopping at the
    /// start of the song going backward.</summary>
    private bool Offset(int line, int row, int offset, out int toLine, out int toRow)
    {
        row += offset;
        while (row >= _shown.RowsPerLine)
        {
            row -= _shown.RowsPerLine;
            line = line + 1 < _shown.Lines ? line + 1 : _shown.LoopLine;
        }
        while (row < 0)
        {
            row += _shown.RowsPerLine;
            line--;
        }

        toLine = line;
        toRow = row;
        return line >= 0;
    }

    /// <summary>The selected channel's job, why the hardware made it that way, and how
    /// well its note table fits the song.</summary>
    private void DrawPart(Screen screen)
    {
        var part = _shown.Parts[_selected];
        string voice = part.Kind == PartKind.Pair ? "LOW BYTE OF A 16 BIT COUNTER"
                     : part.Kind == PartKind.Drums ? $"{part.Kit.Count} DRUM{(part.Kit.Count == 1 ? "" : "S")}"
                     : PokeyChip.DistortionName(part.Voice.Distortion[0] >> 5)
                       + (part.Table.Is16Bit ? ", 16 BIT AT 1.79 MHZ" : "");

        float y = Section(screen, LeftX, LeftWidth, NotesTop,
                          $"CHANNEL {_selected + 1} - {part.Role} - {voice}");

        foreach (string wrapped in screen.Wrap(part.Note, LeftWidth, 12f))
        {
            screen.Text(wrapped, LeftX, y, 12f, Screen.Body);
            y += 18f;
        }

        y += 8f;
        foreach (string tuning in TuningLines(_shown, _selected))
        {
            screen.Text(tuning, LeftX, y, 12f, Screen.Number);
            y += 18f;
        }
    }

    /// <summary>
    /// Each channel as the driver left it this frame: the two bytes, what they mean, and
    /// the note they make.
    ///
    /// <b>The arpeggio flickers here, and that is the chord.</b> The chords channel changes
    /// note every frame, so its line never holds still — which is exactly what the chip is
    /// being told.
    /// </summary>
    private void DrawChannels(Screen screen, MusicDriver.Frame now)
    {
        for (int c = 0; c < MusicDriver.Channels; c++)
        {
            float y = ChannelsTop + c * ChannelHeight;
            var part = _shown.Parts[c];
            bool muted = _driver.Muted[c];
            bool low = _shown.IsLowHalf(c, now?.Line ?? 0);

            int audf = now?.Audf[c] ?? 0, audc = now?.Audc[c] ?? 0, note = now?.Note[c] ?? -1;
            int divider = now?.Divider[c] ?? 0;
            var instrument = now?.Instrument[c];
            int volume = audc & PokeyChip.AudcVolumeMask;

            screen.Text($"{c + 1} {part.Role}", RightX, y, 13f, c == _selected ? Screen.Heading : Screen.Body);
            screen.TextRight(muted ? "MUTED" : instrument?.Name ?? "",
                             RightEdge, y, 13f, muted ? Screen.Heading : Screen.Live);

            string distortion = low ? $"LOW HALF OF {c + 1}+{c + 2}"
                              : volume == 0 ? "SILENT"
                              : PokeyChip.DistortionName(audc >> 5);

            // AUDCTL bit 7 swaps the 17-bit counter for the 9-bit one under the same
            // distortion codes, and the name has to follow it — Guardian's snare says
            // "17 BIT" on the very frame it is audibly the 9-bit rasp otherwise.
            if (((now?.Audctl ?? 0) & PokeyChip.CtlNineBitPoly) != 0)
                distortion = distortion.Replace("17 BIT", "9 BIT");
            screen.Text($"AUDF {audf:X2}   AUDC {audc:X2}   {distortion}", RightX + 12f, y + 22f, 12f,
                        Screen.Number);
            DrawMeter(screen, RightEdge, y + 22f, volume);

            string pitch = "";
            if (low)
            {
                pitch = $"COUNTS FOR CHANNEL {c + 2} - NO SOUND OF ITS OWN";
            }
            else if (instrument != null && note >= 0 && instrument.Table != null)
            {
                double hz = instrument.Table.Hz(divider);
                double cents = 1200.0 * Math.Log2(hz / NoteTable.MidiHz(note));
                pitch = $"{NoteTable.NoteName(note)}   DIVIDER {divider}   {hz:0.0} HZ   {Signed(cents)} CENTS";
            }
            else if (instrument != null)
            {
                pitch = $"{instrument.Name} FRAME TABLE - DIVIDER {audf}";
            }

            screen.Text(pitch, RightX + 12f, y + 42f, 12f, Screen.Body);
            screen.Rule(RightX, y + 64f, RightWidth);
        }
    }

    /// <summary>The volume nibble as fifteen bars, lit to the level written.</summary>
    private static void DrawMeter(Screen screen, float right, float y, int volume)
    {
        for (int v = 0; v < 15; v++)
        {
            float x = right - (15 - v) * 8f;
            screen.Line(new Vector2(x, y), new Vector2(x, y + 12f),
                        v < volume ? Screen.Live : Screen.RuleColour, 3f);
        }
    }

    /// <summary>
    /// The nine bytes, at the address a program would have written them to.
    ///
    /// POKEY sits at <c>D200</c> on an Atari 800, AUDF and AUDC alternating up to AUDCTL
    /// at <c>D208</c>. Everything else on this page is those nine numbers explained.
    /// </summary>
    private static void DrawBytes(Screen screen, MusicDriver.Frame now)
    {
        float y = Section(screen, RightX, RightWidth, BytesTop, "D200 - D208   WRITTEN THIS FRAME");

        const float Size = 16f;
        float advance = screen.Measure("00", Size) - screen.Measure("0", Size);

        screen.Text("D200:", RightX + 12f, y, Size, Screen.Dim);

        string[] labels = { "F1", "C1", "F2", "C2", "F3", "C3", "F4", "C4", "CTL" };
        for (int i = 0; i < labels.Length; i++)
        {
            int value = i == 8 ? now?.Audctl ?? 0 : i % 2 == 0 ? now?.Audf[i / 2] ?? 0 : now?.Audc[i / 2] ?? 0;
            float x = RightX + 12f + (6 + 3 * i) * advance;

            screen.Text($"{value:X2}", x, y, Size, Screen.Number);
            screen.Text(labels[i], x, y + 24f, 10f, Screen.Dim);
        }
    }

    /// <summary>What is coming out of the speaker, drawn as <see cref="SoundPage"/> draws
    /// its waveform: a min-max column per pixel, so nothing aliases into a shape the sound
    /// does not have.</summary>
    private void DrawScope(Screen screen)
    {
        float top = Section(screen, RightX, RightWidth, ScopeTop, "OUTPUT   ONE CHIP, EVERY CHANNEL, 33 MS");
        float middle = top + ScopeHeight / 2f;

        _driver.CopyOutput(Heard, _scope);
        screen.Rule(RightX, middle, RightWidth);

        int columns = (int)RightWidth;
        for (int c = 0; c < columns; c++)
        {
            int from = c * _scope.Length / columns;
            int to = Math.Max(from + 1, (c + 1) * _scope.Length / columns);

            float low = 0f, high = 0f;
            for (int i = from; i < to && i < _scope.Length; i++)
            {
                low = Math.Min(low, _scope[i]);
                high = Math.Max(high, _scope[i]);
            }

            float x = RightX + c;
            screen.Line(new Vector2(x, middle - high * ScopeHeight / 2f),
                        new Vector2(x, middle - low * ScopeHeight / 2f), Screen.Number, 1f);
        }
    }

    private static void DrawHow(Screen screen)
    {
        float y = Section(screen, RightX, RightWidth, HowTop, "HOW");

        const string How =
            "Sixty times a second the Atari stops drawing and runs the music driver. It reads " +
            "a row when one starts, steps every instrument one frame through its tables, and " +
            "writes the nine bytes above. Between writes the chip holds whatever it was told.";

        foreach (string wrapped in screen.Wrap(How, RightWidth, 12f))
        {
            screen.Text(wrapped, RightX, y, 12f, Screen.Body);
            y += 18f;
        }
    }

    private static float Section(Screen screen, float x, float width, float y, string label)
    {
        screen.Text(label, x, y, 12f, Screen.Heading);
        screen.Rule(x, y + 17f, width);
        return y + 27f;
    }

    public void Dispose() => _stream?.Dispose();
}
