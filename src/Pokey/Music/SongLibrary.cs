using System;
using System.Collections.Generic;
using System.Linq;
using Pokey;

namespace Pokey.Music;

/// <summary>
/// Which divider plays which note, for one kind of AUDC.
///
/// <b>A note on a POKEY is a table lookup, not a frequency.</b> AUDF is an integer divider,
/// so the chip can only reach the pitches its dividers land on, and an Atari music driver
/// carried a table from note number to divider. These tables are computed rather than
/// typed, and they report how far each note misses by — that miss is why Atari music has
/// its own slightly sour intonation, and why its composers chose keys by the table.
/// </summary>
public sealed class NoteTable
{
    /// <summary>The "64kHz" clock an unmodified channel runs on — an NTSC Atari 800's base
    /// clock divided by 28, which is 63921Hz and not 64000.</summary>
    private static readonly double SlowHz = PokeyClocks.Slow(PokeyClocks.Atari800NtscHz);

    /// <summary>Distortion A, pure tone: the flip-flop toggles on every underflow, so one
    /// cycle is two of them.</summary>
    public static readonly NoteTable Tone = new("PURE TONE", SlowHz, 2, 1, 255, _ => true);

    /// <summary>
    /// Distortion C, the 4-bit poly: the Atari's bass.
    ///
    /// <b>Why it is 15 and not 2.</b> The channel samples a counter that repeats every 15
    /// base clocks. When the divider's period shares no factor with 15, successive
    /// underflows walk all fifteen positions of that counter before the pattern comes
    /// round, so the output repeats every fifteen underflows — the same divider sounds
    /// seven and a half times lower than a pure tone. When AUDF+1 is a multiple of 3 or 5
    /// the walk visits only some of the positions, the pattern is shorter, and the note is
    /// a different pitch with a different buzz. So the table skips those dividers.
    /// </summary>
    public static readonly NoteTable Buzz = new("DISTORTION C", SlowHz, 15, 1, 255,
                                                 period => period % 3 != 0 && period % 5 != 0);

    /// <summary>
    /// Two channels joined into one 16-bit counter, clocked straight off the 1.79MHz base
    /// clock: AUDCTL <c>CtlJoin34 | CtlCh3Fast</c>, or the 1+2 equivalents.
    ///
    /// <b>The other way to get a bass, and it costs a channel.</b> Sixty-five thousand
    /// dividers at a megahertz and three quarters put every note within a fraction of a
    /// cent, all the way down — no gaps, no buzz, no choosing the key by the table. What it
    /// takes is the low channel, which becomes the bottom byte of the counter and makes no
    /// sound of its own. The addend is the chip's reload delay on the fast clock, 7 for a
    /// joined pair, which <see cref="PokeyChip"/> implements and the C012294 documentation
    /// confirms.
    /// </summary>
    public static readonly NoteTable Wide = new("16 BIT TONE", PokeyClocks.Atari800NtscHz, 2, 7, 65535,
                                                _ => true);

    private readonly double _clockHz;

    /// <summary>Underflows per output cycle.</summary>
    private readonly int _cycle;

    /// <summary>What the counter reloads with on top of AUDF: 1 on the slow clocks, 7 for
    /// a joined pair on the fast one.</summary>
    private readonly int _addend;

    private readonly int[] _audf = new int[128];

    public string Name { get; }

    /// <summary>The largest divider: 255 for one channel, 65535 for a pair.</summary>
    public int Max { get; }

    /// <summary>Whether this table needs two channels joined — the driver then writes the
    /// low byte to the channel below and the high byte to this one.</summary>
    public bool Is16Bit => Max > 255;

    /// <param name="usable">Whether a counter period (AUDF plus the addend) gives this
    /// table's sound. Only distortion C ever says no.</param>
    private NoteTable(string name, double clockHz, int cycle, int addend, int max,
                      Func<int, bool> usable)
    {
        Name = name;
        Max = max;
        _clockHz = clockHz;
        _cycle = cycle;
        _addend = addend;

        for (int midi = 0; midi < _audf.Length; midi++)
        {
            // Nearest in cents, not in hertz: a semitone is a ratio, so "nearest" has to be
            // measured as one or every choice leans toward the flat side. The search is a
            // few dividers either side of the exact answer, which is far wider than the
            // largest gap in the distortion C table.
            double exact = _clockHz / (_cycle * MidiHz(midi)) - _addend;
            int centre = (int)Math.Clamp(Math.Round(exact), 0, max);
            int best = centre;
            double bestMiss = double.MaxValue;

            for (int audf = centre - 8; audf <= centre + 8; audf++)
            {
                if (audf < 0 || audf > max || !usable(audf + addend)) continue;
                double miss = Math.Abs(Math.Log2(Hz(audf) / MidiHz(midi)));
                if (miss < bestMiss) { best = audf; bestMiss = miss; }
            }

            _audf[midi] = best;
        }
    }

    public int Audf(int midi) => _audf[Math.Clamp(midi, 0, _audf.Length - 1)];

    /// <summary>The pitch a divider actually produces under this table's distortion.</summary>
    public double Hz(int audf) => _clockHz / (_cycle * ((double)audf + _addend));

