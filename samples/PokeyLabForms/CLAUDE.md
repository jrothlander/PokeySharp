# PokeyLabForms

**`WaveOutStream` and `LabColors` are this project's, and one other app links them**: a
tracker in the workshop repository next door, which plays through the same winmm streaming
and draws with the same colours. Changing either reaches both, so a change here should be
checked there too.

PokeyLab as a Windows Forms application. `net8.0-windows`, `UseWindowsForms`, no packages.

Same chip, same envelopes, same numbers as the MonoGame `PokeyLab` next door. **What
changes is the interface**, and both front ends stay.

## Why two front ends

There the pages are keyboard-driven lists drawn with a vector beam, which suits a project
about an arcade cabinet but means every value is reached by walking a caret to it. Here
each value is a real control you can click, and everything is on screen at once.

Neither replaces the other. This one is easier to drive; the MonoGame one shares
RimWalker's presentation layer and can be run against the game side by side.

## The single-window premise, and where it stops

Every register, the envelope, the sweep, the scope and the nine bytes themselves are
visible at all times. That is the point: turning a divider and watching the byte, the
pitch, the trace and the verdict all move should cost nothing.

**Only the bottom-right explainer swaps**, as a `TabControl` — Poly counters and Voice.
Poly counters is added first and so opens by default: every channel strip's distortion
field selects one of those counters, so it is the explainer for the mechanism the rest of
the window keeps referring to, where the voice is a separate subject you go looking for.
Those two are different subjects, never consulted at the same instant, and the Voice page
had genuinely outgrown the 316px column it used to share. Tabbing them costs nothing an
A/B needs, because a tone left running on another channel keeps sounding whichever tab is
open.

The cost of the premise is a large minimum window, which is why this is a second front
end rather than a replacement.

## The register strip shows the cause, not another effect

`Views/RegisterBitsView.cs`, docked full width along the bottom. Every other display in
the window shows an *effect* — the scope shows what came out, the sweep graph shows pitch
over time, the poly trains show the mechanism in between. But every control on the form is
a human abstraction, a dropdown reading "distortion 5" and a slider reading "volume 2",
and the chip has never heard of any of them. It is handed `$A2`. Until this strip there
was nowhere in the app you could see the byte.

Three things it makes visible that were otherwise only assertable: **AUDC's arithmetic**
(the three fields are coloured apart, so `distortion x 32 + volume` reads straight off the
cells), **a joined pair** (set AUDCTL bit 4 and channel 1's row greys out — it is a low
byte, it clocks its partner, it is not a voice, which is the whole answer to why joining
1+2 silences a sound playing on channel 1), and **the envelope's wrap** during playback.

Three decisions worth keeping:

- **Full width and always visible, not a third explainer tab.** The value is watching a
  byte change while your hand is on the control that changed it; by the time you switch to
  a tab, the thing you wanted to see has happened. It costs 142px of an already large
  window, and `ClientSize`/`MinimumSize` were raised to pay for it out of the window rather
  than out of the poly trains, which were already the tightest thing on screen.
- **It is a read-out. The cells do not click.** Every bit already has a control elsewhere
  on the form, and a second editing surface for the same state is both a synchronisation
  problem and a question about which one is authoritative. Same standing as the poly
  trains.
- **The changed-bit markers are sampled, and the code says so.** The window refreshes at
  30 Hz while the envelope ticks at a couple of hundred, so a marker means "this differs
  from the frame ~7 ticks ago", not "this was written". A bit that toggles twice in the gap
  reads as unmoved. Repainting a form full of controls at the envelope's rate would cost
  far more than it showed.

**The strip mirrors the form's own column order: four channels stacked on the left,
AUDCTL beside them on the right.** That is the whole reason it reads as the same machine
rather than an unrelated grid — the channel strips below it are a vertical stack in the
left column, and the AUDCTL panel sits immediately to their right. An earlier version
paired the channels 1-2 / 3-4 across two halves, which fitted a short strip at the bottom
of the window but matched nothing above it. Channels are left-anchored and AUDCTL
right-anchored, so the slack collects in the middle, where it separates two genuinely
different things: eight per-channel registers, and the one register that governs the chip.

AUDCTL is drawn deliberately **out of alignment** with the channel rows, and with wider
cells. Those eight columns are one field read as a number; these eight are eight unrelated
switches that each carry their own name, and lining them up would imply a relationship
that is not there.

