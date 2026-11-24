# Mascot credits

## Robot sprite sheet

`robot-sprites.png` (192x336, 24x24 px frames, 14 animations) is original pixel art drawn for this project, with no third-party material. It is produced by [tools/generate-mascot-sprites.ps1](../../tools/generate-mascot-sprites.ps1), which also writes `contact-sheet.png` (every frame, labelled). `preview-life.png` is a simulated minute of the character's life drawn by the app's own renderer onto a mock taskbar. The sheet, the script, `mascot-config.json` and the previews are dedicated to the public domain under [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/). The app enlarges the sheet by whole factors with nearest-neighbour scaling.

## Shout sound

| File | Title | Author | Source | License |
| --- | --- | --- | --- | --- |
| `shout.wav` | ahhh (cc0) | UncleSigmund | [Wikimedia Commons](https://commons.wikimedia.org/wiki/File:UncleSigmund_-_ahhh_(cc0)_(freesound).mp3) (originally from freesound) | CC0 1.0 |

The recording ("Someone screams ahhh") was trimmed to 1.3 s, faded in and out, and converted to mono 22.05 kHz 16-bit WAV for this app. It is played when the robot jumps onto another monitor, and when a work session ends if the Pomodoro's own sound is turned off (otherwise the Pomodoro's gong plays alone). The Pomodoro phase-end sounds are credited in `Assets/Sounds/CREDITS.md`.
