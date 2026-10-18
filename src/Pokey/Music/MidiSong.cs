using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Pokey.Music;

/// <summary>One note of a MIDI file, in the file's ticks.</summary>
public readonly record struct MidiNote(int Tick, int Length, int Pitch, int Velocity)
{
    public int End => Tick + Length;
}

/// <summary>
/// One voice of a MIDI file: the notes one track plays on one MIDI channel. A format 0 file
/// keeps every channel in its single track, and a format 1 file can too, so a track with
/// notes on three channels becomes three of these.
/// </summary>
public sealed class MidiVoice
{
    /// <summary>The track it came from, counting the first as 0.</summary>
    public int Track { get; init; }

    /// <summary>The MIDI channel, 0 to 15 - 9 is the General MIDI drum kit.</summary>
    public int Channel { get; init; }

    public string Name { get; init; }

    /// <summary>The General MIDI instrument, 1 to 128, from the first program change; 0 if
    /// there was none.</summary>
    public int Program { get; init; }

    public List<MidiNote> Notes { get; } = new();

    public bool Drums => Channel == 9;

    /// <summary>The most notes sounding at once.</summary>
    public int MostAtOnce
    {
        get
        {
            var edges = Notes.SelectMany(n => new[] { (n.Tick, 1), (n.End, -1) }).OrderBy(e => e.Item1).ThenBy(e => e.Item2);
            int now = 0, most = 0;
            foreach (var (_, step) in edges) most = Math.Max(most, now += step);
            return most;
        }
    }

    public double AveragePitch => Notes.Count == 0 ? 0 : Notes.Average(n => n.Pitch);
}

/// <summary>
/// A Standard MIDI File, read: its voices and notes in ticks, the tempo map, the time
/// signatures, key signatures and markers. Nothing is interpreted here beyond what the file
/// says - turning it into rows is the importer's business.
///
/// <b>What it reads.</b> Formats 0 and 1 (and 2, as if 1), running status, a note-on at
/// velocity 0 as a note-off, system exclusive and every meta event skipped over by its
/// length, and a file wrapped in a RIFF "RMID" chunk. A note-off pairs with the earliest
/// note still open at that pitch on that channel; a note never closed ends where its track
/// does. SMPTE time division is refused - it has no beats to put on a grid.
/// </summary>
public sealed class MidiSong
{
    public int Format { get; private init; }

    /// <summary>Ticks a quarter note.</summary>
    public int Division { get; private init; }

    /// <summary>The first track's name - which a format 1 file uses for the song's.</summary>
    public string Title { get; private set; }

    public List<MidiVoice> Voices { get; } = new();

    /// <summary>Microseconds a quarter note, from each tick it changes; 500000 (120 a
    /// minute) until the first.</summary>
    public List<(int Tick, int Micros)> Tempos { get; } = new();

    public List<(int Tick, int Numerator, int Denominator)> Meters { get; } = new();

    /// <summary>Key signatures: sharps (negative for flats), and whether it is minor.</summary>
    public List<(int Tick, int Sharps, bool Minor)> Keys { get; } = new();

    /// <summary>Markers and cue points, in order.</summary>
    public List<(int Tick, string Text)> Markers { get; } = new();

    /// <summary>The last tick anything ends at.</summary>
    public int End => Voices.SelectMany(v => v.Notes).Select(n => n.End).DefaultIfEmpty(0).Max();

    /// <summary>The file's own length: where its longest track ends. Past the last note when
    /// the music finishes on a rest - which a looping song's silent last bar is.</summary>
    public int Length { get; private set; }

    public int MicrosAt(int tick)
    {
        int micros = 500_000;
        foreach (var (at, value) in Tempos)
        {
            if (at > tick) break;
            micros = value;
        }
        return micros;
    }

