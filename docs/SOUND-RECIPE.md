# From PokeyLab to your game

How to design a sound in PokeyLabForms and play it in a MonoGame project.

Dial a voice in by ear in the lab, write the numbers down, and let the game render them 
through the same emulated chip at load time. Nothing is recorded and no audio file ships 
— the game builds every sound from about a dozen integers.

Worked here with a maze game in mind (a chomp, a power-up, a death), but nothing about it
is specific to that.

---

## The loop, in one picture

```
   PokeyLabForms                            your game
   ─────────────                            ─────────
   turn knobs        ──►  Copy as C#  ──►   PokeyChip renders them
   until it right         (clipboard)       to a SoundEffect at load
```

Nothing is typed by hand. The **Copy as C#** button under the AUDC grid puts the whole
voice on the clipboard in the form the code below expects — both step chains, the tick
rate and the base clock, which are the two values that live outside the grids and are the
easiest to forget.

---

## Step 1 — design it in the lab

Work in the **Envelope** panel, top right. Ignore the measured-bands readout beneath the
graph; that is calibration for one specific arcade sound and means nothing for yours.

Start from a preset near what you want and edit it, rather than from nothing:

| Want | Start from |
|---|---|
| a short blip | **Starfighter: Blip** |
| a falling sweep | **Lasers: Classic zap** |
| a rising sweep | **Platformer: Power up** |
| a noisy hit | **Space shooter: Explosion, small rock** |

Press **Play** to hear it. Tick **Loop** while you are dialling — a sound you can hold is
far easier to judge than one you re-fire every time.

### What you are actually editing

Two grids. **AUDF** is the divider, which controls pitch. **AUDC** is one byte holding
both volume and timbre. Each row is one step, and each step has four fields:

| Field | Meaning |
|---|---|
| **Value** | what the register starts at |
| **Inner** | ticks between each change |
| **Delta** | how much to add each time |
| **Outer** | how many changes to make |

A step lasts `Inner × Outer` ticks, and **Tick Hz** sets how fast a tick is. So the
duration of a step is `Inner × Outer ÷ TickHz` seconds.

**Remember AUDF is a divider, so it is upside down.** A *positive* delta on AUDF makes the
pitch *fall*. That trips everybody up once.

### Getting the AUDC value you want

AUDC packs two things into one number:

```
AUDC = distortion × 32 + volume
```

Volume is 0–15. Distortion is 0–7, and the useful ones are:

| Distortion | Sound | AUDC at volume 8 |
|---|---|---|
| 5 or 7 | pure tone | **168** |
| 6 | 4-bit poly — buzzy, pitched growl | **200** |
| 1 or 3 | 5-bit poly — coarse buzz | **40** |
| 4 | 17-bit poly — proper noise | **136** |

The line under the AUDC grid decodes whatever you have typed, so you can check yourself.

**Only the volume nibble moves.** A delta on AUDC changes the volume and leaves the
distortion alone — that is the hardware's behaviour, not a limitation here, and it means
a sound keeps one timbre for its whole life.

### Also write down

- **Tick Hz** — the box above the grids.
- **Base clock** — the combo under AUDCTL. Every pitch scales with it, so a sound
  designed at one clock and played at another will be in the wrong key.

You can ignore AUDCTL's checkboxes unless you deliberately used them; the game code below
writes 0, which is what the arcade does at init.

---

## Step 2 — press Copy as C#

The button sits under the AUDC grid. It puts this on the clipboard:

```csharp
// PokeyLab — "Lasers: Rasp", Tempest 1.512 MHz, 220 Hz tick
// AUDC 163 = distortion 5 (PURE TONE), volume 3
ChipSound.Build(
    audf: new[] { new Step(18, 1, +6, 30) },
    audc: new[] { new Step(163, 1, -8, 30) },
    tickHz: 220, baseClockHz: PokeyClocks.TempestHz, amplitude: 0.5f);
```

That is the whole sound — about a dozen integers and no audio file. Chains longer than
three steps break across lines rather than running off the edge.

The two comment lines are for you rather than the compiler: the first records which
preset and clock it came from, and the second decodes the AUDC byte so you are not
staring at `163` in six months wondering what timbre it was.

You will want to change `amplitude`, which the button always emits as `0.5f` because the
lab has no volume control of its own — the chip's own volume nibble is inside AUDC.

---

## Step 3 — reference Pokey from your project

