# POKEY Register Bit‑Field Reference

This document provides a modern, developer‑friendly breakdown of POKEY registers, including bit‑fields, meanings, and usage notes.

---

## Audio Registers

### AUDF1, AUDF2, AUDF3, AUDF4 — Audio Frequency Registers
Addresses: 00, 02, 04, 06 (write-only)

- Bits 7–0: Frequency divisor  
  - N = AUDF + 1  
  - Fout = Fin / (2 * N)

---

### AUDC1, AUDC2, AUDC3, AUDC4 — Audio Control Registers
Addresses: 01, 03, 05, 07 (write-only)

- Bits 7–5: Noise / distortion select  
  - 000: Pure tone  
  - 001: 5-bit poly  
  - 010: 4-bit poly  
  - 011: 4-bit poly (alt)  
  - 100: 17-bit poly  
  - 101: 17-bit poly (alt)  
  - 110: 5-bit poly (alt)  
  - 111: Force volume-only mode
- Bit 4: Volume-only mode (forces audio input = 1)
- Bits 3–0: Volume (0–15)

---

### AUDCTL — Audio Control
Address: 08 (write-only)

- Bit 7: Use 9-bit poly instead of 17-bit  
- Bit 6: Clock Channel 1 at 1.79 MHz  
- Bit 5: Clock Channel 3 at 1.79 MHz  
- Bit 4: Channel 2 clock = Channel 1 (16-bit mode)  
- Bit 3: Channel 4 clock = Channel 3 (16-bit mode)  
- Bit 2: High-pass filter on Channel 1 (clocked by Ch3)  
- Bit 1: High-pass filter on Channel 2 (clocked by Ch4)  
- Bit 0: Use 15 kHz base clock instead of 64 kHz  

---

## Keyboard & Serial Control

### KBCODE — Keyboard Code
Address: 09 (read-only)

- Bit 7: CONTROL key active  
- Bit 6: SHIFT key active  
- Bits 5–0: Key code (0–63)

---

### SKCTL — Serial/Keyboard Control
Address: 10 (write-only)

- Bit 7: Force serial output = 0 (or low tone in two-tone mode)  
- Bit 6: Serial clock direction (input/output)  
- Bit 5: Serial mode select  
- Bit 4: Serial mode select  
- Bit 3: Two-tone serial output mode  
- Bit 2: Keyboard scan enable  
- Bit 1: Keyboard debounce enable  
- Bit 0: Fast pot scan enable  

---

### SKSTAT — Serial/Keyboard Status
Address: 11 (read-only)

- Bit 7: Serial framing error  
- Bit 6: Keyboard overrun  
- Bit 5: Serial input overrun  
- Bit 4: Direct serial input state  
- Bit 3: Serial output busy  
- Bit 2: Keyboard key pressed  
- Bit 1: BREAK key pressed  
- Bit 0: SHIFT/CTRL/BREAK decode state  

---

## Pot (Analog) Registers

### POT0–POT7 — Pot Values
Addresses: 00–07 (read-only)

- Bits 7–0: Latched pot value (0–228)

---

### ALLPOT — Pot Line Digital State
Address: 08 (read-only)

- Bit 7: Pot 7 valid  
- Bit 6: Pot 6 valid  
- Bit 5: Pot 5 valid  
- Bit 4: Pot 4 valid  
- Bit 3: Pot 3 valid  
- Bit 2: Pot 2 valid  
- Bit 1: Pot 1 valid  
- Bit 0: Pot 0 valid  

---

### POTGO — Start Pot Scan
Address: 0B (write-only)

- Writing any value:  
  - Resets scan counter  
  - Releases dump transistors  
  - Begins pot charge timing  

---

## Random Number Generator

### RANDOM — Random Number
Address: 0A (read-only)

- Bits 7–0: High 8 bits of polynomial counter  

---

## Serial I/O Registers

### SERIN — Serial Input
Address: 0C (read-only)

- Bits 7–0: Received serial byte  

---

### SEROUT — Serial Output
Address: 0D (write-only)

- Bits 7–0: Byte to transmit  

---

## Interrupt Registers

### IRQEN — Interrupt Enable
Address: 0E (write-only)

- Bit 7: BREAK key interrupt enable  
- Bit 6: Keyboard interrupt enable  
- Bit 5: Serial input ready interrupt enable  
- Bit 4: Serial output needed interrupt enable  
- Bit 3: Serial transmission finished interrupt enable  
- Bit 2: Timer 4 interrupt enable  
- Bit 1: Timer 2 interrupt enable  
- Bit 0: Timer 1 interrupt enable  

---

### IRQST — Interrupt Status
Address: 0F (read-only)

- Bit 7: BREAK key interrupt active  
- Bit 6: Keyboard interrupt active  
- Bit 5: Serial input ready  
- Bit 4: Serial output needed  
- Bit 3: Serial transmission finished  
- Bit 2: Timer 4 interrupt  
- Bit 1: Timer 2 interrupt  
- Bit 0: Timer 1 interrupt  

---

## Timer Control

### STIMER — Start Timers
Address: 09 (write-only alias)

- Writing any value:  
  - Reloads AUDF values  
  - Resets audio channel states  
  - Starts timer countdown  
