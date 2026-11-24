# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

Proposed version for this release: **1.1.0** (the rename is a visible change, and the sentences are a new feature). The version in `PomoQuote.csproj` is left at 1.0.0 until the owner decides.

### Changed

- **The app is now called PomoQuote** (was HelloWorldBot): window title (and the Pomodoro title, now "mm:ss Work - PomoQuote"), About dialog, tray icon, exe name (`PomoQuote.exe`), project, namespaces and test project (`PomoQuote.Tests`), single-instance name, settings folder and documentation. The GitHub repository keeps the name HelloWorldBot.
- The big "Hello World" text is replaced by an English sentence with its Vietnamese translation (see below); the bundled digital font is no longer used and was removed.
- The widget is taller (about 260x210) to make room for the sentence strip.

### Added

- **Sentences**: 357 English sentences (motivation, idioms and proverbs, conversation) with natural Vietnamese translations, bundled offline in `Assets/Quotes/quotes.json` with sources and licenses in `Assets/Quotes/CREDITS.md`. The sentence is shown as large as fits (English at most three lines, translation below), over the plain background and every wallpaper, and compactly in the widget.
- A new sentence at start, at each Pomodoro phase and optionally every 5/10/30/60 minutes, or on demand with **View > Next sentence (Ctrl+N)**; a shuffled pass through all sentences before repeats, never the same one twice in a row, and the last one is remembered across restarts.
- **Settings > Quotes...** (categories, Vietnamese translation on/off, interval) and **View > Vietnamese translation**; saved in `%AppData%\PomoQuote\quotes.json`.
- **Settings migration**: on first start the files in `%AppData%\HelloWorldBot` are copied to `%AppData%\PomoQuote` once, without overwriting or deleting anything; a failure is logged and the app starts with the defaults.
- Unit tests for the data file, the rotation, the quote settings, the migration and the text layout.

### Added (earlier, not yet released)

- **Taskbar mascot**: minimizing the window hides it to the system tray (tray menu: Open, Start/Pause Pomodoro, Settings, Exit) and a pixel-art robot walks along the top edge of the taskbar (the bottom of the screen if the taskbar is hidden or on another edge). Click the robot or the tray icon to bring the window back. The robot follows the Pomodoro: brisk walk while working, a jump and shout when a work session ends, asleep during a break, a slow stroll when idle.
- The robot walks across every monitor and jumps with a shout onto the next one. It copes with different resolutions, monitors above/below each other and monitors plugged or unplugged while it walks. Its click-through window never takes focus, it stops while the screen is locked or the PC sleeps, and hides while a full-screen app is in front.
- **Settings > Mascot...**: enable the mascot, sound on/off, volume, speed, robot size and which monitors it walks on (saved in `%AppData%\HelloWorldBot\mascot.json`). Sprite and shout sound are CC0 (credits in `Assets/Mascot/CREDITS.md`).
- The app now runs as a single instance, and the Pomodoro balloons share one tray icon with the mascot.
- Unit tests for the robot's route, movement, monitor crossing, animation timing, settings file and sound scaling.
- **Widget mode** (View > Widget mode, Ctrl+W): the window turns into a compact (about 260x140) borderless, always-on-top widget that you can drag anywhere. It shows the time, the weather and the Pomodoro phase, remaining time and progress bar. Right-click it for Exit widget mode, Start/Pause Pomodoro, Opacity, Settings and Exit app. **View > Widget opacity** sets 100%, 80% or 60%.
- Widget mode, position and opacity are remembered in `%AppData%\HelloWorldBot\widget.json`. On start the widget is moved fully inside a visible monitor, also when the monitor it was on is no longer connected. Switching modes keeps the running Pomodoro, the weather and the settings.
- Unit tests for widget placement (multi-monitor clamping) and the widget settings file.
- **Pomodoro timer** panel next to the clock: Work 25 min, Break 5 min, a 15 min long break after 4 work sessions (all adjustable), with a progress ring, Start / Pause / Resume, Skip and Reset. Each phase end plays a short alert sound, shows a balloon notification and the window title shows "mm:ss Work" or "mm:ss Break". Today's finished work sessions are counted and remembered.
- **Settings > Pomodoro...** for the durations, sessions before a long break, sound on/off and auto-start of the next phase. Options and the daily count are saved in `%AppData%\HelloWorldBot\pomodoro.json`. Alert sounds are CC0 (credits in `Assets/Sounds/CREDITS.md`).
- Unit tests for the Pomodoro state machine (phases, long break, skip, pause/resume, clock jumps) and its settings file.
- Live weather under the title: city, temperature (°C), conditions, humidity and wind, from the free [Open-Meteo](https://open-meteo.com/) API (no API key). It loads on start, refreshes about every 10 minutes without blocking the window, and shows "Weather unavailable" (keeping the last known values) when offline.
- **Settings > City...** to choose the city. It is looked up with Open-Meteo geocoding and saved in `%AppData%\HelloWorldBot\settings.json`; the default is Hanoi.
- Unit tests (`HelloWorldBot.Tests`) for the Open-Meteo parsing, the weather service error handling and the settings file.
- **View > Wallpaper** menu: pick one of three bundled background photos (Night Sky, Foggy Forest, Mountain Lake) or None. The photo covers the window, resizes with it, and the clock gets a light dial so it stays readable. Photo credits are in `Assets/Wallpapers/CREDITS.md`.

## [1.0.0] - 2026-11-23

### Added

- Windows Forms app showing "Hello World" centered in the window, in green with a bundled digital font (36pt) (PR #1).
- Real-time analog clock below the text, with 12 hour marks and hour, minute and second hands (PR #4).
- Build and run instructions in the README (PR #1).
- Menu bar with a Help > About dialog showing the app name and version; the version is set by `<Version>` in `HelloWorldBot.csproj`.
- This changelog.

### Fixed

- "Hello World" stays on one centered line at any window size (PR #1).
- The full clock rim stays inside its area at any window size (PR #4).

[Unreleased]: https://github.com/quanap5/HelloWorldBot/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/quanap5/HelloWorldBot/releases/tag/v1.0.0