    /// <summary>How far a note lands from equal temperament at A = 440, in cents.</summary>
    public double Cents(int midi) => 1200.0 * Math.Log2(Hz(Audf(midi)) / MidiHz(midi));

    public static double MidiHz(int midi) => 440.0 * Math.Pow(2.0, (midi - 69) / 12.0);

    private static readonly string[] Names =
        { "C-", "C#", "D-", "D#", "E-", "F-", "F#", "G-", "G#", "A-", "A#", "B-" };

    /// <summary>Tracker spelling: <c>D-4</c>, <c>A#3</c>. Three characters, so a column of
    /// them lines up. Flats are spelled as sharps, the way every tracker did.</summary>
    public static string NoteName(int midi) => Names[midi % 12] + (midi / 12 - 1);

    /// <summary>The note without its octave: <c>D</c>, <c>A#</c>.</summary>
    public static string Letter(int midi) => Names[midi % 12].TrimEnd('-');

    private static readonly (string Suffix, string Label, int Third, int Fifth)[] Qualities =
    {
        ("maj", "MAJ", 4, 7), ("m", "MIN", 3, 7), ("dim", "DIM", 3, 6), ("aug", "AUG", 4, 8),
    };

    /// <summary>
    /// A chord token to its three notes, or null if it is not one.
    ///
    /// <c>Dm</c>, <c>Bb</c>, <c>Bdim</c>, <c>Caug</c>: no octave, so the root is put between
    /// G3 and F#4 - the octave where the pure-tone table is best in tune (see NoteTable) and
    /// no chord climbs into the lead's register. <c>Cmaj5</c>, <c>Dm4</c>, <c>Bdim3</c>: the
    /// quality, then the root's octave, for a chord that has to sit somewhere else - a scale's
    /// chords climbing to the I an octave up. The quality is spelled out (<c>maj</c>) when an
    /// octave follows, since <c>C5</c> is a note.
    /// </summary>
    public static int[] Chord(string token)
    {
        int semitone = Parse(token, out int at);
        if (semitone < 0) return null;
        string rest = token.Substring(Math.Min(at, token.Length));
        int digits = rest.Length;
        while (digits > 0 && char.IsDigit(rest[digits - 1])) digits--;
        string suffix = rest.Substring(0, digits), octaveText = rest.Substring(digits);

        var quality = suffix == "" ? Qualities[0] : Array.Find(Qualities, q => q.Suffix == suffix);
        if (quality.Suffix == null) return null;
        if (suffix == "" && octaveText != "") return null;      // a note, not a chord

        int root;
        if (octaveText == "")
        {
            root = 60 + (semitone + 12) % 12;
            if (root > 66) root -= 12;
        }
        else if (int.TryParse(octaveText, out int octave)) root = (octave + 1) * 12 + semitone;
        else return null;
        return new[] { root, root + quality.Third, root + quality.Fifth };
    }

    private static int QualityOf(int[] notes)
        => notes is { Length: 3 } ? Array.FindIndex(Qualities, q => notes[1] - notes[0] == q.Third && notes[2] - notes[0] == q.Fifth) : -1;

    /// <summary>The tracker column's name for a chord: <c>D MIN</c>, <c>B DIM</c>.</summary>
    public static string ChordLabel(int[] notes)
        => $"{Letter(notes[0])} {(QualityOf(notes) is int q and >= 0 ? Qualities[q].Label : "MAJ")}";

    /// <summary>
    /// The token that makes a chord: <c>Dm</c> where the root is in the octave a bare token
    /// puts it, <c>Dm5</c> where it is not - so every file written before chords could have
    /// an octave writes back exactly as it was. Null if the notes are not a triad a token
    /// can name.
    /// </summary>
    public static string ChordToken(int[] notes)
    {
        int q = QualityOf(notes);
        if (q < 0) return null;
        int root = notes[0], usual = 60 + root % 12;
        if (usual > 66) usual -= 12;
        string letter = Letter(root);
        if (root == usual) return letter + (q == 0 ? "" : Qualities[q].Suffix);
        return letter + Qualities[q].Suffix + (root / 12 - 1);
    }

    /// <summary>
    /// <c>D4</c>, <c>A#3</c> or <c>Bb3</c> to a note number, or -1.
    ///
    /// A flat is a lowercase b so that <c>Bb3</c> and <c>B3</c> cannot be confused — the
    /// letter is the note and the b after it is the accidental.
    /// </summary>
    public static int Parse(string token, out int next)
    {
        next = 0;
        if (string.IsNullOrEmpty(token)) return -1;

        int semitone = char.ToUpperInvariant(token[0]) switch
        {
            'C' => 0, 'D' => 2, 'E' => 4, 'F' => 5, 'G' => 7, 'A' => 9, 'B' => 11, _ => -1,
        };
        if (semitone < 0) return -1;

        next = 1;
        if (next < token.Length && token[next] == '#') { semitone++; next++; }
        else if (next < token.Length && token[next] == 'b') { semitone--; next++; }

        return semitone;
    }
}

