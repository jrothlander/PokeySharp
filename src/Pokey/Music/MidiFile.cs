using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Pokey.Music;

/// <summary>
/// A song as a Standard MIDI File, for a studio program to play with instruments of its own.
///
/// <b>What crosses over is the music, not the sound.</b> Every note, its length, the chords,
/// the drums, the tempo, the sections and the loop point are in the song already, and this
/// writes them out; the per-frame tables that make the chip sound like a chip - its
/// distortions, its noise drums, the fast arpeggio that fakes a chord on one channel - do
/// not, because on the other side they are replaced by real instruments. A chip chord comes
/// out as a chord played properly, which is the one place the file is better than the song.
///
/// <b>Nothing here is a guess about the music.</b> Rows become ticks exactly - 960 a beat
/// divides by the 2, 3, 4 and 6 rows a beat the songs use - the tempo is the speed the driver
/// plays at, and a note lasts until the next note or note-off on its channel, which is what
/// the tracker means. The only choices are which General MIDI instrument stands in for each
/// voice, and those are the caller's.
/// </summary>
public static class MidiFile
{
    /// <summary>Ticks in a quarter note. 960 divides by every rows-a-beat the format allows,
    /// so no note lands between ticks.</summary>
    public const int TicksPerBeat = 960;

    /// <summary>The General MIDI drum notes the kit letters stand for, by what the drum is
    /// called: the usual kit, so a snare is a snare wherever the file is opened.</summary>
    public static int DrumNote(string name)
    {
        string n = (name ?? "").ToUpperInvariant();
        bool Has(params string[] words) => words.Any(w => n.Contains(w));
        return Has("KICK", "BD", "BASS_DRUM", "BASS DRUM", "THUD", "BOOM", "DOOM") ? 36
             : Has("SNARE", "SN") ? 38
             : Has("CLAP") ? 39
             : Has("OPEN") ? 46
             : Has("CLOSED", "HAT", "HH", "TICK") ? 42
             : Has("CRASH", "CYMBAL") ? 49
             : Has("RIDE") ? 51
             : Has("TOM", "TIMP") ? 45
             : Has("WIND", "HISS", "NOISE") ? 82
             : 37;                                     // a side stick: audible, and not a kick
    }

    /// <summary>A General MIDI instrument (1 to 128) for a voice, from what it is called and
    /// which table it plays - a starting point the person changes. Chip names say more than
    /// they look: PIANO, BASS, FLUTE, STRINGS, ORGAN, LEAD.</summary>
    public static int Program(string role, string instrument, NoteTable table)
    {
        string n = ((role ?? "") + " " + (instrument ?? "")).ToUpperInvariant();
        bool Has(params string[] words) => words.Any(w => n.Contains(w));
        return Has("PIANO") ? 1
             : Has("HARPSICHORD", "HARP") ? 47
             : Has("ORGAN") ? 20
             : Has("GUITAR") ? 28
             : Has("BASS") ? 39                        // synth bass 1: a chip bass is a synth bass
             : Has("VIOLIN", "FIDDLE") ? 41
             : Has("CELLO") ? 43
             : Has("STRING", "PAD", "RING") ? 49
             : Has("TRUMPET", "BUGLE", "BRASS", "HORN") ? 57
             : Has("TROMBONE", "TUBA") ? 58
             : Has("SAX") ? 66
             : Has("OBOE") ? 69
             : Has("BASSOON") ? 71
             : Has("CLARINET") ? 72
             : Has("FLUTE", "PICC", "WHISTLE") ? 74
             : Has("CHOIR", "VOICE", "VOX") ? 53
             : Has("BELL", "CHIME", "GLOCK") ? 15
             : table == NoteTable.Buzz ? 39
             : 81;                                     // square lead: what a chip channel is
    }

    /// <summary>What a song becomes, beyond the notes themselves.</summary>
    public sealed class Options
    {
        /// <summary>The General MIDI instrument (1 to 128) for each channel; 0 to let
        /// <see cref="Program"/> choose.</summary>
        public int[] Programs = new int[MusicDriver.Channels];

