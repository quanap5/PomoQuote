# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

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
