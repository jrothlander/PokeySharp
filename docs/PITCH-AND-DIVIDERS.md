# Pitch on POKEY: dividers, cents, and why a tune takes two channels

POKEY cannot be told to play a note. It can only be told how far to count. Everything
about how the chip is tuned follows from that one fact: why its top octave is sour,
why a melody that has to be in tune costs two of the four channels, why the Atari's bass
has notes missing, and why its composers picked their keys by the chip rather than by
the song.

This is the thing about POKEY most worth understanding before writing music for it, and
the one most likely to surprise anyone who has worked with a chip, or a synthesiser, that
takes a frequency.

Every figure below was computed by `Pokey.Music.NoteTable`, for an NTSC Atari 800. Other
machines run a different base clock, so their numbers move a little, but the shape stays
the same.

---

## 1. The short version

- A channel divides a clock by an **8-bit number**, so it has 256 pitches and no others.
- Those pitches are **packed tightly at the bottom and spread wide at the top**. Low
  notes land within a few cents of true; above C5 they can be twenty or thirty cents
  out, and nothing can be adjusted to fix that.
- **Two channels joined** make one 16-bit counter on a clock 28 times faster. That puts
  every note within a cent or two, but the second channel goes silent: it becomes the
  bottom byte of the counter.
- So a high melody costs a choice: **in tune, or a fourth voice**. You cannot have both.

---

## 2. A channel counts; it does not play a frequency

Each channel has a divider register, `AUDF`, from 0 to 255. The channel counts clock
pulses down from that value, and every time it runs out (an *underflow*) it reloads and
its output flips. In pure tone, two flips make one cycle of the square wave:

```
frequency = clock / (2 × (AUDF + addend))
```

- **clock** is about 64 kHz on an unmodified channel: 63,921 Hz, which is the
  1.79 MHz base clock divided by 28. It is never exactly 64,000.
- **addend** is the chip's reload delay. It is 1 on the slow clocks and 7 for a joined
  pair on the base clock. The C012294 documentation confirms both; see
  `src/Pokey/CLAUDE.md`.

To play A4 (440 Hz):

```
63,921 / (2 × 440) − 1 = 71.6
```

That isn't a whole number, and `AUDF` has to be one. Here are the three nearest:

| AUDF | Frequency | Against 440 Hz |
|---|---|---|
| 71 | 443.90 Hz | +15.3 cents |
| **72** | **437.82 Hz** | **−8.6 cents** |
| 73 | 431.90 Hz | −32.2 cents |

So POKEY's A4 is 72, nine cents flat, and that is as close as an unmodified channel can
get. A cent is a hundredth of a semitone. Most people hear about 5–10 cents on a
sustained note, and twenty cents sounds out of tune.

The rounding is done in cents rather than hertz. A semitone is a ratio, so "nearest" in
hertz always leans flat.

---

## 3. Why the error grows as the note rises

Moving `AUDF` by one changes the frequency by a ratio of `(N+1)/N`. When `N` is large,
that ratio is tiny. When `N` is small, it is big. Low notes need large dividers and high
notes need small ones, so the choice of available pitches thins out exactly where a
melody lives:

| Note | Hz | AUDF | Plays | Off by | Gap to the next divider |
|---|---|---|---|---|---|
| D3 | 146.83 | 217 | 146.61 | −2.6 c | 7.9 c |
| A3 | 220.00 | 144 | 220.42 | +3.3 c | 11.9 c |
| D4 | 293.66 | 108 | 293.22 | −2.6 c | 15.8 c |
| A4 | 440.00 | 72 | 437.82 | −8.6 c | 23.6 c |
| D5 | 587.33 | 53 | 591.86 | +13.3 c | 31.8 c |
| A5 | 880.00 | 35 | 887.79 | +15.3 c | 47.4 c |
| D6 | 1174.66 | 26 | 1183.72 | +13.3 c | 63.0 c |
| A6 | 1760.00 | 17 | 1775.59 | +15.3 c | 93.6 c |
| D7 | 2349.32 | 13 | 2282.90 | −49.7 c | 119.4 c |

