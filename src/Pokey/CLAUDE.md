# Pokey

The POKEY (C012294) audio section, emulated at the base clock. **Pure C#: no UI, no
audio API, no dependencies, and it stays that way.**

That constraint is the whole design. Kept free of all of it, the chip can be driven by
anything, and several things already do: the two PokeyLab front ends, the headless
self-test, **RimWalker itself**, and — through `Pokey.Music` — Demo1's song page,
PokeyTracker and RimWalker's `tools/soundexport`.

## Who drives it, and how differently

**RimWalker** references this project and renders through it in `Core/Sfx.cs`
(`RenderPokeyVoice`). It **pre-renders** each effect to a `short[]` once: create a chip,
walk the envelope, write AUDF/AUDC at each tick boundary and `Render` the slice between
boundaries, then scale and convert. Two things there are worth not breaking:

- **The base clock is derived, not passed.** `EffectiveClockHz * PokeyClocks.SlowDivider`
  — times 28 — because the chip divides by 28 internally to make its slow clock. Feeding
  it that keeps every voice exactly where the reference captures put it. Handing it
  Tempest's real 1.512MHz instead would raise everything by a fifth. **That is the open
  clock question, and it is deliberately not answered by a refactor.**
- **`ChipGain` is 8**, for two separate reasons: `Mix()` divides by all four channels'
  headroom and only one is driven, and the unipolar output is halved by the DC blocker.

**PokeyLab and PokeyLabForms** drive a *live* chip instead, streaming continuously so a
divider can be turned while a note is sounding.

⚠️ **`Sfx.cs` walks the envelope itself** — `PokeyAudfAt` / `PokeyAudcAt` — rather than
using `StepEnvelope`, so the step-walking arithmetic exists twice. They agree today. If
you change how a step is interpreted, change both, or the game and the lab will disagree
about what the same ROM bytes mean.

## Scope: the sound half only

The real chip also does keyboard scan, paddle pots, serial IO and timers. None of that is
here, and on the hardware this project cares about — an arcade cabinet, and the Atari
7800 — most of it is not even connected. What is here is the four audio channels, the
three polynomial counters, and every AUDCTL bit that changes what comes out of them.

## How it actually makes a sound

Each channel has an 8-bit counter reloaded from its AUDF register, counting down at a
clock the channel selects. On underflow the channel *may* toggle a one-bit output
flip-flop — **may**, because AUDC's distortion field can gate that toggle behind a
polynomial counter's current bit.

So **the divider sets the rate and the poly decides whether each opportunity is taken.**
Toggling every time gives a square wave; toggling only when a poly bit is high gives that
poly's texture, pitched by the divider. That single mechanism accounts for every sound a
POKEY can make, which is why `PokeyChip` is mostly bookkeeping around `Tick()`.

## Files

| File | What |
|---|---|
| `PokeyChip.cs` | The chip: registers, the tick, the mix, resampling, the DC blocker |
| `PolyCounters.cs` | The three shift registers, plus the 9-bit reduction |
| `PokeyClocks.cs` | Base clocks per machine and the dividers derived from them |
| `StepEnvelope.cs` | The arcade's sound engine — **not part of the chip**, see below |
| `SampleVoice.cs` | The 4-bit DAC path — the opposite of everything else here |
| `EnvelopeBank.cs` | A named set of envelopes as a text file — also not part of the chip |
| `SoundRender.cs` | Renders a voice offline to samples, PCM and WAV — the callable layer, see below |
| `SoundDesign.cs` | A voice described in hertz and seconds, compiled to steps — see below |
| `Music/SongLibrary.cs` | The song model: `NoteTable`, `Instrument`, `Part`, `Cell`, `Tune`, and *Rim Patrol* built in code — see Music below |
| `Music/SongFile.cs` | The `.song` text format, read and written |
| `Music/MusicDriver.cs` | The VBI-style music driver: a row, the instruments stepped a frame, nine bytes written |
| `Music/SongExport.cs` | A song rendered for audio files: intro and seamless loop, the full version once through, or one file for a song that ends |
| `Music/MidiFile.cs` | A song as a Standard MIDI File: the notes, not the chip, for a studio program to play |
| `Music/MidiSong.cs` | A Standard MIDI File read: voices, notes in ticks, tempo map, time and key signatures, markers |