/// <summary>
/// An instrument, the way an Atari music driver stored one: tables with one entry per
/// frame.
///
/// <b>The chip has no envelopes, so an instrument is a list of what to write.</b> Frame 0
/// of a note writes the first entry, frame 1 the second, and the last entry is held for as
/// long as the note lasts. That is the whole mechanism behind every attack, decay, drum
/// and vibrato on this page.
/// </summary>
/// <param name="Table">Which divider plays a note. Null for drums, which do not take
/// one — they carry their own <paramref name="Audf"/>.</param>
/// <param name="Volume">AUDC's volume nibble, per frame.</param>
/// <param name="Distortion">AUDC's high nibble, per frame. <c>A0</c>, <c>C0</c> and
/// <c>80</c> are what Atari BASIC's SOUND statement calls distortion 10, 12 and 8.</param>
/// <param name="Audf">A divider per frame, for an instrument with no note.</param>
/// <param name="Vibrato">Added to the note's divider, cycled, once the note is older than
/// <paramref name="VibratoDelay"/> frames.</param>
/// <param name="Release">Volume lost per frame after a note-off.</param>
public sealed record Instrument(
    string Name, NoteTable Table, int[] Volume, int[] Distortion,
    int[] Audf = null, int[] Vibrato = null, int VibratoDelay = 0, int Release = 2)
{
    public const int Tone = 0xa0, Buzz = 0xc0, Noise = 0x80;

    /// <summary>One entry of a per-frame table, holding the last one forever.</summary>
    public static int At(int[] table, int frame) => table[Math.Min(frame, table.Length - 1)];
}

public enum PartKind
{
    /// <summary>Each token names a drum.</summary>
    Drums,
    /// <summary>Each token is a note: <c>D4</c>, <c>A#3</c>.</summary>
    Notes,
    /// <summary>Each token is a chord, <c>Dm</c> or <c>Bb</c>, played as an arpeggio.</summary>
    Chords,
    /// <summary>No tracks: the low byte of the 16-bit channel above it. It counts for its
    /// partner and makes no sound of its own.</summary>
    Pair,
}

public enum CellKind { Empty, Off, Play }

/// <summary>One row of one channel. <see cref="Label"/> is what the tracker column
/// shows.</summary>
public readonly record struct Cell(CellKind Kind, Instrument Instrument, int[] Notes, string Label)
{
    public static readonly Cell Empty = new(CellKind.Empty, null, Array.Empty<int>(), "---");
    public static readonly Cell Off = new(CellKind.Off, null, Array.Empty<int>(), "OFF");
}

/// <summary>
/// Everything one channel plays: its instrument and a set of named one-bar tracks.
///
/// Tracks are written as text — one token a row — because that is the shape
/// a tracker shows and the only shape that can be read back by eye. <c>.</c> is nothing
/// new, <c>-</c> is a note-off.
/// </summary>
public sealed class Part
{
    private readonly Dictionary<string, Instrument> _kit;

    /// <summary>A drum part's tokens and the instruments they name. Empty for anything
    /// else.</summary>
    public IReadOnlyDictionary<string, Instrument> Kit
        => _kit ?? (IReadOnlyDictionary<string, Instrument>)new Dictionary<string, Instrument>();
    private readonly Dictionary<string, Cell[]> _tracks = new();
    private readonly List<string> _problems;

    public string Role { get; }
    public PartKind Kind { get; }

    /// <summary>The instrument every note or chord on this channel plays. Null for drums,
    /// whose tokens each pick one from the kit.</summary>
    public Instrument Voice { get; }

    /// <summary>What the channel is doing and why it was done that way on the hardware.</summary>
    public string Note { get; }

    public NoteTable Table => Voice?.Table;

    /// <summary>The song this part belongs to, for the length its tracks are laid out at.</summary>
    private readonly Tune _tune;

    internal Part(Tune tune, string role, PartKind kind, Instrument voice, Dictionary<string, Instrument> kit,
                  string note, List<string> problems)
    {
        _tune = tune;
        Role = role;
        Kind = kind;
        Voice = voice;
        _kit = kit;
        Note = note;
        _problems = problems;
    }

    public IReadOnlyDictionary<string, Cell[]> Tracks => _tracks;

    /// <summary>
    /// Adds a track. Problems are collected rather than thrown, the same contract as
    /// <see cref="EnvelopeBank"/>: a typo costs one cell, and the page shows it.
    /// </summary>
    /// <param name="voice">
    /// The instrument this track plays, when it is not the part's own.
    ///
    /// <b>This is how a channel changes sound between sections</b> — on the hardware it
    /// was never more than the driver being pointed at a different instrument table, and
    /// a track is the natural unit to point it from. Drum tracks ignore it; their tokens
    /// name the instrument.
    /// </param>
    public Part Track(string name, string rows, Instrument voice = null)
    {
        var tokens = rows.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cells = new Cell[_tune.RowsPerLine];

        if (Kind == PartKind.Pair)
            _problems.Add($"{Role} {name}: A LOW BYTE HAS NO TRACKS OF ITS OWN");

        for (int row = 0; row < cells.Length; row++)
        {
            if (row >= tokens.Length) { cells[row] = Cell.Empty; continue; }
            cells[row] = Parse(tokens[row], voice ?? Voice, out string problem);
            if (problem != null) _problems.Add($"{Role} {name} ROW {row}: {problem}");
        }

        if (tokens.Length != cells.Length)
            _problems.Add($"{Role} {name}: {tokens.Length} ROWS, NOT {cells.Length}");

        _tracks[name] = cells;
        return this;
    }

