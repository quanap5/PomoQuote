# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- Dialogs opened while the always-on-top widget is on screen (the settings dialogs and their messages) no longer open behind the widget, where they were partly or completely hidden and could not be clicked. A dialog now opens in front, beside the widget and inside that screen's usable area (the Windows folder picker is narrowed when that is what keeps it beside the widget; where a screen has no free spot, a dialog overlaps the widget but stays in front and on the screen); one of the app's dialogs that is too large for the screen scrolls. The walking mascot stays under an open dialog.

## [1.5.0] - 2026-11-26

The expressions release: 4,396 idioms, phrasal verbs, common expressions, proverbs, quotes and life maxims join the 14,034 sentences PomoQuote already had, for 18,430 in all, still bundled offline.

Highlights:

- **4,396 new entries** in six categories: 1,441 idioms, 778 phrasal verbs, 775 common expressions and 248 proverbs from [English Wiktionary](https://en.wiktionary.org), and 568 quotes and 586 life maxims from public-domain books.
- **Eight categories** in **Settings > Quotes...**, drawn by weight, so the conversation sentences are now about a third of what you see instead of about 97% (with all categories on, the default).
- **Hover a sentence** for what an expression means, an example, or a quote's author and work, with the credit.
- **Please note**: 4,247 of the new Vietnamese translations are **machine drafts that no person has reviewed**; the app says "machine translation, not reviewed" under each.
- **Licences**: the new data is in its own files beside the MIT code. The Wiktionary entries, their translations included, are **CC BY-SA 4.0**; the public-domain quotes and maxims have **CC0 1.0** translations. See `Assets/Quotes/data/LICENSE.md`, which also comes with the program.

### Added

- Categories for proverbs, quotes, life maxims, common expressions and phrasal verbs in **Settings > Quotes...**, and a loader for their licensed data files (one file per source and licence; an incomplete or wrongly licensed record is refused). A settings file saved with all categories includes the new ones; a saved choice of some categories stays as it was.
- A machine-drafted Vietnamese translation that nobody reviewed is marked under the sentence, and the sentence's tooltip shows its meaning, example and credit.
- 3,242 entries from [English Wiktionary](https://en.wiktionary.org), bundled offline: 1,441 idioms, 778 phrasal verbs, 775 common expressions and 248 proverbs, each with an English meaning and a Vietnamese translation, under CC BY-SA 4.0 (credited in the tooltip and in **Help > About**). Most translations are machine drafted and marked as not reviewed.
- 568 quotes and 586 life maxims from public-domain books by authors who died in 1955 or before, each naming its author and work in the tooltip, with machine-drafted Vietnamese (CC0 1.0) marked as not reviewed.

### Changed

- The next sentence is drawn by category weight (conversation 35, idioms 12, proverbs 12, quotes 10, life maxims 10, common expressions 8, phrasal verbs 8, motivation 5) instead of from one shuffled pile, so conversation sentences no longer make up about 97% of what you see. Each category still shows all of its sentences before repeating one.

### Known issues

- **Machine translations are not reviewed.** 4,247 of the new translations are machine drafts, marked "machine translation, not reviewed"; no person has reviewed them (the other 149 are Wiktionary's own). They were drafted by one model run and checked by a second one of the same model family, so a mistake both make goes unnoticed. Translations the check doubted were left out.
- **Quotes include speech by characters** in plays and novels. The tooltip names the author and the work, not the speaker: an attribution means that the sentence stands in that author's work, not that it is the author's own opinion.
- **A saved choice that includes Idiom now shows much more.** The saved list stays as it was, but the Idiom category grew from 131 to 1,572 sentences: with Motivation and Idiom ticked there are 1,685 sentences instead of 244, and about 7 in 10 shown are idioms.
- **Start-up and memory.** The first window appears about 95 ms later than in 1.4.0 and the program uses about 15 MB more private memory (about 43 MB instead of 28 MB), because of the new data. Measured on one Windows 11 PC.
- **Long tooltips may be cut off** at 200% display scaling or on a narrow screen (calculated from their width, not seen on screen).
- **Not checked on screen:** the tooltips of the new entries, **Help > About** and **Settings > Quotes...** as they are now, and display scaling of 125% and 150% (their content and the fit of every sentence were checked by tests).
- 113 of the 357 original sentences are conversation lines and share the Conversation category with the Tatoeba pairs, so switching that category off hides them too. A separate category for the Tatoeba pairs is a possible follow-up.
- The Vietnamese translations of the 357 original sentences, and those of the Tatoeba pairs, have not been reviewed by a native speaker; about 3 in 100 Tatoeba pairs read in a sample were loose or slightly wrong, or had a Vietnamese typo.
- The Compact provider logo can stay in the old theme's colours for up to about 12 seconds after you change the Windows theme while the widget is on System.
- A rare crash at exit: an `InvalidOperationException` from the provider list's *Closed* handler in `WidgetView.Usage.cs` (`ShowProviderList`) when the app closes while the list is open.
- Not checked in the running app before 1.3.0 and still not checked by hand: dragging the cat, the Mascot Options dialog, a restart with the cat selected, and the real Grok and Antigravity readings.
- The weather effect is very faint by design: its colours are kept dark enough to keep the text at a contrast of 4.5:1 or more (the intro GIF brightens it so it shows). It is a still picture, not an animation, when Windows animations are turned off or Reduce motion is on.
- [#46](https://github.com/quanap5/PomoQuote/issues/46): non-standard Codex windows (not 5 hours or one week) get identical truncated labels.
- [#44](https://github.com/quanap5/PomoQuote/issues/44): the PowerShell form of the Claude status line bridge (used only when Git Bash is missing) does not propagate the exit code of the original command.
- The Claude icon is Anthropic's official mark used **without written permission** from Anthropic (the owner accepted the risk; the contact is marketing@anthropic.com; see `THIRD_PARTY_ASSETS.md`). The Grok icon is likewise xAI's official mark without written permission.
- Windows only; WSL is not supported yet.

## [1.4.0] - 2026-11-26

The sentences release: 13,677 English-Vietnamese sentence pairs from Tatoeba join the 357 sentences PomoQuote already had, for 14,034 in all, with each sentence's authors credited.

Highlights:

- **14,034 sentences** instead of 357: the new ones are everyday conversation pairs from [Tatoeba](https://tatoeba.org), all bundled offline (the app still makes no network request for sentences).
- **Credits where the licence asks for them**: Tatoeba text is CC BY 2.0 FR, so each sentence's authors (the English sentence's and its Vietnamese translation's) are shown as a tooltip on the sentence, in the widget and in the normal window, and **Help > About** names Tatoeba and the licence.
- **Please note**: the Vietnamese translations come from Tatoeba contributors and have **not been reviewed by a native speaker**; in a sample, about 3 in 100 pairs were loose or slightly wrong. With all categories on (the default) about 97% of the sentences you see are now these conversation sentences: **Settings > Quotes...** picks the categories.

### Added

- **13677 English-Vietnamese sentence pairs from Tatoeba** (category conversation) join the 357 sentences, all bundled offline in `Assets/Quotes/quotes.json` (about 2.7 MB; the 357 original sentences and their groups are unchanged). They are licensed CC BY 2.0 FR, which asks for the author's name: every entry records the Tatoeba id and the author of the English sentence and of its Vietnamese translation, the sentence shows them as a tooltip (widget and normal window), **Help > About** credits Tatoeba, and `Assets/Quotes/CREDITS.md` and `THIRD_PARTY_ASSETS.md` record the licence, the changes made (typography only) and the selection rules. The pairs were chosen by `tools/import-tatoeba.mjs` (a development tool, never run by the app, which makes no network request for sentences) from 23,285 candidates: both authors known, a length and shape that fits the widget, no adult, offensive, violent, drug, political or religious content, no pair tagged vulgar, slang, ambiguous or non-literal, heuristic checks of the translation and of spelling, one pair per sentence, no duplicates; `Assets/Quotes/tatoeba-import-report.md` gives the count after each step. The loader now refuses a CC BY entry that lacks its ids and authors. The translations come from Tatoeba contributors and are not reviewed by a native speaker; about 3 in 100 pairs read in a sample were loose or slightly wrong.

### Known issues

- With the Conversation category switched off, 244 of the original sentences remain, not 357: 113 of the original sentences are conversation lines and share the category with the Tatoeba pairs. A separate category for the Tatoeba pairs is a possible follow-up.
- The Vietnamese translations of the 357 original sentences, and those of the Tatoeba pairs, have not been reviewed by a native speaker; about 3 in 100 Tatoeba pairs read in a sample were loose or slightly wrong, or had a Vietnamese typo.
- The program now keeps about 20 MB more memory and starts about 45 ms slower than 1.3.0 because of the larger sentence file.
- The Compact provider logo can stay in the old theme's colours for up to about 12 seconds after you change the Windows theme while the widget is on System.
- A rare crash at exit: an `InvalidOperationException` from the provider list's *Closed* handler in `WidgetView.Usage.cs` (`ShowProviderList`) when the app closes while the list is open.
- Not checked in the running app before 1.3.0 and still not checked by hand: dragging the cat, the Mascot Options dialog, a restart with the cat selected, and the real Grok and Antigravity readings.
- The weather effect is very faint by design: its colours are kept dark enough to keep the text at a contrast of 4.5:1 or more (the intro GIF brightens it so it shows). It is a still picture, not an animation, when Windows animations are turned off or Reduce motion is on.
- [#46](https://github.com/quanap5/PomoQuote/issues/46): non-standard Codex windows (not 5 hours or one week) get identical truncated labels.
- [#44](https://github.com/quanap5/PomoQuote/issues/44): the PowerShell form of the Claude status line bridge (used only when Git Bash is missing) does not propagate the exit code of the original command.
- The Claude icon is Anthropic's official mark used **without written permission** from Anthropic (the owner accepted the risk; the contact is marketing@anthropic.com; see `THIRD_PARTY_ASSETS.md`). The Grok icon is likewise xAI's official mark without written permission.
- Windows only; WSL is not supported yet.
## [1.3.0] - 2026-11-25

The characters, themes and providers release: a choice of four mascot characters, a Light, Dark or System theme for the widget, Grok and Antigravity usage next to Codex and Claude Code, and an optional automatic Claude refresh.

Highlights:

- **Four mascot characters** (Robot, Animal, Man, Woman, all original pixel art with the same animations) and a **Show the mascot** switch right in the widget's right-click menu: the mascot now walks the taskbar beside the widget, not only when the window is minimized.
- **Theme: System / Light / Dark** for the widget (System follows Windows live), with every colour coming from one set of theme tokens.
- **AI usage for Grok** (the weekly allowance of a Grok plan, through the Grok Build CLI) and **Antigravity** (through `agy`), both off until you turn them on in Settings > AI Usage.
- An optional **automatic Claude refresh** (off by default; every 15, 30 or 60 minutes) that keeps the Claude bars current when no Claude Code session is open.

### Added

- **Mascot characters and a mascot toggle in widget mode.** Four selectable pixel-art characters, all original art drawn for the project (CC0) with the same 14 animations as the robot (idle, look, walk, run, jump, land, sit, sleep, wave, celebrate, surprised, sad, drag, shout): **Robot** (unchanged, still the default), **Animal** (a cat), **Man** and **Woman**. **Settings > Mascot** (the normal window, the widget and the tray menus) now has **Show the mascot**, the character picker with the current one ticked, and **Options...** (the old dialog); the widget's right-click menu has **Show the mascot** directly. In widget mode the mascot walks the taskbar beside the widget while it is on (like when the window is minimized; hiding the widget to the tray leaves it out, and a double click brings the widget back); changes apply at once and are saved in `mascot.json` (`Character`; a missing or unknown id means the robot; the mascot stays on by default). Characters are one registry entry each (`MascotCharacters`), with the sheets and their credits in `Assets/Mascot`. Sheets and previews are made by `tools/generate-mascot-sprites.ps1` and `tools/generate-mascot-preview.ps1`.
- **Widget theme: System / Light / Dark** (Settings > Theme, the widget menu and the tray menu). System (the default) follows the Windows app theme and updates live when it changes; Light and Dark are fixed. A choice applies at once and is remembered in `widget.json` (`Theme`; a missing, damaged or unknown value means System). Every widget colour now comes from one set of theme tokens (`WidgetTheme`; Dark is the previous look, unchanged): panel, text, borders, bars, timer and quota colours, the quote card, the Expand button and logo selector, the Pomodoro icon cut-outs, the provider logo variant, and the weather-effect particles (dark on the light panel). The opacity slider and per-state accents are not part of it.
- **Optional automatic Claude refresh** (Settings > AI Usage > Claude Code): "Refresh Claude usage automatically", off by default, every 15, 30 (default) or 60 minutes, with a one-time note that each refresh counts a very small amount against the Claude quota. It runs the existing one-request refresh (`claude -p`, smallest model, one word; same command line, 30 s timeout with process-tree kill, one per minute, same snapshot, no new data source) on its own once-a-minute timer, which exists only while the setting is on. It is skipped when a Claude session or an earlier refresh updated the numbers less than half an interval ago, while a window is at 100%, while the PC is locked or asleep (it waits 45 s after an unlock or resume), and while the widget is hidden to the tray; a failed refresh waits for the next interval (or for a change of the setting or a successful manual refresh). The widget source line says *auto refresh* (*refreshed by hand* when the setting is off); stale marking and the 280 px layout are unchanged. New settings in `usage.json` (a missing or damaged file means off, 30 minutes): `ClaudeAutoRefresh`, `ClaudeAutoRefreshMinutes`, `ClaudeAutoRefreshAcknowledged`.
- **AI usage: Grok** (Settings > AI Usage...): a fourth provider, off by default, that shows the weekly credit allowance of your Grok plan (one `Week` row, the plan name, the time left; a monthly or other period under its real length) read every 5 minutes from the Grok Build CLI's own agent server, `grok agent stdio`: PomoQuote sends exactly `initialize` and the billing request of its `x.ai` extension, never a prompt and never a session, so no model is called and no credit spent; the process is killed with its children on a 12 s timeout and on exit, and the answer is read tolerantly (*unsupported version* and no more polling if the CLI does not know the request or the shape is not understood). No login or file of the CLI is read or stored (`grok` keeps it). The CLI does its normal start-up work each time it starts (it may tidy its stale worktree pool and start the MCP servers you configured); only its worktree auto-clean and its update check are switched off for that child, so while Grok is on `grok agent stdio` starts and stops every 5 minutes. **Test connection** makes the first read. Uses the official xAI icon, its white background made transparent (source, hash and the accepted trademark risk in `THIRD_PARTY_ASSETS.md`). Model changes: a new data-source value (`GrokAcp`) and an optional environment argument on the shared JSON-line process session; the provider abstraction, the collector and the existing states are unchanged.
- **AI usage: Antigravity** (Settings > AI Usage...): a third provider, off by default, that reads the quota with the vendor's own command, `agy -p /usage --output-format json`, every 5 minutes (about 6 s each, 12 s limit, killed on timeout, private empty working folder). It uses no model and no quota: the answer must come from the usage command with no conversation turn and no tokens, otherwise the provider says *unsupported version* and stops asking until the app restarts. No login is read or stored (`agy` keeps it), and neither Antigravity's local server nor its sign-in client is used. Antigravity reports a limit per model group: the widget shows one row per window length (`Week`, and `5h` when there is a five-hour limit) for the most used group, and the tooltips and the Expanded source line list every group (*Gemini 33%, Claude and GPT 0%*); warnings work per group (each group warns once per window and re-arms on its own, and the balloon names the group). **Test connection** makes the first read. Minimal model changes: a new data-source value (`AntigravityCli`); the provider abstraction, the collector and the existing states are unchanged.

### Fixed

- **Theme: weather effect colours at startup.** With Windows in light mode and the theme System (or a saved Light), the weather particles, the lightning flash and the sun glints now use the light-panel colours from the first frame; before, the effect was set up for a dark panel until the theme changed once, so snow was invisible on the light panel and rain was much weaker. The effect now matches the panel whenever the theme is applied, including at startup.
- Two tests that failed now and then (the second-launch signal and the handle-leak check) are deterministic; the second-launch signal now waits for its named event to be released when the app closes, so the event is gone when it exits.

### Known issues

- The Compact provider logo can stay in the old theme's colours for up to about 12 seconds after you change the Windows theme while the widget is on System.
- A rare crash at exit: an `InvalidOperationException` from the provider list's *Closed* handler in `WidgetView.Usage.cs` (`ShowProviderList`) when the app closes while the list is open.
- Not checked in the running app before this release: dragging the cat, the Mascot Options dialog and a restart with the cat selected; the weather-colour fix at startup; and the real Grok and Antigravity readings (they were tested with fakes and, for the spikes, one real read each, not QA'd in the finished app).
- The weather effect is very faint by design: its colours are kept dark enough to keep the text at a contrast of 4.5:1 or more (the intro GIF brightens it so it shows). It is a still picture, not an animation, when Windows animations are turned off or Reduce motion is on.
- [#46](https://github.com/quanap5/PomoQuote/issues/46): non-standard Codex windows (not 5 hours or one week) get identical truncated labels.
- [#44](https://github.com/quanap5/PomoQuote/issues/44): the PowerShell form of the Claude status line bridge (used only when Git Bash is missing) does not propagate the exit code of the original command.
- The Claude icon is Anthropic's official mark used **without written permission** from Anthropic (the owner accepted the risk; the contact is marketing@anthropic.com; see THIRD_PARTY_ASSETS.md). The Grok icon is likewise xAI's official mark without written permission.
- Windows only; WSL is not supported yet.
- The Vietnamese translations of the 357 sentences have not been reviewed by a native speaker.

## [1.2.0] - 2026-11-25

The widget release: AI usage for Codex and Claude Code, a redesigned widget, provider logos, the date with the Vietnamese lunar date, weather effects, and widget mode as the default with hide to tray.

Highlights:

- The app opens as a small always-on-top **widget** by default; hide it to the tray with **Ctrl+H** or its right-click menu and bring it back from the tray icon (Settings > Start in: Widget, Normal window or Remember last).
- **AI usage** on the widget for **Codex** and **Claude Code**: percent used, bars and reset times, in a Compact or an Expanded layout, with provider logos.
- A new look: pill progress bars, the sentence in a message bubble, the Pomodoro state icon (Idle, Work, Rest), and the solar and **Vietnamese lunar date** on the time row.
- **Weather effects** behind the widget: rain, snow, clouds, fog, stars and more, faint by design (still when Windows animations are off).
- Claude Code usage through a safe **status line bridge** that asks first, backs up your settings and can be removed again.
- The widget has no taskbar button and lives in the tray (by its window style it is not listed in Alt+Tab); the normal window has its taskbar button and logo.

### Added

- **New README intro GIF** (`docs/intro.gif`, 19 s, 720 px wide, loops): made with the motion-video workflow (HyperFrames) mostly from real captures of the current build on sample data (the taskbar strip is stylised): the logo, the widget (clock, solar and Vietnamese lunar date, weather, Pomodoro, English sentence with its Vietnamese translation), the Pomodoro in Idle, Work and Rest, AI usage for Codex and Claude Code in Compact and Expanded, weather effects, the taskbar robot and the Hide to tray menu. The README text and feature list describe the current app (widget as the default mode, tray, weather effects). Documentation only.
- **Widget mode is the default start mode, with hide to tray**: a new **Start in** setting (Widget, the new default; Normal window; Remember last) in the Settings menus, the widget menu and the tray menu, saved in `widget.json` (a missing or damaged file means Widget; an existing install that never saved the setting now starts as the widget: pick Normal window or Remember last to keep the old behaviour). The widget can be hidden to the tray with **Hide to tray** in its right-click menu or **Ctrl+H**; in widget mode the tray icon is always present (click hides or shows, double click shows) and its menu has Show or Hide widget, Normal window, Start Pomodoro, Settings and Exit. The widget never has a taskbar button, visible or hidden: it lives in the tray icon, which is always there in widget mode (by its window style it is not listed in Alt+Tab), while the normal window has its taskbar button and logo. While hidden the effect timer and the clock tick make no tick, while the weather, usage and Pomodoro timers and their alerts keep working; showing the widget again restores its position (moved onto a visible work area if needed) and opacity. Starting a second copy while the widget is hidden shows the first one.
- **Weather effects on the widget** (new **Settings > Weather effects...**): a faint animation behind the widget's content from the weather it already shows: sun glints (clear, day), twinkling stars (clear, night), drifting clouds (partly cloudy to overcast, denser as cloudier), haze (fog), falling streaks (drizzle, rain and showers by intensity, icy-tinted for freezing rain), slow flakes (snow and snow grains) and heavy rain with a rare soft flash (thunderstorm), with day and night colours, a cross-fade when the weather changes, and nothing for an unknown weather. On by default at the light **Low** intensity (**Normal** is the full effect); **Reduce motion**, or Windows animations turned off, shows one still picture. The effect has its own timer (about 15 frames a second) that makes no tick while it is off, or the widget is minimised, hidden, in the tray, covered, the session locked or the computer asleep; it uses a bounded, pooled set of particles with cached drawing objects, never changes the widget's size or layout, and keeps the text at a contrast of 4.5:1 (secondary) and 7:1 (primary) or more with up to 4 overlapping streaks, flakes or cloud puffs and a thunderstorm's flash on top (a test composites the renderer's own colours and opacities), so the effect is subtle by design; a thunderstorm flashes once every 15 to 40 seconds. Saved atomically in `%AppData%\PomoQuote\weather-effects.json`.
- **Widget redesign (AI Usage, quote, bars)**: the usage area has two layouts. **Compact** shows one provider at a time in at most three lines with its logo on the far left (name, plan, source and update time in the logo's tooltip; wheel over the logo, click or arrow keys to switch provider; a badge for error, stale data or near limit); **Expanded** shows every enabled provider as a plain block (full name, plan, every window, percent, countdown, source, update time; no card, border or background) and scrolls only its own area. A small arrow button, centred on a separator line under the quote bubble, switches the layout; the layout and the selected provider are remembered after a restart, and the provider order of Settings is used. New pill progress bars for the timer and the quotas (no stray dot at 0%), `N/A` instead of 0% when a quota is missing, a clock icon and tooltip for stale data, tooltips with the exact used, left and reset time, windows named by their real length, an "updating" mark for the provider being read (its last numbers stay), larger text (12 px), better contrast for the secondary text, and the sentence in a message-bubble card that wraps instead of cutting the text. The usage area has no cards (Expanded is plain rows with only space between providers); the Expand/Collapse button sits centred on the separator line under the quote bubble; Expanded grows downward so the clock and timer keep their place (only the usage area scrolls on a short screen); the logos are cropped of their empty margins when loaded and fill an about 20 px box at full brightness (the official files are unmodified); the timer colours are never red, orange or yellow (those are for quota warnings only); numbers kept from an earlier read are marked old (clock icon, badge, amber note) when the latest read has no plan limits or no reply; long limit and plan names end with an ellipsis and a tooltip. The widget is now 280 px wide at 100%.
- **Provider logos**: Codex shows the official OpenAI Blossom and Antigravity (packaged for later) the official Antigravity icon, as unmodified files that are cropped of their empty margins in memory and scaled; Claude shows Anthropic's official Claude Spark from its press kit, unmodified (the owner accepted the trademark risk; no written permission was obtained from Anthropic), with a neutral glyph as the fallback if that file is missing. The files are packaged with the program (nothing is fetched), with sources, terms and a trademark notice in `THIRD_PARTY_ASSETS.md`. The Settings dialog shows the same logos.
- Widget details: the **timer bar has a tooltip** (phase, elapsed, remaining, exact end time); the quote text keeps **12 px or more** while its card can grow (clamp about 210 px; below 12 px only at the clamp, an ellipsis only past the smallest size) and the closing quotation mark has its own room under the last line; **Expanded is keyboard-operable** (Tab, Up/Down/Home/End, an accessible name and description per block, a dotted focus rectangle on the focused block); **Help > About** shows the third-party marks notice (OpenAI, Anthropic, Google are not affiliated).
- **Date on the time row**: the solar date in Vietnamese and the Vietnamese lunar date on two lines, centred on the clock digits and right-aligned to the widget's right content margin, 12 px text that steps down to 10 px only when a line does not fit (Hồ Ngọc Đức's algorithm at UTC+7, leap months marked) on two lines, worked out once a day. **Pomodoro state icon** (Idle, Work, Rest, with a pause mark) aligned with the weather icons. **Claude icon**: Anthropic's official Claude Spark from its press kit (URL, retrieval date and SHA-256 in `THIRD_PARTY_ASSETS.md`), used unmodified, with the neutral glyph as the fallback (delete the two `claude-on-*` files to go back to it); the trademark notice, the guideline quotes and the note that PomoQuote has no permission from Anthropic are in `THIRD_PARTY_ASSETS.md`.
- **Claude status line bridge safety**: the status line does not point at the folder the app runs from. Setting the bridge up copies its program to `%LocalAppData%\PomoQuote\bridge\` (refreshed after an update) and is refused unless the app runs as `PomoQuote.exe`. The installed command first tests that program and otherwise runs your original status line directly, so a moved or deleted app cannot take your status line down. When PomoQuote starts it checks the bridge once; a missing program, a damaged saved command or a shell mismatch (Git Bash installed or removed since) shows a notification and a *bridge missing* state on the Claude strip, and **Check** also reports them.
- The Claude settings file now follows `CLAUDE_CONFIG_DIR` like Claude Code, with `POMOQUOTE_CLAUDE_SETTINGS` (file) and `POMOQUOTE_STATE_DIR` (PomoQuote's own files) as overrides for tests and relocated configs. A new Claude profile with no settings file can be set up (the file is created with only the status line and removed again by **Remove bridge** if it still holds nothing else), and **Remove bridge** first saves a fresh copy of the file.
- **AI usage: Claude Code** through a status line bridge (Settings > AI Usage... > Claude). Only on your explicit confirmation, after a dialog shows the exact change, PomoQuote backs up your user-level `~/.claude/settings.json` (never overwritten, in `%LocalAppData%\PomoQuote\bridge-backups\`) and sets only `statusLine` (a new one, or your own command wrapped so it runs unchanged). The bridge (`PomoQuote.exe --bridge claude`) saves a redacted snapshot (percent and reset time only; no email, session id, folder, transcript, tokens or costs), passes your original status line through and never breaks Claude Code. **Remove bridge** restores the original and keeps later edits; **Check** reports a moved or deleted bridge and project-level overrides. The widget shows Claude next to Codex with waiting, stale, not set up and no-plan-limits states and the same warning levels.
- **Refresh Claude now** (Settings and widget menu): one `claude -p` request with the smallest model and a one-word prompt reads the current limits on demand (30 s limit, once a minute, asked once because it uses a very small amount of quota). No credential is read and nothing of the answer is kept.
- **AI usage on the widget (Codex)**: **Settings > AI Usage...** turns on a strip under the sentence that shows the Codex quota for the five-hour and the weekly window: percent used, a bar, the time left until the reset, the plan and the time of the last read, with a stale marker. It reads `codex app-server` (a short-lived process every 3 minutes by default, 10 s limit) of the Codex CLI already on the PC; **PomoQuote never reads or stores a token or password**, never opens credential files and never changes your login. Windows only; WSL is not supported yet. Off by default.
- Settings for it: enable, detected install and login state (read only), test connection, display order, layout (Compact or Expanded), refresh interval, warning levels (default 80% and 95%, one balloon per window and level, remembered across restarts). Saved in `%AppData%\PomoQuote\usage.json`; the last good numbers are cached in `usage-cache.json` and shown again after a restart.
- A multi-provider core (normalised model, per-provider timer, time limit and error isolation, immutable snapshots, alert de-duplication) so that more providers can be added (Codex and Claude Code today). It reads again at once after sleep/resume and when the network returns, and shows clear states for not installed, not logged in, unsupported version and read failed, keeping the last good numbers.
- Unit tests for the Codex answer parser, the provider against a fake `app-server` (including real child processes that hang, crash or answer in pieces), the collector, the caches and settings files, alerts, the widget text, and a privacy test that scans the code and the files it writes for credential names.

### Fixed

- Leaving widget mode when the app started in widget mode left the normal window without its taskbar button.

### Changed

- Documentation and credits were made consistent: `THIRD_PARTY_ASSETS.md` lists every packaged asset with its source, terms and hash (a test checks that every file in `Assets/Providers/` has a row), and the README, CHANGELOG and credits files must be valid UTF-8 (`DocsEncodingTests`).
- The widget is 280 px wide at 100% (it was 260) and its height follows the sentence (the bubble grows with the text) and the AI usage area (Compact is one fixed block; Expanded grows with its blocks up to a limit); without AI usage the widget keeps its base layout.

### Known issues

- The weather effect is very faint by design: its colours are kept dark enough to keep the text at a contrast of 4.5:1 or more (the intro GIF brightens it so it shows). It is a still picture, not an animation, when Windows animations are turned off or Reduce motion is on.
- [#46](https://github.com/quanap5/PomoQuote/issues/46): non-standard Codex windows (not 5 hours or one week) get identical truncated labels.
- [#44](https://github.com/quanap5/PomoQuote/issues/44): the PowerShell form of the Claude status line bridge (used only when Git Bash is missing) does not propagate the exit code of the original command.
- The Claude icon is Anthropic's official mark used **without written permission** from Anthropic (the owner accepted the risk; the contact is marketing@anthropic.com; see `THIRD_PARTY_ASSETS.md`).
- Windows only; WSL is not supported yet.
- Antigravity is not shown yet (its icon is packaged for a later release).
- The Vietnamese translations of the 357 sentences have not been reviewed by a native speaker.

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

[Unreleased]: https://github.com/quanap5/PomoQuote/compare/v1.5.0...HEAD
[1.5.0]: https://github.com/quanap5/PomoQuote/compare/v1.4.0...v1.5.0
[1.4.0]: https://github.com/quanap5/PomoQuote/compare/v1.3.0...v1.4.0
[1.3.0]: https://github.com/quanap5/PomoQuote/compare/v1.2.0...v1.3.0
[1.2.0]: https://github.com/quanap5/PomoQuote/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/quanap5/PomoQuote/releases/tag/v1.1.0
[1.0.0]: https://github.com/quanap5/PomoQuote/commit/ef0b64619e98481a45595c17f268dd091b359724