<p align="center">
  <a href="https://killendar.net"><img src="docs/wordmark.png" width="640" alt="The Killendar - Free Encrypted Calendar"></a>
</p>

A calendar for Windows with no account, no sync and nothing phoning home. Your appointments live in
a single file on your own machine, and you can put a password on it. Once it is locked, that file is
encrypted at rest with SQLCipher using AES-256.

Unlocked it is an ordinary SQLite database, so you can read it, back it up, or carry it to another
machine with a copy and paste. No service to sign up for, no telemetry, no ads.

[killendar.net](https://killendar.net) &nbsp;|&nbsp; part of [Killer Tools](https://killertools.net)

## Features

- **Four views** - month, week, day and agenda, with one shared previous / next / today control.
- **Appointments in the sidebar**, not a dialog box. Title, start and end, all-day, location,
  description and attendees, with the panel sliding out from the icon rail.
- **Overlapping appointments sit side by side** in the week and day views, so a double-booked hour
  shows both rather than hiding one behind the other.
- **Repeating appointments** - daily, weekly on the days you tick, monthly or yearly, every N of
  those, ending never, after a set number, or on a date. A series is one stored appointment whose
  dates are worked out as the calendar draws them, so editing a weekly standup is one edit and a
  series with no end date costs nothing. Editing or deleting one date asks whether you mean just
  that date or the whole series.
- **Drag to move** - drag an appointment to another time or day in the week and day grids, or to
  another date in month view. One date of a series moves on its own, as an exception; right-click
  any of its dates for "Edit the series" when you mean the whole thing.
- **A 5-day work week toggle** in the week header drops Saturday and Sunday, and the grid density
  control steps the hour grid from hour lines down to quarter hours.
- **Import and export in the formats people actually hand you** - iCalendar (.ics, RFC 5545,
  written from scratch with no external dependencies) both ways; CSV both ways in Outlook's
  column format, one row per date; a saved email invite (.eml) in; and a month-by-month web page
  out, carrying all six themes and a print stylesheet so print-to-PDF covers printing. Export
  asks whether you mean the whole calendar or a date range. Repeats survive the .ics round trip,
  including dates you deleted from a series or edited on their own. Import skips anything already
  in your calendar, and says what it could not keep rather than swallowing it: repeat rules too
  exotic to draw, entries with no readable date, and tasks or journal entries Killendar has
  nowhere to put are all counted and reported.
- **Optional encryption** - the lock button in the title bar puts a password on your Killendar and
  it is encrypted at rest with SQLCipher (AES-256). Opt-in: no password means a plain SQLite file.
- **As many Killendars as you like** - work, on-call, family. Create, rename, delete, load and
  switch from the Killendars button in the title bar. Each file has its own password, or none.
- **Double-click a `.kcal`** and Killendar opens it. Registered for your user only, no elevation,
  removed on uninstall. Only one instance runs, so two copies never write to the same calendar.
- **Six themes** - Dark, Light, Black, Blood, Greed and Cyanotic - with six accent hues on the
  first three, switchable at runtime. Ships on dark with the red accent, the same red as the wordmark.
- **Ten languages**, switchable from the rail without a restart.
- **Keyboard driven** - single keys for everything, no modifier gymnastics.
- **Verified code signing** shown in the About card, checked with WinVerifyTrust rather than just
  read off the file, so a tampered build cannot display a signer it does not own.

## Install

**Portable** - download `Killendar.exe` and run it. It is a single self-contained file; nothing is
written outside your profile until you ask for it.

**Installed** - run the exe and use the install prompt. Per-user goes to
`%LOCALAPPDATA%\Programs\Killendar`; the all-users option installs to Program Files and prompts for
elevation once.

**Silent / deployment** - `Killendar.exe /silent` installs machine-wide with no UI, for winget,
Chocolatey and RMM tools. Uninstall from Add/Remove Programs, or `Killendar.exe /uninstall`.

Uninstalling never deletes your appointments.

## Keyboard shortcuts

| Key | Action |
|-----|--------|
| `N` | New appointment |
| `T` | Jump to today |
| `Left` / `Right`, or `,` / `.` | Previous / next period |
| `M` `W` `D` `A`, or `1` `2` `3` `4` | Month / week / day / agenda |
| `B` | Show or hide the appointment panel |
| `F1` | Keyboard shortcuts overlay |
| `F12` | About |
| `Esc` | Close the panel or overlay |
| `Ctrl+I` / `Ctrl+E` | Import / export |

Single keys only fire when you are not typing in a field. `Esc` always works.

## Where your data lives

| What | Where |
|------|-------|
| Appointments | `%APPDATA%\Killendar\*.kcal` (starts as `Default.kcal`) |
| Theme, accent, language, date format, window position | `%LOCALAPPDATA%\Killendar\settings.json` |

A `.kcal` file is a Killendar. It is an ordinary SQLite database, so any SQLite tool will open it -
nothing here is a private format you cannot get your data out of. Times are stored in UTC, so a
calendar carried to another timezone still says the right thing, while all-day entries stay on the
date you put them on.

Killendars are per-user. Installing for all machines still gives every user who signs in their own
fresh Killendar.

If you used a build from before the database (appointments in `events.json`), the first launch moves
them into `Default.kcal` for you and keeps the old file as `events.json.migrated` rather than
deleting it.

## Encryption

The lock button in the title bar sets a password. The Killendar is then encrypted at rest with
SQLCipher: AES-256 with per-page HMAC-SHA512, and SQLCipher's own PBKDF2 key derivation. The same
button changes the password, or removes it and rewrites the file as plain SQLite. Encryption is
opt-in; a Killendar with no password is an ordinary readable database.

An encrypted Killendar asks for its password when the app starts. **There is no recovery.** If the
password is lost the appointments are lost - that is what the encryption is for. The locked file is
renamed aside and kept on disk rather than deleted, and a new empty Killendar opens in its place.

What this does *not* protect:

- Nothing is protected while Killendar is open and unlocked.
- Not against a keylogger, a memory dump, or anyone who has your password.
- `settings.json` stays plaintext. It holds theme, accent, locale, date format, window position and
  which prompts you have dismissed - nothing about your appointments.
- iCalendar export is plaintext by definition. Exporting an encrypted Killendar to `.ics` writes an
  unencrypted copy of it. Killendar warns you the first time you do this and lets you turn the
  warning off; it never blocks the export, because handing a colleague an `.ics` is the whole point
  of having one.

## Building

Requires the .NET Framework 4.8 developer pack and Visual Studio 2022 (or the Build Tools).

```
dotnet build Killendar.csproj -c Release
```

`brand/` holds the working artwork and is not tracked. Everything the app consumes is generated out
of it into `Resources/`, which is tracked, so a fresh clone builds without it.

## License

GPLv3. See [LICENSE](LICENSE).