## SoundDesign: hertz and seconds, compiled to steps

`SoundDesign` describes a voice the way somebody designing one thinks about it — a pitch
that goes from somewhere to somewhere over a duration, a named texture, a level — and
`Compile()` returns the same `StepEnvelope.Step` pair the ROM stores.

**That it compiles to steps and nothing else is the load-bearing part.** An easy API that
could express things the engine cannot would be a second engine, with two behaviours to
keep in step and no way to tell which one a sound came from. Because it compiles down,
anything designed here can be printed, hand-tuned, saved to a `.sound` bank, or checked
against the arcade's own numbers.

`SelfTest` asserts the strongest form of that: describing the lasers sample's classic zap
in plain terms — 3857 Hz down to 278 over 73 ms, pure tone, fairly loud — compiles to
`Step(6, 1, +6, 16)` and `Step(169, 16, 0, 1)`, the four numbers that zap was written with
by hand. Not close to them. Them. If that check ever fails, the layer has drifted into
being its own synthesiser. (It used to be Tempest's player shot against the ROM's bytes;
see "Tempest's voices are not in this library" below.)

**What it hides is the hardware; what it keeps is the character.** Gone from the caller's
view: dividers, the packed AUDC byte, the `outer - 1` rule, the eight distortion codes
that are really six. Kept, because they are why it sounds like this: the staircase, the
integer quantisation of the divider ladder, and the nibble wrap that turns a delta of -8
into a tremolo rather than a fade.

Two limits are stated rather than papered over. `CompiledVoice` reports the pitch and
duration it *achieved*, which differ from what was asked because dividers are integers and
ticks are whole. And `LevelMove` offers a fade or a tremolo, never both, because AUDC has
one delta — offering both and letting one silently win would be worse than saying so.

## SoundRender: the layer both front ends had written themselves

`SoundRender.Render` walks a voice offline and hands back floats; `ToPcm16` packs them.
That is all a caller needs to turn four numbers into something playable.

**It exists because two apps had each written it.** Demo1 had a `ChipSound` class and
RimWalker had its own loop in `Core/Sfx.cs`, each carrying a copy of the gain correction
and the tail fade. Two places to fix a bug, and two places for one of them to drift.

The gain in particular had no business being in a caller. One channel is a quarter of the
mix and the chip's output is unipolar, so the DC blocker halves the swing again — the
factor is 8, it follows from the hardware, and nobody choosing a laser sound should ever
meet it. It is a private constant here now.

**Deliberately no audio API, and that is not purity for its own sake.** Demo1 links
MonoGame's WindowsDX build and RimWalker links DesktopGL. An adapter assembly naming
`SoundEffect` would drag both into one build and collide. PCM is the only thing the two
agree on, so the binding stays in each app as a single line.

**`ToWav` is a file format, not an audio API**, so it belongs here: bytes in, bytes out,
naming nothing either front end links. It takes floats or, for a caller with its own
renderer such as RimWalker, shorts. It exists for the same reason as the rest of this
class — `samples/lasers` and the song harness had each written the 44-byte header, and a
header is exactly what is right in one copy and subtly wrong in the next.
`tests/Pokey.SelfTest/SelfTest.cs`'s *wav files* section reads it back the way a player does: tags
at their offsets, the two sizes, the rate fields agreeing, and the body equal to
`ToPcm16`'s bytes.

## EnvelopeBank: sets of envelopes, as a file

`Recipe` is one sound — two chains, an amplitude and a note. `EnvelopeBank` is a named
set of them plus the clock and tick they were designed against, and a parser for the
`.sound` files at the repo root.

**It lives here rather than in the app that first needed it** because `RimWalker` and
`Demo1` both read those files, and a game linking source out of a demo would have the
dependency backwards. The Speech csproj already records the rule: when a third thing
wants a shared file, extract it rather than link it again.