The gap column is the important one. **A note can be off by up to half the gap**, and
whether any particular note is unlucky is just arithmetic. Around D6 the dividers are 63
cents apart: 26 gives +13, 27 gives −50, and there is nothing in between. Around D7 the
gap is wider than a semitone, so some notes don't exist at all.

A useful rule of thumb is that the gap is about `1731 / (AUDF + addend)` cents.

### What that does to each octave

The worst note in each octave on the pure-tone table:

| Octave | Worst note | Every note, in cents |
|---|---|---|
| 3 | 5.7 c | C+2 C#−3 D−3 D#+4 E−1 F+0 F#−2 G+1 G#−1 A+3 A#+2 B+6 |
| 4 | 11.1 c | C+2 C#+5 D−3 D#−5 E−1 F−9 F#+8 G−10 G#−1 A−9 A#−11 B−8 |
| 5 | 21.7 c | C+2 C#−10 D+13 D#+12 E+17 F−9 F#+8 G−10 G#+22 A+15 A#+14 B+19 |
| 6 | 32.9 c | C−26 C#−10 D+13 D#−21 E+17 F−9 F#−32 G+33 G#+22 A+15 A#+14 B+19 |

Octave 3 is essentially in tune, octave 4 is acceptable, octave 5 is where it starts to
show, and octave 6 is sour. The bottom limit is set by the other end of the register:
`AUDF` 255 plays 124.85 Hz, which is B2 nineteen cents sharp. Nothing on a single
channel goes lower on pure tone.

**The errors don't cancel.** Each note is off by its own amount in its own direction, so
in octave 5 an E (+17) against a G (−10) is a 27-cent mistuned third. This is why the
Atari's own composers chose keys by the table: some keys simply land on better dividers
than others.

---

## 4. Joining two channels: the 16-bit pair

AUDCTL, POKEY's control register, can chain two channels into one counter:

| AUDCTL bit | Does |
|---|---|
| `$10` | Joins channels 1 and 2 |
| `$08` | Joins channels 3 and 4 |
| `$40` | Runs channel 1 (the low byte of a 1+2 pair) on the 1.79 MHz base clock |
| `$20` | Runs channel 3 (the low byte of a 3+4 pair) on the 1.79 MHz base clock |

So `$28` gives a 3+4 pair on the fast clock and `$50` gives a 1+2 pair. Both together,
`$78`, give two in-tune voices and no others.

Two things change at once. The divider goes from 8 bits to 16, which gives 65,536 values
instead of 256. The clock goes from 64 kHz to 1.79 MHz, which makes every divider 28
times larger for the same note, so the gaps shrink by the same factor. Together:

| Note | 8-bit AUDF | 8-bit off by | 16-bit divider | 16-bit off by | 16-bit gap |
|---|---|---|---|---|---|
| A4 | 72 | −8.6 c | 2027 | −0.13 c | 0.85 c |
| D5 | 53 | +13.3 c | 1517 | −0.38 c | 1.14 c |
| D6 | 26 | +13.3 c | 755 | −0.38 c | 2.27 c |
| A6 | 17 | +15.3 c | 501 | +1.58 c | 3.40 c |

On the 16-bit pair, D6 is divider 755, a third of a cent flat, with 754 and 756 either side
at +1.9 and −2.7. The pair also reaches down to 13.65 Hz, so the same trick gives an
in-tune bass below anything an 8-bit channel can play.

### What the pair costs

**The low channel stops being a voice.** It holds the bottom byte of the counter and
is not played as a voice: a song file gives it no tracks and no instrument. A 3+4 pair is one voice on two channels, so a song using one has three voices.
A song using both pairs has two.

**It is one 16-bit counter, not two 8-bit counters in series.** Two in series would give
a period of `(low+1) × (high+1)`: a product, with big gaps in what it can express.
POKEY's join carries from the low byte into the high one, so it reaches every value in
between. The emulator gets this right; `src/Pokey/CLAUDE.md` says why it matters, and
that getting it wrong still makes a plausible sound.

### Why the obvious alternatives don't help

- **Run one 8-bit channel on the fast clock** (`$40` or `$20` without the join). The
  gaps shrink, but `AUDF` 255 on 1.79 MHz is about 3,455 Hz. That is above almost every
  note anyone writes, so this mode is for whistles and effects.
