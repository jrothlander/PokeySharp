# Demo1

Three demonstrations of making noise from nothing but numbers: a browsable library of
chip sounds, a voice, and a four-channel song played the way an Atari 800 played one.
It is the runnable companion to `docs/SOUND-RECIPE.md` (in the workshop repo this came from, not here).

> **About the songs named below.** This repository carries seven songs: *La Carrera*,
> *Rim Patrol*, *Rim Suite*, *Warp Lane*, *Fur Elise*, *Morning Mood* and *In the Hall of
> the Mountain King*. The pages below describe the whole library of the workshop
> repository this came from, so a song named here may not be in `songs/`. Everything said
> about the format, the driver and the chip applies either way, and the song page plays
> any `.song` file you put in the folder.

**Nothing here loads a file at build time.** Not a sound, not a font, not a word of
the speech, not a note of the song. Every effect is a handful of integers rendered
through an emulated POKEY, every letter on screen is line segments, and every spoken
phrase is synthesised from its spelling. `Content/Content.mgcb` is empty and should
stay that way — that is the whole argument of the demo.

## The three pages

`Game1` owns which page is showing and draws the tab bar; everything below the bar
belongs to the page. `1`, `2` and `3` switch, and the tab labels are clickable.
Switching calls `IPage.Leave` on the page being left — a default no-op that only the
song page overrides.

**Not Tab, which is what the shortcut used to be.** MonoGame's WindowsDX window is a
WinForms form, and Tab is a dialog navigation key there — it never reaches
`Keyboard.GetState`, so the shortcut silently did nothing.

- **`SoundPage`** — a list of sounds down the left, and for the selected one its
  AUDF/AUDC chains, what the divider and volume do over its life, and the rendered
  waveform. `B` cycles banks, `O` opens one from disk.
- **`VoicePage`** — a dozen lines said through `Speech/` at five settings. The level
  count is the interesting one: 15 is four bits, all a POKEY's volume nibble could
  hold and how S.A.M. actually spoke, against 255 for the same synthesiser
  unquantised.
- **`SongPage`** — fourteen songs, `N` to switch. *Rim Patrol* is drums on channel 1,
  distortion-C bass on 2, lead on 3 and arpeggiated chords on 4; *Warp Lane* is drums,
  lead, and a 16-bit bass on channels 3 and 4 joined; *Rim Suite* is five contrasting
  sections that change tempo, AUDCTL and every channel's instrument as they go; *La
  Carrera* is a ranchera with no drums — bass, strummed guitar, voice and a second
  voice in thirds; *Cavern* is a dungeon theme that plays its melody plain and then
  through POKEY's high-pass filter; *Guardian* is a boss battle whose drums run on the
  9-bit noise counter; *High Seas* is a sea-shanty jig in 6/8, the one song not counted
  in fours; *Pachelbel Canon* is the public-domain canon, three violins playing one line
  a ground apart over the same eight bass notes; *Alla Hornpipe* is Handel's, from the
  *Water Music*, with strings, trumpets and horns trading the tune over a 16-bit bass;
  *In the Hall of the Mountain King* is Grieg's, one theme handed up through the
  orchestra and then accelerated to the end with a `speed` line every two bars;
  *Platform Shoes* is an original disco song — four on the floor, an octave bass, off-beat
  guitar scratches — whose drum channel plays kick and clap as one instrument;
  *William Tell* is the whole gallop from Rossini's overture, 228 bars and three minutes,
  a row to the sixteenth note so the galloping rhythm is exact;
  *Stage Clear* and *Game Over* are
  jingles that play once and end.
  The left half is the song as a
  tracker shows it; the right half is each channel's AUDF/AUDC as the driver wrote them
  this frame, the nine bytes at `D200`, and the output. `M` mutes a channel and `S`
  solos it while it plays.

## The song page

Four files, split the same way as the sounds: **`SongLibrary`** is the data model
(note tables, instruments, parts, `Tune`) and the one song built in code;
**`SongFile`** reads and writes the `.song` files in `songs/`; **`MusicDriver`** is the
player. Those three now live in the Pokey library, in `Pokey/Music` under the
`Pokey.Music` namespace — they were written here and moved when the song editor became
the third program to need them. **`SongPage`**, the half that needs an audio device, is
the only one left in Demo1.