**The gutter is derived from the note width, and must stay that way.** Channel notes
("VOL ONLY", "LOW BYTE") are deliberately *not* a column inside the block — reserving one
put a permanently empty column at the right-hand edge and made the whole display read as
shoved left. Instead a note draws just past the end of its row, into the gap before
AUDCTL. At a nominal gap those collided with whatever was to the right;
`gutter = noteW + 14` makes that impossible however the rest is sized, and each note is
clipped against the thing it would actually run into rather than against the panel edge.

**A channel row is sixteen cells, not eight** — AUDF and AUDC each get their own eight.
Sizing the row off `Bits` rather than `2 * Bits` understates its width, which leaves the
note clip and the AUDCTL placement working off a row edge that is not where the row ends.

## The AUDC step is edited as fields, not as a byte

The envelope panel's AUDC grid has a distortion picker and a volume spinner under it, and
they compose the `Value` cell rather than duplicating it. **AUDC's Value is the one field
on that panel nobody can read at a glance**: AUDF's value is a divider and 16 means
sixteen, while AUDC's 162 means distortion 5 and volume 2, which is only obvious once you
already know where the field boundary falls. Editing the fields and watching the byte
assemble in the grid demonstrates `distortion x 32 + volume` instead of asserting it —
the same job the coloured cells do for the live chip in the register strip. The note
underneath writes the sum out: `AUDC 162 = 5 x 32 + 2 = $A2`.

The Value cell stays editable, and it stays the ROM's own number. Two consequences worth
keeping: **bit 4 is carried through, not exposed** (volume-only turns the channel into a
four-bit DAC, which is the sample voice's business, so a step that has it set keeps it
through any edit here and the note says so), and `ComposeAudc` reads the *selected* row,
so the grid's selection is what these two controls point at.

`UpdateNotes` saves and restores `_loading` rather than clearing it, because it runs from
inside `RefreshChains` as well as on its own — clearing it there would let the rest of that
refresh write to the chip.

**The grids get whatever the fixed rows do not, and that is the constraint to respect when
adding anything to this panel.** A chain can be eight steps — the extra-life flourish is —
and a grid showing one row cannot tell you whether there is a second, which defeats the
reason both chains are shown in full here rather than one step at a time like PokeyLab.
Adding the composer row alone squeezed them to a single visible row. Two structural
savings bought it back, and both should survive: **Channel and Tick Hz share one settings
line**, and **each chain's Add/Remove buttons share a row with its heading** rather than
taking one underneath (48px per chain spent on two half-line things). Loop moved to the
transport row for the same reason — it modifies playback, so it belongs there anyway.
Together with a 20px `RowTemplate.Height`, that is four visible steps per grid plus a
scrollbar. Anything new on this panel comes out of that budget.

## The Sound list reads the banks, and that makes the chip the clock's authority

The list is one compiled-in voice, the library's zap, followed by every sound in every `.sound`
file in `banks/`, discovered once at construction from beside the executable. Those files
already existed and `Demo1` and `RimWalker` already read them; the lab was the one place
in the repo that could not play a sound it had itself been used to design.

**Reading the bank rather than copying its numbers into a second table** is the whole
point. A table here would be a second copy of the recipes with nothing keeping it in step,
and the file is what `Copy as C#` and `samples/lasers` both write.

**A selection now sets the base clock, which is a real change of ownership.** The Base
clock combo used to be the only thing that set `Chip.BaseClockHz` and so never needed to
read it back. A bank carries the clock it was designed against — every pitch in it scales
with that number, so RimWalker's set at Tempest's clock is in the wrong key, not slightly
off — so loading one has to move the clock, and `AudctlPanel.SyncFromChip` now follows the
chip like every other readout on the form. **The built-in voice carries Tempest's clock
for the same reason**: once anything in the list can change it, a voice that leaves it
alone inherits whichever machine the previous selection picked, and would silently play a
fifth out.

Tempest's own eight voices used to be compiled in here too. They are the ROM's numbers, so
they moved to a private bank (`banks/tempest.sound` in RimWalker) on 2026-11-08; to
audition them, copy that file into `banks/` beside the lab's executable.

Tick Hz follows a *bank* sound but not the built-in one: it already runs at the
220 Hz the app opens at, and the tick is a knob the guide asks you to turn. The recipe's
`amp` is ignored — this lab has no output volume, and the chip's own volume nibble is
inside AUDC where it can be seen.

**The banks report on themselves in the list's tooltip, not in a label.** The panel has no
spare row (see the budget above), and the problems still have to be *shown* — the parser
never throws, so a mistyped line is skipped and recorded, and a caller that swallows
`Problems` turns one typo into one silently missing sound.

