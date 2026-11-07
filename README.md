# PokeySharp

The Atari POKEY sound chip, emulated in C#. Four channels, the polynomial counters,
AUDCTL, 16-bit pairs, the high-pass filters and volume-only mode — rendered through a box
filter and a DC blocker, at whatever sample rate you ask for.

**No dependencies.** Not on a UI framework, not on an audio API, not on a package. It is a
library that turns register writes into samples.

```
dotnet add package PokeySharp
```

```csharp
using Pokey;

var chip = new PokeyChip { BaseClockHz = PokeyClocks.Atari800NtscHz };

chip.SetAudf(0, 72);      // the divider: A-4, nine cents flat, as this chip really is
chip.SetAudc(0, 0xa9);    // pure tone, volume 9

var buffer = new float[44100];
chip.Render(buffer, 0, buffer.Length, 44100.0);
```

That is the whole contract. Hand it register writes, ask for samples, play them however
you like — `WaveOutStream`, MonoGame, NAudio, a WAV file.

## What is here

| | |
|---|---|
| **`PokeySharp`** (`src/Pokey`) | The chip, a sound-design layer, step envelopes, sound banks, and a frame-based music driver that plays a text song format. |
| **`PokeySharp.Speech`** (`src/Pokey.Speech`) | A formant speech synthesiser. Independent of the chip — it renders to a caller-chosen number of levels, which happens to suit a 4-bit DAC. |

Both target `net8.0` and are MIT licensed.

## Three things it does beyond making a tone

**Sound effects as data.** A `.sound` bank is a text file of envelope chains — value,
inner count, delta, outer count — so a laser is about a dozen integers rather than a WAV.
Five banks are in `banks/`. See `docs/SOUND-RECIPE.md` for the loop: dial one in by ear,
write the numbers down, let the game render it at load time.

**Music as text.** A `.song` file holds instruments as per-frame tables, four channels of
named tracks, and an order list. `MusicDriver` plays it sixty times a second, exactly as an
Atari music driver did. Twenty-eight songs are in `songs/`, and
`docs/SONG-FILE-TUTORIAL.md` builds one from an empty file.

**Speech.** Phonemes in, or English text in if you can tolerate rules that are only mostly
right. It sings, too.

## Samples

| | |
|---|---|
| `samples/PokeyTracker` | A tracker: open a song, play it, edit it from an on-screen piano, watch the registers change. WinForms. |
| `samples/PokeyLabForms` | The laboratory — turn a knob, hear what moves. The fastest way to learn what AUDCTL does. WinForms. |
| `samples/Demo1` | The demo that plays all of it: sounds, speech and songs. MonoGame. |
| `samples/lasers` | The API applied to ten laser shots, one layer at a time. Console; writes WAVs. |
| `samples/voicedump` | Speech to a WAV from the command line. Console. |

```
dotnet run --project samples/PokeyTracker
dotnet run --project samples/PokeyLabForms
```

## Documentation

| | |
|---|---|
| `docs/POKEYLAB-GUIDE.md` | The chip from nothing, using the lab to make each idea audible. Start here. |
| `docs/SOUND-RECIPE.md` | Designing a sound effect, start to finish. |
| `docs/SOUND-TUNING.md` | Tuning envelope chains against reference recordings. |
| `docs/SONG-FILE-TUTORIAL.md` | The `.song` format, instruction by instruction. |
| `docs/PITCH-AND-DIVIDERS.md` | Why POKEY's high notes are out of tune, and why an in-tune melody costs two channels. |

Each project also carries a `CLAUDE.md` recording why it is built the way it is — what the
hardware documentation confirmed, what it corrected, and what is still unverified.

## Testing

```
dotnet run --project tests/Pokey.SelfTest
```

Exit code 0 is a pass. The checks **render real audio and count what came out** rather than
asserting against the emulator's own idea of what it should have produced — a divider off
by one, a 16-bit pair that multiplies instead of combining, or a poly counter fallen into
its all-zero fixed point all still make a sound, just never the right one. That is exactly
the class of bug that survives being listened to.

## Accuracy

Built against
[the chip's own documentation](https://7800.8bitdev.org/index.php/POKEY_C012294_Documentation),
which settles the distortion table, the counter reload addends, the derived clocks and how
the high-pass filters are built. It does not give the polynomial tap positions, so those
are flagged as unverified in `src/Pokey/CLAUDE.md` — the sequences have the right periods
and the right statistics, and may not be bit-identical to the silicon.

## Status

**0.1.0, and pre-1.0 for a reason**: the public surface is wide and some of it should
probably be internal. Expect it to move before 1.0. `net8.0` only for now — a
`netstandard2.0` build for Unity and .NET Framework is an hour's work and will happen if
anyone asks.

## Licence

MIT. The chip is Atari's; the emulation is mine.