**`MusicDriver` is a VBI music driver, deliberately.** Once per frame it reads a row
if one starts, steps every channel's instrument one frame through its tables, and
writes AUDF/AUDC for four channels plus AUDCTL. Nothing else makes the music — an
instrument is a table of per-frame volumes, distortions and dividers, a chord is one
note per frame in turn, a drum is a frame table with no note. Resist adding an
envelope or a mixer that is not "a byte written once a frame": the page's claim is
that this is all there was.

**One `PokeyChip` for all four channels**, unlike the sound pages, which render each
effect on its own. The channels share the mix, the headroom and the poly counters as
they did on the hardware — which is also why `MusicDriver.Gain` is 1.25 rather than
`SoundRender`'s 8: `Mix()` already divides by four channels' worth.

**The frame rate is derived, not 60.** 262 scanlines of 114 cycles off the POKEY's
own crystal is 59.92 Hz, and that 114 is the same divider as the chip's "15 kHz"
clock. Tempo can only move in whole frames per row: 7 is 128 BPM, 6 would be 150.

**Streamed, not pre-rendered.** Everything else in the demo renders once; the song
runs live through a `DynamicSoundEffectInstance`, the way PokeyLab's chip does, so a
mute lands mid-song and the registers on screen are the ones being written. Two
consequences worth not breaking:

- **The page shows what is heard, not what is rendered.** Audio is queued a few
  blocks ahead, so `MusicDriver` keeps a short history of frames tagged with their
  start sample, and the page asks for the one under `submitted − pending × block`.
  Drawing the driver's own position instead puts the tracker visibly ahead of the
  sound.
- **Only this page's `Update` feeds the stream**, so `Leave` pauses it. A song left
  playing behind another page would starve and stutter.

No audio device is not a failure: `Pump` then advances the driver on elapsed time
and discards the samples, so the page still runs, silently, and says so.

**The key was chosen by the tables.** A note is a divider, so every note misses equal
temperament by something. `NoteTable` computes the misses rather than hard-coding a
table, and the page prints the worst per channel. The pure-tone table is within ~10
cents through octave 4; the distortion-C bass table has no usable C (+31) or A2
(+33), so the song is in D minor, where every bass note lands within 14 cents. Rewrite
a track into another key and the page's tuning line is where it will show.

**Distortion C's table has gaps, and they are real.** The 4-bit poly repeats every 15
base clocks, so a divider whose AUDF+1 shares no factor with 15 walks all fifteen
positions and sounds 7.5× lower than a pure tone; one where AUDF+1 is a multiple of 3
or 5 gets a shorter pattern and a different pitch. `NoteTable.Buzz` skips those. The
harness proves the period at base-clock resolution rather than trusting the argument.

**The two songs are two answers to one problem**, and that is why there are two. A
single channel cannot play a bass note in tune. *Rim Patrol* keeps four voices and
fakes it with distortion C; *Warp Lane* sets AUDCTL `28` (`CtlJoin34 | CtlCh3Fast`),
which makes channels 3 and 4 one 16-bit counter on the 1.79 MHz clock — every bass
note within 0.1 cent, octave bounces available, any key — and pays with the chords.
Keep the second song's point if it is ever replaced: something should show AUDCTL
doing work.

How a pair is modelled, because it is the one place a channel is not independent:

- **`NoteTable.Wide`** is a 16-bit table with the fast clock's reload addend of 7.
  `Is16Bit` is what tells the driver to split the divider — low byte to the channel
  below, high byte to this one.
