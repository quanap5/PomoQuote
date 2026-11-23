# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
