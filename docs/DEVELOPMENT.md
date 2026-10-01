# Development guide

## Building and running

Install the .NET 8 SDK and SDL2 (on Windows `build.ps1` downloads `SDL2.dll`; on Linux `sudo apt install libsdl2-2.0-0`).

```bash
dotnet build
dotnet run -- --roms path/to/digdug.zip          # play
dotnet run -- --roms path/to/digdug.zip --frames 3600 --shotframes 3600 --out out/shot --at 2000:coin1:6 --at 2100:start1:6
```

The core (`Z80`, `Machine`, `Video`, `Sound`, `RomSet`) has no SDL or OS dependencies; only `App.cs` / `Sdl.cs` do.

## Developer switches

All of these write into `./out` (git-ignored) and need the ROMs. On Windows they print to the console that launched the exe.

| Switch | What it does |
|---|---|
| `--disasm` | Writes `out/cpu1.asm`, `cpu2.asm`, `cpu3.asm` – Z80 disassembly of the three programs. **Derived from the ROMs, so never commit it.** |
| `--dumpgfx` | Writes PNG sheets of the decoded characters, playfield tiles, sprites and palette |
| `--makeicon` | Regenerates `assets/icon.png` / `icon.ico` (original artwork, no ROM content) |
| `--frames N` | Run headless for N frames, then print CPU state |
| `--shotframes a,b,c --out base` | Save `base_<frame>.png` screenshots (2× scale) |
| `--at frame:key:duration` | Scripted input; keys `coin1 coin2 start1 start2 fire up down left right service` |
| `--wav file.wav` | Record the sound output |
| `--trace06` | Log every 06xx control/data access |
| `--pcs N` | Every N frames print the three program counters and a few game variables |
| `--peek addr,addr` | Print RAM bytes (addresses 8000–9FFF) at the end of the run |
| `--hit addr` | Count how often the main CPU executes that address |
| `--cov a-b` | Print which main-CPU code ranges ran during frames a..b |
| `--spr` / `--hex N` / `--text` | Dump sprite RAM / raw tile codes / decoded screen text |
| `--sprstats` | Report unusual sprite codes / flags used |
| `--nopf` | Hide the playfield layer |

(The first ~1900 frames are the power-on self-test; the attract mode begins after that.)

## How the hardware was worked out

1. Disassemble the three ROMs (`--disasm`) and read the reset/boot code and interrupt handlers.
2. Implement the obvious parts (memory map, latches, interrupts), run headless, look at the output.
3. Use `--trace06`, `--hit`, `--cov` and `--peek` to find where the game polls for something the emulation
   does not yet provide (this is how the 51xx credit/joystick format and the DIP byte layout were found).
4. Verify each guess visually (`--shotframes`) or numerically (`--wav` analysis) before moving on.