- **Pairing is per line, read from that line's AUDCTL** (`Tune.IsLowHalf(channel,
  line)`), because *Rim Suite* joins channels 3 and 4 for one section only. A
  `PartKind.Pair` part is the whole-song case — *Warp Lane*'s channel 3 — and has no
  tracks. Either way the driver does not write a low half on its own account (`Quiet`
  zeroes it and clears whatever it was playing, then the partner writes its AUDF);
  `CellAt` returns empty for it; `M` and `S` on it act on its partner instead.
- **A note whose width does not match its channel is cut, never played.** Across a line
  where AUDCTL changes, a 16-bit note can be left fading on a channel that is no longer
  joined, or an 8-bit one on a channel that now is. Both would sound as a wrong pitch
  that still sounds like music, so `Write` drops them.
- **`Tune.Check` catches a mis-set AUDCTL, line by line**, which matters because every
  way of getting it wrong still makes a plausible noise: without the join the high byte
  plays alone; without the fast clock every note is 28 times too low; an 8-bit track on
  a joined high half is read as the top of a 16-bit divider; a track on a low half is
  never heard.
- **`Frame.Divider`** carries the whole 16-bit value, since neither AUDF byte alone
  says what note it is.

**Tempo, AUDCTL and a section name are per line**, like a tracker's speed command:
`Tune.FramesPerRow`, `Audctl` and `Section` are the settings for lines added *from then
on*, and each order line keeps what was in force when it was added. That is what lets
*Rim Suite* run at 90, 112, 128 and 150 BPM and join a pair for one section — and the
page header reads the line being heard for its section and tempo, so both follow the
song. *In the Hall of the Mountain King* uses the same thing as an accelerando: Grieg's
"stringendo al fine" is a `speed` line every two bars, 13 frames a row down to 7, 138
beats a minute to 257. Whole frames make it a staircase rather than a slope, but steps
of one frame in two bars are close enough that it is heard as a slope.

**The header gives up status to fit a long name.** A song name and section can meet the
status in the middle of the line, so `DrawHeader` measures both and drops the status's
parts in order — the frame rate, then the frame count, then the speed — until it clears
the title. Nothing on the left is shortened.

**A track can name its own instrument** (`Part.Track(name, rows, voice)`), which is how
a channel changes sound between sections — on the hardware that was only ever the
driver pointed at a different table. On a pitched channel one track can hold notes and
chords alike: a token with an octave (`D4`) is a note, one without (`Dm`) is a chord, and a
chord can name its quality and root octave (`Bdim`, `Caug`, `Dm4`, `Cmaj5`).
`Part.NotesUsed` groups by table, since a channel that changes instrument can use more
than one, and the page prints a tuning line for each.

**A strum is an arpeggio with a fast decay.** *La Carrera*'s guitar (`STRUM` in `songs/la-carrera.song`)
plays each chord's notes one frame apart and is gone in four or five frames — a real
strum is the same thing, only faster — so the chord channel that shimmers under *Rim
Patrol* becomes a rhythm guitar with nothing but a shorter volume table. It is also the
one song with no drum part: channel 1 is the bass, which the page and driver handle
like any other pitched part.

**The high-pass filter needs a channel that is never heard.** *Cavern* sets AUDCTL `02`
(`CtlFilter1By3`): every time channel 3 fires, channel 1's output is latched, and what
channel 1 plays is its wave XOR the latch. So channel 3 is written a divider and a
volume of zero — its `SHADOW` instrument plays the melody's notes one step flat — and it
shapes channel 1 without a sound of its own. Measured on the emulator, at E4:

| Channel 3 against channel 1 | What channel 1 does |
|---|---|
| filter off | a square, duty cycle about 50% |
| **same divider** | **silence** — the latch lands on the same phase every time and cancels it |
| one step off | duty sweeps 3% to 96%, about 6½ sweeps a second — the shimmer |
| two or three off | the same sweep at 13 and 20 a second — a wobble |

Two consequences that are easy to break. **The filtered lead has no vibrato**: vibrato
moves its divider, and any frame where it lands on the shadow's is a frame of silence.
And **the shadow's tracks must mirror the lead's exactly**, one step flat, or the same
thing happens on the note that differs — the check that was run walks every filtered
frame and finds the gap at exactly one in all 1,123 where the lead sounds. The song plays
the melody unfiltered first and then filtered, so the one bit of AUDCTL is the only
difference between them.

**The 9-bit counter turns noise into a pitch.** *Guardian* sets AUDCTL `80`
(`CtlNineBitPoly`), and every noise channel — distortion 0 and 4, the ones on the long
counter — switches from 17 bits to 9. Measured on the emulator: on the 17-bit counter a
noise channel never repeats within 1,200 underflows; on the 9-bit one it repeats every
**73**, so it is a buzz at 64 kHz / (AUDF+1) / 73 — 876 Hz at divider 0, 125 Hz at 6.
Seventy-three rather than 511 for the reason distortion C's table has gaps: the channel
samples the counter every 28 base clocks, and 511 = 7 × 73 shares the 7. The kick is
unaffected (distortion C is the 4-bit counter). The song plays one drum bar each way
before the fight, and a check of those two bars found all eight channel bytes identical
in all 80 frames and only AUDCTL different. The page names the counter from AUDCTL, not
from the distortion code alone — before that, the snare read "17 BIT" on the frame it was
audibly the 9-bit rasp.

*Rim Suite* also uses the oldest thickening trick there is: in the solo, channel 3
plays the guitar's notes through `DOUBLE` (`songs/rim-suite.song`), whose one-entry vibrato table is
a constant +1 on the divider. Two channels a few hertz apart beat against each other,
and the ear hears one chorused sound.

**Switching song is a hard cut** (`MusicDriver.Load`). Every channel is cleared,
because two songs need not agree on what a channel is — a note left fading on channel
4 of a song where 3 and 4 are one counter would land in the wrong register. The change
is applied at the next frame, and each `Frame` carries its `Tune`, so for the few
blocks still queued the page keeps drawing the old song against its own order list
rather than indexing the new one with the old line numbers.

**Editing is not switching** (`MusicDriver.Edit`), and PokeyTracker is why the difference
exists. An edited copy of the song being played is swapped in at the next frame with the
place kept — line, row, frame within the row, every note still sounding, the mutes —
because an editor makes one on every keystroke, and restarting the line each time would
make an edit impossible to hear in context. Nothing is cleared, since nothing is
inconsistent: a sounding note keeps its instrument until its channel's next cell, and a
note whose width no longer suits the line's AUDCTL is cut by the same check that cuts one
at any AUDCTL change. A line past the new end goes back to the loop line. Two smaller
things for an editor: `Seek(line, row)` starts from a row, and `Hold` reads the row it is
on once and stays there, which is how a typed note is auditioned alone through the real
driver.

**The font has no `#`**, and note names need one. `Screen.Text` draws it over the
blank the font leaves, rather than adding a glyph to RimWalker's linked font — whose
glyph set is the game's, and is listed on the game's own font page.

