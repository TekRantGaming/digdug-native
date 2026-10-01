# Changelog

## 1.0.0

Initial release.

- Hardware-level emulation of the Dig Dug arcade board (3x Z80, 06xx/51xx/53xx I/O, video, 3-voice wavetable sound); bring your own ROMs
- SDL2 front end for Windows and Linux (self-contained `.exe` and AppImage)
- Game controller support (Xbox, PlayStation 4/5, Switch Pro, generic gamepads) with hot-plugging
- In-game menu: fullscreen, window size, scaling, filter, volume, audio device picker, lives, bonus, rank, auto coin, auto pump, widescreen
- Widescreen fill for 16:9 and wider screens with a frame marking the real playfield
- ROM discovery with an on-screen drop page, file picker and drag-and-drop; `--rom-status` diagnostic
- Build checks that fail if any ROM-like file is found in the repository or in a release package, and a clean-machine test of the packaged exe and AppImage
- Developer tools: Z80 disassembler, headless runner, tracing, test bot