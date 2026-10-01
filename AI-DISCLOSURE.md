# AI disclosure

**This project was created entirely by an AI.** All source code, build scripts, CI workflows, documentation, the
application icon and the hardware research notes in this repository were written by **Claude** (an AI model made by
Anthropic; model "Claude Sonnet 5.5", used through the *Claude Code* agent), working at the direction of the repository
owner. No part of the code was written by hand.

## What a human did

- Gave the goals and requirements (a native, ROM-loading PC port of Dig Dug with controller support, a menu, widescreen
  fill and Linux packaging).
- Play-tested the Windows build and reported bugs (audio routing, controller input, pump behaviour, sprites at the
  bottom of the screen, win condition), which were then fixed by the AI.
- Reviewed and approved publishing the repository and releases.

The human owner did **not** review or audit every line of code.

## How it was built and checked

The AI disassembled the game's three Z80 programs from a ROM set supplied by the owner, worked out how the arcade
hardware behaves, wrote the emulator, and verified it by running the game headlessly and inspecting rendered frames,
recorded audio, game memory and the results of an automated test bot. Some behaviour was inferred rather than copied
from existing documentation (see the "(assumed)" markers in [`docs/HARDWARE.md`](docs/HARDWARE.md)).

## Things to keep in mind

- **There may be bugs and inaccuracies.** AI-written code can contain mistakes that testing did not catch. The software
  is provided "as is" (see [LICENSE](LICENSE)).
- **The Linux AppImage is built by CI and has had less hands-on testing than the Windows build.**
- **Copyright status.** The legal status of AI-generated code varies by jurisdiction and is unsettled. The repository
  owner releases it under the MIT license to the extent they hold any rights. This does not affect Namco/Bandai Namco's
  rights in Dig Dug.
- **No game data is included.** The AI did not and cannot supply ROMs; users must provide their own. The repository
  contains no ROM files, and the build pipeline fails if any ROM-like file is found in the repository or in a release
  package (see `packaging/check-no-roms.sh`).

## Contributions

AI-assisted pull requests are welcome, but please say so in the PR description, and never include ROMs or ROM-derived
data.
