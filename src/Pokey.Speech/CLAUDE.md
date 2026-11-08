# Speech

A formant speech synthesiser: phonemes in, a sample buffer out. English text in, if you
want it.

**Does not reference `Pokey` and knows nothing about it.** It renders to a caller-chosen
number of levels, so "4-bit" is the front end's fact, not this library's. That split is
what makes it testable on its own and what would let it drive anything else.

It began inside `src/Pokey/` on the honest argument that it was pure arithmetic with no UI —
still true — and was split out once it grew a phoneme inventory, a transition model and a
letter-to-sound engine, none of which has anything to do with a sound chip.

## Who drives it

Three things, and the split above is why the third was cheap.

- **`PokeyLabForms`** — the Voice tab, where lines are designed. It is the only one
  that can *edit* a voice.
- **`Demo1`** — the voice page, which says a dozen lines at five settings so 4-bit and
  8-bit can be heard back to back.
- **`RimWalker`** — `Core/Voice.cs`. The cabinet asks "Shall we play a game?" on the
  title screen, names each level, and says "extra life" and "game over". Every phrase
  is synthesised at load from its spelling, so the game ships no recordings and
  "level forty one" exists without anyone having said it.

**`SpeechSource.Examples` is the durable home for a tuned line, and both other front
ends read it by label rather than copying.** `PokeyLabForms` persists nothing at all —
a melody or phoneme string typed into it is gone when the window closes — so a line
improved in the lab has to be pasted back into that table or it is lost. Doing it that
way means `Demo1` and `RimWalker` pick the improvement up with no second edit and no
chance of the copies drifting.

`RimWalker`'s copy is reached through `Voice.TitleLabel`; if that label is ever
renamed in `Examples`, the game silently falls back to the letter-to-sound rules'
attempt. Its self-test checks the label still resolves, because that failure is
otherwise inaudible — the line still speaks, just less well.

## The layers

Each is a separate stage, and the boundaries are where the design decisions live.

| File | Layer | Turns |
|---|---|---|
| `LetterToSound.cs` | spelling | English → phoneme codes |
| `Prosody.cs` | prosody | phonemes → stressed, reduced phonemes |
| `Song.cs` | melody | phonemes + notes → sung phonemes |
| `Phoneme.cs` | inventory | codes → synthesis parameters |
| `FrameStream.cs` | coarticulation | phonemes → control frames |
| `FormantSynth.cs` | synthesis | frames → samples |
| `VoiceProfile.cs` | speaker | which voice does all of the above |
| `SpeechSource.cs` | facade | short helpers, and the shipped examples |

## Provenance — read this before trusting a number

**Mixed, and the difference matters.**

- **Vowel formants are Peterson & Barney's 1952 adult-male averages.** Measured data,
  solid, the same source the rest of the project uses.
- **Consonant targets, all durations, hold fractions and anti-resonance frequencies are
  conventional textbook values adjusted by ear.** They are a starting point, not
  evidence. Do not cite them.
- **`VoiceProfile.Deliberate` is fitted to a measured reference** through
  `samples/voicedump` and a numpy analysis script — the same method `ARCADE-SOURCE.md` §9 (in the RimWalker repository, not this one)
  used on the shot sound.

**Do not port the reverse-engineered S.A.M. tables.** S.A.M. is copyrighted 1982
commercial software and that port's status is murky. The clean sources are the NRL
letter-to-sound rules (a US Navy report, public domain), published formant literature,
and ARPABET as a convention.

The matcher in `LetterToSound` implements the NRL context classes faithfully. **The
126-rule table is not the NRL table** — it is hand-written for this project, smaller and
less good, and says so in its own summary.

## Things that are load-bearing

**An unmarked phoneme is stressed.** Stress reaches a phoneme only by being written on
it, so a hand-typed `HH EH L OW` behaves exactly as it did before prosody existed.
Defaulting the other way would silently reduce every vowel anyone had ever typed.

**Marking stress and reducing vowels are separable, because their risks differ.** Wrong
stress gives a word odd rhythm — recognisably the right word. Wrong reduction changes
which vowel it is — a different word. Hand-written dictionary entries are marked but
never reduced; rule-derived words get both.

**A sung note owns its duration outright.** No stress multiplier, no speed control.
`Song.Sing` already worked the length out from tempo, beats and consonant cost, and
scaling it afterwards undoes that arithmetic. This was a real bug and **invisible by
ear** — a song running 9% fast still sounds like a song.

**Consonants are charged against the note, not added to it.** Otherwise rhythm drifts
later with every cluster.

**A note replaces the speaking pitch, never multiplies it.** Multiplying would put a tune
out of key by however far the sentence had declined.

**The resonator clamps below Nyquist**, and that is not defensive. `/s/` asks for F3 at
3800 Hz; at 4 kHz that is nearly twice Nyquist, and unclamped the pole angle wraps and
the filter resonates at a mirrored, plausible-sounding, entirely wrong frequency.

**`FrameStream` uses targets and interpolation.** Each phoneme plants its target in the
middle of its own span rather than filling it, so every boundary gets a transition for
free. `Phoneme.Hold` is how much a sound resists being moved — a stop holds 0.75, a vowel
0.30. With one flat value a `/t/` was as slurred as an `/aa/`.

**Nasals need an anti-resonance, not a weak vowel.** The murmur poles are similar across
/m/ /n/ /ŋ/; it is largely the zero that tells them apart. Adding it took /m/ and /n/ at
1900 Hz from near-identical to 0.883 against 0.102.

**`NoiseScale` was measured to, not reasoned to.** An RMS estimate said 0.12 would
balance voicing against noise; at 0.12 the fricative was still four times the vowel,
because the quantiser normalises to whichever is loudest. It is 0.015.

## Testing, and how the rules are scored

Tests live in `tests/Pokey.SelfTest/SelfTest.cs`. Run:

```
dotnet run --project PokeyLab -- --selftest
```

**The letter-to-sound rules are scored against two word sets, and the numbers mean
different things.** The 30 words the rules were written against report 100% and therefore
say nothing about unseen words. A second 20-word set chosen afterwards scored **55% at
first run**, exposed two genuine defects (the suffix class matched mid-word; `DGE` was
absent), reached 60% after fixing them, and **75% once stress reduction landed** — a
number measured on words nobody tuned against, which went up because of the prosody work.

**Keep both sets and do not add rules aimed at the second one.**

## Known gaps, all stated in code

- Stops have a locus and a closure but no release-specific burst spectrum.
- Stress assignment cannot see word class — `REcord` against `reCORD`.
- Compounds (`sunset`) get their second element wrongly reduced.
- `IH` is deliberately left unreduced: reducing it fixes `pencil` and breaks `music`.
- A melody's rests occupy a note slot rather than inserting silence; phrasing is done
  with `-` in the lyric.
- Two fitted values in `Deliberate` are **deliberately under-chased** — pushing them
  would be fitting to noise, and widening F2's bandwidth drags its measured centre off.

## Licensing of shipped content

*Daisy Bell* (1892) and *Lavender's Blue* (traditional, in print by ~1672) are long out
of copyright. ⚠️ **The 1948 Disney arrangement of "Lavender Blue" is copyrighted** — only
the traditional couplet is here and it should stay that way. Reference recordings are
measured, never committed.

## See also

- `docs/POKEYLAB-GUIDE.md` — the user-facing guide, including the phoneme notation
- `src/Pokey/CLAUDE.md` — the chip this usually plays through
- `samples/voicedump` — renders to WAV so output can be measured against a reference
