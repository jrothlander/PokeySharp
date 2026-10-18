# PokeyLabForms

A Windows Forms front end for the POKEY sound chip emulator — every register on one
screen, with a live scope, the arcade's own sound envelopes, and a speech synthesiser
that talks and sings through the chip's 4-bit volume nibble.

## Running

```
dotnet run --project PokeyLabForms
```

Requires the .NET 8 Windows Desktop runtime. No packages to install.

**Check the status bar first.** It should say *Audio running*. If it says *No audio*, the
chip is not advancing — every number still works, but the scope stays flat and nothing
makes a sound. The chip is ticked inside the audio render, so no audio means no time
passing.

You should hear a tone immediately. Channel 1 opens on a pure tone at mid volume, so an
empty window is not mistaken for a broken one.

## What is on screen

| Area | What it does |
|---|---|
| **Channels 1–4** | Divider, distortion, volume, volume-only. Live pitch in each title. |
| **AUDCTL** | The eight control bits, the raw byte, the base clock, and Mute. |
| **Envelope** | The arcade's four-byte step tables, and how they measure against real recordings of a cabinet. The **Sound** list is one built-in zap followed by every sound in `banks/`. **Copy as C#** hands the dialled-in voice over ready to paste. |
| **The sweep** | The envelope plotted tick by tick, with the measured target box. |
| **Output** | The scope — trigger-aligned, so a steady tone stands still. |
| **Voice** tab | Type English or phonemes, pick a voice, add a melody. |
| **Poly counters** tab | The shift registers, drawn as the bit trains they are. |

## Five minutes with it

1. **Drag Channel 1's AUDF slider** and watch the Hz figure beside it. The pitch moves
   unevenly, because AUDF is a divider, not a pitch — an inverse.
2. **Change Distortion** away from *PURE TONE* and look at the scope. Then open the
   **Poly counters** tab: the cells are the fixed sequence being consulted, the ticks
   underneath are where this channel samples it. Change AUDF again — **the ticks move,
   the cells do not.** That is why POKEY noise has a pitch.
3. **Tick *Join ch1 and ch2*.** Channel 1 becomes the low half with no pitch of its own;
   Channel 2 reports the pair's.
4. **Press Play** on the Envelope panel to hear the built-in zap. The green box is what
   recordings of Tempest's player shot measured, kept as a target for tuning against.
5. **Open the Voice tab and press Speak.** Watch the channel it uses drop into
   volume-only mode — the divider and the polys stop doing anything at all.
6. **Press Copy as C#** and paste somewhere. You get the `ChipSound.Build` call for
   whatever is dialled in, clock and all — the point being that you never retype four
   numbers off four spinners, which is where transcription errors come from.
7. **Open the Sound list and scroll past the zap.** Everything below it comes
   out of the `.sound` files in `banks/` — five sets, including a space shooter and ten lasers. Pick
   *Lasers: Classic zap*, tick **Loop**, and drag the AUDF delta: that is the whole loop
   this lab exists for.

## The Sound list, and where the rest of it comes from

The first entry is the library's own zap, compiled in so the list is never empty. The rest
are read at startup from the `.sound` files in `banks/`, which sit beside the executable
and are the same files `Demo1` browses and `RimWalker` plays from.

**Selecting a sound sets the base clock it was designed against**, and a bank sound sets
Tick Hz too. Every pitch in a voice scales with the clock, so a set designed at Tempest's
1.512 MHz and auditioned at RimWalker's 0.896 MHz is in the wrong key rather than slightly
off — which is why the Base clock combo now follows the selection instead of standing on
its own.

To add your own, drop a `.sound` file in `banks/` and restart. The format is one keyword
per line and is documented in [`Pokey/README.md`](../Pokey/README.md); a line the parser
cannot read is skipped rather than fatal, and whatever was skipped is listed in the Sound
list's tooltip.

## Getting a sound out of here

The lab can design a voice; **Copy as C#** is how it leaves. The clipboard gets the
call ready to paste into a game, with the clock named rather than written as a bare
number, so the line still says which machine it came from six months later.

From there, [`docs/SOUND-RECIPE.md`](../docs/SOUND-RECIPE.md) walks the rest of the
loop, and [`Demo1`](../Demo1/README.md) will play it back beside two dozen others. To
keep a whole set rather than one sound, put the same four numbers in a `.sound` file —
see [`Pokey/README.md`](../Pokey/README.md) for the format; `RimWalker` reads its own
effects from one.

## Making it talk

The Voice tab flows downward, and the order matters:

```
Example  ──►  fills everything below
English  ──►  derives the phonemes
Phonemes ──►  what actually gets spoken
Melody   ──►  empty = speak, notes = sing
```

English is converted by letter-to-sound rules that are right about three-quarters of the
time on unseen words, so their output lands in an editable box rather than going straight
to the synthesiser. The readout names which words were guessed by rule.

```
HH EH L OW  -  W ER L D
```

A hyphen is a pause; a digit is stress (`AA1` strong, `AX0` reduced); a `?` makes the
pitch rise. Double-click the phoneme list on the right to insert a code.

## Making it sing

Put notes in **Melody**:

```
150: C5 A4 F4/2 C4/2
```

Tempo, then one note per syllable. `/2` is two beats, `R` is a rest, `C4` is middle C.
The readout counts for you — `24 syllables on 24 notes`.

## Full guide

**`docs/POKEYLAB-GUIDE.md`** teaches all of this from nothing, in order, with things to
try at each step. Start there if any of the above was opaque.

## Testing

The self-test lives in the sibling `PokeyLab` project and covers the shared libraries:

```
dotnet run --project PokeyLab -- --selftest
```

It should print `PASS`.
