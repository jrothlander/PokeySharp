# Demo1

A browsable shelf of arcade sounds, a talking demo, and a four-channel song, built on
the `Pokey` and `Speech` libraries. Nothing it plays is a file — every effect is a
handful of integers rendered through an emulated POKEY, every spoken line is
synthesised from its spelling, and the song is nine bytes written sixty times a second.

```bash
dotnet run --project Demo1
```

`1`, `2` and `3` switch pages, or click the tab. `Esc` quits.

> **About the songs named below.** This repository carries seven songs: *La Carrera*,
> *Rim Patrol*, *Rim Suite*, *Warp Lane*, *Fur Elise*, *Morning Mood* and *In the Hall of
> the Mountain King*. The pages below describe the whole library of the workshop
> repository this came from, so a song named here may not be in `songs/`. Everything said
> about the format, the driver and the chip applies either way, and the song page plays
> any `.song` file you put in the folder.


## 1 — Sounds

| | |
|---|---|
| Up / Down | choose a sound |
| Left / Right | jump a category |
| Enter | play it |
| Click a name | choose and play |
| `B` | next bank |
| `O` | open a bank file |

For whichever sound is selected you get its AUDF and AUDC chains exactly as
PokeyLabForms' **Copy as C#** prints them, what the divider and the volume do over the
sound's life, and the waveform that actually comes out.

Three things worth having it open for:

- **The pitch plot is a stair, not a curve.** It shows what a constant delta really
  does to pitch — a hyperbola, steep at the top and flattening — which is the most
  common surprise when designing a sweep.
- **A tone and a poly look different**, not just sound different. Compare *Laser*
  against *Heavy cannon* in the waveform pane.
- **Duration is most of the design.** *Explosion* and *Hit* are nearly the same shape;
  one runs 273 ms and the other 41 ms, and they read as completely different events.

### Banks

Six sets ship. `B` cycles them; `O` opens any `.sound` file.

| Bank | |
|---|---|
| Starter set | built in, 23 sounds |
| Space shooter | evocation, on the arcade's own timings |
| Lasers | ten shots, written by `samples/lasers` |
| Maze chase | evocation |
| Platformer | evocation |
| Starfighter | evocation |

None of them are arcade waveform data. The evocations stand in for machines that
used a Namco WSG or an NES APU, so nothing there can be a copy; each file opens by
saying which parts do not carry across to a POKEY.

**The space shooter is the sharpest case of that**, and its header is worth reading. That
cabinet had no sound chip at all — a board of discrete analog circuits, gated by seven
write-only latches — so there is no waveform in its source to transcribe. What *is* in
the source, and what that file takes exactly, is the timing: how many frames each sound
is held on, which of them decay and how fast, and what the game changes between one
firing and the next.

The bank files are plain text at `banks/` in the repo root, meant to be edited by
hand. The format is documented in [`src/Pokey/README.md`](../../src/Pokey/README.md). In the
workshop repo this came from, the game reads `banks/rimwalker.sound` too, so a voice
retuned there and auditioned here is the
one the game plays.

## 2 — Voice

| | |
|---|---|
| Up / Down | choose a line |
| Left / Right | change the voice |
| Enter | say it |

A dozen lines a game might say, plus film quotations and one sung. Five settings, and
the interesting one is the level count: **15 is four bits**, which is all a POKEY's
volume nibble could hold and exactly how S.A.M. spoke on an Atari in 1982. **255** is
the same synthesiser with the quantisation taken off. One argument's difference in the
call, and it is most of what made 1982 speech sound like 1982.

The page shows the pipeline with real numbers — words in, phonemes out, how many level
bytes, how long — and which words came from the letter-to-sound rules rather than the
dictionary. Those are the ones worth listening to before shipping anything.

## 3 — Song

| | |
|---|---|
| Enter | play / pause — or, once a jingle has ended, play it again |
| `N` | next song |
| `O` | open a `.song` file |
| Left / Right | previous / next line of the song |
| Up / Down | choose a channel |
| `M` | mute the chosen channel |
| `S` | solo it — press again for all four |
| Click a channel | choose it |