**Tracks are text, one token a row**, parsed with problems collected rather than
thrown, the same contract as `EnvelopeBank`: `.` holds, `-` releases, drums are
`K S H O`, notes are `D4`/`A#3`/`Bb3` (a flat is a lowercase `b`), chords are `Dm` or
`Bb`. A problem shows in the page header in place of the status.

**The meter is per song, not fixed** (`Tune.RowsPerLine`, `RowsPerBeat`, set by
`Tune.Meter`). Sixteen rows of four is 4/4 in sixteenths and every song but one uses it;
*High Seas* is a jig, 6/8, and sixteen does not divide into its two beats of three
eighths — so it has twelve rows a line, three a beat, a row an eighth and a line two bars.
A track is exactly `RowsPerLine` tokens and a wrong count is a problem, so `Meter` refuses
once any part exists: changing it then would leave every track built so far the wrong
length. The page lights every `RowsPerBeat`-th row and the BPM it shows is beats — dotted
quarters, for the jig. Everything that walks a line reads the meter from the song in hand
(the driver from the tune it is playing, the page from `_shown`), never a constant: two
songs in a row need not agree, and a page reading the new song's meter for the old song's
last queued frames would draw the wrong row.

**A line is also the unit a channel changes instrument in**, because `@NAME` belongs to a
track. *Alla Hornpipe* needs a channel to go from trumpet to horn on the fourth quarter of
a bar, where each phrase hands over, so its lines are half a bar (`rows 6`, `beat 2`) and
the hand-over falls on a line boundary. Picking the meter to put line boundaries where the
music changes hands is the whole trick; nothing in the driver knows about bars.

## Song files

