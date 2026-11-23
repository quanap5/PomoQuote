# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Live weather under the title: city, temperature (°C), conditions, humidity and wind, from the free [Open-Meteo](https://open-meteo.com/) API (no API key). It loads on start, refreshes about every 10 minutes without blocking the window, and shows "Weather unavailable" (keeping the last known values) when offline.
- **Settings > City...** to choose the city. It is looked up with Open-Meteo geocoding and saved in `%AppData%HelloWorldBotsettings.json`; the default is Hanoi.
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
