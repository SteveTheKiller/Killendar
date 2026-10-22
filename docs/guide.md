# Using Killendar

A plain guide to what Killendar does with your calendar: what it can import, how repeats work, how reminders behave, how backups and restore work, what stays private, and the command line.

## Your calendar file

Each Killendar is one `.kcal` file. It is an ordinary SQLite database until you give it a password; then it is encrypted with SQLCipher (AES-256). You can keep as many as you like (work, family, on call), each with its own password or none. The Killendars button in the title bar lists them, opens one, renames, deletes, and restores backups.

Right-click the Killendars button to show other Killendars beside the open one. Each one shows in its own color and is read only: new appointments, changes, drags and imports always go to the open one. An encrypted one stays locked until you type its own password, which is never saved. One that is missing or cannot be read is marked as such and does not stop the others. Locking the app locks the encrypted ones again; when the open Killendar has no password of its own, Ctrl+L, the idle lock and the Windows lock still lock them. A shown Killendar can be renamed or deleted in the Killendars window while it is shown.

## Importing and exporting

**iCalendar (.ics), in and out.** Appointments (VEVENT) are imported. Tasks and notes are skipped and counted. Killendar keeps:

- title, start and end, all-day, location, description, attendees and categories
- the organizer and each attendee's reply, and whether the appointment shows as busy or free
- reminders: the first pop-up or sound alarm set some time before the start (an email alarm, or one at a fixed clock time, is kept in the file but not used as the reminder)
- time zones (TZID), including the ones Outlook writes; a zone Killendar cannot identify stops the import with a message rather than guessing a time
- repeats Killendar can draw (see below), with skipped dates and single-date edits

Properties Killendar does not edit (conference details, for example) are kept as they came and written back out on export, unless an edit made them stale.

**CSV, in and out,** uses Outlook's column names, including the reminder columns.

**Saved email invites (.eml)** can be imported.

**Web page out.** Export a month, week, day or agenda page with a key to the category colors. You choose whether locations, descriptions and attendees go on the page; descriptions and attendees are left off unless you ask for them. The page prints cleanly.

**Limits.** A file over 50 MB or with more than 100,000 appointments is refused before anything is added. Appointments already in the Killendar are skipped, not duplicated; the import preview says how many.

## Repeating appointments

Killendar draws these repeats:

- daily, every N days
- weekly on the days you tick, every N weeks
- monthly on the same date (a 31st is skipped in shorter months), or on the first, second, third, fourth or last weekday of the month
- yearly

Each can end never, after a number of times, or on a date. You can skip single dates and change single dates.

When you change or delete one date of a series, you choose **just this date**, **this and following** (the series ends the day before and the rest becomes a new series, keeping its single-date edits and skipped dates), or **the whole series**. One undo puts any of these back.

A rule from another calendar that Killendar cannot draw (for example "every weekday except the second Monday") imports as its first date only, and the import says how many did. A series that keeps another time zone's clock offers just this date or the whole series, not this and following.

## Time zones

Off until you tick **Show time zones** in the date settings (the calendar button on the rail). Then:

- an appointment can have a time zone; its times are typed on that zone's clock, and it keeps that clock when either place changes its clocks
- a repeating appointment keeps the zone it was made in
- Week and Day can show a second zone's hours beside your own

Times that happen twice when the clocks go back are read as the first one. Times the clock skips when it goes forward are read as an hour later.

## Reminders

Give an appointment a reminder in the editor, or set a default for new ones in the date settings. All-day appointments are reminded at 9:00 the day before.

When a reminder is due, Killendar shows it with **Snooze** (5, 10, 15 or 60 minutes) and **Dismiss**. If Killendar is closed to the tray, a notification says so; click it to open the window. Reminders that came due while Killendar was not running are shown when it next opens, until a day after the appointment ends. A locked Killendar says how many reminders are waiting without showing what they are.

Killendar does not send reminders to your phone or email, and it cannot remind you while it is not running.

## Undo, Recently deleted, backups and restore

- **Undo** (Ctrl+Z) takes back the last changes, one step at a time.
- **Recently deleted** keeps what you deleted for 30 days. Click one to restore it.
- **Backups.** Each day you open a Killendar, a copy is saved in a `Backups` folder beside its file, in the background so opening never waits. Killendar keeps every backup from the latest day, the newest from each of the six days before that, and the newest from each of the four weeks before those. A backup is checked after it is written. Backups of an encrypted Killendar are encrypted with the same password, and changing the password re-encrypts them (Killendar tells you if one could not be).
- **Restore.** In the Killendars window, right-click a Killendar and choose **Restore a backup**. The backup is restored as a new Killendar beside the original, so nothing is overwritten.

## Privacy

- No account, no sync and no telemetry. Your appointments never leave your computer unless you export them.
- The only connection Killendar makes on its own is a check for updates at startup. You can turn it off in About.
- **Address suggestions** in the location box use the Photon service from Komoot and are off until you turn them on in About. When on, what you type in the location box is sent to Photon.
- **Copy diagnostics** in About copies a short report for a support request: the version, Windows, your settings and how many appointments, repeats, reminders and backups there are. It contains no appointment text, names, attendee addresses, passwords, file or folder paths, or links, and Killendar sends nothing. You paste it where you choose.
- Opening a meeting link from an appointment is always your click, and only web (http and https) links open.

## Command line

```
killendar agenda <yyyy-MM-dd> <days 1..31> [--limit 1..100] [--database absolute.kcal]
killendar create <json>
killendar --help
```

- `agenda` lists the appointments from a date for 1 to 31 days, as JSON. It reads the active Killendar, or the one given with `--database`, without changing it. An encrypted Killendar cannot be read this way.
- `create` adds an appointment to the Killendar that is open and unlocked in the running app.
- Output is JSON; an optional trailing `--json` is accepted.

Scripted install, uninstall and their exit codes are in [Deployment and uninstall results](deployment.md).