    /// <summary>
    /// One token. On a pitched channel a note carries its octave — <c>D4</c>, <c>A#3</c> —
    /// and a chord does not — <c>Dm</c>, <c>Bb</c> — so one track can hold either, and a
    /// channel can play broken chords in one section and an arpeggio in the next.
    /// </summary>
    private Cell Parse(string token, Instrument voice, out string problem)
    {
        problem = null;
        if (token == ".") return Cell.Empty;
        if (token == "-") return Cell.Off;

        if (Kind == PartKind.Drums)
        {
            if (_kit.TryGetValue(token, out var drum))
                return new Cell(CellKind.Play, drum, Array.Empty<int>(), drum.Name);

            problem = $"'{token}' IS NOT A DRUM";
            return Cell.Empty;
        }

        int semitone = NoteTable.Parse(token, out int at);
        string rest = token.Substring(Math.Min(at, token.Length));

        if (semitone >= 0 && int.TryParse(rest, out int octave))
        {
            int midi = (octave + 1) * 12 + semitone;
            return new Cell(CellKind.Play, voice, new[] { midi }, NoteTable.NoteName(midi));
        }

        if (NoteTable.Chord(token) is { } chord)
            return new Cell(CellKind.Play, voice, chord, NoteTable.ChordLabel(chord));

        problem = $"'{token}' IS NOT A NOTE OR A CHORD";
        return Cell.Empty;
    }

    /// <summary>Every distinct note this channel plays, by the table that plays it — one
    /// channel can use more than one, when its tracks change instrument.</summary>
    public IReadOnlyDictionary<NoteTable, SortedSet<int>> NotesUsed()
    {
        var used = new Dictionary<NoteTable, SortedSet<int>>();
        foreach (var track in _tracks.Values)
        {
            foreach (var cell in track)
            {
                if (cell.Instrument?.Table == null) continue;
                if (!used.TryGetValue(cell.Instrument.Table, out var notes))
                    used[cell.Instrument.Table] = notes = new SortedSet<int>();
                foreach (int midi in cell.Notes) notes.Add(midi);
            }
        }
        return used;
    }
}

/// <summary>
/// A song as an Atari tracker held one: four channels of one-bar tracks, and an order
/// list saying which track each channel plays on each line.
///
/// <b>The order list is the compression.</b> A drum bar written once is played on ten
/// lines. Every tracker of the period worked this way because memory was the constraint,
/// and it is also what makes a song readable: the list is the arrangement, the tracks are
/// the parts.
/// </summary>
public sealed class Tune
{
    /// <summary>
    /// Rows in every track and every line of the order list.
    ///
    /// <b>Sixteen by default — four beats of sixteenths — and not fixed</b>, because not all
    /// music counts in fours. A jig is 6/8: two beats of three eighths a bar, which sixteen
    /// rows cannot divide. With twelve rows of eighth notes a line is two bars of it. Set
    /// before the first part exists: tracks are laid out at this length when added.
    /// </summary>
    public int RowsPerLine { get; private set; } = 16;

    /// <summary>Rows in one counted beat — 4 for sixteenths in 4/4, 3 for eighths in 6/8,
    /// where the beat is a dotted quarter. What <see cref="Bpm"/> counts, and where the
    /// page marks the beat.</summary>
    public int RowsPerBeat { get; private set; } = 4;

    /// <summary>
    /// Changes the song's meter. Refused — false — once a part exists, since its tracks were
    /// laid out at the old length and would silently not fit the new one.
    /// </summary>
    public bool Meter(int rowsPerLine, int rowsPerBeat)
    {
        foreach (var part in Parts) if (part != null) return false;
        if (rowsPerLine < 1 || rowsPerBeat < 1 || rowsPerBeat > rowsPerLine) return false;

        RowsPerLine = rowsPerLine;
        RowsPerBeat = rowsPerBeat;
        return true;
    }

    /// <summary>One line of the order list, with the settings in force when it was added.</summary>
    private readonly record struct OrderLine(string[] Tracks, int FramesPerRow, int Audctl, string Section, bool Skip = false, bool[] Off = null);

    private readonly List<OrderLine> _order = new();
    private readonly List<string> _problems = new();

    /// <summary>Settable because a song file names itself partway through reading — the
    /// same reason as everything else a file sets.</summary>
    public string Name { get; set; }
    public string Key { get; set; }

    /// <summary>The file this came from, or null for a song built in code.</summary>
    public string Source { get; set; }

    /// <summary>Records a problem found while building — a reader's, usually, for a line
    /// it could not use. Collected rather than thrown, like everything else here.</summary>
    internal void Problem(string problem) => _problems.Add(problem);