        /// <summary>The General MIDI drum note for a kit letter, where the guess from the
        /// drum's name is not wanted.</summary>
        public Dictionary<string, int> Drums = new(StringComparer.Ordinal);

        /// <summary>Cut a note where its instrument falls silent, rather than holding it to
        /// the next note - what the sheet music page calls "notes end when the sound does".</summary>
        public bool AsHeard;

        /// <summary>Write the sections and the loop point as markers.</summary>
        public bool Markers = true;

        /// <summary>How many times round the loop to write. One is the song once through; a
        /// game's loop is the marker, not a repeat, so this is for listening.</summary>
        public int Times = 1;
    }

    private sealed record Note(int Tick, int Length, int Pitch, int Velocity);

    /// <summary>The song as the bytes of a .mid file.</summary>
    public static byte[] Write(Tune tune, Options options = null)
    {
        options ??= new Options();
        tune = tune.Heard();   // lines and channels switched off are not in the file
        if (tune.Lines == 0) throw new ArgumentException("The song has no lines to play.", nameof(tune));

        int rows = tune.RowsPerLine, perBeat = Math.Max(1, tune.RowsPerBeat);
        int ticksPerRow = TicksPerBeat / perBeat;
        if (TicksPerBeat % perBeat != 0) ticksPerRow = Math.Max(1, (int)Math.Round((double)TicksPerBeat / perBeat));

        // The lines to write: the song once, then round the loop again for each extra time.
        var lines = new List<int>();
        for (int line = 0; line < tune.Lines; line++) lines.Add(line);
        for (int again = 1; again < Math.Max(1, options.Times) && !tune.Ends; again++)
            for (int line = tune.LoopLine; line < tune.Lines; line++) lines.Add(line);

        // Where each written line starts, in ticks.
        var startOf = new int[lines.Count + 1];
        for (int i = 0; i < lines.Count; i++) startOf[i + 1] = startOf[i] + rows * ticksPerRow;

        var tracks = new List<byte[]> { Conductor(tune, options, lines, startOf, ticksPerRow) };
        int voice = 0;
        for (int c = 0; c < MusicDriver.Channels; c++)
        {
            var part = tune.Parts[c];
            if (part == null || part.Kind == PartKind.Pair) continue;

            bool drums = part.Kind == PartKind.Drums;
            var notes = Notes(tune, c, part, drums, lines, startOf, ticksPerRow, options);
            if (notes.Count == 0) continue;

            // Channel 10 is the drum kit, wherever a MIDI file is played; the voices take the
            // others in turn and step over it.
            if (!drums && voice == 9) voice++;
            tracks.Add(Voice(c, part, notes, drums ? 9 : voice++, drums, options));
        }

        var file = new List<byte>();
        file.AddRange(Encoding.ASCII.GetBytes("MThd"));
        file.AddRange(Big(6));
        file.AddRange(Big16(1));                 // one tempo track and a track a voice
        file.AddRange(Big16((short)tracks.Count));
        file.AddRange(Big16(TicksPerBeat));
        foreach (var track in tracks) file.AddRange(track);
        return file.ToArray();
    }

    public static void Save(string path, Tune tune, Options options = null)
        => File.WriteAllBytes(path, Write(tune, options));

