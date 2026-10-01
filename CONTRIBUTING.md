# Contributing

Thanks for your interest! Bug reports, fixes and improvements are welcome.

## Ground rules

- **Never** upload ROM files, ROM-derived disassembly (`--disasm` output), or other copyrighted game data
  – not in issues, pull requests, or commits. The `.gitignore` is set up to help; please check `git status` before pushing.
- Keep changes focused; one topic per pull request.
- The emulator core (`Z80`, `Machine`, `Video`, `Sound`, `RomSet`) must stay free of SDL / OS dependencies.

## Getting set up

See [Building from source](README.md#building-from-source) and [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).
The headless runner (`--frames N --shotframes ... --at frame:key:duration`) lets you reproduce behaviour without a window,
and `--bot` plays the game automatically for regression checks.

## Reporting bugs

Please include: your OS, whether you used the Windows exe or AppImage, the contents of `digdug.log` (config folder),
what you expected, what happened, and – if possible – a headless reproduction command. Do not attach ROMs.

## Ideas that would be great

- macOS packaging
- Cocktail (flipped screen) mode
- Running the real 51xx/53xx microcontroller firmware (MB88xx core) instead of the function-level emulation
- Controller button remapping UI
