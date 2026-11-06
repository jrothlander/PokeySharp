# Writing a POKEY song by hand

A tutorial, and a shooting script. It builds one complete song — `first-song.song`, in this
folder — from an empty file, one instruction at a time, and every step is something you can
show on screen. Follow it straight through and you have a song and a video.

Everything here is checked against the reader that actually loads these files
(`Pokey/Music/SongFile.cs`) rather than from memory. The finished file loads with **no
problems** and peaks at 0.84, well under clipping.

---

## What you need on screen

| | |
|---|---|
| A text editor | Any one. The file is plain text; nothing is compiled. |
| **PokeyTracker** | Opens the file, plays it, shows the chip. `O` opens, space plays. |
| `songs/` | Where a song has to live for the tracker and the game to find it. |

Keep the editor and the tracker side by side. The whole point of the format is that you can
change a line, save, and hear the change — so the shot that sells it is both windows at once.

---

## 1. The one-minute idea

A `.song` file is the song. There is no project file, no binary, no build step. A keyword
starts every line, the rest of the line is its values, and `#` starts a comment — but only
when it starts a *token*, because `C#4` is a note and `# C#4` is a remark about one.

Three things follow, and they are worth saying out loud on camera:

- **Nothing throws.** A line the reader cannot use becomes a *problem* naming the file and
  line number, and is skipped. One typo costs one cell, not the song.
- **Everything must be defined above where it is used.** Instruments before the channels
  that name them; the meter before the first track.
- **A block ends at the next thing that is not part of it.** There are no `end` markers and
  no indentation rules — the indenting in these files is for your eyes only.

---

## 2. The shape of a file

Five regions, always in this order:

```
song / key / rows / beat        the header - what the song is
instrument ...                  what a note sounds like
channel ... track ...           the music itself, in named tracks
speed / audctl / section / role settings, placed where they take effect
line / loop / end               the order list - what plays, in what order
```

---

## 3. The header

```
song   FIRST SONG
key    D MINOR
rows   16
beat   4
```

| Instruction | Takes | Notes |
|---|---|---|
| `song` | the rest of the line | The title. Spaces are fine here. |
| `key` | the rest of the line | **A label, not a rule.** The driver never reads it; the tools do — it is what the tracker's key check and its scale helpers work from. |
| `rows` | 1–64 | Rows in one line of the order list. Default 16. |
| `beat` | 1–64 | Rows in a beat, for the tracker's highlighting. Default 4. |

`rows` and `beat` **must come before the first channel.** The meter decides how many tokens
a track is, so changing it later would leave every track written so far the wrong length —
the reader refuses rather than silently re-cutting them. Leave both out for 16 and 4;
`high-seas.song` is the odd one at 12 and 3.

---

## 4. An instrument: what a note sounds like

The chip has no envelopes. An instrument is simply **a list of what to write, one entry per
frame** — frame 0 of a note writes the first entry, frame 1 the second, and the last entry
is held for as long as the note lasts. That one sentence explains every attack, every decay
and every drum in the format.

```
instrument LEAD
  table      tone
  volume     11 10 10 9 9 8 8
  distortion tone
  vibrato    0 1 1 0 -1 -1
  delay      10
  release    2
```

| Instruction | Takes | What it does |
|---|---|---|
| `table` | `tone`, `buzz`, `wide`, `none` | Which note table turns a note into a divider. `none` is for drums, which have no notes. |
| `volume` | 0–15, per frame | Required. The last entry holds. |
| `distortion` | `tone`, `buzz`, `noise`, or a high nibble like `$A0` | Required. Also per frame — `tone buzz` is a click then a body, which is most of a kick drum. |
| `audf` | 0–255, per frame | Dividers of its own, for an instrument with no note. A drum *is* a pitch sweep. |
| `vibrato` | −255 to 255, per frame | Added to the note's divider, cycled. |
| `delay` | frames | How old a note must be before the vibrato starts. Default 0. |
| `release` | 0–15 | Volume lost per frame after a note-off. Default 2. |

The name is **one token** — use `_` where you want a space, and it comes back as a space.

An instrument that cannot make a sound is rejected with a problem rather than loaded: no
`volume`, no `distortion`, or neither a `table` for notes nor `audf` for a drum.

