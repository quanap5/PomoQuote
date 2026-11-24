# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Claude bridge safety** (fixes found in the review of the Claude provider): the status line no longer points at the folder the app runs from. Setting the bridge up copies its program to `%LocalAppData%\PomoQuote\bridge\` (refreshed after an update) and is refused unless the app runs as `PomoQuote.exe`. The installed command first tests that program and otherwise runs your original status line directly, so a moved or deleted app cannot take your status line down. When PomoQuote starts it checks the bridge once; a missing program, a damaged saved command or a shell mismatch (Git Bash installed or removed since) shows a notification and a *bridge missing* state on the Claude strip, and **Check** also reports them.
- The Claude settings file now follows `CLAUDE_CONFIG_DIR` like Claude Code, with `POMOQUOTE_CLAUDE_SETTINGS` (file) and `POMOQUOTE_STATE_DIR` (PomoQuote's own files) as overrides for tests and relocated configs. A new Claude profile with no settings file can be set up (the file is created with only the status line and removed again by **Remove bridge** if it still holds nothing else), and **Remove bridge** first saves a fresh copy of the file.
- **AI usage: Claude Code** through a status line bridge (Settings > AI Usage... > Claude). Only on your explicit confirmation, after a dialog shows the exact change, PomoQuote backs up your user-level `~/.claude/settings.json` (never overwritten, in `%LocalAppData%\PomoQuote\bridge-backups\`) and sets only `statusLine` (a new one, or your own command wrapped so it runs unchanged). The bridge (`PomoQuote.exe --bridge claude`) saves a redacted snapshot (percent and reset time only; no email, session id, folder, transcript, tokens or costs), passes your original status line through and never breaks Claude Code. **Remove bridge** restores the original and keeps later edits; **Check** reports a moved or deleted bridge and project-level overrides. The widget shows Claude next to Codex with waiting, stale, not set up and no-plan-limits states and the same warning levels.
- **Refresh Claude now** (Settings and widget menu): one `claude -p` request with the smallest model and a one-word prompt reads the current limits on demand (30 s limit, once a minute, asked once because it uses a very small amount of quota). No credential is read and nothing of the answer is kept.
- **AI usage on the widget (Codex)**: **Settings > AI Usage...** turns on a strip under the sentence that shows the Codex quota for the five-hour and the weekly window: percent used, a bar, the time left until the reset, the plan and the time of the last read, with a stale marker. It reads `codex app-server` (a short-lived process every 3 minutes by default, 10 s limit) of the Codex CLI already on the PC; **PomoQuote never reads or stores a token or password**, never opens credential files and never changes your login. Windows only; WSL is not supported yet. Off by default.
- Settings for it: enable, detected install and login state (read only), test connection, display order and mode (bars or percent only), refresh interval, warning levels (default 80% and 95%, one balloon per window and level, remembered across restarts). Saved in `%AppData%\PomoQuote\usage.json`; the last good numbers are cached in `usage-cache.json` and shown again after a restart.
- A multi-provider core (normalised model, per-provider timer, time limit and error isolation, immutable snapshots, alert de-duplication) so Claude Code and Antigravity can be added later; Codex is the first provider. It reads again at once after sleep/resume and when the network returns, and shows clear states for not installed, not logged in, unsupported version and read failed, keeping the last good numbers.
- Unit tests for the Codex answer parser, the provider against a fake `app-server` (including real child processes that hang, crash or answer in pieces), the collector, the caches and settings files, alerts, the widget text, and a privacy test that scans the code and the files it writes for credential names.

### Changed

- The widget grows by a few lines when AI usage is on (about 260x264 with Codex); without it the widget is unchanged.

## [1.1.0] - 2026-11-24

### Changed

- **The app is now called PomoQuote** (was HelloWorldBot): window title (and the Pomodoro title, now "mm:ss Work - PomoQuote"), About dialog, tray icon, exe name (`PomoQuote.exe`), project, namespaces and test project (`PomoQuote.Tests`), single-instance name, settings folder and documentation. The GitHub repository is now `quanap5/PomoQuote` (it was `quanap5/HelloWorldBot`; old links redirect).
- The big "Hello World" text is replaced by an English sentence with its Vietnamese translation (see Added); the bundled digital font is no longer used and was removed.
- The tray icon is now the PomoQuote logo (it was the robot's first frame); the robot stays the taskbar mascot.
- The widget is taller (about 260x210) to make room for the sentence strip.

### Added

- **English sentences with Vietnamese translations**: 357 sentences (motivation, idioms and proverbs, conversation) with natural Vietnamese translations, bundled offline in `Assets/Quotes/quotes.json` with sources and licenses in `Assets/Quotes/CREDITS.md`. The sentence is shown as large as fits (English at most three lines, translation below), over the plain background and every wallpaper, and compactly in the widget.
- A new sentence at start, at each Pomodoro phase and optionally every 5/10/30/60 minutes, or on demand with **View > Next sentence (Ctrl+N)**; a shuffled pass through all sentences before repeats, never the same one twice in a row, and the last one is remembered across restarts.
- **Settings > Quotes...** (categories, Vietnamese translation on/off, interval) and **View > Vietnamese translation**; saved in `%AppData%\PomoQuote\quotes.json`.
- **Settings migration**: on first start the files in the old `%AppData%\HelloWorldBot` folder are copied to `%AppData%\PomoQuote` once, without overwriting or deleting anything; a failure is logged and the app starts with the defaults.
- **PomoQuote logo and icon**: a clock ring split into focus (warm) and rest (cool) halves around a quotation mark, on a dark rounded square. Original work under the repository's MIT license (`Assets/Logo/`). The multi-size `pomoquote.ico` (16 to 256 px) is the executable's icon and is used for the window title bar and taskbar button, the tray icon and the About dialog, which now shows the logo.
- **Weather icons**: every weather value in the main window and in the widget has its own icon: place (pin), temperature (thermometer), conditions, humidity (drop), wind (swooshes, plus an arrow and compass point for its direction), last update (clock) and unavailable/stale (warning). 21 condition icons cover every weather code the service reports, with sun and moon variants for clear, mainly clear, partly cloudy and showers (from the service's day/night flag); unknown codes show a neutral "?". Stale values keep their icon, dimmed. The weather request also asks for `is_day` and `wind_direction_10m` (both optional when parsing). The icons are original vector drawings painted in code, sharp at any display scale (credits in `Assets/Weather/`).
- **Taskbar mascot**: minimizing the window hides it to the system tray (tray menu: Open, Start/Pause Pomodoro, Settings, Exit) and a pixel-art robot lives on the top edge of the taskbar (the bottom of the screen if the taskbar is hidden or on another edge). It is a game character, not a picture: frame-by-frame animations from an original CC0 sprite sheet (idle with blinking, looking around, walk, run, jump, landing, sit, sleep, wave, celebrate, surprised, sad, being held, shout), driven by a state machine, and it does things on its own now and then (look around, sit, wave, jump). It follows the Pomodoro: brisk walk while working, a jump and shout when a work session ends, sitting and dozing during a break, asleep during a long break, and sad for a moment when a phase is skipped or the timer is reset.
- The mascot reacts to the mouse: a single click makes it wave or look surprised, you can drag it with the mouse (it dangles from the pointer, can never leave the visible screens, and falls and lands on the taskbar edge when released). A **double click on the robot**, a click on the tray icon or the tray menu's Open brings the window back.
- The mascot walks across every monitor and jumps with a shout onto the next one, and copes with different resolutions, monitors above or below each other and monitors plugged or unplugged while it walks. Its click-through window never takes focus (only the sprite itself can be clicked), it stops while the screen is locked or the PC sleeps, and it hides while a full-screen app is in front.
- **Settings > Mascot...**: enable the mascot, sound on/off, volume, speed, robot size, **random actions** (Off, Few, Normal), which monitors it walks on, and **Reload animations**. Saved in `%AppData%\PomoQuote\mascot.json`. All frame ranges, speeds and action probabilities live in `Assets/Mascot/mascot-config.json`; an optional `%AppData%\PomoQuote\mascot-config.json` overrides single fields (invalid values are ignored with a warning). Sprite, shout sound and config are CC0 (credits in `Assets/Mascot/CREDITS.md`).
- The app now runs as a single instance, and the Pomodoro balloons share one tray icon with the mascot.
- **Widget mode** (View > Widget mode, Ctrl+W): the window turns into a compact (about 260x210) borderless, always-on-top widget that you can drag anywhere. It shows the time, the weather, the Pomodoro phase, remaining time and progress bar, and the current sentence with its translation. Right-click it for Exit widget mode, Next sentence, Start/Pause Pomodoro, Opacity, Settings and Exit app. **View > Widget opacity** sets 100%, 80% or 60%.
- Widget mode, position and opacity are remembered in `%AppData%\PomoQuote\widget.json`. On start the widget is moved fully inside a visible monitor, also when the monitor it was on is no longer connected. Switching modes keeps the running Pomodoro, the weather and the settings.
- **Pomodoro timer** panel next to the clock: Work 25 min, Break 5 min, a 15 min long break after 4 work sessions (all adjustable), with a progress ring, Start / Pause / Resume, Skip and Reset. Each phase end plays a short alert sound, shows a balloon notification and the window title shows "mm:ss Work - PomoQuote" or "mm:ss Break - PomoQuote". Today's finished work sessions are counted and remembered.
- **Settings > Pomodoro...** for the durations, sessions before a long break, sound on/off and auto-start of the next phase. Options and the daily count are saved in `%AppData%\PomoQuote\pomodoro.json`. Alert sounds are CC0 (credits in `Assets/Sounds/CREDITS.md`).
- **Live weather** under the title: city, temperature (°C), conditions, humidity and wind, from the free [Open-Meteo](https://open-meteo.com/) API (no API key). It loads on start, refreshes about every 10 minutes without blocking the window, and shows "Weather unavailable" (keeping the last known values) when offline.
- **Settings > City...** to choose the city. It is looked up with Open-Meteo geocoding and saved in `%AppData%\PomoQuote\settings.json`; the default is Hanoi.
- **View > Wallpaper** menu: pick one of three bundled background photos (Night Sky, Foggy Forest, Mountain Lake) or None. The photo covers the window, resizes with it, and the clock gets a light dial so it stays readable. Photo credits are in `Assets/Wallpapers/CREDITS.md`.
- **MIT license** (`LICENSE`), a rewritten README with badges and an intro GIF made with the motion-video skill from real screenshots of the app (`docs/intro.gif`), and credits for the bundled assets.
- Unit tests (`PomoQuote.Tests`) for the sentence data, rotation and layout, the settings migration, the icon, the Open-Meteo parsing, weather icons and lines, the Pomodoro state machine, the widget placement, and the mascot (route, movement, state machine, brain, drag, config, settings and sound).

### Known issues

- [#30](https://github.com/quanap5/PomoQuote/issues/30): when the app is started in widget mode, there is no taskbar button after leaving widget mode.
- The Vietnamese translations of the 357 sentences have not been reviewed by a native speaker.
- The manual QA items listed in [#22](https://github.com/quanap5/PomoQuote/issues/22) are not all verified yet.

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

[Unreleased]: https://github.com/quanap5/PomoQuote/compare/v1.1.0...HEAD
[1.1.0]: https://github.com/quanap5/PomoQuote/releases/tag/v1.1.0
[1.0.0]: https://github.com/quanap5/PomoQuote/commit/ef0b64619e98481a45595c17f268dd091b359724
