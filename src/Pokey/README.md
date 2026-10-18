# Pokey

A POKEY (C012294) audio-section emulator in C#. Four channels, three polynomial
counters, AUDCTL, box-filtered resampling and a DC blocker.

**No dependencies.** Not on a UI framework, not on an audio API, not on a package. It is
a library that turns register writes into samples, and nothing else.

## Using it

```csharp
var chip = new PokeyChip { BaseClockHz = PokeyClocks.TempestHz };

chip.SetAudf(0, 60);      // divider
chip.SetAudc(0, 0xa8);    // pure tone, volume 8

var buffer = new float[512];
chip.Render(buffer, 0, buffer.Length, 44100.0);
```

`Render` resamples from the chip's own clock down to whatever rate you ask for, and
carries the fractional cycle across calls — so streaming in small blocks gives exactly
what one long render gives.

### Registers

| Call | Does |
|---|---|
| `SetAudf(channel, 0..255)` | The divider. Pitch is `clock / (2 × (audf + addend))`. |
| `SetAudc(channel, byte)` | Bits 0–3 volume, bit 4 volume-only, bits 5–7 distortion. |
| `SetControl(byte)` | AUDCTL: joins, clock selection, filters, 9-bit poly. |
| `BaseClockHz` | The machine. Every pitch moves with it. |

There are read-side helpers so a UI does not have to carry its own copy of the rules:
`PitchHz`, `Distortion`, `DistortionName`, `IsHighHalf`, `IsLowHalf`, `IsFast`,
`InputClockHz`.

### Playing step envelopes

`StepEnvelope` walks four-byte step tables the way Tempest's sound interrupt did — a
value, how many ticks each change holds, the change, and how many changes — rewriting
AUDF and AUDC on a timer. One voice is built in, `ZapAudf` / `ZapAudc`, an original laser
zap; every other voice comes from a bank file (below). Tempest's own voices are not part
of this library, since they are Atari's numbers.

```csharp
var envelope = new StepEnvelope { AudfChain = StepEnvelope.ZapAudf,
                                  AudcChain = StepEnvelope.ZapAudc };
envelope.Trigger(chip);
envelope.Render(chip, buffer, 0, buffer.Length, 44100.0);
```

Render through the envelope rather than the chip — it renders the chip flat when nothing
is playing, so a caller can always just call it.

### Sets of sounds, as a file

`EnvelopeBank` reads a `.sound` file: a named set of envelopes, plus the clock and tick
they were designed against. A game loads its own effects this way; `Demo1` browses any of the
banks in `banks/`.

```csharp
var bank = EnvelopeBank.Load("banks/lasers.sound");
foreach (var recipe in bank.Recipes)
    Console.WriteLine($"{recipe.Name}  {bank.Seconds(recipe) * 1000:0} ms");

foreach (string problem in bank.Problems) Console.WriteLine(problem);
```

Nothing throws on bad input — an unreadable line lands in `Problems` and is skipped, so
one typo costs one sound rather than the bank. **Show those problems**; that is why
they are collected instead of swallowed.

The format is one keyword per line, and a chain is one line per step so the four
numbers read in the order the lab prints them:

```
# A comment.
bank   Lasers
clock  1512000
tick   220

category Lasers

sound Rasp
audf  18 1 +6 30
audc  163 1 -8 30
amp   0.45
wrap
note  The divider rises, so the pitch falls. Its -8 a tick lands only on the
note  volume nibble, which is what makes it buzz rather than change timbre.
```

| Keyword | |
|---|---|
| `bank` | display name |
| `clock` | base clock in Hz — **every pitch scales with this** |
| `tick` | how often the envelope rewrites the registers |
| `category` | groups the sounds that follow |
| `sound` | starts one; ends at the next `sound` or the file |
| `audf` / `audc` | one chain step: value, inner, delta, outer. Repeatable |
| `amp` | 0..1 |
| `wrap` | this sound runs a register past the end **on purpose** |
| `note` | prose. Repeatable, joined with a space |

`wrap` is a **declaration, not a switch**. Nothing at runtime reads it: both registers
wrap rather than clamp whatever the file says, and the arcade relies on that — the
shot's buzz is a volume nibble wrapping every tick. It exists because an *accidental*
wrap is how a hand-edited chain fails silently, so a checker can flag every wrap and
this is how a file says "that one is on purpose". It also tells the next reader, which
is worth as much.

### Speaking

`SampleVoice` drives a channel as a 4-bit DAC instead. Nothing about the chip's
oscillators is involved.

```csharp
var voice = new SampleVoice { Samples = fourBitBytes, SampleRateHz = 8000 };
voice.Trigger(chip);
voice.Render(chip, buffer, 0, buffer.Length, 44100.0);
```

See `PokeySharp.Speech` for something that produces those bytes.

## Building

```
dotnet build src/Pokey
```

Targets `net8.0`. Nothing else to install.

## Testing

The checks need no test framework - they are a console program, and its exit code is
the result:

```
dotnet run --project tests/Pokey.SelfTest
```

They render real audio and count what came out rather than asserting against the
emulator's own idea of what it should have produced. That is deliberate — a divider off
by one, a 16-bit pair that multiplies instead of combining, or a poly counter fallen into
its all-zero fixed point all still make a sound, just never the right one. That is
exactly the class of bug that survives being listened to.

## Learning what any of it means

`docs/POKEYLAB-GUIDE.md` teaches the chip from nothing, using the PokeyLab UI to make
each idea audible.

## The hardware it emulates

**`7800.8bitdev.org/index.php/POKEY_C012294_Documentation`** — the chip's own
documentation. It settles the distortion table, the counter reload addends, the derived
clock frequencies and how the high-pass filters are built; it does not give the
polynomial tap positions, which is why those remain flagged as unverified here.

`src/Pokey/CLAUDE.md` lists precisely what that reference confirmed, the one thing it
corrected, and what it leaves open.