    /// <summary>Seconds from the start to a tick, through every tempo change on the way.</summary>
    public double Seconds(int tick)
    {
        double seconds = 0;
        int at = 0, micros = 500_000;
        foreach (var (change, value) in Tempos)
        {
            if (change >= tick) break;
            seconds += (change - at) * micros / 1e6 / Division;
            at = change;
            micros = value;
        }
        return seconds + (tick - at) * micros / 1e6 / Division;
    }

    public static MidiSong Load(string path) => Read(File.ReadAllBytes(path));

    /// <summary>A file's bytes read, or an <see cref="InvalidDataException"/> saying what is
    /// wrong with them.</summary>
    public static MidiSong Read(byte[] file)
    {
        int at = IndexOf(file, "MThd");
        if (at < 0) throw new InvalidDataException("This is not a MIDI file: it has no MThd header.");

        int U32(int p) => (file[p] << 24) | (file[p + 1] << 16) | (file[p + 2] << 8) | file[p + 3];
        int U16(int p) => (file[p] << 8) | file[p + 1];
        Need(file, at + 14);

        int headerLength = U32(at + 4);
        int format = U16(at + 8), tracks = U16(at + 10), division = U16(at + 12);
        if ((division & 0x8000) != 0)
            throw new InvalidDataException("The file is timed in SMPTE frames, not beats, so it has no grid to put on rows.");
        if (division == 0) throw new InvalidDataException("The file says a quarter note is no ticks long.");

        string title = null;
        var song = new MidiSong { Format = format, Division = division };
        var voices = new Dictionary<(int Track, int Channel), (List<MidiNote> Notes, int Program)>();
        var names = new Dictionary<int, string>();
        at += 8 + headerLength;

        for (int t = 0; t < tracks && at + 8 <= file.Length; t++)
        {
            // Skip any chunk that is not a track - the format allows them.
            while (at + 8 <= file.Length && Encoding.ASCII.GetString(file, at, 4) != "MTrk") at += 8 + U32(at + 4);
            if (at + 8 > file.Length) break;

            int end = Math.Min(file.Length, at + 8 + U32(at + 4));
            int p = at + 8, tick = 0, status = 0, running = 0;
            var open = new Dictionary<(int Channel, int Pitch), Queue<(int Tick, int Velocity)>>();
            var programs = new Dictionary<int, int>();

            List<MidiNote> NotesOf(int channel)
            {
                if (!voices.TryGetValue((t, channel), out var v))
                    voices[(t, channel)] = v = (new List<MidiNote>(), programs.TryGetValue(channel, out int pr) ? pr : 0);
                return v.Notes;
            }

            int Var()
            {
                int value = 0;
                for (int i = 0; i < 4; i++)
                {
                    Need(file, p + 1);
                    byte b = file[p++];
                    value = (value << 7) | (b & 0x7F);
                    if ((b & 0x80) == 0) return value;
                }
                throw new InvalidDataException($"Track {t + 1} has a number longer than MIDI allows, at byte {p}.");
            }

            while (p < end)
            {
                tick += Var();
                Need(file, p + 1);
                // A meta or system event is read on its own; a channel status is remembered for
                // the data bytes after it (running status). Files that carry running status
                // across a meta event are common, if not what the standard says, so that works.
                int kindByte = file[p];
                if (kindByte is 0xFF or 0xF0 or 0xF7) { p++; status = kindByte; }
                else if ((kindByte & 0x80) != 0) { p++; status = running = kindByte; }
                else if (running == 0) throw new InvalidDataException($"Track {t + 1} has data with no status byte before it, at byte {p}.");
                else status = running;

                if (status == 0xFF)
                {
                    Need(file, p + 1);
                    int type = file[p++], length = Var();
                    Need(file, p + length);
                    var data = new ArraySegment<byte>(file, p, length);
                    string Text() => Encoding.Latin1.GetString(file, p, length).Trim();
                    switch (type)
                    {
                        case 0x03 when !names.ContainsKey(t): names[t] = Text(); break;
                        case 0x06 or 0x07: song.Markers.Add((tick, Text())); break;
                        case 0x51 when length >= 3: song.Tempos.Add((tick, (data[0] << 16) | (data[1] << 8) | data[2])); break;
                        case 0x58 when length >= 2: song.Meters.Add((tick, data[0], 1 << Math.Min(6, (int)data[1]))); break;
                        case 0x59 when length >= 2: song.Keys.Add((tick, (sbyte)data[0], data[1] != 0)); break;
                    }
                    p += length;
                    if (type == 0x2F) p = end;                   // end of track: nothing after counts
                    continue;
                }
                if (status is 0xF0 or 0xF7)
                {
                    int length = Var();
                    p += length;
                    continue;
                }

                int kind = status & 0xF0, channel = status & 0x0F;
                int dataBytes = kind is 0xC0 or 0xD0 ? 1 : 2;
                Need(file, p + dataBytes);
                int a = file[p], b = dataBytes == 2 ? file[p + 1] : 0;
                p += dataBytes;

                switch (kind)
                {
                    case 0xC0:
                        programs[channel] = a + 1;
                        if (voices.TryGetValue((t, channel), out var v) && v.Program == 0) voices[(t, channel)] = (v.Notes, a + 1);
                        break;
                    case 0x90 when b > 0:
                        if (!open.TryGetValue((channel, a), out var queue)) open[(channel, a)] = queue = new Queue<(int, int)>();
                        queue.Enqueue((tick, b));
                        NotesOf(channel);
                        break;
                    case 0x80:
                    case 0x90:
                        if (open.TryGetValue((channel, a), out var waiting) && waiting.Count > 0)
                        {
                            var (start, velocity) = waiting.Dequeue();
                            NotesOf(channel).Add(new MidiNote(start, Math.Max(1, tick - start), a, velocity));
                        }
                        break;
                }
            }

            // A note never switched off lasts to the end of its track.
            foreach (var ((channel, pitch), queue) in open)
                foreach (var (start, velocity) in queue)
                    NotesOf(channel).Add(new MidiNote(start, Math.Max(1, tick - start), pitch, velocity));

            if (t == 0) title = names.TryGetValue(0, out string n0) ? n0 : null;
            song.Length = Math.Max(song.Length, tick);
            at = end;
        }

        foreach (var ((track, channel), (notes, program)) in voices.OrderBy(v => v.Key.Track).ThenBy(v => v.Key.Channel))
        {
            if (notes.Count == 0) continue;
            bool several = voices.Keys.Count(k => k.Track == track && voices[k].Notes.Count > 0) > 1;
            string name = names.TryGetValue(track, out string said) && said.Length > 0 ? said : $"TRACK {track + 1}";
            if (several) name += $" (CHANNEL {channel + 1})";
            var voice = new MidiVoice { Track = track, Channel = channel, Name = name, Program = program };
            voice.Notes.AddRange(notes.OrderBy(n => n.Tick).ThenBy(n => n.Pitch));
            song.Voices.Add(voice);
        }

        song.Tempos.Sort((x, y) => x.Tick.CompareTo(y.Tick));
        song.Meters.Sort((x, y) => x.Tick.CompareTo(y.Tick));
        song.Markers.Sort((x, y) => x.Tick.CompareTo(y.Tick));
        song.Title = title;
        return song;
    }

    private MidiSong() { }

    private static void Need(byte[] file, int upTo)
    {
        if (upTo > file.Length) throw new InvalidDataException("The file ends in the middle of a track.");
    }

    private static int IndexOf(byte[] file, string tag)
    {
        var bytes = Encoding.ASCII.GetBytes(tag);
        for (int i = 0; i + bytes.Length <= file.Length && i < 1 << 16; i++)
            if (file.AsSpan(i, bytes.Length).SequenceEqual(bytes)) return i;
        return -1;
    }
}
