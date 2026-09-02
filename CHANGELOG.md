# Changelog

All notable changes to Killendar are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.1.1] - Unreleased

### Added
- Multi-day appointments now appear as one continuous block across the Month view (#1).
- Rolling Month view now has separate one-week and full-range navigation buttons (#11).
- Calendar-grid and appointment-sidebar density can now be adjusted independently (#10).
- The appointment sidebar remembers whether it was open across launches and F5 reloads (#12).
- Week start can follow Windows or be set independently to Sunday or Monday (#6).
- Italian localization for the complete app interface and killendar.net, bringing both to thirteen languages.
- Hungarian localization for the complete app interface and killendar.net, bringing both to twelve languages.
- Optional address suggestions for the appointment location field, powered by Photon. The network lookup is off by default and can be enabled from the About card; typed locations stay on the machine unless the user opts in.
- A crash log at %LOCALAPPDATA%\Killendar\crash.log records any unhandled error with its stack, so a report can say what actually failed.

### Changed
- The SQLCipher encryption native is now built from upstream source (SQLCipher 4.18.0, SQLite 3.53.4) and vendored in the repo, replacing the deprecated SQLitePCLRaw.lib.e_sqlcipher package.
- The appointment sidebar heading is larger.

### Fixed
- Typing an hour such as 8 or 8AM into a time box no longer crashes the app; a one-letter time pattern was being read as a standard format specifier (#14).
- Changing the start date or time now moves the end by the same amount, so the appointment keeps its length (#14).
- File-picker names now show their complete text in a hover tooltip when a column clips them.
- Week and rolling Month ranges no longer repeat a full numeric date inside their headings (#9).
- Ctrl+Shift+M now resizes the selected Month-view appointment after its sidebar opens (#13).
- AltGr characters no longer trigger Ctrl shortcuts while typing with international keyboard layouts (#8).
- Address lookup now closes an earlier suggestion list when a newer query returns no results, so stale addresses cannot appear to belong to the current text.
- Black and Dark theme buttons now use readable near-black text on their bright accent fills, and Black theme card borders match the rest of the dark-theme family.
- Self-update now keeps the Add/Remove Programs version current instead of leaving it describing the replaced build.
- On the Sepulchre and Mourning themes the selected theme picker row no longer loses its ring, dot and label while hovered; they turn white.

## [1.1.0] - 2026-08-17

1.1.0 adds seven themes, scalable interface sizing and more flexible month and sidebar layouts. It also fixes all-day editing, calendar layout, localization and machine-wide uninstall.

### Added
- Seven themes: 98SE, Ectoplasm, Decay, Mourning, Sepulchre, Delirium and Malaise. Killendar now includes all thirteen Killer Tools palettes, with six classic accent colors for 98SE.
- Interface scaling from 80% to 150% by scrolling the title-bar wordmark. Scale-aware minimum sizes keep the calendar and sidebar usable.
- A folder picker in Manage Killendars for changing the data folder, including one-click portable storage beside the executable.
- F5 reloads the open `.kcal` file from disk, including an encrypted Killendar already unlocked in the current session.
- Polish translation, bringing the interface to eleven languages.

### Changed
- Ctrl+wheel over Month view zooms between one and six visible weeks, increasing appointment capacity as the cells grow.
- The appointment sidebar can be resized down to 250 pixels and remembers its width between runs.
- The themed file picker remembers separate open and save folders, includes Explorer Quick Access, supports horizontal mouse-wheel scrolling, and is now used when loading an external Killendar.
- The theme picker now uses a stable full-height list with a separate accent strip. Menus, overlays, selection states, sidebar layout and rounded corners have also been tightened across the interface.

### Fixed
- Editing an all-day appointment no longer shows its exclusive stored boundary as the visible end date or adds another day each time the appointment is saved.
- Machine-wide uninstall now requests administrator access and correctly removes the Program Files copy, Common Start Menu shortcut and HKLM registration.
- Multi-day and ordinary appointments now share the same sorted, scrollable stack inside each Month-view day, eliminating the competing overlay lanes that caused overlaps and misalignment.
- Week view now places all-day appointments under their actual dates instead of stretching each one across the visible week.
- Calendar-generated weekday and month names now follow the selected interface language instead of the Windows display culture.
- Month-view date numbers remain readable across selected, today, hover and pressed states.
- Killendar now owns all of its theme resources and no longer depends on or loads files from a private sibling `KillerUI` folder, so a standalone clone builds with every theme intact.

## [1.0.0] - 2026-07-31

1.0.0 is the first release of Killendar: a desktop calendar for Windows with month, week, day and agenda views, iCalendar import and export, and appointments that live in a file on your own machine instead of somebody else's server.

### Added

- Month, week, day and agenda views, with one shared previous / next / today control that follows whichever view is open.
- Appointments created and edited in the sidebar, with title, start and end, all-day, location, description, attendees and categories.
- Repeating appointments: daily, weekly on the weekdays you tick, monthly on the same date, or yearly, every N of those, ending never, after a set number of times, or on a date. A series is stored as one appointment and its dates are worked out as the calendar draws them, so changing a weekly standup is one edit rather than a hundred, and a series with no end date costs nothing to keep. The weekday buttons take their letters and their order from your Windows region settings. Underneath the controls, a line shows the next few dates the pattern actually produces, generated by the same code that draws the calendar, so it cannot promise a schedule the calendar will not show.
- A repeat lands on the date you picked or not at all. A monthly appointment on the 31st simply has no occurrence in a 30-day month rather than sliding to the 30th or the 1st, and a yearly one on 29 February happens only in leap years.
- Editing or deleting one date of a repeating appointment asks which you mean, as a pair of buttons at the top of the panel rather than a dialog that interrupts you after you have finished typing. "Just this date" saves that one date on its own or removes it while the rest of the series carries on; "the whole series" changes every date, moving them all by the same amount rather than jumping the series to whichever date you happened to be looking at.
- A map button beside the location field opens whatever you typed as a maps search in your browser. It is not a lookup and there is no autocomplete: nothing is sent anywhere until you click it, so the calendar still talks to nobody while you are just typing an address.
- Color categories, defined per Killendar and stored inside the `.kcal` so they travel with it. An appointment can carry as many as you like, and the first one colors it in every view, with the title flipping to black or white so it stays readable on a pale color. On the three single-hue themes, a starter color that shares the theme's hue is shown as a same-family neighbor - lime on Greed, salmon on Blood, pale blue on Cyanotic - so a green tag does not drown in a green theme; the stored color never changes, and a color you picked yourself is always shown exactly. Appointments with no category get a chip color of their own in every theme, one no accent uses, so an untagged appointment cannot vanish into the selected or hovered day. Manage them from the sidebar rail: add, rename, recolor or delete. Renaming rewrites the category on every appointment that carries it, and deleting removes it from them without touching the appointments themselves. Six are there to start with, in the Killer Tools palette, and they are only seeded into a brand new Killendar - once you have deleted one it stays deleted.
- Categories are assigned by clicking them rather than typing, so a name can never be misspelled into a category that does not exist. A category on an appointment imported from a Killendar you do not have the definition for still shows, in neutral gray, rather than being silently dropped.
- Full color picker for category colors: saturation and hue, RGB and hex entry, an eyedropper that samples anywhere on screen, and nine swatch slots you can overwrite and reset. Picking a color previews it live on the calendar behind the dialog, so you can see how it looks against real appointments before committing; canceling puts the old color straight back.
- Right-click a day in month view, an hour in week or day view, or a heading in the agenda to add an appointment there.
- Overlapping appointments in the week and day views are laid out side by side in lanes, so a double-booked hour shows both.
- All-day appointments get their own strip above the hour grid.
- Month view shows only the weeks the month actually needs, and the first day of the week follows your Windows region settings.
- Agenda view lists the next 60 days grouped by day; clicking a day heading starts an appointment on that date.
- iCalendar (.ics) import and export, written against RFC 5545 with no external dependencies. Import skips appointments already in the calendar and reports how many were added and how many were skipped. Categories travel with an appointment through both, using the standard CATEGORIES property, so they survive a trip through another calendar app. A category you import but have never defined shows in neutral gray rather than being dropped or silently created.
- Repeating appointments survive import and export, including dates deleted from a series and dates edited on their own. A repeat rule Killendar cannot draw, such as "the third Monday of the month", is deliberately not half-imported: putting it on the wrong days would be worse than the appointment arriving once.
- Import says what it could not keep rather than swallowing it. Repeats that arrived as a single date, entries with no readable date, and tasks or journal entries that Killendar has nowhere to put are all counted and reported. An appointment that vanishes silently is worse than one that never imported, because you only find out by missing it.
- CSV import and export, in Outlook's calendar column format. Export writes one row per date, so a weekly standup appears on every Monday of the exported range and the file opens cleanly in Excel. Import reads the same columns by name, so a file with extra Outlook columns or a different column order still works, and anything else is refused with a message naming the columns it needs rather than a guess at what "Date" means. Importing the same file twice skips what is already there.
- Import a saved email invite (.eml) and the appointment inside it is pulled out and added, however deep the mail's structure buries it and however it is encoded. An email with no invite in it says so. Outlook's binary .msg is deliberately not supported.
- HTML export: a month-by-month web page carrying all six Killendar themes and the accent hues, with switchers in its corner. The page opens in whatever theme the app was in when it exported, the reader's choice is remembered by their browser, and a print stylesheet flips to ink-friendly white regardless - so printing your calendar is Ctrl+P or print-to-PDF from any browser.
- Export asks whether you mean the whole calendar or a date range, in a small two-button flyout: "Whole calendar" goes straight to the save dialog, "Date range" reveals from and to boxes and Enter proceeds. For CSV and HTML, "whole calendar" runs from your first appointment to your last, with a never-ending series capped a year out. A ranged .ics carries a repeating series whole rather than clipping its rule, because a rewritten rule that disagrees with the original is worse than a few dates beyond the window.
- The file picker's type combo drives the saved extension: picking "CSV files" is enough to get a .csv, and switching type swaps the extension on the name you typed, the way the Windows dialogs do.
- Six themes (Dark, Light, Black, Blood, Greed, Cyanotic), each switchable at runtime, with six accent hues on Dark, Light and Black. Killendar ships on the dark theme with the red accent, the same red the wordmark and killendar.net are built on.
- Appointments stored in a single `.kcal` file - a Killendar - in `%APPDATA%\Killendar`. It is an ordinary SQLite database, so it is yours to read, back up, move or open with any SQLite tool. No account, no sync, no telemetry. Times are stored in UTC, so a calendar carried to another timezone still says the right thing; all-day entries stay on the date you put them on.
- Optional encryption. The lock button in the title bar sets a password, and the Killendar is then encrypted at rest with SQLCipher (AES-256, per-page HMAC-SHA512, SQLCipher's own PBKDF2 key derivation). Encryption is opt-in: with no password the file is plain SQLite. The same button changes the password, or removes it and stores the file unencrypted again. An encrypted Killendar asks for its password on launch; a wrong one just asks again.
- There is no password recovery, and the app says so before you commit to one. If a password is lost, the locked Killendar is renamed aside and kept on disk rather than destroyed, and a new empty one opens in its place.
- Exporting an encrypted Killendar warns you first that the file it writes - `.ics`, CSV or HTML alike - is plain text. It never blocks the export - handing someone an `.ics` is what the feature is for - and a "Don't remind me again" box turns the warning off for good. Plaintext Killendars never see it, and ticking the box then canceling means "not this time", not "never again".
- Keep as many Killendars as you like - one for work, one for on-call, one for the family. The Killendars button in the title bar lists every `.kcal` in your data folder with its size, when it changed and whether it is encrypted, and lets you create, rename, delete, load one from a file, or switch to it. Double-click a Killendar to open it; renaming is inline, from the right-click menu or F2. Each file has its own password, or none. The name of the open Killendar shows in the title bar once you have more than one.
- Double-click a `.kcal` and Killendar opens it. A copy is added to your data folder and opened; the file you double-clicked is left where it is, because a Killendar is written to constantly and that is not something to do to a file on a network share or in Downloads. The association is registered for your user only, needs no elevation, and is removed when you uninstall.
- Only one Killendar runs at a time. A second launch hands its file to the window already open and exits, rather than leaving two copies of the app writing to the same calendar.
- Window size, position, theme and accent are remembered between runs.
- About card showing the version and release date, the code-signing publisher, the certificate thumbprint and the running exe's SHA-256, plus a GitHub update check with one-click self-update.
- The publisher panel validates the Authenticode signature with WinVerifyTrust rather than only reading the embedded certificate, so a tampered build cannot display a signer it does not own. The chain check is cache-only, so opening About never stalls waiting on the network.
- Runs portable or installs per-user or for all machines, including a silent install path for winget, Chocolatey and RMM deployment. Uninstalling leaves your appointments in place. A portable copy says so in the footer and offers to install itself, with a desktop shortcut and an all-users option; an installed copy never shows it.
- Ten interface languages (English, Bengali, Czech, German, Spanish, French, Japanese, Turkish, and Simplified and Traditional Chinese), switchable from the sidebar rail without restarting.
- Keyboard shortcuts for everything: single keys for new, today, paging and switching views, and Ctrl+I / Ctrl+E for import and export. Single keys stand down while you are typing in a field.
- Shortcuts overlay on F1, from the `?` on the sidebar rail, in two views you switch between: a grouped list, and a drawn keyboard that lights the keys that do something, colored by what they do and with a Ctrl layer. Which view you prefer is remembered. Both views and the shortcut list on killendar.net are generated from a single binding table, so they cannot drift apart as bindings change.
- F1 opens the shortcuts overlay and F12 opens the About card, matching every other Killer Tools app.
- Sidebar icon rail with a collapse chevron, category manager, grid density, shortcuts, language picker and theme picker, matching the rest of the Killer Tools family.
- A 5-day work week toggle in Week view's own header corner, left of the first day: it drops Saturday and Sunday and runs Monday to Friday whatever day your region starts its week on, giving every remaining day almost half again the width. In the week grid, appointments no longer spend that width on a time prefix - the box's position on the grid already says when, the tooltip has the exact times, and a short appointment's text is no longer clipped by its own padding.
- Drag an appointment to move it. In Week and Day view it follows the pointer to another time or another day, drops on the same snap a click gets, and keeps its duration; in Month view it moves to another date and keeps its clock, and an all-day run keeps its length. Dragging one date of a repeating appointment moves just that date - the series itself is never reshaped by a drag. To reschedule the whole series, right-click any of its dates and choose "Edit the series", which opens the series itself in the editor.
- Day cells and hour slots respond to the pointer: the day under the cursor lights up, a click acknowledges before the sidebar opens, and in week and day view a band tracks the exact half hour you are about to land on. The day the sidebar is editing stays marked while you type, and follows the date box, so correcting a date moves the marker with it.
- Grid density for the week and day views, from the rail button or Ctrl and the mouse wheel over the grid. Four steps, from hour lines only to quarter hours; each step also makes the hours taller, and a click snaps to whatever the grid is actually showing, so it can never offer a line you cannot land on. The same knob drives the sidebar's day list, read as detail per row: first the title wraps instead of trimming, then the location shows under it, then the description and attendees - so at the top step everything the hover tooltip says is on the row itself.
- The toolbar sheds rather than clips. As the window narrows, the view buttons, then export, import, today and new fold into an overflow menu, in that order. Previous, next and the date you are looking at never leave.
- Opening the appointment sidebar widens the window rather than squeezing the calendar, and closing it hands the width back. The calendar keeps the same width throughout instead of stretching while the panel slides.
- Date format picker in the language menu: follow Windows, ISO, US or UK/EU. The field hint is derived from the format actually in force, so it can never suggest something the app would then reject, and switching format reformats an open editor rather than leaving two conventions on screen.
- Typing a date is forgiving: the chosen format is tried first, then ISO, then several common regional forms, so a date that looks reasonable is accepted whatever the setting says.
