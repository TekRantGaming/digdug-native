# Dig Dug (Namco, 1982) – hardware notes

Everything below was worked out while building this emulator: partly from knowledge of the Namco
Galaga-family boards, and partly by disassembling the game's three Z80 programs and checking each
hypothesis against what the running game actually does (screenshots, RAM/trace dumps, audio spectra).
Items marked **(verified)** were confirmed against game behaviour; **(assumed)** items are inferred and
work in practice but may differ from the real chips in corner cases.

## Overview

| Part | Notes |
|---|---|
| CPUs | 3 × Z80 @ 3.072 MHz ("main", "sub", "sound") |
| Frame | 264 lines × 192 CPU cycles = 50 688 cycles → 60.6 Hz **(verified)** |
| Video | 36×28 tile grid (288×224 native, monitor rotated 90° → 224×288), 64 sprites |
| Sound | Namco 3-voice wavetable (WSG), 96 kHz native |
| I/O | Namco 06xx bus controller with a 51xx (inputs/credits) and a 53xx (DIP switches) |

## Interrupts

* Main and sub CPU get a level-triggered IRQ at the start of vblank (line 224), gated by bits 0 and 1 of
  the control latch below. Writing `0` to the mask bit also clears the pending IRQ **(verified)**. Both
  CPUs run in interrupt mode 1.
* The sound CPU gets an NMI at 120 Hz when latch bit 2 is `0` **(verified)**.
* The main CPU gets an NMI from the 06xx while a transfer is active (period ≈ 200 µs = 614 cycles).

## Memory map (identical for all three CPUs, ROM differs)

| Address | Function |
|---|---|
| 0000–3FFF | Main CPU ROM (`dd1a.1–.4`). Sub CPU: 0000–1FFF (`dd1a.5–.6`). Sound CPU: 0000–0FFF (`dd1.7`) |
| 6800–681F | Sound registers (write) |
| 6820–6827 | Control latch (see below) |
| 6830 | Watchdog (ignored) |
| 7000–70FF | 06xx data port |
| 7100 | 06xx control (readable: returns last value written) |
| 8000–9FFF | Shared RAM, 8 KB |
| A000–A007 | Video latch (see below) |
| B800–B83F | 64-byte EAROM (high scores), B840 = EAROM control |

Notable RAM regions:

* 8000–83FF – text/tunnel tile codes (one byte per tile, see *Video*).
* 8B80–8BFF, 9380–93FF, 9B80–9BFF – three 128-byte sprite register banks.
* 8900/8902 – the main CPU's 06xx transfer queue pointers (software only).

### Control latch (0x6820 + n, data bit 0)

| n | Meaning |
|---|---|
| 0 | Main CPU IRQ enable |
| 1 | Sub CPU IRQ enable |
| 2 | Sound CPU NMI **disable** (0 = enabled) |
| 3 | 0 = hold sub + sound CPUs in reset, 1 = run (CPUs restart at 0000 on the rising edge) |
| 4–7 | Chip resets / unused – ignored here |

### Video latch (0xA000 + n, data bit 0)

| n | Meaning |
|---|---|
| 0–1 | Playfield map page select (low, high) |
| 2 | Text colour mode (unused by this emulator) |
| 3 | Playfield disable |
| 4–5 | Playfield colour bank (low, high) |
| 7 | Flip screen (cocktail; not implemented) |

## 06xx / 51xx / 53xx

The 06xx is a serial bus controller. The Z80 writes a **control byte** to 7100 and then moves bytes through
the data port at 7000. While the control byte's low nibble is non-zero the 06xx fires periodic NMIs at the main
CPU, and the NMI handler (at 0066) moves one byte per NMI using `LDI` between the data port and a RAM buffer
**(verified from the disassembly)**.

Control byte = `rrrr dccc`-style: low nibble selects chips (bit 0 = 51xx, bit 1 = 53xx), bit 4 = **read** (1) or
**write** (0). Writing `0x10` ends a transfer; reading 7100 returns the last control byte, and the game polls for
`0x10` to know a transfer finished.

### 51xx – inputs and credits (function-level emulation **(assumed)**)

Real chip: Fujitsu MB8843 running `51xx.bin`. Here it is replaced by a small state machine
(`Namco51` in `Machine.cs`) that implements the commands this game actually sends:

| Write | Meaning |
|---|---|
| `01` + 4 bytes | Set coinage (coins/credit and credits/coin for each slot) |
| `02` | Enter **credit mode** |
| `03` / `04` | Joystick remap off / on |
| `05` | **Switch mode** (raw inputs) |

Reads always return 3 bytes per transfer.

* **Switch mode** (used during the power-on test): byte 0 bit 7 = "not in service mode"; the boot test loops until it sees
  it set **(verified)**.