Songs live as `.song` text files in `songs/` at the repo root, beside `banks/`, and are
copied beside the executable the same way. All but one exist *only* as files;
*Rim Patrol* is also built in code (`SongLibrary.BuiltIn`), as the worked example of the
API and so the page is never empty. **The file wins**: `SongFile.Catalog` offers the
built-in songs, each replaced by a file of the same name, then every other file —
RimWalker's rule for its bank. `O` on the page opens any `.song` file; re-opening one
replaces it rather than adding a stale copy.

The format, line by line — a keyword, then its values; `#` starting a token begins a
comment (so `C#4` is a note and `# C#4` is a remark):

```
song   RIM PATROL                 # the rest of the line; so is key, section, note
key    D MINOR
rows   16                         # rows a line, and rows a beat - before any channel,
beat   4                          # and left out for 16 and 4; high-seas.song says 12 and 3

instrument KICK                   # one token; _ stands for a space in the name
  table      none                 # tone, buzz (distortion C), wide (16-bit), none (drums)
  volume     15 14 13 11 9 7 5 3 1 0      # one entry per frame, last held
  distortion tone buzz            # tone, buzz, noise, or a high nibble like $A0
  audf       40 20 27 34 43       # dividers per frame, for an instrument with no note
  vibrato    0 1 1 0 -1 -1        # added to a note's divider, cycled...
  delay      16                   # ...once the note is this many frames old
  release    2                    # volume lost per frame after a note-off

channel 1 drums DRUMS             # drums, notes or pair; then the role, to line end
  note  One channel for the whole kit...   # repeated note lines join with a space
  kit   K=KICK S=SNARE H=HAT O=OPEN        # drums only
  voice LEAD                               # notes only: the channel's instrument
  track BEAT  K . H .  S . H .  K . K .  S . H .
  track S1 @GUITAR  D5 . F5 . ...          # @NAME: this track plays another instrument

speed  7                          # frames a row, for the lines that follow
audctl $00                        # decimal, $hex or 0xhex, for the lines that follow
section SOLO                      # a name for the lines that follow; bare to clear it
line   INTRO DM . .               # the order list: a track per channel, '.' for none
skip   INTRO DM . .               # a line switched off: kept, jumped over when played
line   INTRO ~DM . .              # '~': that channel switched off on this line, its track kept
loop                              # the song returns to the next line after its last
end                               # or: play once and stop - a jingle; no loop then
```

Blocks end at the next thing that is not part of them, and everything must be defined
above where it is used — instruments before the channels that name them. A `pair`
channel has no tracks; it is the low byte of the channel above it on lines whose
`audctl` joins them, and `Tune.Check` holds the file to that as it does code.

**A song that ends** (`end`, `Tune.Ends`) plays its lines once. At the end of the last
row the driver releases every channel and sets `Frame.Ended`: from then on it still steps
the instruments, so the final notes fade by their own release rates, but it stays on the
last row and reads nothing more — a held note with nothing to release it would otherwise
ring forever. `Seek` and `Load` clear it, and Enter on the page replays an ended song.
`loop` and `end` together are a problem, and the song ends: a jingle stuck looping is the
worse way to be wrong. A `loop` with no line after it is a problem too, and loops to the
top — it would otherwise send the driver past the end of the order list, and deleting the
last line in an editor makes it easy to write. Two things learned writing the jingles,
both about level:

- **A release is at most a quarter-second** — one volume step a frame, from 15 — so a
  long fade has to be in the volume table itself. *Game Over*'s instruments decay for as
  long as each note is held, and the release only finishes what is already quiet.
- **Everything starting at once from silence is the loudest moment a song has.** The DC
  blocker passes the whole jump before the level settles: *Stage Clear*'s first frame
  peaked at 0.82 where the same volumes mid-bar peak at 0.55, and clipped. A one-frame
  softer attack on the instruments that open together fixed it; the harness's clipping
  check is what found it, not a listen.

**Three things worth not undoing:**

- **Nothing throws.** A bad line becomes a problem naming the file and line and is
  skipped, so one typo costs one cell or one line; a file with nothing to play is left
  out of the catalog and said so, because `MusicDriver` cannot play a song with no lines.
