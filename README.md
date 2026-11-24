# PomoQuote

<img src="Assets/Logo/pomoquote-logo-1024.png" alt="PomoQuote logo: a clock ring split into focus and rest halves around a quotation mark" width="96" align="right">

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/9.0)
[![Platform: Windows](https://img.shields.io/badge/platform-Windows-0078D6.svg)](#build-run-and-test)

![PomoQuote intro: the logo, an English sentence with its Vietnamese translation, the Pomodoro timer and weather over a wallpaper, and the small widget](docs/intro.gif)

**PomoQuote** is a Windows desktop app for focused work and learning English. It keeps an English sentence with its Vietnamese translation in front of you, next to a Pomodoro timer, the weather and an analog clock, and a small pixel-art robot walks along your taskbar while you work.

## Features

- **English sentences with Vietnamese translations.** 357 bundled offline sentences (motivation, idioms and proverbs, conversation) in large type that shrinks to fit. A new one appears at start, at each Pomodoro phase, optionally every 5/10/30/60 minutes, or on demand with **View > Next sentence (Ctrl+N)**. It shuffles through all of them before repeating and never shows the same one twice in a row. **Settings > Quotes...** picks the categories, translation on/off and the interval. Data: [Assets/Quotes/quotes.json](Assets/Quotes/quotes.json).
- **Pomodoro timer.** Work 25 min, break 5 min, long break 15 min after 4 work sessions (all adjustable in **Settings > Pomodoro...**), with Start / Pause / Resume, Skip and Reset, a progress ring, an alert sound and a balloon notification at each phase end, the remaining time in the window title, and a count of today's work sessions.
- **Weather.** Current weather for a city (default Hanoi, change it in **Settings > City...**) from [Open-Meteo](https://open-meteo.com/) (free, no API key), refreshed about every 10 minutes, with an icon for every value (sun and moon variants by day and night). Offline it keeps the last values and says so.
- **Wallpapers.** **View > Wallpaper** switches between three bundled photos and the plain background.
- **Taskbar mascot.** Minimize the window and it hides to the system tray while a pixel-art robot walks along the top of the taskbar, across all monitors. It reacts to the Pomodoro (brisk walk while working, shout and jump when a session ends, sleeps during breaks), can be clicked and dragged, and never takes focus. **Settings > Mascot...** sets sound, volume, speed, size, random actions and monitors.
- **Widget mode.** **View > Widget mode** (or **Ctrl+W**) turns the window into a small always-on-top widget (about 260x210) with the time, weather, Pomodoro and the current sentence, plus your Codex quota if you turn on **Settings > AI Usage...**. Drag it anywhere, right-click for options, set its opacity to 100%, 80% or 60%.
- **Analog clock** that shows the real time, and a single running instance of the app.

Settings live in `%AppData%\PomoQuote` (JSON files; a missing or damaged file falls back to the defaults). The app was called HelloWorldBot before: on the first start its settings in `%AppData%\HelloWorldBot` are copied over once, without overwriting or deleting anything.

## AI usage on the widget (Codex and Claude Code)

**Settings > AI Usage...** shows how much of your **Codex** and **Claude Code** quota is used, right on the widget: for the five-hour and the weekly window the percent used, a bar, the time left until the reset, your plan, and when the numbers were read (marked *stale* when they are older than twice the refresh interval). It is **off until you turn it on** there.

- **How it reads:** every 3 minutes (1 to 15, your choice) PomoQuote starts a short-lived `codex app-server` of the Codex CLI installed on this PC, asks it for the rate limits and lets it exit (10 s limit, the process is killed on timeout and when the app closes). Codex keeps your login: **PomoQuote never asks for, reads or stores a password, key or token**, never opens the Codex credential files, and never logs you in or out. If Codex says you are not logged in, run `codex login` in a terminal.
- **Settings:** enable Codex, see whether Codex is found and logged in (read-only `codex --version` and `codex login status`), **Test connection**, display order, display mode (bars, or percent only), how often to read, and warning levels (default 80% and 95%). Warnings are a balloon notification, once per window and level (not repeated after a restart; they come back for the next window or after usage drops below the level, and never for a window whose reset time is unknown).
- **What it shows when something is wrong:** *not installed*, *not logged in*, *unsupported version* (an answer in a shape this version does not understand), or *read failed*, with the last good numbers kept and marked. One provider failing never affects another or the rest of the app. After the PC wakes from sleep or the network comes back, it reads again at once.
- **Files:** `%AppData%\PomoQuote\usage.json` (your choices), `usage-cache.json` (the last good numbers: percent, reset time, plan, hashed account key, no ids or tokens) and `usage-alerts.json` (which warnings were shown).
- **Not yet:** Windows only, **WSL is not supported yet**; Antigravity comes in a later release (the code is already built for several providers).

### Claude Code (status line bridge)

Claude Code only shows its 5-hour and weekly limits to a **status line command**, so PomoQuote can read them through a small bridge. **It never changes your Claude settings on its own**: in **Settings > AI Usage... > Claude** you click **Set up status line bridge**, a dialog shows the exact change (file, the command now, the command after, where the backup goes) and nothing is written until you click **Set up bridge** (Enter and Esc both mean Cancel).

- **What it changes:** only `statusLine` in your Claude settings file (`~/.claude/settings.json`, or `settings.json` inside `CLAUDE_CONFIG_DIR` when you have set it, as Claude Code does). With no status line yet, one is added; with your own status line command, only its `command` text is replaced. If your Claude profile has no settings file yet, a new one is created with only the status line (a note in the backup folder says there was no file before, and **Remove bridge** deletes it again if it still holds nothing else). Everything else in an existing file (other keys, order, formatting, line endings) is left as it was.
- **A stable copy of the bridge:** the status line does not run the app from the folder you started it in. When you set the bridge up, PomoQuote copies its program files to `%LocalAppData%\PomoQuote\bridge\` and the command runs that copy, so moving, deleting or updating the app's own folder cannot break your status line. The copy is refreshed after an update of PomoQuote. The bridge is only set up from `PomoQuote.exe` (not from `dotnet PomoQuote.dll`).
- **If the bridge is gone:** the installed command first checks that its program exists and otherwise runs your original status line command directly with the same input, so your own status line keeps working (if you had none, it shows nothing). That is a protection for your status line, not a promise that nothing can go wrong: while the program is gone PomoQuote cannot read Claude usage. When the window is shown, PomoQuote checks the bridge once; if the program or the saved command is missing, the bridge is the old style, or Git Bash was installed (or removed) so the command is written for a different shell than Claude Code now uses, you get a notification and the Claude strip says *bridge missing*.
- **Bridges set up by PomoQuote 1.1 (old style):** the first version wrote a command that runs the program by an absolute path with no test and no fallback. If that program is moved or deleted, Claude Code runs a missing program and your status line disappears (the fallback above does not apply to it). PomoQuote recognises this form: **Check** and the start-up notification say so, and **Settings > AI Usage > Claude > Upgrade bridge...** rewrites only that one command to the new form after the same confirmation dialog (it shows the command now, the command after, the program copy and the backup folder; Cancel is the default; nothing is automatic). Your original command stays inside, byte for byte, and **Remove bridge** still restores it exactly.
- **Backup and undo:** before any change the whole file is copied to `%LocalAppData%\PomoQuote\bridge-backups\` (never overwritten; if the file changed while PomoQuote was preparing, nothing is written). **Remove bridge** first saves a fresh copy of the file as it is now, then puts your original command back (or removes the key we added), keeps edits you made later, does nothing if the line is not ours, and works even if the bridge program is gone. **Check** lists what is wrong (missing program, wrong shell, damaged saved command), and **Check a project folder** looks for a project-level `statusLine` that overrides the user-level one.
- **What it saves:** per Claude Code session one small file in `%LocalAppData%\PomoQuote\usage\` with only the percent used and reset time of each window, the Claude Code version and a hashed session key. No email, session id, folder, transcript, tokens or costs. Files older than 7 days are deleted; the newest one wins. The bridge adds well under 100 ms (about 60-120 ms measured, mostly .NET startup) and runs only when Claude Code redraws its status line.
- **What the widget shows:** *waiting for the first Claude Code answer* (the numbers appear only after the first reply of a session), *stale*, *not set up*, *no plan limits* (API-key logins have none) or *unsupported*, next to the Codex strip, with the same warning levels.
- **Refresh Claude now** (Settings and the widget menu): one small `claude -p` request (smallest model, one word, `--setting-sources project`, 30 s limit, killed on timeout and when the app exits) reads your current limits, e.g. right after a restart before the first session. It uses a very small amount of quota, so it asks once, runs only on a click, one at a time and at most once a minute. Nothing of the prompt or the answer is kept, and PomoQuote still never reads or stores a token or password.
- **Not yet:** Windows only (WSL is not supported), and a project-level `statusLine` is only detected for folders you check.
- **For tests and relocated configs:** `POMOQUOTE_CLAUDE_SETTINGS` names the settings file to work on (it wins over `CLAUDE_CONFIG_DIR`), and `POMOQUOTE_STATE_DIR` moves PomoQuote's own files (backups, the bridge copy). Both exist so tests and manual runs can use a throwaway file and never touch your real Claude settings.

![The widget with Codex and Claude at 100, 125, 150 and 200 percent](Assets/AiUsage/preview-claude-widget-scales-100-125-150-200.png)

![The Claude strip when the bridge is missing, compared with updated, stale and waiting](Assets/AiUsage/preview-claude-bridge-missing.png)

![Claude states: updated by a manual refresh, high usage, stale, waiting for the first reply, not set up, no plan limits](Assets/AiUsage/preview-claude-states.png)

![The widget with the Codex strip at 100, 125, 150 and 200 percent](Assets/AiUsage/preview-widget-scales-100-125-150-200.png)

## Build, run and test

Requires Windows and the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```powershell
git clone https://github.com/quanap5/PomoQuote.git
cd PomoQuote
dotnet build
dotnet run
dotnet test PomoQuote.Tests
```

There is no CI for this repository yet, so run the tests locally before a pull request.

## Changes and versions

Notable changes are listed in [CHANGELOG.md](CHANGELOG.md) ([SemVer](https://semver.org/)). The version is set once, as `<Version>` in `PomoQuote.csproj`, and shown in **Help > About**.

## Credits and licenses

PomoQuote is released under the [MIT License](LICENSE). The bundled assets have their own, compatible terms, listed in the `CREDITS.md` file of each folder:

| Assets | License | Credits |
|--------|---------|---------|
| Sentences and Vietnamese translations | Public-domain quotations and proverbs, original sentences and translations under CC0 1.0 | [Assets/Quotes/CREDITS.md](Assets/Quotes/CREDITS.md) |
| Mascot sprites (original), shout sound | CC0 1.0 | [Assets/Mascot/CREDITS.md](Assets/Mascot/CREDITS.md) |
| Pomodoro alert sounds | CC0 1.0 | [Assets/Sounds/CREDITS.md](Assets/Sounds/CREDITS.md) |
| Wallpaper photos | CC0 1.0 | [Assets/Wallpapers/CREDITS.md](Assets/Wallpapers/CREDITS.md) |
| Weather icons (original vector drawings) | CC0 1.0 | [Assets/Weather/CREDITS.md](Assets/Weather/CREDITS.md) |
| PomoQuote logo and icon (original work) | MIT, like the code | [Assets/Logo/CREDITS.md](Assets/Logo/CREDITS.md) |

Weather and city search data come from [Open-Meteo](https://open-meteo.com/) under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) ([terms](https://open-meteo.com/en/terms)); the free API is for non-commercial use and requires attribution. The intro GIF is made from screenshots of the app itself.

## How the team works on it

This repository is also the practice project the OrcaTeam uses to try its flow: the Developer makes each change on its own branch and opens a draft pull request, the Reviewer and QA check the exact commit, and the owner merges.

## License

[MIT](LICENSE) © 2026 quanap5
