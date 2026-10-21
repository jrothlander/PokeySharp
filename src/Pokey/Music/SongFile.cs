using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Pokey;

namespace Pokey.Music;

/// <summary>
/// A song as a text file: the <c>.song</c> format, read and written.
///
/// <b>The sound bank's reasoning, applied to music.</b> Line-based rather than JSON, because
/// these are written and edited by hand and a missing comma should not take the file with
/// it; a track is one line, a token a row, exactly as it reads in code and on the
/// tracker page; and <b>nothing throws</b>. A line that cannot be used goes into
/// <see cref="Tune.Problems"/> and is skipped, so one typo costs one cell or one line
/// rather than the song — and the page shows the problem, which is the whole reason they
/// are collected.
///
/// The format is laid out in <c>Demo1/CLAUDE.md</c> under "Song files". In brief: a header
/// names the song, <c>instrument</c> blocks define the per-frame tables, <c>channel</c>
/// blocks hold the tracks, and <c>line</c>s at the end are the order list, with
/// <c>speed</c>, <c>audctl</c> and <c>section</c> changes placed between them exactly where
/// they take effect.
///
/// <b>Free of MonoGame</b>, like the rest of the song code, so the harness and
/// <c>tools/soundexport</c> read the same files the page does.
/// </summary>
public static class SongFile
{
    public const string Extension = ".song";

    // --- Reading -------------------------------------------------------------------

    /// <summary>Reads a song file. Never throws: an unreadable file is a song with one
    /// problem and no lines, which a caller can show rather than crash on.</summary>
    public static Tune Load(string path)
    {
        try
        {
            var tune = Parse(File.ReadAllLines(path), Path.GetFileName(path));
            tune.Source = path;
            return tune;
        }
        catch (Exception e)
        {
            var tune = new Tune(Path.GetFileNameWithoutExtension(path).ToUpperInvariant(), "", 6) { Source = path };
            tune.Problem($"COULD NOT READ {Path.GetFileName(path)} - {e.Message}");
            return tune;
        }
    }

    /// <summary>One token of a line, and where it started — kept so that a directive
    /// taking the rest of the line (a name, a note) can have it verbatim.</summary>
    private readonly record struct Token(string Text, int At);

    /// <summary>
    /// Splits a line into tokens, stopping at a comment.
    ///
    /// <b>A comment is a token that begins with <c>#</c></b>, not any <c>#</c> at all,
    /// because a sharp is part of a note: <c>C#4</c> is a note and <c># C#4</c> is a
    /// remark about one.
    /// </summary>
    private static List<Token> Tokenise(string line, out int end)
    {
        var tokens = new List<Token>();
        end = line.Length;
        int i = 0;

        while (i < line.Length)
        {
            while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
            if (i >= line.Length) break;

            int start = i;
            while (i < line.Length && !char.IsWhiteSpace(line[i])) i++;

            if (line[start] == '#') { end = start; break; }
            tokens.Add(new Token(line.Substring(start, i - start), start));
        }

        return tokens;
    }

