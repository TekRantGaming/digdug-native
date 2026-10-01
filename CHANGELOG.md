# Changelog

## 1.1.0

A big feature update.

**Fixes**
- Lives icons and the dirt layers were drawn wrongly (playfield tile halves swapped): the icons now look right and the stray lines in the dirt are gone

**New**
- Hierarchical menu: Video, Audio, Controls, Game, Cheats, Save states, Extras, Stats, About
- Cheats: infinite lives, invincibility, start round
- Save states (5 slots), rewind (hold R, 15 s), replay recording and playback
- 14 colour themes incl. colour-blind palettes; scanlines, RGB mask, vignette; rotation; sharp/fit/stretch scaling
- Widescreen side panels: dirt, black, glow, live sound scopes, info panel
- Rebindable keys, pump-button choice, stick deadzone, rumble
- Game speed (25-400%), frame advance, per-voice mute, sound smoothing, FPS counter, VSync / borderless / always-on-top, auto pause on focus loss
- Statistics and 19 achievements (local only), screenshots (F12), reset high scores / stats
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