**It is called `EnvelopeBank` and not `SoundBank` deliberately.** MonoGame already has
a `SoundBank` — XACT's — so in any game file importing `Microsoft.Xna.Framework.Audio`
the unqualified name would silently resolve to theirs. The compiler caught it
immediately when the type was first added; a library meant to be used from a game
should not make its host disambiguate.

Three things about the format worth not undoing:

- **Line-based, not JSON.** These are written by hand and pasted into from the lab.
  One line per chain step, so the four numbers sit in the order `Copy as C#` prints
  them, and a missing comma cannot take the file with it. It takes comments, which
  matters for a format whose most useful field is the one explaining why a sound is
  what it is.
- **Nothing throws.** A bad line goes into `Problems` and is skipped, so one typo
  costs one sound rather than the bank. Callers are expected to *show* those problems
  — that is the whole reason they are collected rather than swallowed.
- **The clock belongs to the file.** Every pitch scales with it, so a set designed
  against RimWalker's 32kHz divider clock and played at Tempest's 54kHz is in the
  wrong key, not slightly off. Putting it in the file is what lets banks from
  different games sit side by side and each be right.

**Tempest's voices are not in this library.** `StepEnvelope` once carried the arcade's
eight voices as static tables decoded from the ROM. They are Atari's numbers, so on
2026-11-08 they moved to a `.sound` bank in the private RimWalker repository
(`banks/tempest.sound`), and the library keeps one original voice, `ZapAudf` /
`ZapAudc`, as its default. The engine still follows that ISR exactly — the step layout,
chains, outer - 1, the volume mask — because that is how it works, not what it played.
Don't put ROM-derived voices back into this repository; load them from a bank.

**The `wrap` keyword exists because wrapping is a technique.** Both registers wrap
rather than clamp and the arcade uses it: Tempest's shot takes −8 on a volume of 2
every tick, and that buzz *is* the sound; its descend sweep runs AUDF through zero and
lands on 255 as a low rumble. So a checker cannot simply forbid it — but an
accidental wrap is exactly how a hand-edited chain fails silently.

**`Recipe.AllowWrap` is carried, not enforced.** The parser sets it and nothing in any
shipped project reads it — the registers wrap regardless, because that is what the
hardware does. It is there for a checker to consult, so every wrap can be flagged and a
file can say which ones are deliberate, and for the next person reading the numbers.
Do not add runtime behaviour to it without deciding what clamping would even mean.

## Music: the song model, the .song format and the driver

`Pokey.Music` is songs rather than sounds: a `Tune` of four channels, each a `Part` of
named tracks of `Cell`s, played an order line at a time by `MusicDriver`, which runs once
a frame the way an Atari's vertical-blank music driver did — read a row, step every
instrument one frame through its tables, write AUDF/AUDC and AUDCTL. `SongFile` reads and
writes the `.song` files in `songs/` at the repo root; `NoteTable` turns a note into a
divider for each distortion and says how far off it lands.

**It lives here because a third program needed it.** It was written in Demo1 for the song
page, then linked into RimWalker's `tools/soundexport` and the song harness, and the rule that moved
`EnvelopeBank` applies: the song editor would have been the third project linking source
out of a demo, so the three files moved into this library instead. Demo1, soundexport
and the editor now reference one copy. The move changed no behaviour — every shipped song
renders the same samples, which the harness asserts.

**Like the rest of this project it has no audio API.** `MusicDriver.Render` fills a float
buffer; whoever owns an audio device streams it — Demo1 through MonoGame, PokeyTracker
through waveOut, soundexport into a WAV. The format, the driver's design and the reasons
behind both are documented with the song page, in `samples/Demo1/CLAUDE.md` under "The song page"
and "Song files".

