# lasers

Ten laser shots, and the Pokey API applied to them one layer at a time.

Read `LaserSounds.cs` for the sounds and `Program.cs` for the API. Running it writes
every voice to a WAV, so the numbers can be heard rather than taken on trust.

```
dotnet run --project tools/lasers                  # render all ten to sounds/lasers/
dotnet run --project tools/lasers -- --list        # the table, measured back off the chains
dotnet run --project tools/lasers -- --sweep       # the same laser with no library help
dotnet run --project tools/lasers -- --design      # asked for in hertz and seconds instead
dotnet run --project tools/lasers -- --csharp      # each one as the call a game pastes
dotnet run --project tools/lasers -- --bank        # the set as a .sound file, read back
```

The exit code is the number of recipes that wrap a register without declaring it.

## The ten

| # | Name | ms | Pitch | Timbre |
|---|---|---|---|---|
| 1 | Classic zap | 73 | 3857 → 278 Hz | pure tone |
| 2 | Rapid pew | 45 | 2455 → 293 Hz | pure tone |
| 3 | Heavy blaster | 118 | 659 → 125 Hz | 5-bit growl, fading |
| 4 | Charged beam | 164 | 223 ↗ 1588 ↘ 750 Hz | pure tone, fading |
| 5 | Buzz cannon | 82 | 2077 → 148 Hz | 4-bit buzz |
| 6 | Pulse rifle | 82 | 1286 → 491 Hz | pure tone, gated three times |
| 7 | Ion beam | 273 | 871 → 153 Hz | 17-bit noise, fading |
| 8 | Rasp | 136 | 1421 → 140 Hz | pure tone, tremolo |
| 9 | Ricochet | 123 | 297 ↗ 1421 ↘ 199 Hz | pure tone |
| 10 | Siege cannon | 200 | 443 → 108 Hz | grind, fading |

Those figures are printed by `--list`, which walks the chains rather than repeating the
comments beside them — so if a recipe is edited and the note is not, the table tells the
truth and the note does not.

## The three things that make a laser

**The pitch falls, and AUDF is upside down.** AUDF is a countdown divider, so a *positive*
delta lowers the pitch. Nine of the ten count up.

**It is over quickly.** Nine are under 200ms. The same sweep at a third of the speed stops
reading as a shot and starts reading as an engine; duration is most of the design, which
is why "Rapid pew" is "Classic zap" with a bigger delta and fewer ticks and nothing else.

**Texture is one number.** AUDC is `distortion * 32 + volume`. Changing the distortion
nibble alone turns the zap into the buzz cannon, and every entry here differs from its
neighbours mostly in that one field.

## What each mode is for

`--sweep` writes AUDF and AUDC by hand in a loop and renders the slice between each write,
which is all a POKEY ever does — everything else in the library is that loop, tidied. Its
output is byte-identical in shape to `01-classic-zap.wav`, and comparing the two is the
fastest way to see what `StepEnvelope` and `SoundRender` actually do for you.

`--design` asks for the same sounds in hertz and seconds through `SoundDesign` and prints
what the compiler achieved beside what it was asked for. The last row is the rasp
described in plain terms; it compiles to `Step(18, 1, +6, 30)` / `Step(163, 1, -8, 30)`,
which is table entry 8's own four numbers.

`--bank` writes the set as a `.sound` file and immediately reads it back, because the
parser never throws — a line it cannot understand is skipped and recorded. Open the file
in PokeyLabForms to tune any of these by ear.

## Taking one into a game

`--csharp` prints each recipe as the `ChipSound.Build` call from `docs/SOUND-RECIPE.md`,
the same shape PokeyLabForms' **Copy as C#** button emits. Reference `Pokey` (it has no
dependencies), paste in the thirty-line `ChipSound` from that document, and build the
sounds once at load.

The clock and the tick rate travel with the numbers and are the two easiest things to
forget: every pitch scales with `baseClockHz`, so the same steps at another clock are in
another key.