    public static Tune Parse(IEnumerable<string> lines, string source)
    {
        var tune = new Tune("UNTITLED", "", framesPerRow: 6);
        var instruments = new Dictionary<string, Instrument>(StringComparer.OrdinalIgnoreCase);

        InstrumentBlock instrument = null;
        ChannelBlock channel = null;
        int number = 0;
        bool looped = false;

        void Problem(string text) => tune.Problem($"{source} LINE {number}: {text}");

        // A block ends at the next thing that is not part of it. Instruments are built at
        // the end of theirs, when every table is known; a channel's part is built at its
        // first track, when its note, kit and voice are.
        void CloseInstrument()
        {
            if (instrument == null) return;
            var built = instrument.Build(Problem);
            if (built != null)
            {
                if (instruments.ContainsKey(instrument.Id)) Problem($"INSTRUMENT {instrument.Id} IS DEFINED TWICE");
                instruments[instrument.Id] = built;
            }
            instrument = null;
        }

        void CloseChannel()
        {
            channel?.Part(tune, instruments, Problem);
            channel = null;
        }

        foreach (string raw in lines)
        {
            number++;
            var tokens = Tokenise(raw, out int end);
            if (tokens.Count == 0) continue;

            string keyword = tokens[0].Text.ToLowerInvariant();
            string Rest(int from = 1) => tokens.Count > from ? raw.Substring(tokens[from].At, end - tokens[from].At).Trim() : "";
            string[] Args() => tokens.Skip(1).Select(t => t.Text).ToArray();

            // Inside an instrument block: its tables.
            if (instrument != null && instrument.Accepts(keyword))
            {
                instrument.Set(keyword, Args(), Problem);
                continue;
            }

            // Inside a channel block: its note, kit, voice and tracks.
            if (channel != null && channel.Accepts(keyword))
            {
                channel.Set(keyword, Args(), Rest(), tune, instruments, Problem);
                continue;
            }

            CloseInstrument();

            switch (keyword)
            {
                case "song": tune.Name = Rest(); break;
                case "key": tune.Key = Rest(); break;

                case "instrument":
                    CloseChannel();
                    if (tokens.Count != 2) { Problem("AN INSTRUMENT NEEDS ONE NAME, WITH NO SPACES - USE _"); break; }
                    instrument = new InstrumentBlock(tokens[1].Text);
                    break;

                case "channel":
                    CloseChannel();
                    channel = ChannelBlock.Open(Args(), Rest(3), tune, Problem);
                    break;

                // The meter sets how many tokens a track is, so it has to be known before the
                // first track is read — and once tracks exist, changing it would leave them
                // the wrong length. Tune.Meter refuses in both cases; this says why.
                case "rows":
                case "beat":
                    CloseChannel();
                    if (tokens.Count != 2 || !int.TryParse(tokens[1].Text, out int count) || count is < 1 or > 64)
                    {
                        Problem(keyword == "rows" ? "ROWS IS ROWS A LINE, 1 TO 64" : "BEAT IS ROWS A BEAT, 1 TO 64");
                        break;
                    }
                    bool metered = keyword == "rows"
                        ? tune.Meter(count, Math.Min(tune.RowsPerBeat, count))
                        : tune.Meter(tune.RowsPerLine, count);
                    if (!metered)
                        Problem(tune.Parts.Any(p => p != null)
                            ? $"{keyword.ToUpperInvariant()} MUST COME BEFORE THE FIRST CHANNEL"
                            : "A BEAT CANNOT BE LONGER THAN A LINE");
                    break;

                case "speed":
                    CloseChannel();
                    if (tokens.Count == 2 && int.TryParse(tokens[1].Text, out int speed) && speed is >= 1 and <= 60)
                        tune.FramesPerRow = speed;
                    else Problem("SPEED IS FRAMES A ROW, 1 TO 60");
                    break;

                case "audctl":
                    CloseChannel();
                    if (tokens.Count == 2 && TryNumber(tokens[1].Text, out int audctl) && audctl is >= 0 and <= 255)
                        tune.Audctl = audctl;
                    else Problem("AUDCTL IS A BYTE - 40, $28 OR 0X28");
                    break;

                case "section":
                    CloseChannel();
                    tune.Section = tokens.Count > 1 ? Rest() : null;
                    break;

                case "loop":
                    CloseChannel();
                    tune.LoopLine = tune.Lines;
                    looped = true;
                    break;

                case "end":
                    CloseChannel();
                    tune.Ends = true;
                    break;

                // A line, or one switched off with "skip" - kept in the order list, jumped over
                // when played. A track named with "~" in front is switched off on that line:
                // kept, and silent. See Tune.Line(string[], bool, bool[]).
                case "line":
                case "skip":
                    CloseChannel();
                    if (tokens.Count - 1 > PokeyChip.Channels) { Problem($"A LINE HAS {PokeyChip.Channels} TRACKS AT MOST"); break; }
                    var tracks = new string[PokeyChip.Channels];
                    var off = new bool[PokeyChip.Channels];
                    for (int c = 1; c < tokens.Count; c++)
                    {
                        string name = tokens[c].Text;
                        if (name.Length > 1 && name[0] == '~') { off[c - 1] = true; name = name.Substring(1); }
                        tracks[c - 1] = name == "." ? null : name;
                    }
                    tune.Line(tracks, skip: tokens[0].Text.Equals("skip", StringComparison.OrdinalIgnoreCase), off);
                    break;

                default:
                    Problem($"'{tokens[0].Text}' IS NOT SOMETHING A SONG FILE SAYS HERE");
                    break;
            }
        }

        CloseInstrument();
        CloseChannel();
        tune.Instruments = instruments;

        // Both is a contradiction rather than a preference, and which was meant cannot be
        // guessed — so it is said, and the song ends, which is the safer of the two to get
        // wrong: a jingle that loops forever is worse than a loop that stops once.
        if (tune.Ends && looped)
            tune.Problem($"{source}: A SONG CANNOT BOTH LOOP AND END - IT WILL END");

        // A loop with no line after it would send the driver past the end of the order list.
        // An editor deleting the last line makes that easy to do by accident.
        if (looped && tune.Lines > 0 && tune.LoopLine >= tune.Lines)
        {
            tune.Problem($"{source}: LOOP COMES AFTER THE LAST LINE - IT WILL LOOP TO THE TOP");
            tune.LoopLine = 0;
        }

        if (tune.Lines == 0) tune.Problem($"{source}: NO LINES - NOTHING TO PLAY");
        else tune.Check();

        return tune;
    }