- **`SongFile.Write` writes a song from what it is, not how it was typed** — the
  instruments, cells and order list — so a song built in code can be saved and shared.
  It does not keep comments; the shipped files were written by it and then commented by
  hand, and are edited by hand from here.
- **The harness holds the two directions to each other.** Every song in the catalog is
  written, read back and must render the *same samples*; writing it again must give the
  identical text. The migration of the three songs out of code was checked the same
  way: each file renders bit-for-bit what its C# version did.

## Things that are load-bearing

**`Program.cs` is longhand purely to carry `[STAThread]`.** The file dialog needs a
single-threaded apartment, and an *owned* modal dialog additionally needs its owner
window to belong to the calling thread. Doing it on a worker thread satisfies the
first and breaks the second — which was a real bug: the dialog was shown owned by a
window on another thread, WinForms refused, and the failure went into a silent catch,
so pressing `O` did nothing at all. Do not convert this back to a top-level statement.

**`SoundLibrary` and `VoiceLibrary` are free of MonoGame on purpose.** They are tables
of integers and text, which is what lets a headless harness render and measure every
entry without an audio device. `ChipSound` and `Voice` are the halves that need one.

**The interface bank is separate from the browsable one.** `Game1` owns a `Sounds`
built from `SoundLibrary.Ui` — one menu click. Wiring the shell to whichever bank
happens to be loaded would mean the menu goes silent the moment you open a file with
no sound of that name.

**Stroke weight is tied to text size** (`Screen.Weight`). `VectorRenderer` draws every
stroke three times — a wide dim pass, a medium one, a bright core — and those passes
are multiples of a thickness constant tuned for RimWalker's large HUD text. Used
unchanged for a twelve-pixel list row it puts a thirteen-pixel halo on an eight-pixel
glyph and every counter fills in solid. The ratio is deliberately below what the title
alone would suggest: the glow spreads by a fixed multiple of the core, so small type
needs a relatively lighter stroke to survive it, not merely a smaller one.

**The batch is additive**, matching `RimWalkerGame.Draw`. Those three passes are meant
to sum; composited with alpha instead, each paints over the last and the core never
reaches full brightness — the text comes out both haloed and dim.

**The vector font is linked, not copied**, from `RimWalker/Core/`. `BeamFont` is
deliberately *not* among the linked files: it presents the font as `IFontRenderer`,
which has no stroke-thickness parameter, so every call would take the default weight
and reintroduce the blur above.

## Banks

Sets of sounds come from `.sound` files at the repo root, parsed by
`Pokey.EnvelopeBank`. The format and its keywords live in
[`Pokey/CLAUDE.md`](../Pokey/CLAUDE.md); what matters here is that a loaded file
behaves exactly like the built-in set, because everything drawn reads through
`SoundPage.Bank` rather than through `SoundLibrary`'s statics.

**The clock is per-bank and it is on screen.** Every pitch scales with it, so a set
designed against RimWalker's 32kHz divider clock and played at Tempest's 54kHz is in
the wrong key rather than slightly off — and nothing else on the page would say so.

## Testing

There is no test project. The harness that checks every recipe and every shipped bank
renders — audible, no accidental register wrap, sane duration — lives outside the repo
in the scratch directory and is rebuilt when needed; it links `SoundLibrary.cs`
directly rather than copying it. Four real classes of bug came out of it and are worth
knowing about, because all four are silent at runtime:

- **AUDC written as a bare distortion value.** `192` is distortion 6 with volume
  **zero**. Nine sounds across the shipped banks were silent this way.
- **A chain running off the end** — an AUDF sweep to 263, volume fades to −3.
- **The category shifted by one.** A sound is still pending when the next `category`
  line arrives, so it was filed under the following one. Only visible by looking at
  the rendered list.
- **A wrap check that was too blunt.** Wrapping is a technique, not only a mistake:
  Tempest's shot takes −8 on a volume of 2 every tick and the buzz *is* the sound.
  Hence `Recipe.AllowWrap` and the `wrap` keyword — the checker flags every wrap and a
  file declares which are deliberate. Nothing at runtime reads that flag; the registers
  wrap either way, because that is what the hardware does.

