# Dig Dug – native PC port (Windows + Linux)

A from-scratch **native PC port of Namco's 1982 arcade game *Dig Dug***, written in C# on .NET 8 with SDL2.
It reimplements the arcade board – three Z80 CPUs, the Namco I/O chips, video and sound – so the original game
code runs unmodified, and wraps it in a proper PC front end: in-game menu, fullscreen and scaling options, and
native game-controller support.

> **The game ROMs are not included and are never committed to this repository.** Dig Dug is © Bandai Namco.
> You must supply your own legally obtained ROM set (see [Getting the ROMs](#getting-the-roms)).

## Features

* Runs the original game ROMs bit-for-bit (verified: attract mode, credits, gameplay, enemies, pump, scoring)
* **Controllers**: Xbox 360 / One / Series, PlayStation 4 (DualShock 4) and PlayStation 5 (DualSense), Switch Pro,
  and most other gamepads, with hot-plugging – via SDL's game-controller database
* **In-game menu** (Esc, or the PS / Guide button): fullscreen, window size, sharp or smooth scaling, volume,
  **audio output device picker + test sound**, lives, bonus-life thresholds, difficulty rank, auto-coin, reset
* **Widescreen mode**: on 16:9 and other wide screens the dirt is extended (dimmed) to fill the sides, with a bright frame marking the real playfield edge. Toggle in Options.
* **Auto pump**: hold the fire button and the pump keeps inflating (the arcade game needs release-and-press per pump step)
* Windows `.exe` and Linux **AppImage**; fast-forward (hold Tab); pause; persistent settings and high scores
* The ~30-second power-on self-test is fast-forwarded automatically

## Controls

| Action | Keyboard | Controller |
|---|---|---|
| Move | Arrow keys / WASD | D-pad or left stick |
| Pump (fire) | Space / Ctrl / Z / X | A, B, X, Y, R1 or either trigger |
| Insert coin | 5 / C | Back / Select / Share (or just press Start: *Auto coin* is on by default) |
| 1 player start | 1 / Enter | Start / Options |
| 2 player start | 2 | L1 |
| Menu | Esc / F1 | PS / Guide button, or press the right stick |
| Fullscreen | F11 / F | menu |
| Pause / fast-forward | P / hold Tab | – |
| Reset | F3 | menu |
| Service/test screen | hold F2 while booting | – |

## Getting the ROMs

You need the MAME-style `digdug` ROM set (revision "a"): `dd1a.1 … dd1a.6`, `dd1.7`, `dd1.9`, `dd1.10b`, `dd1.11 … dd1.15`,
`136007.110 … 136007.113`. The game looks for it, in this order:

1. a path given on the command line (`DigDug path\to\digdug.zip`) or `--roms <path>`
2. the path remembered from last time
3. a `roms/` folder or `digdug.zip` next to the program (or next to the `.AppImage`)
4. the per-user config folder: `%APPDATA%\DigDugNative\roms` (Windows) or `~/.config/DigDugNative/roms` (Linux)

If nothing is found, the game explains what to do and you can **drag-and-drop `digdug.zip` onto the window**.
Extracted files or the `.zip` both work.

## Download / build

Prebuilt packages are produced by GitHub Actions (see the *Actions* tab and *Releases*):
`DigDug-windows-x64.zip` and `DigDug-x86_64.AppImage`. Both are self-contained – nothing to install.

To build it yourself you need the [.NET 8 SDK](https://dotnet.microsoft.com/download):

```bash
# Windows (PowerShell): produces out\win\DigDug.exe + SDL2.dll (downloaded from the official SDL release)
./build.ps1

# Linux: produces out/DigDug-x86_64.AppImage (needs libsdl2-2.0-0 and wget)
bash ./packaging/linux/build-appimage.sh

# Any platform, for development (needs SDL2 installed):
dotnet run -- --roms path/to/digdug.zip
```

On Linux: `chmod +x DigDug-x86_64.AppImage` and run it. Controller access follows your distro's normal udev rules.

## No sound?

Open the menu (Esc) -> Options -> **AUDIO** and use Left/Right to pick your output device; a short test tone plays each time you change it.
Some systems with virtual mixers (e.g. SteelSeries Sonar) route new programs to a channel you can't hear. digdug.log lists every device SDL sees.

## Command line

```
DigDug [romfolder-or-zip] [--roms <path>] [--fullscreen] [--lives 1|2|3|5] [--rank A|B|C|D] [--dip0 HEX --dip1 HEX]
```

Settings are saved to `%APPDATA%\DigDugNative\settings.ini` (`~/.config/DigDugNative/` on Linux) together with the
high-score memory (`digdug.nv`) and a small `digdug.log` with audio-device / controller diagnostics.

## Is this a decompilation or an emulator?

Honestly: **it is a hardware emulator, not a decompilation.** The original Z80 machine code from your ROMs still runs
exactly as Namco wrote it; this project provides a faithful re-creation of the arcade board around it, written
specifically for this one game and built as a native, portable PC application (no RetroArch or MAME needed).
A true decompilation would instead recover *source code* for the game logic and compile that to run natively,
without the original machine code. [`docs/HARDWARE.md`](docs/HARDWARE.md) documents what has been learned about the
board, which is the groundwork any such effort would need. Everything that is *not* the game's own code – the CPU,
video, sound and chip emulation, the front end, menu and controller support – is original work in this repository.

## Project layout

```
src/Z80.cs          Z80 CPU core (cycle-counted)
src/Machine.cs      Board: memory map, interrupts, latches, 06xx bus, 51xx/53xx chips
src/Video.cs        Playfield / text / sprite renderer, palette PROMs, 90° rotation
src/Sound.cs        Namco 3-voice wavetable synthesiser
src/RomSet.cs       ROM loader (folder or zip)
src/App.cs          SDL2 front end: window, input, audio, menu
src/Sdl.cs          Minimal SDL2 bindings
src/Settings.cs     Persistent settings
src/Disasm.cs, DevTools.cs, Png.cs   Developer tools
packaging/linux/    AppImage recipe
docs/               Hardware notes and developer guide
```

## Accuracy notes

* The three Z80s, memory map, interrupts, video and the wavetable sound chip are modelled directly.
* The Namco **51xx** (credits/joystick) and **53xx** (DIP switches) are small custom MB88xx microcontrollers; they are
  emulated at the function level (only what this game uses) rather than by running their internal ROMs.
* Not implemented: cocktail (flipped) mode, coin counters.

## License

The source code in this repository is MIT licensed (see `LICENSE`). This covers only the code written for this project –
**not** the Dig Dug game, its ROMs, artwork or sound, which remain the property of their owners. SDL2 is © Sam Lantinga
and contributors (zlib license).