    /// <summary>An instrument being read: its tables, until the block ends.</summary>
    private sealed class InstrumentBlock
    {
        public readonly string Id;
        private NoteTable _table;
        private int[] _volume, _distortion, _audf, _vibrato;
        private int _delay, _release = 2;

        public InstrumentBlock(string id) => Id = id;

        public bool Accepts(string keyword)
            => keyword is "table" or "volume" or "distortion" or "audf" or "vibrato" or "delay" or "release";

        public void Set(string keyword, string[] args, Action<string> problem)
        {
            switch (keyword)
            {
                case "table":
                    _table = args.Length == 1 ? args[0].ToLowerInvariant() switch
                    {
                        "tone" => NoteTable.Tone,
                        "buzz" => NoteTable.Buzz,
                        "wide" => NoteTable.Wide,
                        "none" => null,
                        _ => Bad(),
                    } : Bad();
                    break;

                case "volume": _volume = Numbers(args, 0, 15); break;
                case "audf": _audf = Numbers(args, 0, 255); break;
                case "vibrato": _vibrato = Numbers(args, -255, 255); break;
                case "distortion": _distortion = Distortions(args); break;
                case "delay": _delay = Single(args, 0, 10000); break;
                case "release": _release = Single(args, 0, 15); break;
            }

            NoteTable Bad() { problem($"{Id}: TABLE IS TONE, BUZZ, WIDE OR NONE"); return null; }

            int[] Numbers(string[] values, int low, int high)
            {
                var result = new List<int>();
                foreach (string v in values)
                {
                    if (TryNumber(v, out int n) && n >= low && n <= high) result.Add(n);
                    else problem($"{Id}: '{v}' IS NOT A {keyword.ToUpperInvariant()} VALUE ({low} TO {high})");
                }
                return result.Count > 0 ? result.ToArray() : null;
            }

            int Single(string[] values, int low, int high)
                => Numbers(values, low, high) is { Length: 1 } one ? one[0] : 0;

            int[] Distortions(string[] values)
            {
                var result = new List<int>();
                foreach (string v in values)
                {
                    int? code = v.ToLowerInvariant() switch
                    {
                        "tone" => Instrument.Tone,
                        "buzz" => Instrument.Buzz,
                        "noise" => Instrument.Noise,
                        _ => TryNumber(v, out int n) && n >= 0 && n <= 255 && (n & 0x0f) == 0 ? n : null,
                    };
                    if (code is int c) result.Add(c);
                    else problem($"{Id}: '{v}' IS NOT A DISTORTION - TONE, BUZZ, NOISE OR A HIGH NIBBLE LIKE $A0");
                }
                return result.Count > 0 ? result.ToArray() : null;
            }
        }

        /// <summary>The instrument, or null with a problem if it cannot make a sound —
        /// no volume, no distortion, or neither a note table nor dividers of its own.</summary>
        public Instrument Build(Action<string> problem)
        {
            if (_volume == null) { problem($"{Id}: NO VOLUME TABLE"); return null; }
            if (_distortion == null) { problem($"{Id}: NO DISTORTION"); return null; }
            if (_table == null && _audf == null) { problem($"{Id}: NEEDS A TABLE FOR NOTES, OR AUDF FOR A DRUM"); return null; }

            return new Instrument(Id.Replace('_', ' '), _table, _volume, _distortion,
                                  Audf: _audf, Vibrato: _vibrato, VibratoDelay: _delay, Release: _release);
        }
    }

    /// <summary>A channel being read: everything its part needs before its first track.</summary>
    private sealed class ChannelBlock
    {
        private int _channel;
        private PartKind _kind;
        private string _role;
        private readonly StringBuilder _note = new();
        private readonly Dictionary<string, Instrument> _kit = new();
        private Instrument _voice;
        private Part _part;