The songs have a harness of the same kind, referencing the Pokey project for
`Pokey.Music`, and running over the same catalog the page
offers — the built-in song and `songs/*.song`. It checks the tables against the emulator at
base-clock resolution — distortion C's period is exactly 15 × 28 × (AUDF+1) for the
dividers the bass table uses and not for the ones it skips, pure tone is
2 × 28 × (AUDF+1), a joined pair is 2 × (divider + 7) — and that a 16-bit part without
the AUDCTL to join it is reported. Then, per song: it parses with no problems; it
renders with no clipped samples (that is where the 1.25 gain came from — *Rim
Patrol*'s raw peak is 0.75, *Warp Lane*'s 0.61, *Rim Suite*'s 0.77, *La Carrera*'s
0.77 — which is 0.82 and clipping with its trumpets a level louder, so a new song
should be judged by this check rather than by eye); it renders well
over real time (about 16×, so streaming costs a few percent of a core); and, walking
every frame once through, each frame writes its own line's AUDCTL, a channel is a
silent low byte carrying its partner's low byte on exactly the lines where it is
joined, and no unjoined channel ever holds a 16-bit divider. Finally, that switching song lands
at the next frame from line 0 with the new AUDCTL and no mutes, that the frame history
advances in order, and that a muted channel writes volume 0. It writes each song to a
WAV, which is the quickest way to hear a change to a track. And for song files: every
song round-trips through `SongFile.Write` and `Parse` to the same samples and the same
text, and a deliberately broken file loads what it can and lists what it could not. The
meter survives a write and read, a `rows` after a channel and a track the wrong length
for the meter are both reported, and a 16/4 song writes no meter lines. A
song that ends must report ended after exactly its own length in frames, read no row
after that, stay on its last line, and be silent half a second later.

**`tools/soundexport` is the second thing in the repo to use these files**: each song as
an intro and a seamless loop for a game that plays audio files, from the same catalog. It
linked them at first, as the harness did; the rule in `Pokey/CLAUDE.md` said a third
consumer was the point to move them into `Pokey` instead, and the song editor was that
third consumer — so all of them now reference `Pokey.Music`.

## Conventions

- No UI framework or extra dependencies. `System.Windows.Forms` is referenced for the
  file dialog only, and deliberately **not** imported in `SoundPage.cs` — its `Keys`
  enum collides with XNA's, which that file uses on every line of input handling.
- Names in shipped code stay neutral. The NES-style jump is `"Jump, NES style"`, not
  someone's trademark; the banks are `Maze chase`, `Platformer`, `Starfighter`. The
  notes say plainly what each evokes and what does not carry across. The same goes for
  music: a song here is either an original tune or a piece whose copyright has expired.
  *Pachelbel Canon*, *Alla Hornpipe*, *In the Hall of the Mountain King* and *William
  Tell* are the second kind — Pachelbel died in 1706, Handel in 1759, Rossini in 1868,
  Grieg in 1907 — and their
  headers say so and where the notes came from. The page's font has no `@` or `;`, so a
  channel `note` should use neither. The test is the piece's copyright, not how famous the tune is, and a public-domain
  piece has to be written from the piece itself: a modern edition or arrangement can
  carry its own copyright, so none is copied.
  *Rim Suite* borrows a form — contrasting sections run end to end — and *La Carrera*
  a style — oom-pah bass, off-beat strums, a duet in thirds, trumpet fanfares — and
  *High Seas* an idiom, the shanty jig, rather than any film's pirate theme: a different
  key from the famous one and its own tune. None of them takes anything else; neither a
  form nor a style is a melody. Transcriptions of songs still in copyright, however
  rough, stay out: a chiptune arrangement is still a copy of the tune.

## See also

- `docs/SOUND-RECIPE.md` (in the workshop repo this came from, not here) — designing a voice in the lab and
  getting it into a game.
- [`Pokey/CLAUDE.md`](../Pokey/CLAUDE.md) — the chip and the bank format.
- [`Speech/CLAUDE.md`](../Speech/CLAUDE.md) — the synthesiser behind the voice page.
