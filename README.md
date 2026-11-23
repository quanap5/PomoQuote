# HelloWorldBot

A mini project: a minimal "hello world" bot, used to try the OrcaTeam flow (Lead -> Developer -> Reviewer -> QA).

## What it is

HelloWorldBot is a small practice project. Its purpose is to give the OrcaTeam a simple codebase for running a development job from start to finish.

## Status

Early, just started. The repository contains a Windows Forms app that shows "Hello World" in the center of its window.

## Build and run

Requires Windows and the .NET 9 SDK.

```powershell
dotnet build
dotnet run
```

## How the team works on it

1. **Branch:** the Developer makes each change on its own branch.
2. **Draft PR:** the Developer opens a draft pull request.
3. **Review and QA:** the Reviewer and QA check the exact commit.
4. **Merge:** the owner merges the pull request.

## License

Project license: TBD.

The bundled DSEG font is licensed under the SIL Open Font License 1.1. See `Assets/Fonts/DSEG-LICENSE.txt`.