    /// <summary>The first track: the song's name, its tempo wherever the speed changes, and
    /// the sections and loop point as markers.</summary>
    private static byte[] Conductor(Tune tune, Options options, List<int> lines, int[] startOf, int ticksPerRow)
    {
        var events = new List<(int Tick, byte[] Data)>();
        void Meta(int tick, int type, IEnumerable<byte> data)
        {
            var bytes = data.ToList();
            events.Add((tick, new byte[] { 0xFF, (byte)type }.Concat(Length(bytes.Count)).Concat(bytes).ToArray()));
        }

        Meta(0, 0x03, Encoding.ASCII.GetBytes(tune.Name ?? "SONG"));

        // The bar, so a line comes back a line when the file is read in again: beats of the
        // song's own beat, which is what this file calls a quarter note.
        if (Meter(tune.RowsPerLine, Math.Max(1, tune.RowsPerBeat)) is (int top, int power))
            Meta(0, 0x58, new byte[] { (byte)top, (byte)power, 24, 8 });

        int speed = -1;
        string section = null;
        for (int i = 0; i < lines.Count; i++)
        {
            int line = lines[i], tick = startOf[i];
            if (tune.FramesPerRowAt(line) != speed)
            {
                speed = tune.FramesPerRowAt(line);
                // Microseconds a quarter note, from the frames a row the driver counts.
                double beatsPerSecond = MusicDriver.FrameHz / (speed * (double)tune.RowsPerBeat);
                int micros = (int)Math.Round(1_000_000 / beatsPerSecond);
                Meta(tick, 0x51, new[] { (byte)(micros >> 16), (byte)(micros >> 8), (byte)micros });
            }
            if (!options.Markers) continue;

            string now = tune.SectionAt(line);
            if (now != null && (now != section || tune.StartsSection(line))) Meta(tick, 0x06, Encoding.ASCII.GetBytes(now));
            section = now;
            if (!tune.Ends && line == tune.LoopLine && (i == 0 || lines[i - 1] != line))
                Meta(tick, 0x06, Encoding.ASCII.GetBytes("loop start"));
        }
        if (options.Markers && !tune.Ends) Meta(startOf[^1], 0x06, Encoding.ASCII.GetBytes("loop end"));

        return Track(events);
    }

    /// <summary>A line as a time signature, if one says it exactly: beats over 4, or eighths
    /// or sixteenths when the line is not a whole number of beats. The denominator is a power
    /// of two, as the file stores it.</summary>
    private static (int Top, int Power)? Meter(int rowsPerLine, int rowsPerBeat)
    {
        for (int power = 2, per = 1; power <= 4; power++, per *= 2)
            if (rowsPerLine * per % rowsPerBeat == 0 && rowsPerLine * per / rowsPerBeat is > 0 and < 256)
                return (rowsPerLine * per / rowsPerBeat, power);
        return null;
    }

    /// <summary>Every note a channel plays, in ticks: a note lasts until the next note or
    /// note-off on its channel - or, as heard, until its instrument has faded.</summary>
    private static List<Note> Notes(Tune tune, int channel, Part part, bool drums, List<int> lines, int[] startOf, int ticksPerRow, Options options)
    {
        var notes = new List<Note>();
        var sounding = new List<Note>();
        int soundingAt = 0;

        void Stop(int tick)
        {
            foreach (var note in sounding) notes.Add(note with { Length = Math.Max(ticksPerRow / 4, tick - note.Tick) });
            sounding.Clear();
        }

        for (int i = 0; i < lines.Count; i++)
        {
            int line = lines[i];
            for (int row = 0; row < tune.RowsPerLine; row++)
            {
                var cell = tune.CellAt(channel, line, row);
                if (cell.Kind == CellKind.Empty) continue;
                int tick = startOf[i] + row * ticksPerRow;
                Stop(tick);
                if (cell.Kind != CellKind.Play) continue;

                int velocity = Velocity(cell.Instrument);
                if (drums)
                {
                    // A drum cell says which drum by its instrument, not by a note.
                    notes.Add(new Note(tick, ticksPerRow, Drum(part, cell.Instrument, options), velocity));
                    soundingAt = tick;
                    continue;
                }
                if (cell.Notes == null || cell.Notes.Length == 0) continue;

                foreach (int pitch in cell.Notes) sounding.Add(new Note(tick, 0, pitch, velocity));
                soundingAt = tick;

                // As heard: the instrument's volume table runs out, and the note with it.
                if (!options.AsHeard || Fades(cell.Instrument) is not int frames) continue;
                int end = tick, left = frames, at = row, on = i;
                while (left > 0 && on < lines.Count)
                {
                    left -= tune.FramesPerRowAt(lines[on]);
                    end += ticksPerRow;
                    if (++at < tune.RowsPerLine) continue;
                    at = 0;
                    on++;
                }
                foreach (var note in sounding.ToList())
                    if (end < startOf[^1]) { notes.Add(note with { Length = Math.Max(ticksPerRow / 4, end - note.Tick) }); sounding.Remove(note); }
            }
        }
        Stop(Math.Max(startOf[^1], soundingAt + ticksPerRow));
        return notes.OrderBy(n => n.Tick).ThenBy(n => n.Pitch).ToList();
    }

