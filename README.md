# PokeySharp

The Atari POKEY sound chip, emulated in C#. Four channels, the polynomial counters, AUDCTL, 16-bit pairs, the high-pass filters and volume-only mode — rendered through a box
filter and a DC blocker, at whatever sample rate you ask for.

## History

I wrote this for retro game clones of Tempest and Galaga I built in C# and MonoGame. I didn't like the experience of working with retro sounds in modern games, so I decided to write a simple POKEY chip emulator that would create the sounds in the same way the original games did. It worked really well, and I have kept upgrading it over the past few months. I even built a full digital workbench that uses it to compose game music assets, export/import MIDI, WAV, and MP3 formats. 

For the musical assets, the best option is to export them to WAV, then load the WAV as an asset. But for sound effects, they are so simple that I found it easier to load them at runtime. I prefer this approach because it allows me to create a sound bank that can be modified in a workbench and saved directly in the game's asset folder.

I included a samples folder to demonstrate how to use it and how it works. I also made a few videos and created a YouTube channel for this. I am not a professional video editor, so they are a bit amateurish. I will work to make them better.

**No dependencies.** Not on a UI framework, not on an audio API, not on a package. It is a
library that turns register writes into samples.

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
| `samples/PokeyLabForms` | The laboratory — turn a knob, hear what moves. The fastest way to learn what AUDCTL does. WinForms. |
| `samples/Demo1` | The demo that plays all of it: sounds, speech and songs. MonoGame. |
| `samples/lasers` | The API applied to ten laser shots, one layer at a time. Console; writes WAVs. |
| `samples/voicedump` | Speech to a WAV from the command line. Console. |

#### PokeyLabForms
This demonstrates each part of the POKEY chip itself. Not terribly useful if you just want to use it without understanding how it works. But if you want to dig into the details, this demo will show you how it works and allow you to play around with creating different sounds. It is also helpful to fine-tune sounds to get exactly what you need, then use the settings you discover here in your own sound banks for your games. 

#### Demo1
A C# MonoGame app that only has a menu and nothing else. The menu drives a sample bank of sounds, voices, and songs. I demonstrate how the emulator works and how to use Demo1 here: https://www.youtube.com/watch?v=WPIBTtdR2UY&t=5s

#### Lasers
This is a simple console app that reads the sound back to create WAV files. If you do not want to load your sounds in real-time, you can dump them as WAV files and load them as assets in your game. This console app allows you to just load them from a bank file and create the WAV. In MonoGame, you would need to recompile them 

The demo includes a couple of console apps that will dump the sound banks to WAV files. If you want to convert them to WAVs and not load them during your game's initialization. 

#### Voice Dump
Similar to the Lasers demo but dumps various texted voices to WAVs. This is less about needing the WAV and more about showing you how to create the sound assets. In my games, I create the voice assets on the fly during the loading of the game content. I do not save them as WAV files. 

```
dotnet run --project samples/PokeyLabForms
```

## Documentation

| | |
|---|---|
| `docs/POKEYLAB-GUIDE.md` | The chip from nothing, using the lab to make each idea audible. Start here. |
| `docs/SOUND-RECIPE.md` | Designing a sound effect, start to finish. |
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
## Licence

MIT. The chip is Atari's; the emulation is mine.