**A release is at most a quarter of a second** — one volume step a frame, from 15. Anything
longer has to be in the `volume` table itself.

### The three note tables

| `table` | Clock | What it is for |
|---|---|---|
| `tone` | 63,921 Hz, 2 underflows a cycle | The plain tone. Leads, chords, most things. |
| `buzz` | same clock, 15 underflows a cycle | Distortion C — the Atari bass, 7.5 times lower from the same divider. |
| `wide` | 1,789,790 Hz, 16-bit | Two channels joined. Every note within a fraction of a cent — see §9. |

**`buzz` has gaps.** It can only use dividers whose period shares no factor with 15, so
some notes simply do not exist on it. That is why this tutorial's song is in D minor: the
buzz table has no good C. Say that on camera while the tracker's key check is up — it is
the clearest example in the whole format of the chip deciding something musical.

### A drum

```
instrument KICK
  table      none
  volume     14 12 9 7 4 2 0
  distortion tone buzz
  audf       40 20 27 34 43
```

No table, no notes — the `audf` list *is* the drum: a divider per frame, so the pitch drops
and then rattles. The distortion changing from `tone` to `buzz` on frame 1 is the click.

---

## 5. Channels and tracks

Four channels, because the chip has four. Each one is opened once and holds any number of
named tracks, each exactly one line of music long.

```
channel 1 drums DRUMS
  note  One channel plays the whole kit: each token names an instrument.
  kit   K=KICK S=SNARE H=HAT
  track BEAT  K . H .  S . H .  K . K .  S . H .

channel 3 notes LEAD
  voice LEAD
  track A1    D5 . . .  A4 . C5 .  D5 . F5 .  E5 . D5 .
```

`channel <1-4> <drums|notes|pair> <role>` — the role is the rest of the line and is what
the tracker shows at the top of the column.

| Instruction | Where | What it does |
|---|---|---|
| `note` | any channel | Documentation, in the file, about this channel. Repeated lines join with a space. These are the best thing in the format: the reasoning stays with the music. |
| `kit` | `drums` only | `TOKEN=INSTRUMENT` pairs. The token is what you type in a row. |
| `voice` | `notes` only | The instrument this channel plays. |
| `track` | any | A name, then one token per row. |

`note`, `kit` and `voice` **must all come before the first `track`** — the channel is built
the moment a track needs it.

A track can override the voice: `track S1 @GUITAR D5 . F5 . ...` plays that one track on
another instrument. On the hardware this was never more than pointing the driver at a
different table, and it is how a channel changes character between sections.

### The row tokens

| Token | Means |
|---|---|
| `D5`, `A#1`, `Bb3` | A note: letter, optional `#` or `b`, then the octave. |
| `Dm`, `F`, `A` | A chord — **no octave**. The triad arpeggiates, one note a frame; its root sits between G3 and F#4. |
| `Bdim`, `Caug` | A diminished or augmented chord. |
| `Dm4`, `Cmaj5`, `Bdim3` | A chord with its root's octave — the quality comes first, so a major chord says `maj` (`C5` would be a note). |
| `-` | Note off. The release takes over. |
| `.` | Hold — nothing happens this row. |
| `K`, `S`, `H` … | On a drums channel, a token from its `kit`. |

A track must have exactly as many tokens as the meter says (16 here). Too few and the rest
are empty; either way you get a problem telling you the count. The double spaces between
groups of four are just whitespace — use them, they make a bar readable.

---

## 6. The order list

Everything so far was material. The order list is the song.

```
speed  6
audctl $00

section INTRO
line   BEAT   .      .      .

loop                       # the song returns HERE, not to the top

section VERSE
line   BEAT   DM     A1     PAD
line   BEAT   A      A2     PADA
line   FILL   DM     A1     PAD
```

