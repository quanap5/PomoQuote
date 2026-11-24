# PomoQuote

A Windows desktop app that keeps an English sentence with its Vietnamese translation in front of you while you work: a Pomodoro timer, the weather, an analog clock and a small robot on your taskbar. (Formerly "HelloWorldBot"; the GitHub repository keeps that name.)

## What it is

PomoQuote is also the practice project the OrcaTeam uses to try its flow from start to finish (Lead -> Developer -> Reviewer -> QA).

## Status

Early, but complete enough to use. See [CHANGELOG.md](CHANGELOG.md) for what changed and when.

## Sentences (English with Vietnamese)

The big text area shows an **English sentence** in green with its **Vietnamese translation** underneath. The text is as large as fits: the English sentence uses at most three lines and shrinks to make room, so it never overlaps the weather, the Pomodoro panel or the clock. It stays readable over the plain background and over every wallpaper, at the minimum, default and large window sizes. In widget mode the sentence and translation sit in a strip at the bottom of the widget, cut with an ellipsis when too long.

- **357 sentences**, bundled with the app and read from [Assets/Quotes/quotes.json](Assets/Quotes/quotes.json); nothing is fetched from the internet. Three categories: **motivation** (public-domain quotations and sentences about focus and study), **idioms and proverbs** (traditional sayings) and **conversation** (everyday English sentences). Sources and licenses are in [Assets/Quotes/CREDITS.md](Assets/Quotes/CREDITS.md). The Vietnamese is written to sound natural, not word for word; a native speaker is welcome to correct it (edit `quotes.json`, the tests check the file).
- **A new sentence** appears when the app starts, at the start of each Pomodoro phase, optionally every 5 / 10 / 30 / 60 minutes, and on demand with **View > Next sentence (Ctrl+N)** (also in the widget's right-click menu). Sentences come in a shuffled pass through every sentence of the chosen categories before any repeats, and the same sentence never shows twice in a row. The last sentence shown is remembered so a restart does not begin with it again.
- **Settings > Quotes...** chooses the categories (motivation, idioms, conversation, or all), whether the Vietnamese translation is shown (also **View > Vietnamese translation**) and how often the sentence changes: *each Pomodoro phase* (default), *every 5/10/30/60 minutes (and each phase)* or *off* (only at start and on request). The choices are saved in `%AppData%\PomoQuote\quotes.json`; a missing or damaged file means the defaults.
- The sentences come from an `IQuoteProvider`; only the bundled offline one exists now, so an online source could be added later without touching the rest.

## Renamed from HelloWorldBot: your settings move along

The app was called HelloWorldBot until now. On the first start of PomoQuote the files in `%AppData%\HelloWorldBot` (city, Pomodoro options and count, widget, mascot settings and mascot config) are **copied** to `%AppData%\PomoQuote`. The old folder is never changed or deleted, a file that already exists in the new folder is never overwritten, and the copy is done only once (a marker file in the new folder records it). If something cannot be copied the app logs it (Trace output) and starts normally with the defaults, and tries the missing files again on the next start. The GitHub repository is still named HelloWorldBot.
## Build and run

Requires Windows and the .NET 9 SDK.

```powershell
dotnet build
dotnet run
```

## Help > About and versioning

The **Help > About** menu opens a dialog with the app name and version. The version is set once, as `<Version>` in `PomoQuote.csproj` ([SemVer](https://semver.org/)). Notable changes are listed in [CHANGELOG.md](CHANGELOG.md).

## Taskbar mascot

When you **minimize** the window it hides to the system tray and a pixel-art robot comes out and lives on the taskbar: it walks along the top edge of the taskbar (along the bottom of the screen if the taskbar is hidden or at the top or side). The tray menu offers Open, Start/Pause Pomodoro, Settings and Exit.

- **It is a small game character**, not a picture: every animation is a real frame-by-frame cycle from a sprite sheet (idle with blinking, look around, walk, run, jump with anticipation / rise / fall, landing, sit, sleep with floating Z's, wave, celebrate, surprised, sad, being held, shout). It turns to face where it walks, and its steps keep pace with its speed so the feet do not slide.
- **It has a mind of its own.** Now and then it stops and does something: looks around, sits down, waves, jumps. Which action and how often is decided by weighted dice per mood (see below).
- **Pomodoro moods:** working = walks and runs briskly; work session ends = celebrates, then jumps (and shouts if the Pomodoro's own sound is off); short break = sits down, then dozes off; long break = goes to sleep; no timer = a slow stroll with the odd action; **skipping a phase or resetting the timer makes it sad** for a moment. It never jumps between states abruptly: from sleep it first sits up, stands, and only then runs.
- **Click it** and it reacts (wave or surprised); **drag it** with the mouse and it dangles from your pointer, anywhere on any monitor (it can never leave the visible screens); let go and it falls to the taskbar edge and lands. **Double-click it**, or click the tray icon, to bring the window back.
- With several monitors it walks across all of them and **jumps with a shout** each time it crosses onto the next one. Plugging or unplugging a monitor while it walks is handled.
- It never takes focus, and clicks pass through every transparent pixel (only the sprite itself can be clicked). The animation stops while the screen is locked or the PC sleeps, and the character hides while a full-screen app or game is in front. Only one copy of the app runs at a time.
- **Settings > Mascot...**: show the mascot (off = minimize works as usual), sound on/off, volume, speed, size, **random actions** (Off = it only walks, Few, Normal), which monitors it walks on, and **Reload animations**. Saved in `%AppData%\PomoQuote\mascot.json`.

**Tuning the character.** All animation frame ranges, speeds, durations and the probability of each random action live in [Assets/Mascot/mascot-config.json](Assets/Mascot/mascot-config.json) (read it for the field list). To change something create `%AppData%\PomoQuote\mascot-config.json` with only the fields you want to override, then press **Reload animations** in Settings > Mascot. Invalid values are ignored with a warning and the default is kept.

**The art.** `Assets/Mascot/robot-sprites.png` is original pixel art (24x24 px frames, one palette, nearest-neighbour scaling at every size) drawn by [tools/generate-mascot-sprites.ps1](tools/generate-mascot-sprites.ps1): edit a pose there, run `powershell -File tools/generate-mascot-sprites.ps1` and commit the two PNGs it writes. [Assets/Mascot/contact-sheet.png](Assets/Mascot/contact-sheet.png) shows every frame; [Assets/Mascot/preview-life.png](Assets/Mascot/preview-life.png) shows a simulated minute of its life on a mock taskbar. The sprite sheet and the config are CC0; the shout is a CC0 recording. See [Assets/Mascot/CREDITS.md](Assets/Mascot/CREDITS.md). The app is system-DPI-aware: on monitors with other scale factors Windows scales the character with the rest of the window.

## Widget mode

**View > Widget mode** (or **Ctrl+W**) turns the window into a small widget (about 260x210): no title bar, always on top, with the time, the weather, the Pomodoro phase, remaining time and progress bar, and the current sentence with its translation, on a dark panel that stays readable over any desktop. Drag it anywhere with the mouse. **Right-click** it to exit widget mode (or press Ctrl+W again), start or pause the Pomodoro, change the opacity, open the Settings dialogs or exit the app. **View > Widget opacity** offers 100%, 80% and 60%.

Nothing restarts when you switch: the Pomodoro keeps running and the weather and settings stay as they are. The mode, the widget position and the opacity are saved in `%AppData%\PomoQuote\widget.json`; on start the widget is placed fully inside a visible monitor, so it comes back on screen even if the monitor it was on was unplugged. A missing or damaged file means normal mode.

## Pomodoro timer

The panel on the right shows a Pomodoro timer: **Work** 25 minutes, **Break** 5 minutes, and a **Long break** of 15 minutes after every 4 work sessions (the defaults). Use **Start**, **Pause / Resume**, **Skip** (next phase, without an alert) and **Reset** (back to a fresh work session). When a phase ends the next one starts by itself, a sound plays and a balloon notification appears; while a timer is active the window title reads "mm:ss Work - PomoQuote" / "mm:ss Break - PomoQuote" so you can follow it when the window is minimized. The panel also shows how many work sessions you finished today; the count starts again each day. A running timer is not resumed after a restart.

**Settings > Pomodoro...** sets the three durations (1-180 minutes), the number of work sessions before a long break (1-12), sound on/off and whether the next phase starts automatically. They are saved in `%AppData%\PomoQuote\pomodoro.json`; a missing or damaged file falls back to the defaults.

The timer uses a monotonic clock and a stored end time, so it stays accurate when the window is busy. The alert sounds are CC0; see [Assets/Sounds/CREDITS.md](Assets/Sounds/CREDITS.md).

## Weather and Settings > City

The app shows the current weather (city, temperature in °C, conditions, humidity, wind). It is fetched when the app starts and refreshed about every 10 minutes; requests run in the background and time out after 10 seconds. If the weather cannot be loaded the panel says "Weather unavailable" and keeps the last known values.

**Weather icons.** Every value comes with its own icon, in the main window and in the widget: a pin for the place, a thermometer for the temperature, a drop for humidity, wind swooshes for the wind speed (and an arrow with a compass point for the direction it blows from), a picture for the conditions, a clock while loading and a warning sign when the weather is unavailable or old (the old values then stay, dimmed, with the time of the last update). There is a separate picture for every kind of weather the service reports, with **sun and moon variants** for clear, mainly clear, partly cloudy and the showers, chosen from the service's day/night flag; an unknown code shows a neutral "?". The icons are original vector drawings painted in code, so they are sharp at every display scale, and they carry a dark outline so they read on the plain background, on every wallpaper and in the widget at 100%, 80% and 60% opacity. See [Assets/Weather/CREDITS.md](Assets/Weather/CREDITS.md) and the preview images in `Assets/Weather/`.

**Settings > City...** asks for a city name, looks it up and switches to it (an unknown city or no connection leaves the current city unchanged). The city is saved in `%AppData%\PomoQuote\settings.json`; on first run, or if that file is missing or damaged, the default city is **Hanoi, Vietnam**.

Weather and city search data come from [Open-Meteo](https://open-meteo.com/) (free, no API key; weather data under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/), [terms](https://open-meteo.com/en/terms)).

The free Open-Meteo API is for non-commercial use and requires attribution (CC BY 4.0).

Run the unit tests with:

```powershell
dotnet test PomoQuote.Tests
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

The sentences are public-domain or original (CC0); see [Assets/Quotes/CREDITS.md](Assets/Quotes/CREDITS.md). Other asset credits are listed in the `CREDITS.md` file of each `Assets` folder.