**Another voice can play the driver's notes.** Each `Frame` also records, per channel, the
notes held down (`Held` - a chord's every note, not the arpeggio step `Note` shows; empty once
released, faded, muted or a drum) and whether a note or a drum started that frame (`Struck`,
false on a muted channel; a drum's instrument is the frame's `Instrument`).
`Voiceless[c]` silences the chip on a channel while still playing it, unlike `Muted`, so the
notes are still reported; `FramesFrom(sample, list)` hands over the frames a block rendered.
PokeyTracker's sampled piano and its drum machine are built on these three, and the driver keeps the time - loops,
skipped lines and mutes included. None of it changes what the chip renders when unused.

**`SongExport` is how a song leaves as audio**: rendered through `MusicDriver`, the intro,
and the loop cut from the second time round so its end runs into its start, with the seam
measured - and, if asked, the full version: the song once through, rendered from a copy
of it that ends. It was written in RimWalker's `tools/soundexport`; PokeyTracker's Export audio made it the
second caller, so it moved here under the same rule as the rest of this folder. The move
changed no samples - every song's WAVs are byte-identical before and after. How the cut is
made and why is in RimWalker's `tools/soundexport/README.md`.

**`MidiFile` is how a song leaves as notes** rather than as sound: a Standard MIDI File, one
track a channel, drums on channel 10, tempo from the frames a row, velocity from the volume
tables, sections and the loop point as markers. It belongs here for the same reason `ToWav`
belongs in `SoundRender` - a file format is bytes in, bytes out, and names no platform API.
What it cannot carry is the chip itself: the distortions, the noise drums, the arpeggio that
fakes a chord on one channel. That is the point of it - on the other side those are replaced
by real instruments.

