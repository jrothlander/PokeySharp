# PokeyLab: a guide

This teaches you to use PokeyLab — the Windows Forms one — from knowing nothing about
the POKEY sound chip to making it talk and sing.

You do not need to know anything about audio programming. You do need to be willing to
turn a knob and listen, because almost everything here is arranged so that turning one
thing and watching another move is the explanation.

Read it in order the first time. The later parts assume the earlier ones.

---

## Contents

1. [What you are looking at](#1-what-you-are-looking-at)
2. [The chip: one channel](#2-the-chip-one-channel)
3. [Distortion, and why POKEY noise has a pitch](#3-distortion-and-why-pokey-noise-has-a-pitch)
4. [AUDCTL: the eight switches that change the rules](#4-audctl-the-eight-switches-that-change-the-rules)
5. [Envelopes: the arcade's own sounds](#5-envelopes-the-arcades-own-sounds)
6. [The voice: making it talk](#6-the-voice-making-it-talk)
7. [Prosody: stress, rhythm and questions](#7-prosody-stress-rhythm-and-questions)
8. [Singing](#8-singing)
9. [Voices](#9-voices)
10. [Reference](#10-reference)

---

## 1. What you are looking at

Run it with:

```
dotnet run --project PokeyLabForms
```

The window is one screen with everything on it, which is deliberate. The chip's
registers, the envelope driving them, and the waveform coming out are all visible at
once, because the whole point is watching one change the others.

**The status bar at the bottom is the first thing to check.** It should say *Audio
running*. If it says *No audio*, the chip is not advancing — every number on screen
still works, but the scope will be flat and nothing will make a sound. The chip is
ticked *inside* the audio render, so no audio means no time passing.

The layout, roughly:

| Where | What |
|---|---|
| Top left | The four channels — one block each |
| Top middle | AUDCTL, the base clock, and Mute |
| Top right | The envelope, and how it measures against real recordings |
| Bottom left | The scope — what is actually coming out |
| Bottom right | Two tabs: **Poly counters** (open by default) and **Voice** |
| Along the bottom | The nine bytes — everything the chip is actually told |

You should already be hearing a tone. Channel 1 opens set to a pure tone at a mid
volume, so that an empty window is not mistaken for a broken one.

---

## 2. The chip: one channel

POKEY has four channels. Each one is astonishingly simple, and understanding one is
understanding all four.

A channel is **a counter, a switch, and a volume**.

The counter counts down from whatever you put in **AUDF**. When it hits zero it reloads
and *may* flip a one-bit switch. That switch being on or off is the entire output — the
channel is either pushing or it is not. **Volume** says how hard.

That is it. Every sound POKEY can make is that switch flipping, or not flipping, at
moments the counter picks.

### Try this

Look at Channel 1. Drag the **AUDF** slider slowly and watch two things:

- the **Hz figure in the channel's title bar**, and
- the **scope**, bottom left.

You will notice the pitch does not move evenly. Dragging from 10 to 20 changes the pitch
enormously; dragging from 200 to 210 barely changes it at all.

**This is the single least intuitive thing about the chip, and it is worth sitting with.**
AUDF is not a pitch. It is a *divider* — how many clock ticks to wait between flips. The
pitch is what you get by dividing the clock by it, so the relationship is upside down:

> pitch = clock ÷ (2 × (AUDF + 1))

Small dividers are far apart in pitch. Large dividers are crowded together. That is why
the Hz readout sits right next to the slider — so you can watch the inverse happen.

### The scope

The scope shows the chip's actual output. With a pure tone you will see a square wave,
because that is literally what the channel is doing: a switch, on and off, at even
intervals.

It is trigger-aligned, so a steady tone stands still instead of sliding sideways. If it
were not, an unchanging sound would look like it was moving, which would be worse than
useless.

**Gain** and **Span** beneath it are the two scales, and the current setting is printed in
the corner of the trace.

Gain defaults to **×4**, and that number is not cosmetic. The mix sums four channels and
divides by all four at full volume — `4 × 15 = 60` — so **one channel can never exceed a
quarter of full scale**. Drawn against full scale a single voice would use a quarter of the
frame however loud it was. At ×4 one channel at volume 15 fills the frame exactly; ×1 is
the true full-scale picture, which is what you want when all four are playing.

It does not auto-fit, deliberately. A scope that rescales itself to whatever it is showing
is easier to read and tells you nothing — the trace height stops meaning amplitude, and
turning a volume down would leave the picture unchanged. Fixed steps keep the height
meaningful. A trace driven past the top flattens against the frame, which is the scale
telling you it has run out.

**Span** narrows how much of the captured window is drawn, stretched to the full width. The
short settings are for high-pitched content — at 0.9 ms a mid-range tone is less than half a
cycle and reads as a flat line, which is the zoom working, not a fault.

### The nine bytes

The strip along the very bottom of the window is the other half of the picture. The
scope shows what came *out*; this shows what went *in* — and it is the only display here
that shows the chip's own view of things.

Everything else on the form is a convenience for you. A dropdown reading *5 PURE TONE*,
a slider reading *8*. The chip has never heard of any of that. It gets nine bytes: four
AUDF, four AUDC, and one AUDCTL. That is the entire audio programming interface, and the
strip is all nine of them, live, as bits.

Filled cell means the bit is set, outline means clear, exactly like the poly trains.
**The colours mark the fields**, which is most of what the display is for:

| Colour | Field |
|---|---|
| Amber | AUDF (all eight bits are one number), and AUDC's distortion, bits 7–5 |
| Cyan | AUDC bit 4, volume-only |
| Green | AUDC's volume, bits 3–0 |

**Try this.** Set Channel 1's distortion to 5 and its volume to 2, then read AUDC1. The
top three cells are `101`, the low nibble is `0010`, and the hex on the end says `$A2`.
That is the arithmetic in the open: **AUDC = distortion × 32 + volume**, and `$A2` is
what Tempest's ROM actually contains for the player's shot.

**A red outline round a cell means that bit moved since the last frame.** Start an
envelope playing and the AUDF row lights up as the sweep runs. This is sampled, not
exhaustive — the window redraws thirty times a second and the envelope ticks at two
hundred and twenty, so you are seeing roughly one tick in seven. It is enough to see
which bits are hot.

**And it answers the join question.** Tick *Join ch1 and ch2* in AUDCTL: the JOIN 1+2
cell lights green, and channel 1's whole row goes grey with the note **LOW BYTE OF 1+2**.
Channel 1 has stopped being a voice — it is the bottom eight bits of channel 2's divider
now, and its job is to clock its partner. That is the entire reason a sound playing on
channel 1 goes silent the moment you join the pair, and it is worth seeing rather than
being told.

Nothing in this strip is a control. Every one of these bits has a real control elsewhere
on the form; this is a read-out, like the scope.

---

## 3. Distortion, and why POKEY noise has a pitch

Set Channel 1's **Distortion** to something other than *PURE TONE* and listen. Then look
at the scope again.

The channel is still counting down and still reaching zero at the same rate. What
changed is that now it only flips the switch **sometimes** — and what decides is a
*polynomial counter*.

### The Poly counters tab

Bottom right — it is the tab that opens by default, because it explains the mechanism the
rest of the window keeps pointing at. This is that mechanism.

A polynomial counter is a shift register that free-runs at the chip's clock, producing a
fixed sequence of ones and zeros. There are three of them, of different widths, and the
tab draws each as a row of cells — filled for a one, empty for a zero.

**Two facts explain everything, and both are visible rather than asserted:**

**Width decides character.** The 4-bit counter repeats every 15 steps, so it buzzes at a
pitch of its own. The 17-bit one repeats every 131,071 steps, which is longer than
anyone waits, so it reads as noise. Same mechanism, opposite sound, and the only
difference is how wide the register is. The red **REPEATS** marker on each row is the
whole argument — you can see it on the short counters and it is nowhere on the long ones.

**The counters free-run; the channel samples them.** They advance regardless of what any
channel is doing, and a channel looks at them once per divider period. The small blue
ticks *underneath* each row are where the selected channel samples.

Now go back and change **AUDF** on that channel, then look at the poly tab again. The
ticks move. **The cells do not.** You are hearing a fixed pattern strobed at whatever
rate the divider picks — which is why POKEY noise has a pitch at all, and why turning
the divider changes that pitch without changing the pattern one bit.

The line above the trains tells you which counters the current setting actually consults.
*Pure tone* consults none.

---

## 4. AUDCTL: the eight switches that change the rules

AUDCTL is one byte that changes how the channels behave. The panel shows it as eight
checkboxes in bit order, with the raw value underneath.

The ones worth understanding first:

**Join ch1 and ch2** (and **Join ch3 and ch4**). Two 8-bit counters become one 16-bit
counter. Tick it and watch the channel titles: Channel 1 becomes *(low half)* with no
pitch of its own, and Channel 2 becomes *(16 bit)* and reports the pair's pitch.

This matters because a 16-bit counter reaches **every value in between**, where two 8-bit
counters in series could only produce products. Joining is how you get low, precise
pitches.

**Ch1 on base clock** / **Ch3 on base clock**. Normally channels run off a slow clock
divided down from the base. These take the base clock directly — far faster, so far
higher pitches for the same divider.

**15 kHz clock**. Makes the slow clock slower still, globally.

**9 bit poly**. Shortens the 17-bit counter to 9 bits. Watch the Poly tab as you tick
it — the long counter's period changes and it stops sounding like noise and starts
sounding like a repeating rasp.

**Filter ch1 by ch3** / **Filter ch2 by ch4**. High-pass filtering — on the real chip a
flip-flop and an XOR gate, which is about as crude as a filter gets. One channel's output
is held at the moment another channel fires, and the filtered channel then outputs the
difference between its own level and the held one. That cancels anything the two agree on
and passes only what changed faster than the clocking channel.

Subtle, and the least used of the eight. Worth knowing they are easy to get backwards:
these two bits were swapped in this emulator for a while, and **nothing about the sound
gave it away** — both filters existed and both worked, so asking for one just quietly got
the other. It took reading the chip documentation to notice.

### Base clock

Under AUDCTL. Four choices, and **the pitch of everything moves with it**, because pitch
is clock ÷ divider.

Three are real machines. **RimWalker is not** — it is the clock the game plays at,
back-derived by tuning against reference recordings, and it sits in the list precisely
because it disagrees with Tempest's real 1.512 MHz by about a fifth. That gap is a real
open question in this project, not a rounding error.

The two derived clocks are shown beneath. Note the "64 kHz" clock is never 64000 — it is
the base over 28, so it moves with the machine.

---

## 5. Envelopes: the arcade's own sounds

Top right. This is where PokeyLab started.

**POKEY has no envelopes.** It holds whatever was last written until something writes
again. What the arcade had was an interrupt routine that rewrote the registers on a
timer, walking a table of four-byte steps. That table is what this panel edits, and the
values are the ROM's own.

Pick a **Sound** and press **Play**.

### A step is (Value, Inner, Delta, Outer)

Two grids: one for **AUDF** (the sweep) and one for **AUDC** (volume and timbre). Both
chains run independently, exactly as the two ports do in the interrupt routine, and the
sound lasts as long as the longer of them.

| Field | Meaning |
|---|---|
| **Value** | What the register starts at |
| **Inner** | How many ticks between each change |
| **Delta** | How much to add each time |
| **Outer** | How many changes to make |

A step therefore lasts `Inner × Outer` ticks. **Tick Hz** sets how fast the ticks come —
it changes duration and nothing else, because pitch comes from the divider and the clock.

One asymmetry worth knowing: **on AUDC only the volume nibble moves.** The interrupt
masks the delta so the distortion survives, which keeps a sound's timbre fixed for its
whole life.

### Building an AUDC value

AUDF's **Value** is a divider — 16 means sixteen. AUDC's is not a quantity at all; it is
three fields packed into one byte, and 162 means *distortion 5, volume 2*.

So AUDC gets a **Distortion** picker and a **Volume** spinner under its grid, and those two
compose the Value cell. Change either and watch the byte change:

> AUDC 162 = 5 × 32 + 2 = $A2

That is the whole rule — **`AUDC = distortion × 32 + volume`** — because distortion sits in
bits 7–5 and volume in bits 3–0. Reading one back is the same rule in reverse: divide by 32
for the distortion, take the remainder mod 16 for the volume. In hex it is easier still:
**the low digit is the volume**, and the high digit is the distortion doubled.

The Value cell is still directly editable, and it still holds the ROM's own number — the
pickers are a second way in, not a replacement. Bit 4 (volume-only) has no picker because
it belongs to the sampled voice rather than to an envelope; a step that has it set keeps
it through any edit here, and the note says so.

**A trap this closes:** writing a bare distortion is silent. `192` is distortion 6 — with
volume 0. Nothing comes out. With the two fields as separate controls you cannot make that
mistake by accident.

### The AUDC delta is stranger than it looks

That mask has a consequence the number in the cell does not admit to: since the delta
lands in four bits and those bits **wrap rather than clamp**, only `delta mod 16` does
anything at all.

- **−8 and +8 are the same edit.** Both take a volume of 2 to 10.
- **+16, −32 and +64 all do nothing.** The volume never moves.

So the column accepts 129 numbers and has sixteen behaviours. The useful way to read it
is that the volume walks a ring of sixteen levels, stepping `delta mod 16` places each
tick, and returns to where it started after a cycle whose length is `16 ÷ gcd(delta, 16)`
— odd deltas visit all sixteen levels, and **8 gives the tightest cycle there is, two.**

That last one is Tempest's player shot. Its delta of −8 is not a fade; it slams the volume
between 2 and 10 every tick, and that alternation is the rasp you hear. **The buzz is not
on top of the sound, it is the sound.**

Select any AUDC row and the note under the grid spells this out for that step — the
effective delta, the levels it actually visits, and how often it repeats. A delta that
moves nothing is coloured as a warning, because otherwise it reads as a broken control:
the edit is accepted and the sound is identical.

The grid still shows the ROM's own number, not the effective one. `−8` is what is in the
cartridge and what **Copy as C#** emits; the interpretation stays in the note and the
tooltip where it cannot be mistaken for the data.

### The graph, and the measured box

The graph plots the sweep tick by tick, drawn from the same arithmetic that drives the
registers — so it is the sound being played, not an idealisation.

The green **MEASURED** box is where real recordings of a real cabinet say the player's
shot should start and end. The readout beneath judges four numbers against those bands
and marks each **OK** or **no**.

You will notice the shot does not currently land in the box. That is not a bug in the
display — it is a genuine unsolved question about the clock, and the box is there so it
cannot be quietly forgotten. `docs/SOUND-TUNING.md` goes into it properly.

**Switch it off with "Show the measured target box"** beneath the graph, and the axes go
with it. That second part is the point. While the box is shown the axes are held wide
enough to keep it on screen whatever is loaded, which is right when you are checking a
voice against the captures — and wrong when you are designing one that has nothing to do
with the shot, because a short quiet sweep ends up squashed into a corner of a plot sized
for something else. Unchecked, the plot fits the sweep you actually have: the shot's own
time axis reads 0.145 s rather than 0.229 s, and the curve fills the width.

### Taking a sound with you

**Copy as C#**, under the AUDC grid, puts whatever is dialled in on the clipboard as
the call a game would paste — chains, tick, clock and all. The clock comes out as its
name (`PokeyClocks.TempestHz`) rather than a bare number, because a name still says
which machine it came from when you read the line six months later.

That is the whole handover. Nothing gets retyped off four spinners, which is where
transcription errors come from. `docs/SOUND-RECIPE.md` walks the rest of the way into a
running game, and `Demo1` will play your sound back beside two dozen others.

For a whole set rather than one voice, the same four numbers go in a `.sound` file —
see `src/Pokey/README.md` for the format. RimWalker reads its own effects from one, so a
voice tuned here can be heard in the game without a rebuild.

---

## 6. The voice: making it talk

Open the **Voice** tab, bottom right.

### First, the thing that surprises everyone

Speaking uses **none** of what you just learned. No divider, no poly counter, no
distortion field.

Instead the channel is put into **volume-only mode**, where the divider and poly are cut
out entirely and the volume value is driven straight at the output. Something upstream
then rewrites that 4-bit number thousands of times a second, and the sound is whatever
that upstream thing computed.

**The chip becomes a loudspeaker.** A POKEY speaking is a POKEY switched off.

This is exactly how S.A.M. talked on an Atari in 1982, because volume-only POKEY was the
machine's only sample path.

Watch it happen: press **Speak** and look at the channel the voice is using (Channel 4 by
default). Its title changes to *volume only, no pitch*, its Volume-only box ticks itself,
and the volume number flickers as the nibble is rewritten.

Because it is only using one channel, **you can leave a tone running on another and hear
both at once** — which is the comparison the whole layout exists for.

### The three boxes

They flow downward, and the order matters:

```
   Example  ──►  fills all three below
   English  ──►  derives the phonemes
   Phonemes ──►  this is what actually gets spoken
   Melody   ──►  empty = speak, notes = sing
```

**Phonemes is the source of truth.** English is a convenience that writes into it.

Press Enter in any box to re-render and speak. Click away to re-render without speaking.

### English is not spoken directly, and that is deliberate

Type English and it is converted by a few hundred letter-to-sound rules. Those rules get
most of English right and are **wrong often enough to matter** — so their output lands in
an editable box where you can correct it, rather than going straight to the synthesiser.

The readout names which words came from the rules rather than the built-in dictionary:

> `guessed by rule: quick brown fox jumps lazy dog`

Those are the ones worth a glance. Dictionary words are hand-checked.

**Careful:** editing English *re-derives* the phoneme box and overwrites anything you
hand-corrected there. Edit English first, phonemes second.

### Phonemes

The notation is ARPABET — the same set a pronunciation dictionary uses, so dictionary
output pastes straight in. The list on the right shows every code with an example word;
double-click one to insert it.

```
HH EH L OW  -  W ER L D
```

A hyphen is a pause. A `?` anywhere makes the pitch rise at the end.

### Rate — the one that teaches something

**Timing → Rate** is the interrupt rate: how often the 6502 rewrites the nibble. It is
not the output rate and not the chip's clock.

Try this. Type `AA M AA - AA N AA - AA NG AA` (the three nasals) and listen at 8000 Hz.
Then switch to 4000 Hz.

At 4000 Hz nothing above 1800 Hz survives, and the three nasals are distinguished largely
by what sits above that. **`ng` collapses onto `n` and the word loses its ending.** That
is not a limitation of this program — it is what a 1 MHz machine could actually afford,
and hearing a specific distinction disappear is more instructive than being told the
audio "gets worse".

The readout always tells you what the current rate can carry.

---

## 7. Prosody: stress, rhythm and questions

Type some English and look at the phoneme box. You will see digits:

```
IH0 Z  DH AH0  G AA1 R D AX0 N  OW1 P AH0 N
```

That is *"Is the garden open?"*. The digits are stress marks:

| Digit | Meaning |
|---|---|
| `0` | unstressed |
| `1` | primary stress |
| `2` | secondary |

Only vowels carry them. **No digit means stressed**, so hand-typed phonemes behave as
they always did.

Read the line above and you can see the sentence's shape: *is* and *the* are function
words and get flattened; *garden* and *open* are content words and stay prominent;
garden's second syllable has reduced all the way to `AX0`, a schwa.

Three things follow from a stress mark:

- **Vowel reduction.** An unstressed vowel may become a schwa outright.
- **Duration.** An unstressed syllable is 30% shorter.
- **Pitch.** A stressed one sits about 8% higher.

**Editing them is the useful part.** If a word sounds wrong, move the `1`. If a syllable
should be reduced, change `EH0` to `AX0` — note that changing the *digit* alone only
shortens and lowers it; changing the *code* is what changes the vowel.

A `?` in either box switches the sentence contour from falling to rising. The rise is
confined to the last fifth, because that is what English does — rising throughout sounds
like uncertainty, not a question.

---

## 8. Singing

Put notes in the **Melody** box and it sings instead of speaking.

```
150: C5 A4 F4/2 C4/2 D4 E4 F4 D4 C4 F4/2
```

| Part | Meaning |
|---|---|
| `150:` | tempo in bpm, optional, default 120 |
| `C5` | note name — `C4` is middle C, `F#3` and `Bb4` work |
| `/2` | length in beats, default 1 |
| `R` | a rest |

**One note per syllable, in order.** A syllable is a vowel or diphthong; pauses (`-`) do
not take a note. The readout counts for you:

> `24 syllables on 24 notes`

If it says `3 syllables past the end of the melody`, you are three notes short. This
matters more than it sounds — a melody one note short still sings, holding its last note
through the final syllable, which sounds like a stylistic choice rather than a mistake.

When a melody is present the note owns the pitch and the rhythm outright. **Stress marks
and the Speed control both stop applying** — tempo is the speed control for a song.

Try the shipped examples: *Daisy Bell* (an IBM 704 at Bell Labs sang it in 1961, the
first computer to sing anything, which is why HAL sings it in *2001*), *Lavender's Blue*,
and a solfège scale that names each note as it goes.

The melodies were transcribed from memory and are the least reliable thing here. They are
text boxes for exactly that reason.

---

## 9. Voices

**Voice** picks the speaker: pitch, formant scale, resonance bandwidths and the glottal
rolloff.

- **Default** — a generic adult male at 120 Hz.
- **Low and deliberate** — fitted to a measured reference clip, not chosen by ear. 91 Hz,
  slower, a lower third formant and a broader second one.
- **Higher and brighter** — not fitted to anything; it exists to show the mechanism does
  something.

Selecting a voice also moves the Speed control to that voice's own pace. After that the
Speed control is what decides — the profile only seeds it.

The fitted one is worth a note on method. Every number in it came from measuring a
reference recording and comparing it against our own output through the same analysis.
Before measuring, the guesses about that voice were **backwards in two places** — it was
assumed to be flatter and we were assumed to be too slow, and it has three times the
pitch movement and we were too fast. Tuning a voice by ear against a memory does not
work.

---

## 10. Reference

### Phoneme codes

The full list is in the panel itself, with an example word for each. Broadly:

- **Vowels** `IY IH EH AE AA AO UH UW AH ER AX`
- **Diphthongs** `EY AY OY AW OW`
- **Glides** `W Y L R`
- **Nasals** `M N NG`
- **Fricatives** `F V TH DH S Z SH ZH HH`
- **Stops** `P B T D K G`
- **Affricates** `CH JH`

### Notation summary

| Symbol | Where | Meaning |
|---|---|---|
| `-` | phonemes | a pause |
| `0` `1` `2` | phonemes | stress on a vowel |
| `?` | English or phonemes | rising pitch |
| `120:` | melody | tempo |
| `/2` | melody | note length in beats |
| `R` | melody | rest |

### Keyboard

Enter in any of the text boxes renders and speaks.

### Going deeper

- **`docs/SOUND-RECIPE.md`** — from a voice dialled in here to one playing in a game.
- **`docs/SOUND-TUNING.md`** — the envelope page in detail, and the open clock question.
- **`src/Pokey.Speech/README.md`** — the synthesiser's own documentation.
- **`src/Pokey/README.md`** — the chip emulation, and the `.sound` bank format.
- **`samples/Demo1/README.md`** — a runnable shelf of chip sounds, a talking demo, and eight
  songs played the way an Atari 800 played music.

### The chip's own documentation

For the hardware itself rather than this program:
**`7800.8bitdev.org/index.php/POKEY_C012294_Documentation`**

It settles the distortion table, the counter reload values, the derived clock
frequencies, and how the high-pass filters are actually built. It does **not** give the
polynomial counters' tap positions or their periods, which is why this project still
describes those as unverified.

`src/Pokey/CLAUDE.md` records exactly what that reference confirmed, what it corrected, and
what it leaves open.

### If something seems wrong

Run the self-test. It covers the chip, the synthesiser and the shipped examples:

```
dotnet run --project PokeyLab -- --selftest
```

It should print `PASS`. If it does not, the failing line names what broke.