Eight original tunes set up the way Atari 800 music was: a job per channel, and a
driver in the vertical blank that writes every channel's AUDF and AUDC once a frame.
The first two are two answers to the same problem — a single POKEY channel cannot play
a bass note in tune — the third uses both, the fourth spends all four channels on a
band with no drums, the next two use the AUDCTL features the others do not — the
high-pass filter and the 9-bit noise counter — and the last two are jingles that play
once and end.

**1 — *Rim Patrol*** keeps all four voices and fakes the bass.

| Channel | Job | How |
|---|---|---|
| 1 | Drums | Kick, snare and hats share the channel, so only one sounds at a time. Each is a table of frames: a click of tone, then distortion C or noise. |
| 2 | Bass | Distortion C, the Atari's buzzy bass — the only way below 125 Hz without giving up a channel. |
| 3 | Lead | Pure tone, with a one-divider-step vibrato on held notes. |
| 4 | Chords | An arpeggio: one note of the chord per frame, fast enough to hear as a chord. |

**2 — *Warp Lane*** gives up a channel to get the bass right.

| Channel | Job | How |
|---|---|---|
| 1 | Drums | The same kit. |
| 2 | Lead | Pure tone, plucked, so a line at 150 BPM stays separate notes. |
| 3 | Low byte | AUDCTL `28` joins channels 3 and 4 into one 16-bit counter on the 1.79 MHz clock. Channel 3 is its bottom byte and makes no sound. |
| 4 | Bass | Pure tone on the joined pair: every note within a tenth of a cent, octave bounces, any key. The chords are what it cost. |

**3 — *Rim Suite*** does everything the other two do, one section after another —
about a minute of contrasting sections run end to end:

| Section | Tempo | What it shows |
|---|---|---|
| Ballad | 90 BPM | Drums silent. A slow-attack voice, broken chords, soft distortion-C bass. |
| Solo | 112 BPM | Drums enter. Channel 3 doubles the guitar one divider flat, and the beating between them is a chorus. |
| Choir | 128 BPM | High calls answered low over a fast arpeggio; the kick alone is timpani. |
| Heavy | 150 BPM | AUDCTL switches to `28` mid-song: channel 3 becomes the low byte and a 16-bit bass riff takes over, in E minor — a key the buzz bass cannot play. |
| Coda | 90 BPM | AUDCTL back to `00`, the channels come apart, and the opening phrase returns. |

Every channel changes instrument between sections, and the header follows the section
and tempo of what you are hearing.

**4 — *La Carrera*** is a ranchera for four players and no drums, at 180 BPM:

| Channel | Part | How |
|---|---|---|
| 1 | Bass | The guitarrón's oom-pah on distortion C. A major, because A, D and E all land within 11 cents on the buzz table. |
| 2 | Guitar | Off-beat strums. Each strum is the chord played one note a frame — an arpeggio with a fast decay. |
| 3 | Voice | The melody, with a singer's vibrato; a trumpet in the fanfares. |
| 4 | High voice | A third above the melody in the chorus — the duet sound of the style. |

Solo the two voices in the chorus to hear where the tables show: each note is a few
cents out, and when they miss in opposite directions the third between them is further
out than either.

**5 — *Cavern*** is a dungeon theme at 100 BPM in E phrygian — slow, sparse, and the
first song written directly as a `.song` file rather than converted from code:

| Channel | Part | How |
|---|---|---|
| 1 | Lead | Pure tone, slow attack, no vibrato. In the second half of each pass, filtered. |
| 2 | Drone | Distortion C held for whole bars; E and F a half-step apart are the mode's darkness. |
| 3 | Shadow | **Silent.** Plays the melody one divider step flat at volume 0, and with AUDCTL `02` its counter clocks channel 1's high-pass filter. |
| 4 | Cave | Drips (a tone rising in five frames), a heartbeat thud, and a swell of wind. |

The melody plays twice — *plain*, then *filtered* — and the only difference is one bit
of AUDCTL. Filtered, channel 1's wave is latched every time channel 3 fires, and with
channel 3 one step off, its pulse width sweeps from almost nothing to almost all about
six times a second: the shimmer. Watch channel 3 on the right: its AUDF follows every
note while its volume never leaves 0, and the `CTL` byte reads `02`.