`Pokey` has no dependencies at all — not on MonoGame, not on a package — so it drops into
any C# project.

```xml
<ItemGroup>
  <ProjectReference Include="..\Pokey\Pokey.csproj" />
</ItemGroup>
```

---

## Step 4 — the renderer

About thirty lines, and you write it once. This turns an envelope into a MonoGame
`SoundEffect`:

```csharp
using System;
using Microsoft.Xna.Framework.Audio;
using Pokey;

public static class ChipSound
{
    private const int SampleRate = 44100;

    // PokeyChip.Mix divides by all four channels' worth of headroom and we drive one,
    // which is 4. Its output is unipolar -- a real POKEY sources current one way -- so
    // the DC blocker in Render centres it and halves the swing, which is the other 2.
    private const float ChipGain = 2f * PokeyChip.Channels;

    public static SoundEffect Build(StepEnvelope.Step[] audf, StepEnvelope.Step[] audc,
                                    double tickHz, double baseClockHz, float amplitude)
    {
        var chip = new PokeyChip { BaseClockHz = baseClockHz };
        chip.SetControl(0);

        var envelope = new StepEnvelope
        {
            AudfChain = audf,
            AudcChain = audc,
            TickHz = tickHz,
            Channel = 0,
        };

        int count = Math.Max(1, (int)(SampleRate * envelope.Seconds));
        var mixed = new float[count];

        // StepEnvelope.Render walks the steps and renders in one call, cutting the
        // block at the tick boundaries so a sweep never quantises to the buffer size.
        envelope.Trigger(chip);
        envelope.Render(chip, mixed, 0, count, SampleRate);

        var pcm = new byte[count * 2];
        for (int i = 0; i < count; i++)
        {
            // A short fade at the tail only. The arcade cuts its sounds dead, and
            // fading the attack blunts the very thing that makes a blip read as a blip.
            float tail = Math.Min(1f, (count - i) / (SampleRate * 0.01f));
            float value = Math.Clamp(mixed[i] * ChipGain, -1f, 1f);

            short sample = (short)(value * amplitude * tail * short.MaxValue);
            pcm[i * 2] = (byte)(sample & 0xff);
            pcm[i * 2 + 1] = (byte)((sample >> 8) & 0xff);
        }

        return new SoundEffect(pcm, SampleRate, AudioChannels.Mono);
    }
}
```

Two things worth not changing:

- **`ChipGain` is 8, and both halves of it are real.** Drop it and everything is eight
  times too quiet.
- **PCM is little-endian 16-bit** — low byte first. Get it backwards and you get noise
  that is *almost* your sound, which is a horrible thing to debug.

---

## Step 5 — build them at load, play them at will

```csharp
using Microsoft.Xna.Framework.Audio;
using Pokey;
using Step = Pokey.StepEnvelope.Step;

public sealed class Sounds : IDisposable
{
    private const double Clock = PokeyClocks.TempestHz;   // whatever the lab showed
    private const double Tick  = 220.0;

    private readonly List<SoundEffect> _all = new();
    private readonly SoundEffect _chomp, _power, _death;

    public Sounds()
    {
        _chomp = Track(ChipSound.Build(
            audf: new[] { new Step(60, 1, +8, 6) },
            audc: new[] { new Step(200, 1, -1, 6) },
            Tick, Clock, amplitude: 0.5f));

        _power = Track(ChipSound.Build(
            audf: new[] { new Step(120, 2, -5, 20) },
            audc: new[] { new Step(168, 2, 0, 20) },
            Tick, Clock, amplitude: 0.4f));

        _death = Track(ChipSound.Build(
            audf: new[] { new Step(30, 2, +6, 32) },
            audc: new[] { new Step(40, 2, 0, 32) },
            Tick, Clock, amplitude: 0.6f));
    }

    public void Chomp() => _chomp.Play();
    public void PowerUp() => _power.Play();
    public void Death() => _death.Play();

    private SoundEffect Track(SoundEffect effect) { _all.Add(effect); return effect; }

    // SoundEffect holds unmanaged audio-buffer memory. Build them once at load and
    // dispose them together; building one per frame will leak until it hurts.
    public void Dispose() { foreach (var e in _all) e.Dispose(); _all.Clear(); }
}
```

`SoundEffect` needs an audio device, so build these after graphics and audio are up —
`LoadContent` is the natural place.

---

## Three starting points

