# Tuning the POKEY voices — PokeyLab's envelope page

What every register does, how to read the target box, and how to go after the one
thing that is still known to be wrong.

For where the numbers came from — the decoded sound engine and the measurements
taken off the reference captures — see [`ARCADE-SOURCE.md`](ARCADE-SOURCE.md) §9.
This document is about *using* the tool.

> This guide used to describe an **F8** page inside RimWalker. That page was retired
> once PokeyLab's envelope page covered everything it did and more — multi-step
> chains, all eight arcade voices, and a real POKEY emulation rather than white noise
> standing in for the polynomial counters. Two tuning surfaces for one set of numbers
> was a drift risk with no gain.
>
> **F8 in RimWalker now opens the voice page**, which is a different thing entirely —
> the melody and pace of the spoken lines, not the chip's registers.

---

## Why this exists

Two of the constants in `Core/Sfx.cs` were tuned by ear, and one of them was wrong
by a factor of two for a long time.

Tuning by ear means holding a sound in memory, editing a constant, rebuilding, and
comparing the new sound against the memory. That loop is slow enough that it selects
for whatever you heard most recently rather than for whatever is right. Here the loop
is one keypress — and, more importantly, you get something better than memory to
compare against.

**Nothing you do here changes the game.** PokeyLab is a separate program driving its
own chip. Settings that turn out to be right have to be typed into `Sfx.cs` by hand,
which is the point at which somebody has to write down *why*.

---

## Controls

```bash
dotnet run --project PokeyLab
```

| | |
|---|---|
| **F2** | the envelope page — the ROM's own sounds, and where the lab opens |
| **F3** | the raw register view, plus a scope of what the chip is putting out |
| **F4** | the polynomial counters, and why POKEY noise has a pitch |
| **Esc** | quit |
| **Up / Down** | pick a row |
| **Left / Right** | change the selected value |
| **Hold Left / Right** | keep changing — slow at first, then faster |
| **Fire** | play the envelope (retriggers while it is sounding) |

A *tap* of left or right is always exactly one step. The repeat only starts after
about a third of a second and then ramps up, so it is slow enough at first to land on
a specific number and fast enough after a moment to cross a byte without waiting.

Turn **LOOP** on to hold a sound repeating, which makes comparing against a reference
clip far easier than re-firing by hand.

---

## What POKEY is doing

Two registers per channel, and the whole sound is those two being nudged on a timer.

**`AUDF` is a divider, not a pitch.** The chip divides a clock by it:

```
pitch = clock / (2 x (AUDF + 1))
```

So **a bigger `AUDF` is a lower note**, and the relationship is a curve, not a line —
going from 16 to 17 drops the pitch far more than going from 200 to 201. This is why
the shot's *rising* `AUDF` produces a *falling* sweep, and why the curve on the page
plunges steeply at the start and flattens out towards the end.

**`AUDC` is two things in one byte.** The low nibble is volume, 0–15. The high nibble
selects distortion — which of the chip's polynomial counters shapes the waveform,
with `$A` meaning "no poly at all", a clean square.

The detail that matters: **Tempest's sound ISR sweeps only the volume nibble.** It
masks the delta so the distortion nibble survives untouched, so a sound's timbre is
fixed for its whole life and only its loudness moves. A hand-rolled imitation that
sweeps the whole byte turns a buzz into a timbre slide, which is a different sound
entirely.

---

## The rows

### `SOUND` — which voice