**6 — *Guardian*** is a boss battle at 180 BPM in E phrygian dominant — the villain's
scale, E F G# A B C D — with a sixteenth-note riff that never lets up:

| Channel | Part | How |
|---|---|---|
| 1 | Drums | A double-kick beat and a blast beat, and before the fight a warning siren: a pure tone swept up and back over forty frames. |
| 2 | Bass | Distortion C on E, F, G#, A and D — all within 11 cents; B and C stay out of the bass. |
| 3 | Lead | Pure tone, loud from the first frame, early vibrato. |
| 4 | Stabs | E major, F, A minor and D minor struck as five-frame arpeggios off the beat. |

The chip feature is AUDCTL bit 7, which shortens POKEY's 17-bit noise counter to nine
bits. The noise stops being noise: it repeats every 73 underflows, so the hat becomes an
876 Hz metallic whine and the snare a growl that falls as it decays. Before the fight
starts the same drum bar plays twice — *17-bit noise*, then *9-bit noise* — and every
channel byte is identical between them; only `CTL` changes, `00` to `80`. Channel 1's
line on the right names the counter it is really using.

**7 — *Stage Clear*** and **8 — *Game Over*** are the two jingles every game needs, and
they do not loop. A song file that says `end` instead of `loop` plays its lines once;
then every channel is released, the notes fade by their own release rates, and the
driver reads no more rows. The header says *ENDED*, and Enter plays it again.

| | *Stage Clear* | *Game Over* |
|---|---|---|
| Key, tempo | A major, 150 BPM | D minor, 90 BPM |
| Length | 3.4 s | 5.5 s |
| Lead | A two-voice bugle call — the harmony a tenth below, in the octave the table has in tune — onto a held top A | A chromatic fall, A G# G F E, onto D |
| Under it | IV–V–I in the bass, a snare roll into a crash | i–VI–V–i, and one low thud on the last chord |
| Ends | The held note and the tonic chord fade | Everything was already fading: a release can take at most a quarter-second, so the long fade is built into the volume tables |

RimWalker's `tools/soundexport` writes a song that ends as a single `name.wav`, trimmed to the end
of its fade, rather than an intro and a loop.

**9 — *High Seas*** is a sea-shanty jig in A minor at 109 BPM, and the one song that
counts in six. Every other song is 4/4, sixteen rows a line; this one says `rows 12` and
`beat 3`, so a row is an eighth note, a line is two bars of 6/8, and the tracker lights
every third row. The pirate sound is the idiom rather than any one tune: a minor key, a
fiddle running in eighths, a squeezebox pumping on the off-beats, an oom-pah bass and a
stomp and a clap on deck.

| Channel | Part | How |
|---|---|---|
| 1 | Deck | A stomp on the first beat of each bar and a clap on the second; pickups the second time through, and a snare roll into the top. |
| 2 | Bass | Distortion C, root then fifth. A, D, E, F and G# all land within 11 cents; G misses by 19, so there is no G chord, and F rocks between octaves because its fifth, C, misses by 31. |
| 3 | Fiddle | Pure tone, bowed in a level under full, with vibrato only on notes held past eight frames — the running eighths stay clean and the long shanty calls sing. |
| 4 | Squeezebox | A minor, D minor, E and F as arpeggios on the second and third eighth of every beat: bass, chord, chord — the lilt of 6/8. |

**10 — *Pachelbel Canon*** is the one song here that is not original: Pachelbel died in
1706, so the piece is public domain. It is also the piece most suited to four channels
that exists. The bass plays the same eight notes from start to finish, and three violins
play one line, each starting a ground after the one before, so three different stages
of the same tune sound at once.

| Channel | Part | How |
|---|---|---|
| 1 | Continuo | The ground — A E F# C# D A D E, a beat each — on distortion C, held for the whole beat. |
| 2 | Violin 1 | The line: quarter notes, then eighths, sixteenths and the famous running passages, each statement over one ground. |
| 3 | Violin 2 | The same tracks, one ground behind. |
| 4 | Violin 3 | The same tracks, two grounds behind. |

