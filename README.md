# HelloWorldBot

A mini project: a minimal "hello world" bot, used to try the OrcaTeam flow (Lead -> Developer -> Reviewer -> QA).

## What it is

HelloWorldBot is a small practice project. Its purpose is to give the OrcaTeam a simple codebase for running a development job from start to finish.

## Status

Early, just started. The repository contains a Windows Forms app that shows "Hello World" with a real-time analog clock below it.

## Build and run

Requires Windows and the .NET 9 SDK.

```powershell
dotnet build
dotnet run
```

## Help > About and versioning

The **Help > About** menu opens a dialog with the app name and version. The version is set once, as `<Version>` in `HelloWorldBot.csproj` ([SemVer](https://semver.org/)). Notable changes are listed in [CHANGELOG.md](CHANGELOG.md).

## Taskbar mascot

When you **minimize** the window it hides to the system tray and a pixel-art robot comes out and lives on the taskbar: it walks along the top edge of the taskbar (along the bottom of the screen if the taskbar is hidden or at the top or side). The tray menu offers Open, Start/Pause Pomodoro, Settings and Exit.

- **It is a small game character**, not a picture: every animation is a real frame-by-frame cycle from a sprite sheet (idle with blinking, look around, walk, run, jump with anticipation / rise / fall, landing, sit, sleep with floating Z's, wave, celebrate, surprised, sad, being held, shout). It turns to face where it walks, and its steps keep pace with its speed so the feet do not slide.
- **It has a mind of its own.** Now and then it stops and does something: looks around, sits down, waves, jumps. Which action and how often is decided by weighted dice per mood (see below).
- **Pomodoro moods:** working = walks and runs briskly; work session ends = celebrates, then jumps (and shouts if the Pomodoro's own sound is off); short break = sits down, then dozes off; long break = goes to sleep; no timer = a slow stroll with the odd action; **skipping a phase or resetting the timer makes it sad** for a moment. It never jumps between states abruptly: from sleep it first sits up, stands, and only then runs.
- **Click it** and it reacts (wave or surprised); **drag it** with the mouse and it dangles from your pointer, anywhere on any monitor (it can never leave the visible screens); let go and it falls to the taskbar edge and lands. **Double-click it**, or click the tray icon, to bring the window back.
- With several monitors it walks across all of them and **jumps with a shout** each time it crosses onto the next one. Plugging or unplugging a monitor while it walks is handled.
- It never takes focus, and clicks pass through every transparent pixel (only the sprite itself can be clicked). The animation stops while the screen is locked or the PC sleeps, and the character hides while a full-screen app or game is in front. Only one copy of the app runs at a time.
- **Settings > Mascot...**: show the mascot (off = minimize works as usual), sound on/off, volume, speed, size, **random actions** (Off = it only walks, Few, Normal), which monitors it walks on, and **Reload animations**. Saved in `%AppData%\HelloWorldBot\mascot.json`.

**Tuning the character.** All animation frame ranges, speeds, durations and the probability of each random action live in [Assets/Mascot/mascot-config.json](Assets/Mascot/mascot-config.json) (read it for the field list). To change something create `%AppData%\HelloWorldBot\mascot-config.json` with only the fields you want to override, then press **Reload animations** in Settings > Mascot. Invalid values are ignored with a warning and the default is kept.

**The art.** `Assets/Mascot/robot-sprites.png` is original pixel art (24x24 px frames, one palette, nearest-neighbour scaling at every size) drawn by [tools/generate-mascot-sprites.ps1](tools/generate-mascot-sprites.ps1): edit a pose there, run `powershell -File tools/generate-mascot-sprites.ps1` and commit the two PNGs it writes. [Assets/Mascot/contact-sheet.png](Assets/Mascot/contact-sheet.png) shows every frame; [Assets/Mascot/preview-life.png](Assets/Mascot/preview-life.png) shows a simulated minute of its life on a mock taskbar. The sprite sheet and the config are CC0; the shout is a CC0 recording. See [Assets/Mascot/CREDITS.md](Assets/Mascot/CREDITS.md). The app is system-DPI-aware: on monitors with other scale factors Windows scales the character with the rest of the window.

## Widget mode

**View > Widget mode** (or **Ctrl+W**) turns the window into a small widget (about 260x140): no title bar, always on top, with the time, the weather and the Pomodoro phase, remaining time and progress bar on a dark panel that stays readable over any desktop. Drag it anywhere with the mouse. **Right-click** it to exit widget mode (or press Ctrl+W again), start or pause the Pomodoro, change the opacity, open the Settings dialogs or exit the app. **View > Widget opacity** offers 100%, 80% and 60%.

Nothing restarts when you switch: the Pomodoro keeps running and the weather and settings stay as they are. The mode, the widget position and the opacity are saved in `%AppData%\HelloWorldBot\widget.json`; on start the widget is placed fully inside a visible monitor, so it comes back on screen even if the monitor it was on was unplugged. A missing or damaged file means normal mode.

## Pomodoro timer

The panel on the right shows a Pomodoro timer: **Work** 25 minutes, **Break** 5 minutes, and a **Long break** of 15 minutes after every 4 work sessions (the defaults). Use **Start**, **Pause / Resume**, **Skip** (next phase, without an alert) and **Reset** (back to a fresh work session). When a phase ends the next one starts by itself, a sound plays and a balloon notification appears; while a timer is active the window title reads "mm:ss Work" / "mm:ss Break" so you can follow it when the window is minimized. The panel also shows how many work sessions you finished today; the count starts again each day. A running timer is not resumed after a restart.

**Settings > Pomodoro...** sets the three durations (1-180 minutes), the number of work sessions before a long break (1-12), sound on/off and whether the next phase starts automatically. They are saved in `%AppData%\HelloWorldBot\pomodoro.json`; a missing or damaged file falls back to the defaults.

The timer uses a monotonic clock and a stored end time, so it stays accurate when the window is busy. The alert sounds are CC0; see [Assets/Sounds/CREDITS.md](Assets/Sounds/CREDITS.md).

## Weather and Settings > City

Under "Hello World" the app shows the current weather (city, temperature in °C, conditions, humidity, wind). It is fetched when the app starts and refreshed about every 10 minutes; requests run in the background and time out after 10 seconds. If the weather cannot be loaded the panel says "Weather unavailable" and keeps the last known values.

**Settings > City...** asks for a city name, looks it up and switches to it (an unknown city or no connection leaves the current city unchanged). The city is saved in `%AppData%\HelloWorldBot\settings.json`; on first run, or if that file is missing or damaged, the default city is **Hanoi, Vietnam**.

Weather and city search data come from [Open-Meteo](https://open-meteo.com/) (free, no API key; weather data under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/), [terms](https://open-meteo.com/en/terms)).

The free Open-Meteo API is for non-commercial use and requires attribution (CC BY 4.0).

Run the unit tests with:

```powershell
dotnet test HelloWorldBot.Tests
```

## View > Wallpaper

**View > Wallpaper** switches the window background between three bundled photos (Night Sky, Foggy Forest, Mountain Lake) and **None** (the plain background). The photo is scaled to cover the window and a dark overlay keeps the text readable. The photos are CC0; see [Assets/Wallpapers/CREDITS.md](Assets/Wallpapers/CREDITS.md) for titles, authors and sources.

## How the team works on it

1. **Branch:** the Developer makes each change on its own branch.
2. **Draft PR:** the Developer opens a draft pull request.
3. **Review and QA:** the Reviewer and QA check the exact commit.
4. **Merge:** the owner merges the pull request.

## License

Project license: TBD.

The bundled DSEG font is licensed under the SIL Open Font License 1.1. See `Assets/Fonts/DSEG-LICENSE.txt`.