## The AUDC delta annotation

`StepEnvelope.VolumeDelta` (in `src/Pokey/`, deliberately not here) answers what a delta is
actually worth once `Apply` masks it into the four-bit volume nibble: **only `delta mod 16`
does anything.** `-8` and `+8` are the same edit; `+16`, `-32` and `+64` are all no-ops.
The column clamps to -64..64 — one shared `Limits` row serving both grids, which is right
for AUDF, whose mask is a full byte — so it offers 129 numbers and sixteen behaviours.

The rule lives next to `Apply` rather than in this panel so there is one home for it, and
`DeltaNote` gets the levels a step really visits by calling `StepEnvelope.ValueAt`, not by
recomputing the mask. A step whose `Outer` ends before the ring closes therefore shows what
happens rather than the cycle in the abstract.

Three things worth not undoing:

- **The cells keep the ROM's own number.** `-8` displays as `-8` even though it lands as
  `+8`, because that is the byte in the cartridge and what *Copy as C#* has to emit. The
  interpretation goes in the note and the tooltip, where it cannot be mistaken for data.
- **A no-op delta needs both `ForeColor` and `SelectionForeColor`.** The grid is
  `FullRowSelect`, so the row you just edited is almost always selected, and a selected
  cell ignores `ForeColor` entirely — setting only that marks the case in the one state
  you are least likely to see it in. Firebrick reads as a warning on the window background
  and turns to mud on the selection blue, hence two colours rather than one twice.
- **The notes row is 76px for four lines.** It was 40, then 62; at 62 the no-op sentence
  clipped mid-word, which a `Label` does silently.

`SelfTest.Envelope` checks the helper against the emulator rather than against itself —
it walks all 129 deltas through `ValueAt` and measures the volume's real period. Asserting
the arithmetic twice would prove nothing.

## Audio, and the one real structural difference

MonoGame's `DynamicSoundEffectInstance` has no Windows Forms equivalent, and it could not
simply be dropped: a lab whose chip only runs while a sound is being auditioned is a
different and much less useful tool. The chip has to run continuously so turning a
divider slides the pitch of a note already sounding.

`WaveOutStream.cs` rebuilds the streaming on `winmm.dll`'s waveOut — chosen over a
package because the repo's ground rules ask not to take dependencies casually.

**The refilling runs on a dedicated thread.** A Forms timer would do it, but its
resolution is a frame or two of audio and it stops being serviced promptly whenever the
UI is busy — which is exactly when a knob is being turned.

**Hence `ChipSession.Gate`, and the rule that goes with it: everything that reads or
writes the chip or the envelope takes it first.** That includes reads for display, which
cost nothing at thirty a second and would otherwise be one of those races that is fine
for months and then produces a value nobody can explain.

Buffers are pinned for their whole life. The driver reads that memory from another thread
with no idea the CLR exists, so a compacting collector moving it would be a use-after-move
with no exception to catch.

`ScopeTrace` is deliberately **outside** the gate. It is written on the audio thread and
read on the UI thread, and its ring is built to tolerate exactly that — `Fill` backs off a
full window from the write head. The ring's own guarantee, not a lock, is what makes the
picture whole.

## Drivers are mutually exclusive, and that is a real constraint

`StepEnvelope.Render` and `SampleVoice.Render` both work by slicing the audio block at
their own write boundaries. Neither can wrap the other without inheriting its slice
timing, which is the buffer-size dependence both are written to avoid. Supporting both at
once needs a driver that merges their event streams — a real design, and nothing yet
wants it.

**Tone generators are unaffected.** A channel with a divider and a volume just sounds,
with nobody writing to it, so an A/B against speech works under either driver.

## Three bugs worth not reintroducing

**Stopping a voice that was never playing wipes the channel.** `StepEnvelope.Stop` clears
the volume nibble and `SampleVoice.Stop` clears the whole AUDC byte. Both handovers guard
on the rival actually playing — without it, pressing Speak silenced a tone the user had
dialled in by hand, which is exactly the tone the A/B wants left running.

**`Dock.Fill` does not make a control fill, and that is what breaks row alignment.**
A NumericUpDown, a ComboBox and a TrackBar each refuse to shrink below a floor. When the
cell is shorter than that floor a `TableLayoutPanel` stops centring the control and pins
it to the top instead — so two controls on one row sit at different heights for reasons
that have nothing to do with either of them, and the offset moves whenever anything else
on the form claims a few pixels. The register strip taking 142px off the bottom of the
window dropped the channel rows to 21px, the spinners hit their 19px floor and overflowed
while the sliders still fitted, and **every slider ended up 5px above the number beside
it**.