    /// <summary>Frames until an instrument is silent for good, or null if its last volume
    /// holds - a note that sustains.</summary>
    private static int? Fades(Instrument instrument)
    {
        if (instrument?.Volume == null || instrument.Volume.Length == 0 || instrument.Volume[^1] != 0) return null;
        return Math.Max(1, Array.FindLastIndex(instrument.Volume, v => v != 0) + 1);
    }

    /// <summary>How hard the note is struck: the loudest the instrument's volume table gets,
    /// out of the chip's fifteen - so a song's own dynamics cross over.</summary>
    private static int Velocity(Instrument instrument)
    {
        int volume = instrument?.Volume == null || instrument.Volume.Length == 0 ? 12 : instrument.Volume.Max();
        return Math.Clamp(20 + volume * 7, 1, 127);
    }

    /// <summary>One channel's track: its name, its instrument, and its notes.</summary>
    private static byte[] Voice(int c, Part part, List<Note> notes, int channel, bool drums, Options options)
    {
        var events = new List<(int Tick, byte[] Data)>
        {
            (0, new byte[] { 0xFF, 0x03 }.Concat(Length(part.Role.Length)).Concat(Encoding.ASCII.GetBytes(part.Role)).ToArray()),
        };

        if (!drums)
        {
            int program = options.Programs != null && c < options.Programs.Length && options.Programs[c] > 0
                ? options.Programs[c]
                : Program(part.Role, part.Voice?.Name, part.Voice?.Table);
            events.Add((0, new byte[] { (byte)(0xC0 | channel), (byte)Math.Clamp(program - 1, 0, 127) }));
        }

        foreach (var note in notes)
        {
            int pitch = Math.Clamp(note.Pitch, 0, 127);
            events.Add((note.Tick, new byte[] { (byte)(0x90 | channel), (byte)pitch, (byte)note.Velocity }));
            events.Add((note.Tick + Math.Max(1, note.Length), new byte[] { (byte)(0x80 | channel), (byte)pitch, 0 }));
        }
        return Track(events);
    }

    /// <summary>Which General MIDI drum a kit drum is: the caller's choice for that kit
    /// letter, or the guess from what the drum is called.</summary>
    private static int Drum(Part part, Instrument instrument, Options options)
    {
        string token = part.Kit?.FirstOrDefault(k => ReferenceEquals(k.Value, instrument)).Key;
        if (token != null && options.Drums != null && options.Drums.TryGetValue(token, out int said)) return said;
        return DrumNote(instrument?.Name ?? token);
    }

    // ---- The bytes ---------------------------------------------------------------------------

    /// <summary>Events in time order as an MTrk chunk, each with the ticks since the last.</summary>
    private static byte[] Track(List<(int Tick, byte[] Data)> events)
    {
        var body = new List<byte>();
        int at = 0;
        foreach (var (tick, data) in events.OrderBy(e => e.Tick).ThenBy(e => e.Data[0] & 0xF0))
        {
            body.AddRange(Variable(Math.Max(0, tick - at)));
            body.AddRange(data);
            at = Math.Max(at, tick);
        }
        body.AddRange(new byte[] { 0x00, 0xFF, 0x2F, 0x00 });        // end of track

        var chunk = new List<byte>(Encoding.ASCII.GetBytes("MTrk"));
        chunk.AddRange(Big(body.Count));
        chunk.AddRange(body);
        return chunk.ToArray();
    }

    private static byte[] Big(int value) => new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };
    private static byte[] Big16(int value) => new[] { (byte)(value >> 8), (byte)value };
    private static byte[] Length(int count) => Variable(count);

    /// <summary>A number as MIDI's variable-length quantity: seven bits a byte, the top bit
    /// set on every byte but the last.</summary>
    private static byte[] Variable(int value)
    {
        var bytes = new List<byte> { (byte)(value & 0x7F) };
        for (value >>= 7; value > 0; value >>= 7) bytes.Insert(0, (byte)(value & 0x7F | 0x80));
        return bytes.ToArray();
    }
}