* **Credit mode**: byte 0 = credits as BCD; byte 1 / byte 2 = player 1 / 2:
  low nibble = direction (`0` up, `2` right, `4` down, `6` left, `8` neutral), bit 4 = `0` for one poll on a fresh fire
  press, bit 5 = fire held **(verified – the pump only starts with this exact encoding)**.
  The 51xx counts coins and *spends credits itself*: a start press lowers the credit byte by 1 (or 2 for 2-player) and
  the game detects the drop to start a game **(verified)**.

### 53xx – DIP switches **(verified by sweeping each bit and reading the game's test screen)**

Two bytes are returned, then repeat.

Byte 0:

| Bits | Setting |
|---|---|
| 0–2 | Coin B rate (`1`=1c/1cr, `2`=1c/3cr, `3`=2c/1cr, `4`=1c/6cr, `5`=2c/3cr, `6`=1c/2cr, `7`=3c/1cr, `0`=1c/7cr) |
| 3–5 | Bonus life (`4`=10K/40K, `2`=10K/50K, …) |
| 6–7 | Lives (`0`=1, `1`=2, `2`=3, `3`=5) |

Byte 1:

| Bits | Setting |
|---|---|
| 0–1 | Rank (`0`=A, `2`=B, `1`=C, `3`=D) |
| 2 | Cabinet (1 = upright) |
| 3–5 | Misc. – **bit 5 must be 1** or the main CPU's interrupt routine skips all game processing |
| 6–7 | Coin A rate (`0`=1c/1cr, `1`=2c/1cr, `2`=1c/2cr, `3`=2c/3cr) |

Defaults used here: `0xA1`, `0x3C` (3 lives, 10K/40K, rank A, upright, 1 coin/1 credit).

## Video

* **Layer order:** playfield → text layer → sprites **(verified)**. The black tunnels the player digs are *text-layer
  tiles* (character codes `00–0F`), which is why sprites must be drawn above the text layer.
* **Tile scan:** `tilemap_scan(col,row)`: `row+=2; col-=2; if (col&0x20) ofs=row+((col&0x1f)<<5) else ofs=col+(row<<5)`.
* **Text** (`dd1.9`): 128 glyphs of 8×8 1 bpp, LSB = leftmost pixel. Tile byte bit 7 and bits 4–6 choose the colour
  (`((c>>4)&0xE)|((c>>3)&2)` → palette 0–15). Pen 0 transparent. Character codes: `10–19` digits, `1A–33` letters.
* **Playfield**: `dd1.10b` is the tile map (4 pages × 1024 bytes, page chosen by the video latch);
  `dd1.11` holds the 256 8×8 2 bpp tiles (planes at bit offsets 0 and 4, x offsets `0–3, 8–11`, 16 bytes/tile).
  Colour = high nibble of the map byte (+ 16 × colour bank) looked up through PROM `136007.112`.
* **Sprites** (`dd1.15, .14, .13, .12` loaded back to back): 256 16×16 2 bpp images, 64 bytes each.
  Each of the 64 sprites uses three RAM banks (code/colour, y/x, flip/x-high). Colour lookup via `136007.111`;
  lookup value `0x0F` is transparent. Position: `x = X - 40 + 256*(hi&3)`, `y = (256 - Y + 1) & 0xFF - 32`.
  Bit 7 of the code selects a 2×2 composite (32×32) sprite built from four consecutive images, with
  `code = (code & 0xC0) | ((code & 0x3F) << 2)` **(verified – the title logo is made of these)**.
* **Palette** (`136007.113`, 32 entries, 3-3-2 resistor-weighted RGB). Playfield and text use entries 0–15,
  sprites use 16–31 **(verified – wrong halves give wrong rock/player colours)**.
* **Rotation:** native 288×224 is rotated 90° clockwise: `(x, y) → (223 − y, x)`.

## Sound

Standard Namco 3-voice WSG (as on Pac-Man): 32 registers at 6800, 4-bit each.

* Voice 0: wave select reg 05, frequency regs 10–14 (5 nibbles, reg 10 is the lowest), volume reg 15.
* Voice 1: wave 0A, frequency 11–14 shifted (regs 16–19), volume 1A.
* Voice 2: wave 0F, frequency 1B–1E, volume 1F.
* Waveforms: PROM `136007.110`, 8 waves × 32 samples (low nibble), centred around 8.
* Output sample = Σ wave[(phase>>15)&31] × volume with `phase += frequency` at 96 kHz.
  Measured against the running game, the coin sound sweeps ~280 → 1600 Hz and the round-start jingle is
  clearly melodic, as expected.

## Self-test / boot behaviour

On power-up the game runs a ~30 s RAM/ROM/DIP check (the sub CPUs report in through `$8A00/$8A01`), shows the
settings screen, and only continues once the 51xx reports "not in service mode" (see switch mode above). The
front end fast-forwards through this phase (first 1900 frames). Holding **F2** at boot enters the service screen.

## Not implemented

* Cocktail screen flip, coin counters/lockouts, the real MB88xx cores (51xx/53xx are function-level).
* Bit-exact timing of the 06xx NMI period (200 µs is an estimate that the game tolerates).