    /// <summary>
    /// How many vertical blanks each row is held for, for the lines added from here on.
    /// This is the tempo: the driver runs once a frame, so a row can only last a whole
    /// number of them.
    ///
    /// <b>Settable mid-song, like a tracker's speed command</b> — a line keeps whatever was
    /// in force when it was added, so changing this between <see cref="Line(string[])"/> calls is a
    /// tempo change at that point in the song.
    /// </summary>
    public int FramesPerRow { get; set; }

    /// <summary>The line the song returns to after its last, so an intro plays once.</summary>
    public int LoopLine { get; set; }

    /// <summary>
    /// True for a song that plays once and stops — a jingle. After its last line the
    /// driver releases every channel, lets the notes fade by their own release rates, and
    /// reads no more rows. <see cref="LoopLine"/> means nothing for such a song.
    /// </summary>
    public bool Ends { get; set; }

    /// <summary>
    /// What the driver writes to AUDCTL, for the lines added from here on. Zero — every
    /// channel on the 64kHz clock, none joined — unless a part uses
    /// <see cref="NoteTable.Wide"/>.
    ///
    /// Per line for the same reason as <see cref="FramesPerRow"/>: a song can join two
    /// channels for one section and give them back for the next, which is exactly what a
    /// driver writing AUDCTL every frame makes possible.
    /// </summary>
    public int Audctl { get; set; }

    /// <summary>A name for the lines added from here on — "BALLAD", "HEAVY" — shown in the
    /// page header in place of the key. Null for a song in one piece.</summary>
    public string Section { get; set; }

    public Part[] Parts { get; } = new Part[PokeyChip.Channels];

    public IReadOnlyList<string> Problems => _problems;
    public int Lines => _order.Count;

    /// <summary>Every instrument the song file defined, by the name the file gives it -
    /// including any no channel uses. Empty for a song built in code, whose instruments
    /// are only reachable through its parts. An editor lists these; the driver never
    /// needs them.</summary>
    public IReadOnlyDictionary<string, Instrument> Instruments { get; internal set; }
        = new Dictionary<string, Instrument>();

    public Tune(string name, string key, int framesPerRow)
    {
        Name = name;
        Key = key;
        FramesPerRow = framesPerRow;
    }

    public int FramesPerRowAt(int line) => _order[line].FramesPerRow;
    public int AudctlAt(int line) => _order[line].Audctl;
    public string SectionAt(int line) => _order[line].Section;

    public double Bpm(double frameHz, int line)
        => frameHz * 60.0 / (FramesPerRowAt(line) * RowsPerBeat);

    public Part Drums(int channel, string role, Dictionary<string, Instrument> kit, string note)
        => Parts[channel] = new Part(this, role, PartKind.Drums, null, kit, note, _problems);

    public Part Notes(int channel, string role, Instrument voice, string note)
        => Parts[channel] = new Part(this, role, PartKind.Notes, voice, null, note, _problems);

    public Part Chords(int channel, string role, Instrument voice, string note)
        => Parts[channel] = new Part(this, role, PartKind.Chords, voice, null, note, _problems);

    /// <summary>Gives a channel over to be the low byte of the one above it.</summary>
    public Part Pair(int channel, string role, string note)
        => Parts[channel] = new Part(this, role, PartKind.Pair, null, null, note, _problems);

    /// <summary>The AUDCTL bits that join a channel to the one above it, or 0 for a channel
    /// that cannot be a low half.</summary>
    private static int JoinBit(int channel)
        => channel == 0 ? PokeyChip.CtlJoin12 : channel == 2 ? PokeyChip.CtlJoin34 : 0;

    /// <summary>The fast-clock bit a joined pair needs for <see cref="NoteTable.Wide"/>.</summary>
    private static int FastBit(int channel)
        => channel == 0 ? PokeyChip.CtlCh1Fast : PokeyChip.CtlCh3Fast;

    /// <summary>
    /// Whether a channel is the low half of a joined pair on a given line, which the driver
    /// does not write on its own account.
    ///
    /// Read from that line's AUDCTL rather than from the channel's part, because a channel
    /// can be a voice in one section and a low byte in the next. A <see cref="PartKind.Pair"/>
    /// part is the whole-song case, and <see cref="Check"/> holds it to its AUDCTL.
    /// </summary>
    public bool IsLowHalf(int channel, int line)
        => Parts[channel]?.Kind == PartKind.Pair || (AudctlAt(line) & JoinBit(channel)) != 0;

