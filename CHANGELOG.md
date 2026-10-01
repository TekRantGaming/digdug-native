# Changelog

## 1.0.1

- Added an AI disclosure (AI-DISCLOSURE.md, README banner)
- Removed the roms/ placeholder folder from the repository so it can't be mistaken for game data
- CI now fails if any ROM-like file is found in the repository, the Windows package or the AppImage

## 1.0.0

First public release.

- Hardware-level emulation of the Dig Dug arcade board (3x Z80, 06xx/51xx/53xx I/O, video, 3-voice wavetable sound)
- SDL2 front end for Windows and Linux (self-contained `.exe` and AppImage)
- Game controller support (Xbox, PlayStation 4/5, Switch Pro, generic gamepads) with hot-plugging
- In-game menu: fullscreen, window size, scaling, filter, volume, audio device picker, lives, bonus, rank, auto coin, auto pump, widescreen
- Widescreen fill for 16:9 and wider screens with a frame marking the real playfield
- Auto pump and auto coin conveniences
- Persistent settings and high scores; ROM discovery with file picker and drag-and-drop
- Developer tools: Z80 disassembler, headless runner, tracing, test bot