        public static ChannelBlock Open(string[] args, string role, Tune tune, Action<string> problem)
        {
            if (args.Length < 3 || !int.TryParse(args[0], out int n) || n < 1 || n > PokeyChip.Channels)
            {
                problem($"CHANNEL IS: CHANNEL <1-{PokeyChip.Channels}> <DRUMS, NOTES OR PAIR> <NAME>");
                return null;
            }

            PartKind? kind = args[1].ToLowerInvariant() switch
            {
                "drums" => PartKind.Drums,
                "notes" or "chords" => PartKind.Notes,
                "pair" => PartKind.Pair,
                _ => null,
            };
            if (kind == null) { problem($"'{args[1]}' IS NOT DRUMS, NOTES OR PAIR"); return null; }
            if (tune.Parts[n - 1] != null) problem($"CHANNEL {n} IS DEFINED TWICE - THE SECOND REPLACES THE FIRST");

            return new ChannelBlock { _channel = n - 1, _kind = kind.Value, _role = role.ToUpperInvariant() };
        }

        public bool Accepts(string keyword) => keyword is "note" or "kit" or "voice" or "track";

        public void Set(string keyword, string[] args, string rest, Tune tune,
                        Dictionary<string, Instrument> instruments, Action<string> problem)
        {
            if (keyword != "track" && _part != null)
            {
                problem($"{_role}: {keyword.ToUpperInvariant()} MUST COME BEFORE THE FIRST TRACK");
                return;
            }

            switch (keyword)
            {
                case "note":
                    if (_note.Length > 0) _note.Append(' ');
                    _note.Append(rest);
                    break;

                case "kit":
                    foreach (string pair in args)
                    {
                        int eq = pair.IndexOf('=');
                        if (eq > 0 && instruments.TryGetValue(pair[(eq + 1)..], out var drum)) _kit[pair[..eq]] = drum;
                        else problem($"{_role}: '{pair}' IS NOT TOKEN=INSTRUMENT, OR THE INSTRUMENT IS NOT DEFINED YET");
                    }
                    break;

                case "voice":
                    if (args.Length == 1 && instruments.TryGetValue(args[0], out var voice)) _voice = voice;
                    else problem($"{_role}: VOICE NAMES ONE INSTRUMENT DEFINED ABOVE IT");
                    break;

                case "track":
                    var part = Part(tune, instruments, problem);
                    if (part == null || args.Length < 1) { problem($"{_role}: A TRACK NEEDS A NAME"); break; }

                    // "@NAME" after the track's name: this track plays another instrument.
                    Instrument over = null;
                    int from = 1;
                    if (args.Length > 1 && args[1].StartsWith('@'))
                    {
                        from = 2;
                        if (!instruments.TryGetValue(args[1][1..], out over))
                            problem($"{_role} {args[0]}: NO INSTRUMENT {args[1][1..]} DEFINED ABOVE IT");
                    }

                    if (part.Tracks.ContainsKey(args[0])) problem($"{_role}: TRACK {args[0]} IS DEFINED TWICE");

                    // Notes with no instrument would parse and then never sound.
                    if (part.Kind == PartKind.Notes && over == null && part.Voice == null)
                        problem($"{_role} {args[0]}: NO INSTRUMENT - GIVE THE CHANNEL A VOICE OR THE TRACK AN @NAME");

                    part.Track(args[0], string.Join(' ', args.Skip(from)), over);
                    break;
            }
        }

        /// <summary>The part, built the first time it is needed. A notes channel with no
        /// voice is allowed as long as every track names its own.</summary>
        public Part Part(Tune tune, Dictionary<string, Instrument> instruments, Action<string> problem)
        {
            if (_part != null) return _part;

            string note = _note.ToString();
            _part = _kind switch
            {
                PartKind.Drums => tune.Drums(_channel, _role, _kit, note),
                PartKind.Pair => tune.Pair(_channel, _role, note),
                _ => tune.Notes(_channel, _role, _voice, note),
            };
            return _part;
        }
    }

