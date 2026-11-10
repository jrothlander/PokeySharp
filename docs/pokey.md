
# POKEY Chip Technical Reference
*A modern, developer‑friendly guide to the Atari CO12294 POKEY chip.*

POKEY is a multi‑function I/O and audio chip used in Atari 8‑bit computers and arcade systems. It provides:

- Four programmable audio channels  
- Multiple polynomial noise generators  
- High‑pass and low‑pass filtering  
- Keyboard scanning with debounce logic  
- Eight analog input ports (“pot” lines)  
- Three timers  
- A pseudo‑random number generator  
- A full serial I/O subsystem  
- Eight interrupt sources  

This document summarizes POKEY’s behavior in a clean, modern format suitable for emulator authors, audio developers, and hardware enthusiasts.

---

## 1. Audio System

POKEY’s audio engine consists of **four semi‑independent channels**, each with:

- An 8‑bit frequency divider (`AUDFx`)  
- An 8‑bit control register (`AUDCx`)  
- Optional noise generation  
- Optional high‑pass filtering  
- A 4‑bit DAC volume output  

### 1.1 Frequency Generation

Each channel uses a divide‑by‑N counter:

N = AUDFx + 1
Fout = Fin / (2 * N)

Clock sources (`Fin`) include:

| Clock Source | Description | Control Bit |
|--------------|-------------|-------------|
| 64 kHz       | Default     | —           |
| 15 kHz       | Lower‑rate clock | AUDCTL bit 0 |
| 1.79 MHz     | High‑rate clock | AUDCTL bits 5 or 6 |
| Chained clock | For 16‑bit mode | AUDCTL bits 3 and 4 |

#### 16‑bit Mode

Channels can be paired:

- Channel 1 → Channel 2  
- Channel 3 → Channel 4  

This produces:

- Two 16‑bit channels, or  
- One 16‑bit + two 8‑bit channels  

### 1.2 Noise Generation

POKEY includes three polynomial counters:

- **17‑bit**  
- **5‑bit**  
- **4‑bit**

Channels **sample** these counters at their divider rate, effectively creating a **low‑pass filter** (noise cannot change faster than the sample clock).

Noise selection is controlled by bits 5–7 of `AUDCx`.

### 1.3 High‑Pass Filtering

Channels 1 and 2 support a simple high‑pass filter:

- Channel 1 uses Channel 3’s divider as the HPF clock  
- Channel 2 uses Channel 4’s divider  

Enabled via AUDCTL bits 1 and 2.

### 1.4 Volume Control

Each channel ends with a 4‑bit DAC:

- `0` = silent  
- `15` = maximum volume  

Setting bit 4 of `AUDCx` forces **volume‑only mode**, bypassing tone/noise generation.

---

## 2. Keyboard Scanner

POKEY scans a 6×N keyboard matrix using:

- A 6‑bit binary counter  
- A compare latch  
- A keycode latch  
- Debounce logic  
- Two sense lines (full decode + CTRL/SHIFT/BREAK)

### 2.1 Scan Process

1. Counter increments each scan line (~15.7 kHz).  
2. When a key is pressed, the sense line goes low.  
3. The counter value is latched.  
4. If the counter matches the latch and the key is still low → **valid key**.  
5. Keycode is stored and an IRQ is generated.  
6. Debounce logic ensures stable detection.

### 2.2 Special Keys

CTRL, SHIFT, BREAK are decoded separately and **do not debounce**.

---

## 3. Pot Ports (Analog Inputs)

POKEY includes **eight analog input lines** (P0–P7). Each line:

- Charges through an external RC network  
- Latches the counter value when it crosses a threshold  
- Resets when the counter reaches 228  

### 3.1 POTGO Sequence

Writing to `POTGO`:

1. Clears the counter  
2. Releases dump transistors  
3. Starts counting  
4. Latches values as each pot line rises  
5. Re‑enables dump transistors at count 228  

### 3.2 ALLPOT

`ALLPOT` returns the digital state (valid/not valid) of all eight pot lines.

Fast‑scan mode requires removing the external capacitors.

---

## 4. Timers

Audio channels **1, 2, and 4** can generate timer interrupts when their counters roll over.

Writing to `STIMER`:

- Reloads AUDF values  
- Forces channels to known states  
- Resets timer logic  

---

## 5. Random Number Generator

POKEY includes a **17‑bit polynomial counter** (optionally reduced to 9 bits).

`RANDOM` returns the upper 8 bits.

This is the source of Atari’s classic pseudo‑random behavior.

---

## 6. Serial I/O

The serial subsystem includes:

- Serial output (transmit)  
- Serial input (receive)  
- Output clock  
- Bi‑directional clock  
- Shift registers  
- Framing and overrun detection  

### 6.1 Transmission

- CPU writes to `SEROUT`  
- Hardware shifts data with start/stop bits  
- IRQ signals “ready for next byte”  

Two‑tone mode replaces logic levels with audio channels 1 and 2.

### 6.2 Reception

- Hardware receives 8 bits + start/stop  
- Stores result in `SERIN`  
- IRQ signals “data ready”  
- Overrun and framing errors reported in `SKSTAT`  

### 6.3 Clocking Modes

Clock direction and source are controlled by `SKCTL` bits 4–6.

---

## 7. Interrupts

POKEY supports **eight interrupt sources**:

- BREAK key  
- Other key  
- Serial input ready  
- Serial output needed  
- Transmission finished  
- Timer 1  
- Timer 2  
- Timer 4  

Interrupts are controlled via:

- `IRQEN` (enable mask)  
- `IRQST` (status register)  

---

## 8. Register Summary

| Address | Name | Description |
|---------|------|-------------|
| 00–07   | P0–P7 | Pot values |
| 08      | ALLPOT | Pot line digital states |
| 09      | KBCODE / STIMER | Keyboard code / Start timers |
| 0A      | RANDOM | Random number generator |
| 0B      | POTGO | Start pot scan |
| 0C      | SERIN | Serial input |
| 0D      | SEROUT | Serial output |
| 0E      | IRQEN | Interrupt enable |
| 0F      | IRQST | Interrupt status |
| 10      | SKCTL | Serial/keyboard control |
| 11      | SKSTAT | Serial/keyboard status |
| 00/02/04/06 | AUDF1–4 | Audio frequency registers |
| 01/03/05/07 | AUDC1–4 | Audio control registers |
| 08 (mirror) | AUDCTL | Audio control |

---

## 9. Practical Usage Notes

- Noise sampling rate = divider output → acts as a low‑pass filter  
- High‑pass filters only exist on channels 1 and 2  
- 16‑bit mode is essential for low‑frequency tones  
- Pot timing must match scan‑line timing (~15.7 kHz)  
- Serial I/O timing depends heavily on SKCTL configuration  
- RANDOM is deterministic unless clocked irregularly  

---

## 10. Emulator Implementation Tips

- Implement polynomial counters exactly; many games rely on their quirks  
- Divider rollover timing must be cycle‑accurate for authentic sound  
- Keyboard debounce logic is essential for compatibility  
- Pot timing must match hardware scan‑line timing  
- Serial I/O requires correct start/stop bit framing  
- IRQ timing must match hardware behavior  

---

## 11. Credits & Preservation

This reference is based on:

- Original Atari hardware behavior  
- Reverse‑engineering efforts  
- Community documentation  
- Modern analysis and emulator research  


