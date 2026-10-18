# Speech

A formant speech synthesiser in C#. Phonemes in, samples out — or English in, if you can
tolerate a rule set that is only mostly right.

**No dependencies, and no reference to `Pokey`.** It renders to a caller-chosen number of
levels, so it happens to suit a 4-bit DAC but does not know about one.

## Using it

The short way:

```csharp
var samples = SpeechSource.Speak("HH EH L OW - W ER L D",
                                 sampleRateHz: 8000,
                                 maxLevel: 15,
                                 out var unknown);
```

`samples` is one byte per sample, `0..maxLevel`, unipolar. `unknown` collects any tokens
that matched nothing — they are spoken as silence rather than throwing, so a typo leaves
a gap you can hear and a message you can read.

**`maxLevel` cannot exceed 255, and asking for more does not fail — it corrupts.** The
parameter is an `int` and `Quantise` clamps to it, but the buffer is `byte[]`, so a value
above 255 truncates every sample modulo 256 and hands back something that sounds like
loud noise rather than speech. 15 is the POKEY nibble; 255 is the ceiling. A byte is
generous for a library written to feed a 4-bit DAC, so this is a ceiling rather than a
limitation — but it is worth knowing before you go looking for it in your own code.

### In a game

`Demo1` has a working example: a page of spoken lines, each synthesised at load from its
English spelling with no audio file anywhere, and the same sentence playable through a
POKEY's four bits or through eight. The game-facing part is `Demo1/Voice.cs` and it is
about thirty lines — derive the phonemes, call `Speak`, hold each level until the next to
get PCM, hand that to a `SoundEffect`.

```
dotnet run --project samples/Demo1
```

Press **2** for the voice page, or click the tab.

### Tuning a line, and keeping the tuning

`PokeyLabForms` is where you tune — pick an example, edit the phonemes or the melody, hear
it immediately. **Nothing it does is persisted.** There is no settings file and no
autosave, so anything typed into those boxes is gone when the window closes.

`SpeechSource.Examples` is the durable home. Paste the tuned phoneme string or melody into
that entry's field and both front ends pick it up: the lab loads the boxes from it, and
`Demo1`'s voice page looks its quotations up there by label rather than keeping a copy —
so a line improved in the lab plays improved in the demo with no second edit and no chance
of the two drifting apart.

A melody is worth reaching for when the contour cannot say the line on its own. "Shall we
play a game?" is a question, and `Intonation.Rising` alone does not make it sound like one;
five hand-written notes do:

```
250: C2/1.5 C2/1.5 F2/1.5 E2/.8 A1/1.5
```

One note per syllable, and notes override prosody entirely — a sung syllable takes both its
pitch and its length from its note. `Song.Sing` reports how the two lined up ("5 syllables
on 5 notes"), which is the quickest way to find a tune that runs out early or long.

### From English

```csharp
string phonemes = LetterToSound.Translate("Good morning, Dave.", out var guessed);
// -> "G UH1 D M AO1 R N IH0 NG - D EY1 V"
```

`guessed` lists the words that went through the rules rather than the built-in
dictionary. Those are the ones worth checking.

**Translate into something editable, not straight into the synthesiser.** The rules get
roughly three-quarters of unseen words exactly right; the rest need a human.

### With a voice

```csharp
var samples = SpeechSource.Speak(phonemes, 8000, 15, out _,
                                 voice: VoiceProfile.Deliberate);
```

Three profiles ship: `Default`, `Deliberate` (fitted to a measured reference), and
`Bright`. A profile carries pitch, speed, accent depth, per-formant scale, three
bandwidths and the glottal rolloff.

### Singing

```csharp
var samples = SpeechSource.Speak("D EY1 Z IY0 - D EY1 Z IY0", 8000, 15, out _,
                                 melody: "150: C5 A4 F4/2 C4/2");
```

One note per syllable. `150:` is tempo, `/2` is beats, `R` is a rest. A note replaces the
speaking pitch and rhythm entirely.

## Notation

**Phonemes are ARPABET** — the set CMU's pronouncing dictionary uses, so dictionary output
pastes in unchanged.

| Symbol | Meaning |
|---|---|
| `-` | a pause |
| `AA1` | primary stress on a vowel |
| `AX0` | unstressed — `0` is unstressed, no digit means stressed |
| `?` | rising sentence contour |

The full inventory with example words is in `Phoneme.cs`, and the PokeyLab UI lists it
with a double-click-to-insert control.

## How good is it

Honestly measured, and there are two numbers because they mean different things.

| Word set | Score |
|---|---|
| The 30 words the rules were written against | 100% — says nothing about unseen words |
| 20 words chosen afterwards | **75%** |

The second is the real one. Most of the remaining misses need a stress model that can see
word class, and two are undecidable from spelling at all (`river` against `driver`).

## Building

```
dotnet build src/Pokey.Speech
```

Targets `net8.0`.

## Testing

```
dotnet run --project tests/Pokey.SelfTest
```

Sections `speech source`, `phoneme utterances`, `coarticulation`, `speaking rate`,
`prosody`, `voice profiles`, `singing` and `letter to sound`.

Measurements are taken out of rendered audio rather than read back off the parameters
that produced it — a synthesiser that ignored its frame stream and produced one long
neutral vowel would pass every structural check.

## Measuring against a reference

`tools/voicedump` renders an utterance to WAV at the DAC's own rate and bit depth, so
your output and a reference recording can go through one analysis. That is how
`VoiceProfile.Deliberate` was fitted, and it is the only honest way to tune a voice —
guessing got two of its five parameters backwards.

## Learning it

`docs/POKEYLAB-GUIDE.md` teaches the whole thing from nothing, using the PokeyLab UI.