The fix in `ChannelStrip` is three rules together, and all three are load-bearing:
anchor left-and-right instead of docking, so the width stretches and the height is left
alone; give every control on a row the *same* height, taken from `NumericUpDown.
PreferredHeight` so it is right at any DPI; and size the three register rows absolutely to
that height rather than as four equal shares, so nothing is ever clamped. The checkbox has
no floor and takes the remainder. Group padding is `(8, 2, 8, 2)` because the rows need
those pixels more than the group does.

One measured constant survives: the slider carries one pixel more top margin than the
spinner, because **a TrackBar draws its thumb about 1.5px above the centre of its own
box** — it reserves the bottom for tick marks it is not drawing. Verified by measuring
thumb centres against the number fields, not by eye; the residual is 0.5px, which is the
integer-pixel floor.

**A `TableLayoutPanel` given a `ColumnCount` but no `ColumnStyle` makes every column
AutoSize.** The column then takes the width of its widest child and stretches every other
child to match. That is how combo boxes ended up 396px wide inside a 452px panel, clipped
past their own dropdown arrows, and how the step grids lost their `Outer` column off the
right edge. **Every single-column panel here states its style.** `AutoSize` on a panel
containing a `Percent` column is the other half of the same trap.

## Files

| File | What |
|---|---|
| `MainForm.cs` | Layout, the 30 Hz refresh, the one place the gate is taken |
| `ChipSession.cs` | The live chip, the envelope, the voice, mute, and the gate |
| `WaveOutStream.cs` | winmm P/Invoke and the feeder thread |
| `LabColors.cs` | Palette for the three instrument displays only |
| `Views/` | Scope, sweep graph, poly trains, register bits — handed data, never the chip |
| `Panels/` | Channel strips, AUDCTL, envelope, measured verdict, voice, poly |

`ScopeTrace.cs` is **linked** from `samples/shared/`, not copied, so a fix to the trigger search
lands in both front ends. It keeps its `PokeyLab` namespace because it is PokeyLab's file.

`banks/*.sound` is **copied** beside the executable by the csproj, exactly as `Demo1`
takes it. Copied and not embedded because the point of the format is that a file can be
edited and reopened; embedding would make the shipped banks the only ones that could never
be changed. The set includes `lasers.sound`, which `samples/lasers` writes.

## Copy as C# — how a voice leaves the lab

The envelope panel emits the dialled-in voice as the `ChipSound.Build` call a game
would paste. The lab could design a sound but not hand it over: you read four numbers
off four spinners and retyped them, which is where transcription errors come from.

`ChipSession.Clocks` carries each clock's **symbol name** alongside its value for this,
so the copied line says `PokeyClocks.TempestHz` rather than a bare `1512000`. A number
would still work; a name says which machine it came from, which is the thing you want
six months later. Anything reading that table destructures three fields now, not two.

The output is wrapped for chains longer than three steps, using `Environment.NewLine`
rather than `\n` — a mixed-ending block pasted into an editor is its own small
annoyance. See [`docs/SOUND-RECIPE.md`](../../docs/SOUND-RECIPE.md) for the loop this
feeds, and [`src/Pokey/README.md`](../../src/Pokey/README.md) for the `.sound` file format if a
whole set is wanted rather than one voice.

## Conventions

- **No packages.** Not for audio, not for layout, not for anything.
- Views are handed plain arrays and know nothing about the chip or the gate. All locking
  is in `MainForm.Sync`, taken once, so the window always shows one consistent instant.
- A panel's `SyncFromChip` documents that its caller holds the gate.
- A focused control is never overwritten by a sync — whichever control has focus wins for
  as long as it has focus. That rule is the difference between a live readout and a fight.

## Testing

There is **no `--selftest` here**, deliberately. The emulation checks in
`tests/Pokey.SelfTest/SelfTest.cs` already cover the shared libraries, and duplicating them would buy
two copies to keep in step and no coverage.

```
dotnet run --project PokeyLab -- --selftest
```

## See also

- `docs/POKEYLAB-GUIDE.md` — how to actually use this window
- `src/Pokey/CLAUDE.md`, `src/Pokey.Speech/CLAUDE.md` — the two libraries it drives
