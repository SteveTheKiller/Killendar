# Changelog

All notable changes to Killendar are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - Unreleased

1.0.0 is the first release of Killendar: a desktop calendar for Windows with month, week, day and agenda views, iCalendar import and export, and appointments that live in a file on your own machine instead of somebody else's server.

### Added

- Month, week, day and agenda views, with one shared previous / next / today control that follows whichever view is open.
- Appointments created and edited in the sidebar, with title, start and end, all-day, location, description and attendees.
- Overlapping appointments in the week and day views are laid out side by side in lanes, so a double-booked hour shows both.
- All-day appointments get their own strip above the hour grid.
- Month view shows only the weeks the month actually needs, and the first day of the week follows your Windows region settings.
- Agenda view lists the next 60 days grouped by day; clicking a day heading starts an appointment on that date.
- iCalendar (.ics) import and export, written against RFC 5545 with no external dependencies. Import skips appointments already in the calendar and reports how many were added and how many were skipped.
- Six themes (Dark, Light, Black, Blood, Greed, Cyanotic), each switchable at runtime, with six accent hues on Dark, Light and Black. Killendar ships on the dark theme with the red accent, the same red the wordmark and killendar.net are built on.
- Appointments stored in a single `.kcal` file - a Killendar - in `%APPDATA%\Killendar`. It is an ordinary SQLite database, so it is yours to read, back up, move or open with any SQLite tool. No account, no sync, no telemetry. Times are stored in UTC, so a calendar carried to another timezone still says the right thing; all-day entries stay on the date you put them on.
- Optional encryption. The lock button in the title bar sets a password, and the Killendar is then encrypted at rest with SQLCipher (AES-256, per-page HMAC-SHA512, SQLCipher's own PBKDF2 key derivation). Encryption is opt-in: with no password the file is plain SQLite. The same button changes the password, or removes it and stores the file unencrypted again. An encrypted Killendar asks for its password on launch; a wrong one just asks again.
- There is no password recovery, and the app says so before you commit to one. If a password is lost, the locked Killendar is renamed aside and kept on disk rather than destroyed, and a new empty one opens in its place.
- Exporting an encrypted Killendar to `.ics` warns you first that the file it writes is plain text. It never blocks the export - handing someone an `.ics` is what the feature is for - and a "Don't remind me again" box turns the warning off for good. Plaintext Killendars never see it, and ticking the box then cancelling means "not this time", not "never again".
- Keep as many Killendars as you like - one for work, one for on-call, one for the family. The Killendars button in the title bar lists every `.kcal` in your data folder with its size, when it changed and whether it is encrypted, and lets you create, rename, delete, load one from a file, or switch to it. Renaming is inline: double-click the name. Each file has its own password, or none. The name of the open Killendar shows in the footer once you have more than one.
- Double-click a `.kcal` and Killendar opens it. A copy is added to your data folder and opened; the file you double-clicked is left where it is, because a Killendar is written to constantly and that is not something to do to a file on a network share or in Downloads. The association is registered for your user only, needs no elevation, and is removed when you uninstall.
- Only one Killendar runs at a time. A second launch hands its file to the window already open and exits, rather than leaving two copies of the app writing to the same calendar.
- Window size, position, theme and accent are remembered between runs.
- About card showing the version and release date, the code-signing publisher, the certificate thumbprint and the running exe's SHA-256, plus a GitHub update check with one-click self-update.
- The publisher panel validates the Authenticode signature with WinVerifyTrust rather than only reading the embedded certificate, so a tampered build cannot display a signer it does not own. The chain check is cache-only, so opening About never stalls waiting on the network.
- Runs portable or installs per-user or for all machines, including a silent install path for winget, Chocolatey and RMM deployment. Uninstalling leaves your appointments in place.
- Ten interface languages (English, Bengali, Czech, German, Spanish, French, Japanese, Turkish, and Simplified and Traditional Chinese), switchable from the sidebar rail without restarting.
- Keyboard shortcuts for everything: single keys for new, today, paging and switching views, and Ctrl+I / Ctrl+E for import and export. Single keys stand down while you are typing in a field.
- Sidebar icon rail with a collapse chevron, language picker and theme picker, matching the rest of the Killer Tools family.
- Date format picker in the language menu: follow Windows, ISO, US or UK/EU. The field hint is derived from the format actually in force, so it can never suggest something the app would then reject, and switching format reformats an open editor rather than leaving two conventions on screen.
- Typing a date is forgiving: the chosen format is tried first, then ISO, then several common regional forms, so a date that looks reasonable is accepted whatever the setting says.