    /// <summary>A number as a file writes it: decimal, <c>$28</c> or <c>0x28</c>.</summary>
    private static bool TryNumber(string text, out int value)
    {
        if (text.StartsWith('$'))
            return int.TryParse(text[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    // --- Finding them -------------------------------------------------------------

    /// <summary>Every song file in a directory, alphabetically. A missing directory is an
    /// empty list, not an error.</summary>
    public static List<Tune> Discover(string directory)
    {
        var songs = new List<Tune>();
        if (!Directory.Exists(directory)) return songs;

        foreach (string path in Directory.GetFiles(directory, "*" + Extension).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            songs.Add(Load(path));
        return songs;
    }

    /// <summary>
    /// The songs to offer: the built-in ones, each replaced by a file of the same name,
    /// then every other file.
    ///
    /// <b>The file wins, the code is the fallback</b> — RimWalker's rule for its sound bank.
    /// A built-in song is always there, so the page never has nothing to play; a file of
    /// the same name is the edited copy, so changing one means changing the file.
    ///
    /// A file with nothing to play is left out, and its problem returned in
    /// <paramref name="skipped"/> for the caller to show — a driver cannot play a song
    /// with no lines, and a file that failed should say so rather than just be missing.
    /// </summary>
    public static List<Tune> Catalog(string directory, out List<string> skipped)
    {
        var songs = new List<Tune>(SongLibrary.BuiltIn);
        skipped = new List<string>();

        foreach (var file in Discover(directory))
        {
            if (file.Lines == 0)
            {
                skipped.Add(file.Problems.Count > 0 ? file.Problems[^1] : $"{file.Source}: NOTHING TO PLAY");
                continue;
            }

            int same = songs.FindIndex(s => string.Equals(s.Name, file.Name, StringComparison.OrdinalIgnoreCase));
            if (same >= 0) songs[same] = file;
            else songs.Add(file);
        }

        return songs;
    }

    // --- Writing ---------------------------------------------------------------------

    /// <summary>
    /// A song as the text of a song file.
    ///
    /// Written from what the song <i>is</i> — its instruments, parts, cells and order list —
    /// rather than from how it was typed, so a song built in code can be saved and shared.
    /// Reading the text back gives a song that renders the same samples; the harness
    /// asserts exactly that for every song, which is what keeps this writer and
    /// <see cref="Parse"/> describing the same format.
    /// </summary>
    public static string Write(Tune tune)
    {
        var text = new StringBuilder();
        void Line(string s = "") => text.Append(s).Append('\n');
        string N(int v) => v.ToString(CultureInfo.InvariantCulture);
        string Join(int[] values) => string.Join(' ', values.Select(N));

        // Instruments by first appearance, each with a file name that is one token and
        // unique in this file.
        var ids = new Dictionary<Instrument, string>(ReferenceEqualityComparer.Instance);
        void Name(Instrument instrument)
        {
            if (instrument == null || ids.ContainsKey(instrument)) return;
            string id = instrument.Name.Replace(' ', '_');
            string unique = id;
            for (int n = 2; ids.ContainsValue(unique); n++) unique = $"{id}_{n}";
            ids[instrument] = unique;
        }

        foreach (var part in tune.Parts)
        {
            if (part == null) continue;
            Name(part.Voice);
            foreach (var drum in part.Kit.Values) Name(drum);
            foreach (var track in part.Tracks.Values)
                foreach (var cell in track) Name(cell.Instrument);
        }

        Line($"# {tune.Name} - a song for the POKEY music driver in Pokey/Music.");
        Line("# The format is in Demo1/CLAUDE.md, under \"Song files\". Instruments are tables with");
        Line($"# one entry per frame; each channel holds named tracks of {N(tune.RowsPerLine)} rows; the lines at the");
        Line("# end are the order list. In a track '.' holds and '-' releases. '#' starts a comment.");
        Line();
        Line($"song   {tune.Name}");
        if (!string.IsNullOrEmpty(tune.Key)) Line($"key    {tune.Key}");
        if (tune.RowsPerLine != 16 || tune.RowsPerBeat != 4)
        {
            Line($"rows   {N(tune.RowsPerLine)}");
            Line($"beat   {N(tune.RowsPerBeat)}");
        }

        foreach (var (instrument, id) in ids)
        {
            Line();
            Line($"instrument {id}");
            Line($"  table      {TableName(instrument.Table)}");
            Line($"  volume     {Join(instrument.Volume)}");
            Line($"  distortion {string.Join(' ', instrument.Distortion.Select(DistortionName))}");
            if (instrument.Audf != null) Line($"  audf       {Join(instrument.Audf)}");
            if (instrument.Vibrato != null)
            {
                Line($"  vibrato    {Join(instrument.Vibrato)}");
                Line($"  delay      {N(instrument.VibratoDelay)}");
            }
            Line($"  release    {N(instrument.Release)}");
        }

        for (int c = 0; c < tune.Parts.Length; c++)
        {
            var part = tune.Parts[c];
            if (part == null) continue;

            string kind = part.Kind switch { PartKind.Drums => "drums", PartKind.Pair => "pair", _ => "notes" };
            Line();
            Line($"channel {c + 1} {kind} {part.Role}");
            foreach (string chunk in Wrap(part.Note, 82)) Line($"  note  {chunk}");
            if (part.Kind == PartKind.Drums)
                Line($"  kit   {string.Join(' ', part.Kit.Select(k => $"{k.Key}={ids[k.Value]}"))}");
            if (part.Voice != null) Line($"  voice {ids[part.Voice]}");

            int width = part.Tracks.Keys.DefaultIfEmpty("").Max(k => k.Length);
            foreach (var (name, cells) in part.Tracks)
            {
                // The track's own instrument, if its notes play one other than the voice.
                var own = cells.FirstOrDefault(cell => cell.Kind == CellKind.Play).Instrument;
                string over = part.Kind != PartKind.Drums && own != null && own != part.Voice ? $"@{ids[own]} " : "";

                var tokens = cells.Select(cell => TokenFor(cell, part)).ToArray();
                string rows = string.Join("  ", Enumerable.Range(0, (tokens.Length + tune.RowsPerBeat - 1) / tune.RowsPerBeat)
                    .Select(beat => string.Join(' ', tokens.Skip(beat * tune.RowsPerBeat).Take(tune.RowsPerBeat))));

                Line($"  track {name.PadRight(width)} {over}{rows}");
            }
        }

        Line();
        Line("# The order list: one line per bar, a track per channel, '.' for none.");

        int speed = -1, audctl = -1;
        string section = null;
        bool first = true;

        for (int l = 0; l < tune.Lines; l++)
        {
            if (l == tune.LoopLine && !tune.Ends) Line("loop");
            if (first || tune.SectionAt(l) != section)
            {
                section = tune.SectionAt(l);
                if (section != null || !first) Line(section != null ? $"section {section}" : "section");
            }
            if (tune.FramesPerRowAt(l) != speed) Line($"speed  {N(speed = tune.FramesPerRowAt(l))}");
            if (tune.AudctlAt(l) != audctl) Line($"audctl ${(audctl = tune.AudctlAt(l)):X2}");
            first = false;

            var tracks = Enumerable.Range(0, tune.Parts.Length)
                                   .Select(c => tune.TrackAt(c, l) is string t ? (tune.IsOff(c, l) ? "~" + t : t) : ".");
            Line($"{(tune.Plays(l) ? "line" : "skip")}   {string.Join(' ', tracks.Select(t => t.PadRight(6))).TrimEnd()}");
        }

        if (tune.Ends) Line("end");
        return text.ToString();
    }

    /// <summary>A cell back as the token that makes it: a note with its octave, a chord
    /// without one, a drum by its kit token.</summary>
    private static string TokenFor(Cell cell, Part part)
    {
        switch (cell.Kind)
        {
            case CellKind.Empty: return ".";
            case CellKind.Off: return "-";
        }

        if (part.Kind == PartKind.Drums)
            return part.Kit.First(k => ReferenceEquals(k.Value, cell.Instrument)).Key;

        int root = cell.Notes[0];
        if (cell.Notes.Length == 1) return $"{NoteTable.Letter(root)}{root / 12 - 1}";
        return NoteTable.Letter(root) + (cell.Notes[1] - root == 3 ? "m" : "");
    }

    /// <summary>A note as several <c>note</c> lines of a readable width — the reader joins
    /// them back with a space, so where the breaks fall changes nothing.</summary>
    private static IEnumerable<string> Wrap(string text, int width)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;

        var line = new StringBuilder();
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                yield return line.ToString();
                line.Clear();
            }
            if (line.Length > 0) line.Append(' ');
            line.Append(word);
        }
        if (line.Length > 0) yield return line.ToString();
    }

    private static string TableName(NoteTable table)
        => table == null ? "none" : table == NoteTable.Tone ? "tone" : table == NoteTable.Buzz ? "buzz" : "wide";

    private static string DistortionName(int code) => code switch
    {
        Instrument.Tone => "tone",
        Instrument.Buzz => "buzz",
        Instrument.Noise => "noise",
        _ => $"${code:X2}",
    };
}