    /// <summary>
    /// Checks every line's AUDCTL against what its channels play.
    ///
    /// <b>Worth a check because every way of getting it wrong still makes a sound.</b>
    /// A 16-bit note without the join plays its high byte alone as an 8-bit divider;
    /// without the fast clock, the pair counts at 64kHz and every note is 28 times too
    /// low; an 8-bit note on a joined high half is read as the top of a 16-bit divider.
    /// All of them are plausible noises, which is how they would survive a listen. The
    /// driver cuts any such note rather than playing it wrong, so this is where it shows.
    /// </summary>
    public void Check()
    {
        for (int c = 0; c < Parts.Length; c++)
            if (Parts[c] == null) _problems.Add($"CHANNEL {c + 1} HAS NO PART");

        for (int line = 0; line < _order.Count; line++)
        {
            int audctl = AudctlAt(line);

            for (int c = 0; c < Parts.Length; c++)
            {
                if (Parts[c] == null) continue;

                if (Parts[c].Kind == PartKind.Pair && (audctl & JoinBit(c)) == 0)
                    _problems.Add($"LINE {line + 1}: {Parts[c].Role} IS A LOW BYTE BUT AUDCTL {audctl:X2} DOES NOT JOIN IT");

                string name = TrackAt(c, line);
                if (name == null || !Parts[c].Tracks.TryGetValue(name, out var cells)) continue;

                if (IsLowHalf(c, line))
                {
                    _problems.Add($"LINE {line + 1}: {Parts[c].Role} {name} IS ON A LOW BYTE AND WILL NEVER BE HEARD");
                    continue;
                }

                bool joined = c > 0 && IsLowHalf(c - 1, line);
                bool fast = c > 0 && (audctl & FastBit(c - 1)) != 0;

                foreach (var cell in cells)
                {
                    if (cell.Kind != CellKind.Play || cell.Instrument?.Table == null) continue;
                    bool wide = cell.Instrument.Table.Is16Bit;

                    if (wide && !(joined && fast))
                        _problems.Add($"LINE {line + 1}: {Parts[c].Role} {name} IS 16 BIT BUT AUDCTL {audctl:X2} "
                                      + "DOES NOT JOIN IT ON THE FAST CLOCK");
                    else if (!wide && joined)
                        _problems.Add($"LINE {line + 1}: {Parts[c].Role} {name} IS 8 BIT ON A JOINED PAIR");
                    else continue;

                    break;
                }
            }
        }
    }

    /// <summary>One line of the order list: a track name per channel, or null for
    /// silence. It takes the tempo, AUDCTL and section in force now.</summary>
    public Tune Line(params string[] tracks) => Line(tracks, skip: false, off: null);

    /// <summary>
    /// One line of the order list that may be switched off, whole or a channel at a time -
    /// what a song file says with <c>skip</c> in place of <c>line</c>, and <c>~</c> before a
    /// track name.
    ///
    /// <b>Switched off, not removed.</b> A skipped line stays in the order list - an editor
    /// counts it, shows it, and can switch it back on - and the driver jumps over it, as the
    /// exports do through <see cref="Heard"/>. A channel switched off keeps its track, so the
    /// notes can still be seen and edited, and is silent on that line. It is how an
    /// arrangement is tried: a section out, an instrument out, and back again, losing nothing.
    /// </summary>
    public Tune Line(string[] tracks, bool skip, bool[] off)
    {
        for (int c = 0; c < tracks.Length && c < Parts.Length; c++)
        {
            if (tracks[c] != null && !Parts[c].Tracks.ContainsKey(tracks[c]))
                _problems.Add($"LINE {_order.Count + 1}: {Parts[c].Role} HAS NO TRACK {tracks[c]}");
        }
        _order.Add(new OrderLine(tracks, FramesPerRow, Audctl, Section, skip, off != null && off.Any(o => o) ? off : null));
        return this;
    }

    public string TrackAt(int channel, int line)
    {
        var tracks = _order[line].Tracks;
        return channel < tracks.Length ? tracks[channel] : null;
    }

    /// <summary>Whether a line is played - false for one switched off with <c>skip</c>.</summary>
    public bool Plays(int line) => !_order[line].Skip;

    /// <summary>Whether a channel is switched off on a line (<c>~</c> before its track).</summary>
    public bool IsOff(int channel, int line)
    {
        var off = _order[line].Off;
        return off != null && channel < off.Length && off[channel];
    }

    /// <summary>The first line from <paramref name="line"/> on that plays, or -1.</summary>
    public int NextPlaying(int line)
    {
        for (int l = Math.Max(0, line); l < _order.Count; l++) if (!_order[l].Skip) return l;
        return -1;
    }

    /// <summary>What a channel sounds like on one row: <see cref="CellAt"/>, but silent where
    /// the channel is switched off on that line. What the driver plays; an editor showing the
    /// track uses <see cref="CellAt"/>.</summary>
    public Cell HeardAt(int channel, int line, int row)
        => IsOff(channel, line) ? (row == 0 ? Cell.Off : Cell.Empty) : CellAt(channel, line, row);