| Instruction | Takes | What it does |
|---|---|---|
| `line` | up to 4 track names | One line of the song: a track per channel, `.` for a channel that rests. |
| `skip` | the same as `line` | A line switched **off**: kept in the song, jumped over when it plays and left out of exports. Change it back to `line` to switch it on. |
| `speed` | 1–60 | Frames per row — the tempo. Applies to the lines that follow it. Default 6. |
| `audctl` | a byte: `40`, `$28` or `0x28` | The chip's control register, for the lines that follow. |
| `section` | the rest of the line | A name for the lines that follow, shown in the tracker. Every `section` line starts a section, so `section B` twice in a row is the chorus played twice, shown as two. Bare `section` clears the name. |
| `role` | `intro`, `verse`, `pre-chorus`, `chorus`, `bridge` or `ending` | What the section above it is for in the song's form. Not played - it is for tools that draw a song's structure, such as PokeyTracker's form chart, when the section's name does not say. The next `section` line clears it; so does bare `role`. |
| `define` | a section name | A section written once: the `line`s (and `skip`s and `role`) under it are played by `play`, not where they are written. |
| `play` | a section name | Plays a `define`d section here, all its lines, as a section of that name. |
| `loop` | — | The song returns to the **next** line. |
| `end` | — | Play once and stop. |

**A section played more than once can be written once.** A chorus that comes back four
times is four copies of the same lines - and a change made to one copy and forgotten in the
others is a song that drifts. Write it once under `define`, and `play` it wherever it goes:

```
define CHORUS
role   chorus
line   BEAT   C      C1     PAD
line   FILL   G      C2     PADG

section VERSE
line   BEAT   DM     A1     PAD
play   CHORUS
section VERSE
line   BEAT   DM     A1     PAD
play   CHORUS
play   CHORUS
```

A `define` block is its lines, its `skip`s and its `role`, and nothing else: it ends at the
first other instruction. So `speed` and `audctl` stay in the order list, and the last chorus
can still be played slower than the first. Blocks can go anywhere in the file; above the
order list reads best. Change a line of the block and every play changes - that is the point -
and a play that should differ is written out in full instead. PokeyTracker does both: right-click
a line or a section for **Write repeated sections once**, which turns every exact repeat into
a `define` and its `play`s, and **Write out this ...**, which turns one `play` back into lines.
Either way the song plays exactly as it did.

**Switching parts off.** To try an arrangement without deleting anything, write `skip` in
place of `line` for a line to leave out, or put `~` in front of a track name - `line   BEAT
DM     ~A1    DM` - to silence that channel on that line while keeping its track. PokeyTracker
does both from the order list's right-click menu (**Switch off or on**), a line, a section or
a channel at a time, and shows what is off struck through.

`speed`, `audctl`, `section`, `role` and `play` are placed *between* lines exactly where they take effect,
which is why they sit in this region rather than in the header.

**Three ways to get the ending wrong**, all of which the reader catches:

- **`loop` goes before the lines it repeats, not after the last one.** It marks the return
  point. Writing it at the bottom gives you *LOOP COMES AFTER THE LAST LINE — IT WILL LOOP
  TO THE TOP*, which is exactly what happened while writing this tutorial. Put it after the
  intro, and the intro plays once.
- **`loop` and `end` together** is a contradiction, not a preference. The reader says so and
  the song ends — a jingle stuck looping is the worse way to be wrong.
- **A song that `end`s** releases every channel on the last row and stays there, so the final
  notes fade by their own release rates instead of ringing forever.

---

## 7. Hear it

Save the file into `songs/`, then in PokeyTracker press `O` and open it.

What to show, in this order — it is also the order in which a viewer stops being lost:

1. **The order list** playing: the highlight moving down one line at a time.
2. **A track** as the rows scroll — point at a `-` and let them hear the release.
3. **The piano** across the top, with the notes lighting as they play.
4. **The chip panel**: four channels, AUDF and AUDC changing sixty times a second.
5. Change one note in the editor, save, and let the tracker pick it up. That is the shot.

---

## 8. From a row to two bytes

This is where the format meets the hardware, and it is one slide's worth of arithmetic.

A row says `A4`. The note table turns that into a divider:

```
divider = clock / (cycle * the note's hertz) - addend
63,921 / (2 * 440) - 1 = 71.6  ->  AUDF $48 (72)
```

Rounded to the nearest — measured in **cents**, not hertz, because a semitone is a ratio and
hertz leans every choice flat. Reading it back: 63,921 / (2 × 73) = 437.8 Hz, nine cents
flat. That is the real number, not an approximation of one; this chip is slightly out of
tune and the whole sound depends on it.

AUDC is one byte in two halves: the top three bits are the distortion from the instrument's
table this frame, the bottom four the volume from its volume table — or the release counting
down after a note-off.