It is in A major rather than D, because the bass table misses D major's B and G by 26 and
19 cents, and abridged to six statements of the line rather than the original's many. The
rows are 32nd notes, so the file says `beat 8` and the header's 64 BPM is the piece's own
quarter note. It ends rather than looping; RimWalker's `tools/soundexport` writes it as one file.

**11 — *Alla Hornpipe*** is Handel's, from the *Water Music* (1717), and also public
domain. The notes were read bar by bar off a public-domain full score. It covers the
first section, bars 1 to 39, which Handel marks to be repeated, so the song loops it.

The movement is a conversation. The strings state the tune, the trumpets take it up, the
horns answer an octave lower, and everyone joins at the end of each phrase:

| Channel | Part | How |
|---|---|---|
| 1 | Upper | The top part of whoever has the tune — first violins, first trumpet or first horn — changing instrument when the tune changes hands. |
| 2 | Lower | The second part of the same group, and the oboes under the violins when they climb. |
| 3 | Low byte | Joined to channel 4 by AUDCTL `28`, silent. |
| 4 | Bass | Cello and violone on the 16-bit table, every note within a cent — which is why it can stay in Handel's D. |

A track names one instrument, so the song uses half-bar lines (`rows 6`, `beat 2`). Every
hand-over in this piece happens on the fourth quarter of a bar, and with half-bar lines
that is always the start of a line, where a channel can switch from trumpet to horn.

**12 — *In the Hall of the Mountain King*** is Grieg's, from *Peer Gynt* (1875), all 88
bars, read off a public-domain full score. It's one idea repeated until it breaks: a
four-bar theme in B minor and a four-bar answer pass from cellos to bassoons to violins
to oboes, louder each time, until the whole orchestra takes them at bar 50.

| Channel | Part | How |
|---|---|---|
| 1 | Drums | A quiet bass drum every other bar, timpani as it builds, a bass-drum-and-cymbal march in the tutti, and a roll into the last chord — written as a volume table that beats every frame. |
| 2 | Theme | The tune wherever it is, changing instrument as it's handed on: pizzicato, bassoon, violins, oboes, then everyone. |
| 3 | Low byte | Joined to channel 4 by AUDCTL `28`, silent. |
| 4 | Bass | The staccato pulse, then the tutti's falling eighths, on the 16-bit table so B minor is in tune. |

At bar 66 Grieg writes *stringendo al fine* — faster to the end. The song does it with
`speed` lines in the order list, one frame less per row every two bars, from 138 beats a
minute to 257. Change those lines to change how it accelerates.

**13 — *Platform Shoes*** is an original disco song at 112 BPM in F# minor — the style
of the late seventies, with its own tune, bass line and chords.

| Channel | Part | How |
|---|---|---|
| 1 | Drums | Four on the floor: kick on every beat, open hat on every off-beat, and on two and four the kick and clap as *one* instrument — a thump on the buzz table, then noise — so one channel plays what sounds like three drums. |
| 2 | Bass | The octave bass on distortion C. F# minor because F#, D, E and C# are in tune in both octaves the bass jumps between. |
| 3 | Guitar | Chords scratched on the off-beat sixteenths, cut off in six frames. |
| 4 | Lead | The tune, with a slow vibrato that only arrives on held notes. |

**14 — *William Tell*** is the whole gallop that ends Rossini's overture (1829) — bars 226
to 453, about three minutes — read from Max Wilkinson's typeset score (CC BY 3.0) and
checked against an older public-domain score of the opera. The trumpet call and the
gallop gathering; the famous tune and the full orchestra's answer in C# minor; the
violins' running figure; two big *tutta forza* passages around a quiet C#-minor episode;
all of it again in shorter order, the tune fortissimo, a stringendo section Rossini
repeats (the second time is faster here), and the hammered ending on E. It plays once
and ends.