**`MidiSong` is the way back in**: it reads any Standard MIDI File into what the file says -
voices (a track's notes on one channel), tempos, meters, keys, markers - and interprets
nothing. Turning that into rows, channels and chord tokens is PokeyTracker's `MidiImport`,
because those are choices a person makes in a window. The writer records a time signature
(the song's beat as the quarter note) so that a song exported and read back keeps its lines.

**The driver has two ways to change song, for two different cases.** `Load` is a new
song: a hard cut, from the top, mutes cleared. `Edit` is the same song changed — what an
editor makes on every keystroke — and keeps the place and every sounding note. `Seek` can
start from a row, and `Hold` keeps the driver on one row, which is how PokeyTracker plays
a typed note on its own through the real driver. The reasons are in `samples/Demo1/CLAUDE.md`.

**`Tune.Instruments` is every instrument a file defined**, by its name in the file, used or
not. The reader always built them all; only the ones a channel used were reachable, and
PokeyTracker's Instruments window needs the rest. A song built in code has none there.

**A problem about a line of the order list counts from 1** (`LINE 12: ...`), as both
viewers number their lines. It counted from 0 until PokeyTracker listed problems beside
an order list numbered from 1. Problems from `SongFile` are different: they name the file
and count its text lines.

## Things that are load-bearing

**`Tick()`'s order matters.** Polys run first and unconditionally, because they free-run
at the base clock no matter what the channels are doing — that shared timebase is why two
channels on the same distortion stay related rather than drifting apart. Then the slow
divider, then the channels, low halves before high so a joined pair's carry lands in the
tick it was generated.

**A joined pair is one 16-bit counter, not two 8-bit counters in series.** Two in series
would give a period of `(low+1) × (high+1)` — a product with huge gaps in what it can
express, where a real 16-bit counter reaches every value in between. Getting this wrong
still makes a sound, just never the one asked for, which is how it would survive being
listened to.

**`Mix()` is unipolar, 0..1, on purpose.** The real chip drives current one way; there is
no negative half. Centring it here would put a full-scale DC offset on a silent chip and
make turning a channel off produce a click. `Render()` removes the DC the way the
hardware did, with a one-pole high-pass standing in for a capacitor.

**`Render()` is box-filtered, not point-sampled.** The chip runs about forty times faster
than any sane sample rate, so reading its level once per output sample would alias every
square edge into the audio band. The fractional cycle carries across calls, so streaming
in small blocks gives the same output as one long render — which both front ends depend
on and the self-test asserts.

**The poly tap positions are standard maximal-length polynomials and remain unverified.**
Any maximal-length LFSR of a given width gives the right period and so the right broad
character, but the exact bit order affects the precise waveform. Checked against the
C012294 documentation (below), **which does not give them either** — so this is still
open, and the caveat is now a checked one rather than an assumed one.

## Checked against the C012294 documentation

`7800.8bitdev.org/index.php/POKEY_C012294_Documentation`. Worth re-reading before
changing anything in `PokeyChip`.

**It confirmed, and these are no longer assumptions:**

- **The whole distortion table.** All eight AUDC codes and which polys gate each,
  including that 1 and 3 are both plain 5-bit and 5 and 7 are both pure tone.
- **The reload addends.** `+4` for an 8-bit channel on the base clock, `+7` for a
  16-bit pair on it, `+1` on the slow clocks. These were commented here as "the
  hardware's reload delay, not a fudge" and now have a source.
- **The clock dividers.** The reference gives the derived clocks as 63.9210 kHz and
  15.6999 kHz, which are exactly base ÷ 28 and ÷ 114 at 1.78979 MHz.
- **The high-pass mechanism** — a D-type flip-flop and an XOR gate, which is the
  sample-and-hold this implements.
- **Volume-only disconnects the dividers, the noise counters *and* the filters.**
  `Mix()` bypassing `Level()` is right for that reason, not just conveniently.

**It corrected one thing, and it was invisible:**

- ⚠️ **AUDCTL bits 1 and 2 were swapped.** Bit 1 is the channel-1 filter clocked by
  channel 3; bit 2 is the channel-2 filter clocked by channel 4. The filtering logic
  was always right — the two enable bits were the wrong way round, so a program asking
  for one silently got the other. **Nothing in the sound would tell you**, because both
  filters exist and both work. `SelfTest.Filters` now pins the mapping to behaviour
  rather than to the constants, so a future swap fails a test instead of passing one.

**It does not cover:** poly tap positions, poly periods, output mixing, DC offset, or
whether the 4-bit volume DAC is linear. Those caveats stand.

## StepEnvelope is not part of the chip

POKEY has no envelopes — it holds whatever was last written until something writes again.
What Tempest has is an interrupt routine that rewrites AUDF and AUDC on a timer, walking
a table of four-byte steps. `StepEnvelope` reproduces that, and lives here only because
it is pure arithmetic with no UI and no audio API.

The step order `(value, inner, delta, outer)` is verified against the ISR at
`tempest.a65:17461`, which is where that order comes from rather than from guessing. The
two chains run **independently**, exactly as the two ports do, and the voice lasts as
long as the longer of them.

**One deviation, stated rather than hidden:** the arcade's ISR marks a voice idle and
simply stops writing, so POKEY holds the last value — which for the shot is a non-zero
volume and would drone. Real cabinets plainly do not drone, so something else stops them
that this decode has not accounted for. `Stop()` silences instead, because that is the
useful behaviour for a tool; the open question is recorded, not papered over.

## SampleVoice is the opposite of all of it

`SampleVoice` puts a channel in volume-only mode and rewrites the volume nibble at the
sample rate. The divider and the poly do nothing. **A POKEY speaking is a POKEY switched
off**, and that is how S.A.M. talked on an Atari in 1982, because volume-only POKEY was
the machine's only sample path.

Structurally it *is* `StepEnvelope` — both walk a table on a timer and slice the audio
block at their write boundaries. The difference is only the rate and the payload.

## Conventions

- **No dependencies. Ever.** If something here needs a package, it belongs somewhere else.
- **No UI and no audio API.** A front end may not leak backwards into this project.
- Keep it deterministic — the poly counters and anything seeded must give the same
  sequence every run, because that is what makes them testable.
- Tests live in `tests/Pokey.SelfTest/SelfTest.cs`, not here, so that this project has no test
  framework dependency either. Run them with
  `dotnet run --project PokeyLab -- --selftest`.

## See also

- `docs/POKEYLAB-GUIDE.md` — how to actually use any of this