    /// <summary>
    /// The song as it is heard: the lines that play, in order, with every switched-off
    /// channel silent and the loop on the first line that plays from the loop line on. Itself
    /// when nothing is switched off. What an export writes, so that a file of the song says
    /// what the speaker said; the parts are shared, not copied.
    /// </summary>
    public Tune Heard()
    {
        if (_order.All(o => !o.Skip && o.Off == null)) return this;

        var heard = new Tune(Name, Key, FramesPerRow)
        {
            Source = Source, Ends = Ends, Audctl = Audctl, Section = Section,
            RowsPerLine = RowsPerLine, RowsPerBeat = RowsPerBeat, Instruments = Instruments,
        };
        for (int c = 0; c < Parts.Length; c++) heard.Parts[c] = Parts[c];
        heard._problems.AddRange(_problems);

        int loop = -1;
        for (int l = 0; l < _order.Count; l++)
        {
            var o = _order[l];
            if (o.Skip) continue;
            if (loop < 0 && l >= LoopLine) loop = heard._order.Count;
            var tracks = o.Tracks.Select((t, c) => IsOff(c, l) ? null : t).ToArray();
            heard._order.Add(new OrderLine(tracks, o.FramesPerRow, o.Audctl, o.Section, false, null));
        }
        heard.LoopLine = loop < 0 ? 0 : loop;
        return heard;
    }

    /// <summary>What a channel does on one row. A silent line is a note-off on its first
    /// row and nothing after, so whatever the previous line left sounding stops.</summary>
    public Cell CellAt(int channel, int line, int row)
    {
        if (IsLowHalf(channel, line)) return Cell.Empty;

        string name = TrackAt(channel, line);
        if (name == null || !Parts[channel].Tracks.TryGetValue(name, out var cells))
            return row == 0 ? Cell.Off : Cell.Empty;
        return cells[row];
    }
}

/// <summary>
/// The song built in code: <i>Rim Patrol</i>, and the instruments it plays.
///
/// <b>One song stays here on purpose, and the rest live in <c>songs/</c>.</b> Files are
/// how songs are edited and shared — see <see cref="SongFile"/> — but a song built in code
/// is two things a file cannot be: the worked example of setting one up programmatically,
/// and a song that is always there, so the page never has nothing to play. A file of the
/// same name replaces it (<see cref="SongFile.Catalog"/>), so <c>songs/rim-patrol.song</c>
/// is the copy that plays and this is the fallback.
///
/// <b>Free of MonoGame on purpose</b>, like <see cref="SoundLibrary"/>: it is tables of
/// integers and text, so a headless harness can render and measure it without an audio
/// device.
/// </summary>
public static class SongLibrary
{
    // --- The kit, all on one channel ---------------------------------------

    /// <summary>A click of tone, then distortion C falling away. The buzz table's divider
    /// rising from 20 to 80 is a pitch falling from 200Hz to 50, which is as close to a
    /// thump as this channel gets.</summary>
    public static readonly Instrument Kick = new("KICK", null,
        Volume: new[] { 15, 14, 13, 11, 9, 7, 5, 3, 1, 0 },
        Distortion: new[] { Instrument.Tone, Instrument.Buzz },
        Audf: new[] { 40, 20, 27, 34, 43, 52, 62, 71, 80 });

    /// <summary>One frame of tone for the crack, then the 17-bit poly for the rattle,
    /// darkening as it decays.</summary>
    public static readonly Instrument Snare = new("SNARE", null,
        Volume: new[] { 14, 13, 11, 9, 7, 6, 5, 4, 3, 2, 1, 0 },
        Distortion: new[] { Instrument.Tone, Instrument.Noise },
        Audf: new[] { 50, 6, 7, 8, 9, 10, 11, 12, 13 });

    /// <summary>The poly at divider 0, the brightest noise the chip has, for four
    /// frames.</summary>
    public static readonly Instrument Hat = new("HAT", null,
        Volume: new[] { 7, 4, 2, 1, 0 },
        Distortion: new[] { Instrument.Noise },
        Audf: new[] { 0 });

    public static readonly Instrument OpenHat = new("OPEN", null,
        Volume: new[] { 7, 6, 5, 5, 4, 4, 3, 3, 2, 2, 1, 1, 0 },
        Distortion: new[] { Instrument.Noise },
        Audf: new[] { 0 });

    // --- The pitched voices ---------------------------------------------------

    public static readonly Instrument Bass = new("BASS", NoteTable.Buzz,
        Volume: new[] { 13, 12, 11, 10, 10, 9 },
        Distortion: new[] { Instrument.Buzz },
        Release: 3);

    /// <summary>Vibrato of one divider step either way, arriving only on a held note —
    /// on a short one it would read as being out of tune rather than as expression.</summary>
    public static readonly Instrument Lead = new("LEAD", NoteTable.Tone,
        Volume: new[] { 12, 11, 11, 10, 10, 9, 9, 9, 8 },
        Distortion: new[] { Instrument.Tone },
        Vibrato: new[] { 0, 1, 1, 0, -1, -1 }, VibratoDelay: 16, Release: 1);

    public static readonly Instrument Arpeggio = new("ARPEGGIO", NoteTable.Tone,
        Volume: new[] { 8, 7, 6, 6, 5, 5, 5, 4 },
        Distortion: new[] { Instrument.Tone },
        Release: 1);

    private static readonly Dictionary<string, Instrument> Kit = new()
    {
        ["K"] = Kick, ["S"] = Snare, ["H"] = Hat, ["O"] = OpenHat,
    };

    /// <summary>The songs built in code. <see cref="SongFile.Catalog"/> is what to offer a
    /// player: these, each replaced by a file of the same name, then every other file.</summary>
    public static Tune[] BuiltIn { get; } = { RimPatrol() };