- **The 15 kHz clock** (AUDCTL `$01`) goes the other way. It reaches down to about 31 Hz,
  but its dividers are four times smaller for the same note, so the gaps are four times
  wider. Around A4 they are nearly a semitone.
- **Distortion C, the Atari's buzz bass**, sounds 7.5 times lower from the same divider,
  which is how a single channel gets a bass at all. It can only use dividers whose period
  shares no factor with 15, so some notes don't exist on it and others are badly out.
  That is the other reason keys were chosen by the chip.

The 16-bit pair is the only way POKEY plays a high note in tune.

---

## 5. What it forces: a worked example

`songs/sailors-hornpipe.song` is a fife tune that runs from D4 up to D6, with a chord
line and a bass under it. That makes four parts on a four-channel chip. Here is the tune
on a single 8-bit channel:

```
D4−3 E4−1 F#4+8 G4−10 G#4−1 A4−9 B4−8 C#5−10 D5+13 E5+17 F#5+8 G5−10 A5+15 B5+19 C#6−10 D6+13
```

The second strain lives up around A5 to D6, where B5 is 19 cents sharp and the G a third
below it is 10 flat. On a sustained, breathy instrument that is plainly sour, so the
song puts the tune on a 3+4 pair, where every note is within a cent. Now:

| Channel | Plays |
|---|---|
| 1 | Chords: pure tone, roots G3 to F#4, where the 8-bit table is good |
| 2 | Bass: pure tone, D3 to A3, the best octave the 8-bit table has |
| 3 | The low byte of the tune's divider: silent |
| 4 | The tune, 16-bit, D4 to D6 |

**No channel is left for a drum.** Adding one means giving something up, and each way
is a different trade:

1. **Share a channel by time.** The bass plays on beats 1 and 3 and a snare plays on 2 and
   4, from one channel. Nothing goes out of tune and nothing is lost. A hornpipe's natural
   accompaniment is that oom-chick anyway.
2. **Drop the tune an octave onto one channel.** Played D3 to D5 it is within 10 cents
   everywhere but the top D5 (+13), which frees channel 3. But it now sits in the middle of
   the chords and loses the bright, high fife character the tune is known by.
3. **Keep the tune's pitch on one channel**, and accept the top octave sounding sour.

None of those is a mistake. The pattern is that **every POKEY arrangement starts by
deciding which part gets to be in tune**, and that decision comes before the arrangement
itself. A low part is almost free. A high, held part is expensive. A short, percussive
high part (a stab, an arpeggio, a trill) can usually live with the 8-bit table, because
the ear doesn't settle on a note it only hears for a frame or two.

---

## 6. In this library

| Where | What |
|---|---|
| `NoteTable.Tone` | Pure tone on the 64 kHz clock: the 8-bit table above. |
| `NoteTable.Buzz` | Distortion C on the same clock, with the dividers it cannot use skipped. |
| `NoteTable.Wide` | The 16-bit pair on the 1.79 MHz clock. |
| `NoteTable.Audf(midi)` / `Cents(midi)` | The divider a note gets, and how far off it lands. Compute rather than guess. |
| `table tone` / `buzz` / `wide` | Which of those an instrument in a `.song` file uses. |
| `channel 3 pair LOW BYTE` | Declares the silent low half of a pair. It has no tracks. |
| `audctl $28` | Joins 3 and 4 on the fast clock, for the lines that need it. |

All three pieces have to agree: a `wide` instrument, a `pair` channel under it, and the
join in AUDCTL. Every wrong combination still makes a sound. Without the join the chip
plays the high byte alone, and without the fast clock every note is 28 times too low.
So the song reader checks for these and reports them by line, rather than leaving them
to be noticed by ear. `docs/SONG-FILE-TUTORIAL.md` §9 walks through it.

PokeyTracker shows the same numbers while you write. Hover a piano key and it gives the
divider and the cents the cursor's channel would play it at. The piano roll and the
piano mark the notes that land badly on the channel's table, and the chip view shows each
channel's pitch and error as it plays. That is the table in §3, aimed at your song.