Two seconds into `first-song.song`, the driver is writing exactly this:

```
LINE 1 ROW 5   AUDCTL $00
  CH1  AUDF $0C  AUDC $80          hat, noise, faded out
  CH2  AUDF $39  AUDC $CB          bass, distortion C, volume 11
  CH3  AUDF $48  AUDC $A8          lead on A-4 - pure tone, volume 8
  CH4  AUDF $48  AUDC $A3          chord, same divider, quieter
```

`$48` on channel 3 is the 72 worked out above. That is the whole chain, from a letter in a
text file to a byte in a register, and it is short enough to show in one take.

---

## 9. When you want 16-bit notes

Two channels joined count as one 16-bit divider, which puts every note within a fraction of
a cent instead of nine of them. It costs a channel and it needs three things to agree:

1. The low channel declared as `channel 3 pair LOW BYTE` — a `pair` channel has **no tracks
   of its own**; it is the bottom half of the channel above it.
2. The instrument on the high channel using `table wide`.
3. `audctl $28` on those lines — `$08` joins 3 and 4, `$20` runs the pair off the fast clock.
   (For channels 1 and 2 it is `$10` and `$40`.)

`blue-danube.song` is the worked example: its melody goes up to D6, where the 8-bit table is
twenty cents out.

Every way of getting this wrong still makes a sound, which is why the reader checks it: a
16-bit note without the join plays its high byte alone, and without the fast clock the pair
counts at 64 kHz and every note is twenty-eight times too low. You will be told, by line.

---

## 10. When the file is wrong

Problems name the file and the line, and read like this:

```
first-song.song LINE 42: BASS A1 ROW 3: 'H2' IS NOT A NOTE OR A CHORD
first-song.song LINE 51: LEAD: VOICE NAMES ONE INSTRUMENT DEFINED ABOVE IT
first-song.song: LOOP COMES AFTER THE LAST LINE - IT WILL LOOP TO THE TOP
```

The common ones, in the order you will meet them:

| What it says | What it means |
|---|---|
| `... IS NOT SOMETHING A SONG FILE SAYS HERE` | A keyword outside the block that accepts it — usually a `volume` line that drifted away from its instrument. |
| `NO INSTRUMENT - GIVE THE CHANNEL A VOICE OR THE TRACK AN @NAME` | Notes with nothing to play them. They would parse and then be silent. |
| `'X' IS NOT A DRUM` | A token that is not in this channel's `kit`. |
| `n ROWS, NOT 16` | A track with the wrong number of tokens. Count the groups. |
| `TRACK X IS DEFINED TWICE` | Two tracks with the same name; the second wins. |
| `CHANNEL n HAS NO PART` | A channel you named in a `line` but never opened. |

---

## 11. The whole file

`first-song.song` in this folder is the finished article: four channels, six instruments,
four lines and a loop, in D minor. It loads clean, and it is small enough to read on screen
in one shot.

---

## 12. Shooting script

About twelve minutes, assuming you build the file live and play it at every step. Each
section above is one beat of the video.

| # | Beat | On screen | Roughly |
|---|---|---|---|
| 1 | "The song is the file" | The editor and the tracker side by side | 0:45 |
| 2 | The five regions | Scroll the finished file top to bottom, fast | 0:45 |
| 3 | The header | Type `song` / `key` / `rows` / `beat` | 0:45 |
| 4 | An instrument | Type `LEAD`, play one note, change `volume`, play again | 2:00 |
| 5 | A drum | Type `KICK`, show `audf` as a pitch sweep | 1:15 |
| 6 | Channels and tracks | Add the drum track, then the bass | 2:00 |
| 7 | The order list | Add the lines, then `loop`, and get the loop wrong on purpose | 1:30 |
| 8 | Hear it | The tracker: order list, rows, piano, chip panel | 1:30 |
| 9 | Two bytes | The AUDF arithmetic against the chip panel | 1:30 |
| 10 | What it refuses | Break one line, save, show the problem | 0:45 |

Three lines worth having ready, because they are what people remember:

- "There is no build step. This file *is* the song."
- "The instrument is a list of what to write, one entry per frame. That is the whole
  envelope system."
- "The bass table has no good C, so the song is in D minor. The chip picked the key."