    private static Tune RimPatrol()
    {
        // Seven frames a row is 128 beats a minute at four rows a beat. The tempo can only
        // move in whole frames, so the choices near here are 150 (six) and 112 (eight).
        var tune = new Tune("RIM PATROL", "D MINOR", framesPerRow: 7);

        tune.Drums(0, "DRUMS", Kit,
            "One channel for the whole kit, so only one drum sounds at a time - a hat cuts " +
            "the kick off. Each drum is a table of frames: the kick is a click of tone and " +
            "then distortion C falling away, the snare a click and then noise.")
            .Track("INTRO", "K . . .  H . . .  K . . .  H . O .")
            .Track("BEAT",  "K . H .  S . H .  K . K .  S . H .")
            .Track("DRIVE", "K . H H  S . H .  K . H K  S . O .")
            .Track("FILL",  "K . H .  S . H .  S . S S  . S S S");

        tune.Notes(1, "BASS", Bass,
            "Distortion C, the Atari's bass. A pure tone on the 64 kHz clock cannot go below " +
            "125 Hz, but the 4-bit poly makes the same divider sound 7.5 times lower. Only " +
            "dividers whose AUDF+1 shares no factor with 15 give a clean note, so the table " +
            "has gaps - and the song is in D minor because this table has no good C.")
            .Track("DM", "D2 - D2 D3  - D2 - D2  D2 - D2 D3  - D2 A1 -")
            .Track("BB", "A#1 - A#1 F2  - A#1 - A#1  A#1 - A#1 F2  - A#1 F1 -")
            .Track("F",  "F2 - F2 F1  - F2 - F2  F2 - F2 F1  - F2 E2 -")
            .Track("A",  "A1 - A1 E2  - A1 - A1  A1 - A1 E2  - C#2 E2 -")
            .Track("DMEND", "D2 . . .  . . . .  D2 - D2 -  D2 - A1 -");

        tune.Notes(2, "LEAD", Lead,
            "Pure tone, distortion A. The vibrato is the divider nudged one whole step either " +
            "way once a note has been held a while - the coarsest vibrato there is, because a " +
            "divider has no half steps.")
            .Track("A1", "D5 . . .  A4 . C5 .  D5 . F5 .  E5 . D5 .")
            .Track("A2", "D5 . . .  . . C5 .  A#4 . . .  A4 . F4 .")
            .Track("A3", "C5 . . .  A4 . C5 .  F5 . . .  E5 . C5 .")
            .Track("A4", "C#5 . . .  . . . .  E5 . . .  - . . .")
            .Track("B1", "F5 . . .  E5 . D5 .  C5 . D5 .  A4 . . .")
            .Track("B2", "A#4 . . .  C5 . D5 .  F5 . . .  D5 . . .")
            .Track("B3", "C5 . A4 .  F4 . A4 .  C5 . F5 .  E5 . C5 .")
            .Track("B4", "C#5 . . .  E5 . . .  A5 . . .  - . . .")
            .Track("C1", "F5 . . .  . . . .  D5 . . .  F5 . . .")
            .Track("C2", "A5 . . .  . . . .  G5 . F5 .  E5 . C5 .")
            .Track("C3", "E5 . . .  C#5 . . .  A4 . C#5 .  E5 . . .")
            .Track("C4", "D5 . . .  . . . .  . . . .  - . . .");

        tune.Chords(3, "CHORDS", Arpeggio,
            "One channel cannot play three notes, so it plays each for one frame in turn. " +
            "Sixty changes a second is too fast to follow as a tune, and the ear hears a " +
            "chord instead.")
            .Track("DM", "Dm . . .  . . Dm .  . . . .  Dm . . .")
            .Track("BB", "Bb . . .  . . Bb .  . . . .  Bb . . .")
            .Track("F",  "F . . .  . . F .  . . . .  F . . .")
            .Track("A",  "A . . .  . . A .  . . . .  A . . .");

        // Drums, bass, lead, chords. Two bars of rhythm section, two with the chords in,
        // then three sections of four; the loop skips the intro.
        tune.Line("INTRO", "DM", null, null)
            .Line("INTRO", "BB", null, null)
            .Line("BEAT",  "F",  null, "F")
            .Line("FILL",  "A",  null, "A");

        tune.LoopLine = tune.Lines;

        tune.Line("BEAT",  "DM", "A1", "DM")
            .Line("BEAT",  "BB", "A2", "BB")
            .Line("BEAT",  "F",  "A3", "F")
            .Line("DRIVE", "A",  "A4", "A")
            .Line("BEAT",  "DM", "B1", "DM")
            .Line("BEAT",  "BB", "B2", "BB")
            .Line("BEAT",  "F",  "B3", "F")
            .Line("FILL",  "A",  "B4", "A")
            .Line("DRIVE", "BB", "C1", "BB")
            .Line("DRIVE", "F",  "C2", "F")
            .Line("DRIVE", "A",  "C3", "A")
            .Line("FILL",  "DMEND", "C4", "DM");

        tune.Check();
        return tune;
    }
}