These are shapes to modify in the lab, not finished sounds. Type them into the grids,
press Loop, and turn things.

**A chomp** — short, buzzy, falling. Positive AUDF delta so the pitch drops.

```
AUDF   Value 60   Inner 1   Delta  +8   Outer  6
AUDC   Value 200  Inner 1   Delta  -1   Outer  6      (distortion 6, volume 8)
```
27 ms at 220 Hz. Make it shorter with a smaller Outer, grittier with distortion 1.

**A power-up** — a rising pure tone. Negative delta so the divider falls and pitch rises.

```
AUDF   Value 120  Inner 2   Delta  -5   Outer 20
AUDC   Value 168  Inner 2   Delta   0   Outer 20      (pure tone, volume 8)
```

**A death** — long, falling, coarse. The 5-bit poly keeps it musical rather than hissy
even at the bottom.

```
AUDF   Value 30   Inner 2   Delta  +6   Outer 32
AUDC   Value 40   Inner 2   Delta   0   Outer 32      (5-bit poly, volume 8)
```

For an arpeggio or a two-part sound, add rows. Each AUDF row runs after the previous one,
and the AUDC chain runs independently alongside — the sound lasts as long as the longer of
the two.

---

## Twenty-two more, in a program you can run

`Demo1` is this whole document as a working application. Build and run it and you get a
browsable shelf of arcade sounds — weapons, impacts, movement, pickups, interface, states
— each one playable, and each shown as the numbers that make it.

```
dotnet run --project Demo1
```

Arrow keys move, left and right jump between categories, Enter plays, and clicking a name
does both. For whichever sound is selected it shows the AUDF and AUDC chains exactly as
`Copy as C#` would emit them, what the divider and the volume do over the sound's life,
and the rendered waveform itself.

It has a second page — press **2**, or click the tab — where the same idea is applied to
speech: game lines synthesised at load from their English spelling, playable through a
POKEY's four bits or through eight. See `src/Pokey.Speech/README.md`.

The three things worth having it open for:

- **The pitch plot is a stair, not a curve.** It shows what a constant delta actually does
  to pitch — a hyperbola, steep at the top and flattening out — which is the single most
  common surprise when designing a sweep in the lab.
- **A tone and a poly look different**, not just sound different. Compare *Laser* against
  *Heavy cannon* in the waveform pane.
- **Duration is most of the design.** *Explosion* and *Hit* are close to the same shape;
  one runs 273 ms and the other 41 ms, and they read as completely different events.

The recipes live in `samples/Demo1/SoundLibrary.cs`, which is deliberately free of MonoGame — it is
a table of integers, so it can be rendered and checked without an audio device. Copy an
entry into your own game and it will work unchanged.

---

## What RimWalker does differently, and why

If you read `RimWalker/Core/Sfx.cs` expecting the code above, two things will not match.

**It walks the envelope itself** rather than using `StepEnvelope`, in `PokeyAudfAt` and
`PokeyAudcAt`. That predates the shared class and means the step arithmetic now exists
twice. **Use `StepEnvelope` in new code** — the duplication is a wart, not a pattern.

**Its clock is multiplied by 28.** `Sfx.cs` stores `EffectiveClockHz`, which is the
chip's *slow* clock, and multiplies back up to a base because the chip divides by 28
internally. That is a compatibility knot around an unresolved question — see
`ARCADE-SOURCE.md` §9 (in the RimWalker repository, not this one). **Pass the base clock the lab shows you** and skip all of it.

---

## Why the button exists

This step used to be manual — read sixteen integers off a grid, type them into source —
and it was the only place in the whole loop where you could silently get it wrong.
Neither failure announces itself:

- **A transposed digit** gives a sound that still plays, just not the one you designed.
- **A forgotten Tick Hz or base clock** gives the right shape at the wrong speed or in
  the wrong key, which reads as a bad design decision rather than a typo.

The clipboard removes both, because it carries the tick rate and the clock along with the
steps rather than leaving you to remember them.

It still cannot check that you pasted it into the right place. If a sound comes out wrong
in the game, paste the snippet's numbers back into the lab and A/B them — that isolates
whether the problem is the voice or the wiring in about a minute.

---

## See also

- **`samples/Demo1/`** — twenty-two of these as a running program; see above.
- **`docs/POKEYLAB-GUIDE.md`** — the lab from nothing, if any of the above was opaque.
- **`src/Pokey/README.md`** — the library's own API.