| Channel | Part | How |
|---|---|---|
| 1 | Drums | Timpani galloping under the brass, rolling under the held chord and tapping E and B under the quiet tune; bass drum, cymbal and triangle as one instrument — a thump then ringing noise — in the loud passages. |
| 2 | Tune | The trumpets' call, then the violins, then everyone. The violins' fast passages are an octave below where they are written, where the pure-tone table is in tune. |
| 3 | Low byte | Joined to channel 4 by AUDCTL `28`, silent. |
| 4 | Bass | Cellos and basses on the 16-bit table: galloping on E, a C# pedal under the answer, a pizzicato scale under the running figure. |

A row is a sixteenth note (`rows 8`, `beat 4` — a line is one bar of 2/4), because the
gallop is an eighth and two sixteenths: in a track it reads `B4 . B4 B4`.

The left half is the song as a tracker shows it, scrolling through the row being
played. The right half is the chip: each channel's two bytes, what they mean, and the
note and pitch they make — then the nine bytes at `D200`, where POKEY sits on an Atari
800, and the output.

Three things worth having it open for:

- **Solo each channel.** Hearing the drums alone makes it obvious they are one voice
  taking turns; hearing the chords alone makes the arpeggio audible as the fast
  flicker it is.
- **The chords line never holds still** on the right. It changes note every frame,
  and that is exactly what the chip is being told.
- **The tuning line under each channel.** Every note is an integer divider, so every
  note misses by a few cents. *Rim Patrol*'s bass table has no usable C, which is why
  it is in D minor — Atari composers picked keys the same way. *Warp Lane*'s bass line
  says 0.
- **The `CTL` byte** at the end of the `D200` row: `00` for *Rim Patrol*, `28` for
  *Warp Lane*, and channel 3's AUDF there turning into the low half of the bass's
  divider. In *Rim Suite* it changes while you watch, going into and out of the heavy
  section.

To use a song in a game that plays audio files, RimWalker's `tools/soundexport --songs` writes each
one as an intro and a loop that joins itself without a click.

### Song files

The songs are plain text files in `songs/` at the repo root — edit one, share it, or
drop a new one in, and the page lists it next time it starts (or press `O` to open one
now). A song file is its instruments, its four channels of tracks, and an order
list:

```
song   MY SONG
key    A MINOR

instrument LEAD
  table      tone                  # which divider table plays its notes
  volume     12 11 10 10 9 9 8     # one entry per frame; the last is held
  distortion tone

channel 1 notes MELODY
  voice LEAD
  track A   A4 . C5 .  E5 . . .  D5 . C5 .  A4 . . .
  track B   G4 . B4 .  D5 . . .  - . . .  . . . .

channel 2 notes BASS
  voice LEAD
  track A   A3 . . .  E3 . . .  A3 . . .  E3 . . .

channel 3 notes -
channel 4 notes -

speed  7                           # frames a row: 7 is 128 beats a minute
line   A A . .                     # one line per bar: a track per channel, '.' for none
loop
line   B A . .
```

Sixteen tokens a track, one per row: `.` holds, `-` releases, a note carries its octave
(`D4`, `A#3`, `Bb3`) and a chord does not (`Dm`, `Bb`). For music that does not count in
fours, `rows` and `beat` before the first channel change that — `rows 12` and `beat 3`
is 6/8, as in `high-seas.song`. A mistake does not stop the file loading — it shows in
the page header, naming the line. The shipped songs are the best examples;
[`CLAUDE.md`](CLAUDE.md) documents every keyword, including drums, the 16-bit bass and
tempo changes mid-song.

*Rim Patrol* is also built in code, in `src/Pokey/Music/SongLibrary.cs`, as the example of setting a
song up programmatically. The file of the same name replaces it when present.

## Making a sound of your own

Dial it in with `PokeyLabForms` (in the workshop repo this came from, not here), press **Copy as C#**,
and paste. `docs/SOUND-RECIPE.md` (in the workshop repo this came from, not here) walks the whole loop from
the lab to a game. To add one to a bank instead, put the same four numbers in a file:

```
sound Laser
audf  8 1 +5 20
audc  168 2 0 10
amp   0.45
note  Starts above everything else in the mix and drops fast.
```

Then press `O` and open it.

## Building

Windows only — it uses `MonoGame.Framework.WindowsDX` and the common file dialog.

```bash
dotnet build Demo1
```
