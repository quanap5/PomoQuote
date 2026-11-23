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