**`PLAYER SHOT`** and **`FOOTSTEP`**, the two sounds taken from the arcade's tables.
Only the shot has measured targets; see [Reading the graph](#reading-the-graph).

### `TICK HZ` — how fast the envelope is stepped

How many times a second the sound ISR runs. **This sets duration and nothing else.**
It cannot change any pitch, only how quickly the sweep moves through them.

RimWalker ships 220. Measured: the shot lasts 0.136–0.184 s and runs 32 ticks, so the
tick rate is 174–235 Hz. The 60 Hz frame interrupt would make the shot four times too
long; the ISR genuinely runs several times a frame.

### `BASE CLOCK` — which machine, and the unexplained one

Everything on POKEY is one base clock and a chain of dividers. The "64 kHz" clock is
not an oscillator — it is base ÷ 28 — so a POKEY in a machine with a different base
clock has a different divider clock, and every pitch moves with it.

| option | base | divider clock |
|---|---|---|
| `RIMWALKER 0.896` | 896 kHz | 32,000 Hz |
| `TEMPEST 1.512` | 1.512 MHz | 54,000 Hz |
| `ATARI NTSC 1.79` | 1.78979 MHz | 63,921 Hz |

**The first is not a real machine, and that is now settled rather than suspected.**
It is the clock RimWalker plays at, back-derived from tuning the shot until it
matched the captures, and it is in the list so a voice can be heard at the pitch the
game will actually give it. Having it sit beside Tempest's real 1.512 MHz is the
point: the two disagree by a fifth, and that gap is the open question.

**There is no hardware route to it.** MAME clocks both of Tempest's POKEYs at
`MASTER_CLOCK / 8` — 12.096 MHz ÷ 8 = 1.512 MHz, the same as the CPU — and POKEY
divides its own clock input by 28 to make the "64 kHz" clock, which is why the
Atari's figure is 63,921 and never the round number its name suggests. 1.512 MHz ÷
28 is 54,000, full stop. So `RIMWALKER 0.896` is not an unknown clock waiting to be
explained; it is a **known-wrong** one, kept because the game is tuned to it. See
`ARCADE-SOURCE.md` §9.

### `AUDF` and `AUDC` — `STEPS`, `EDITING STEP`, and the four fields

Each register gets its own chain of steps, and the two run independently. `STEPS`
grows or shrinks the chain, `EDITING STEP` picks which one the four fields below it
are editing, and those fields are the arcade's own four bytes:


| row | meaning |
|---|---|
| `VALUE` | the register value the step opens on |
| `DELTA` | added every `INNER` ticks (signed; on AUDF, negative sweeps *upward* in pitch) |
| `OUTER` | how long the step lasts — the delta is applied `OUTER - 1` times |
| `INNER` | ticks between applications |

A step lasts `INNER x OUTER` ticks, so `INNER` slows a sweep without changing where
it ends up. The shot uses `INNER 1` — a change every tick; the level arrival uses
`INNER 11`, which is what stretches it past three seconds.

**AUDF wraps at a byte rather than clamping.** The ISR adds with a plain 8-bit
`adc` and no guard, so a divider driven past 0 lands on 255. The tube descent does
exactly that, and reading it as a clamp instead parks it at 16 kHz and squeals.

**These are what set the sweep's depth**, and depth is the one thing neither
constant above can touch. Worth being precise about why: `PITCH FUDGE` multiplies
both ends, so the *ratio* between them survives it unchanged.

On **AUDC**, `VALUE` is the whole byte: the high nibble picks distortion, the low
nibble is volume. `$a2` is distortion 5 — pure tone — at volume 2. The delta sweeps
**only the volume nibble**, because the ISR masks it, so a sound's timbre is fixed
for its whole life. The decoded distortion is shown under the rows.

---

## Reading the graph

Pitch up the side, time along the bottom, drawn as a **staircase** because that is
what it is — the value is held for a whole tick and then jumps. It is the same
arithmetic the audio buffer is rendered from, not a smoothed version, so a sweep that
saturates at `AUDF` 255 flattens on screen exactly as it does in the sound.

The green rectangle is the **measured answer**. Its top-left corner is where the shot
should start, its bottom-right is where it should end, and its width is how long it
should last. Two crosses mark the middle of the start and end bands, to make clear it
is a start corner and an end corner rather than a region the whole curve should sit
inside.

**Tuning is: make the curve begin in the top-left corner and end in the bottom-right
one.** That is a question two people can agree on, which "does this sound about
right" is not.

Under the graph, four quantities are read out live against their bands, each marked
`OK` or `NO`: where the sweep starts, where it ends, how long it lasts, and the
fall ratio between the two ends.

### How much the bands are worth

They are wider than the measurement's own scatter, deliberately. The clips are lossy
captures of a video, with cabinet rumble and other sounds mixed in; the two ridge
traces put the sweep's start at 956 and 958 Hz while individual frames near the onset
ran past 1300. A tight band around one estimate would dress that up as a precision it
does not have.

**What they are good for is catching an error of a factor** — which is exactly the
error that was actually there. Do not chase the last few Hz; the captures cannot
adjudicate it.

The footstep has no bands at all. Its clip is 0.089 s, but a four-tick blip cannot
account for all of that, and its pitch never separated from the rumble. Inventing a
target for it would be worse than having none.

---

## The open problem, and how to go after it

Start and duration are settled and the shipped constants already meet both. **The
sweep depth is not:**

| | shipped | measured |
|---|---|---|
| starts at | 941 Hz | 900–1100 |
| ends at | 68 Hz | 140–195 |
| lasts | 0.145 s | 0.136–0.184 |
| **fall** | **13.8 : 1** | **5.7–6.6 : 1** |

The shot dives roughly an octave and a half further than the original. Since `AUDF`
`STEPS` is 32 and `DELTA` is 7, the divider ends at `16 + 7x31 = 233`, about two and
a half times further than the captures say it should travel.

**Note the 31, not 32.** A step spans `INNER x OUTER` ticks but applies `OUTER - 1`
deltas — the ISR adds the delta only on the counter decrements that leave it
non-zero, and the one reaching zero advances to the next step instead. This document
said 240 until that was read off the ISR rather than inferred.

**So do not touch `TICK HZ` or `PITCH FUDGE` first.** They are the two rows that
cannot fix this, and moving them will break the two quantities that already agree.

`PITCH FUDGE` in particular **cannot** fix it, and it is worth knowing why rather
than taking it on trust:

```
fall = (AUDF_end + 1) / (AUDF_start + 1)
```

**There is no clock in that.** The fall is a ratio of two divider values, so every
clock row, every fudge and every base-clock option leaves it exactly where it is.
13.76:1 is wrong at 32kHz, at 54kHz and at 63.9kHz alike. Whatever is wrong here
is in the step's own bytes, and that conclusion does not depend on settling the
clock question at all.

The reverse is also worth stating, because the two questions have been quoted as
evidence for each other: **the 32kHz clock is not an independent finding.** The
measured start pitch is one equation with two unknowns —
`f = clock / (2 x (AUDF + 1))` — so fixing `AUDF` at 16 and solving gives 32kHz,
while fixing the clock at Tempest's 54kHz and solving gives `AUDF` ≈ 27. The
capture supports both equally. `ARCADE-SOURCE.md` §9 has the full argument.

Where the divider *should* end: at an effective 32 kHz, a 170 Hz target needs
`AUDF + 1 = 16000/170`, so **`AUDF` ≈ 93** rather than 233.

Two readings get there, and they are worth trying against each other because they
correspond to two different decode errors:

**The delta is misread.** Keep 32 steps, reduce the delta. `(93 - 16) / 31 = 2.5`, so
try **`AUDF DELTA 3`**: the divider ends at `16 + 3x31 = 109`, giving **145 Hz and a
fall of 6.5:1 — inside both bands**, with duration and start pitch untouched since
neither depends on the delta. `DELTA 4` overshoots the fall (8.3:1) and `DELTA 2`
undershoots (198 Hz, 4.8:1), so 3 is the only whole number that fits at 32 steps.
This is the tidiest answer and the one to try first.

*(Under the old off-by-one this landed at 6.65:1, just outside the band. Correcting
the delta count moved it inside — the one place the fix changed a conclusion rather
than a decimal.)*

**The step count is misread.** Keep the arcade's delta of 7, reduce the steps.
Reaching `AUDF` 93 needs 11 deltas, so **`AUDF STEPS 12`** — 170 Hz, dead on the
measured end, with a fall of 5.5:1, just under the band. But 12 ticks at 220 Hz lasts
only 0.055 s, so `TICK HZ` would have to drop to about 83 to keep the duration right
— and 83 Hz is not a plausible ISR rate, where 220 sits comfortably near a multiple
of the 60 Hz frame interrupt. **That is an argument against this reading**, not proof,
but it is worth weighing.

**The step is grouped differently.** Keep *both* cited bytes — `AUDF` 16 and delta
7 — and question only `INNER`. Duration is `INNER x OUTER` ticks, so any pairing
that multiplies to 32 leaves it exactly as it is; what changes is how many deltas
actually land, which is `OUTER - 1`.

| `INNER` x `OUTER` | deltas | `AUDF` end | fall | duration |
|---|---|---|---|---|
| 1 x 32 *(shipped)* | 31 | 233 | 13.76 | 0.145s |
| **2 x 16** | **15** | **121** | **7.18** | **0.145s** |
| 4 x 8 | 7 | 65 | 3.88 | 0.145s |

**This is the only candidate of the three that breaks nothing else.** The delta
reading needs the unexplained clock to be real; the step-count reading needs an
83Hz tick; this one holds the duration by construction and touches no other
quantity. It lands at 7.18:1 — outside the band, but a third of the distance the
shipped table is. Set `INNER 2` and `OUTER 16` in the panel and hear it.

With `DELTA 6` as well it lands at **6.29:1, inside the band**, on 16 real steps.
Two changed fields is a worse story than one, so try `INNER` alone first and see
how much of the gap it closes.

**Both were tried in this panel, and the readouts are worth knowing before you
repeat it.** `INNER 2` / `OUTER 16` alone: fall 13.8 → **7.2**, `LASTS` unmoved at
0.145s, start and end untouched. Adding `DELTA 6` and switching `BASE CLOCK` to
`RIMWALKER 0.896` turns **all four rows green at once** — 941Hz, 150Hz, 0.145s,
6.3:1 — which no other setting has managed.

**Read that result carefully.** It goes green at the *derived* clock, not at
Tempest's. Start pitch is `clock / (2 x (AUDF + 1))` and contains neither `INNER`
nor `OUTER`, so no amount of regrouping can move it — switch `BASE CLOCK` back to
`TEMPEST 1.512` and the first two rows go red again while `FALL` stays green. This
candidate settles the sweep depth. **It does not touch the clock question**, and
anything claiming otherwise is confusing two independent quantities.

Neither of the first two lands cleanly inside every band at once, which is itself
informative: the fall band is the tightest thing here and both sit just outside it
in opposite directions. Do not over-fit to that — see [how much the bands are
worth](#how-much-the-bands-are-worth).

Either way the captures cannot settle *which* — they can only say where the sweep
ends up. The decisive evidence is the four bytes of the step in `tempest.a65`, and
this page is for narrowing down what to expect before going to look.

---

## When you land on something

1. Check the readouts and the fall ratio.
2. Play it against the reference clips in `sounds/` with `LOOP` on.
3. Get the values out. There are now two places they can go, and they are for
   different things:
   - **`banks/rimwalker.sound`** to hear it in the game without a rebuild. `Sfx`
     reads that file at startup and looks each voice up by name. This is the fast
     loop, and [`Demo1`](../Demo1/README.md) will play the same file back to you.
   - **`Core/Sfx.cs`** to make it the answer — the `*Audf` / `*Audc` constants for
     the envelope, `PokeyTickHz` / `PokeyPitchFudge` for the clock — **with a
     comment saying what the evidence was.** The constants are what the game falls
     back to, so a value that only exists in the file is a value that disappears if
     the file does.

   PokeyLabForms' **Copy as C#** emits the call for you, so the four numbers need
   not be retyped off four spinners; it still will not edit either file, because
   what belongs in a commit is the reasoning, not just the digits.
4. Run both suites:

```bash
dotnet run --project RimWalker -- --simulate
```

```bash
dotnet run --project PokeyLab -- --selftest
```

RimWalker's `pokey voices` section asserts that the shipped constants still start and
last where the captures say, and that every transcribed envelope still matches the
ROM byte for byte. It reports the fall ratio without asserting it, because that is
the open gap rather than a regression — if you close it, that line is where you will
see it, and this document and ARCADE-SOURCE §9 both need updating to match.

Its `the sound bank file` section covers the three ways an edit to
`banks/rimwalker.sound` goes wrong **silently**: a renamed sound falls back to the
constant so the edit appears to do nothing, a chain longer than one step plays only
its first because `Sfx`'s renderer predates `StepEnvelope`, and a changed clock moves
every pitch in the game. If an edit there seems to have had no effect, run this first.
