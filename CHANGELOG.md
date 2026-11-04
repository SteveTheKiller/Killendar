# Killendar Changelog

All notable changes to Killendar are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.4.1] - 2026-11-04

Killendar 1.4.1 closes security gaps in the command pipes and the updater, saves dates the same way on every Windows region, stops unsaved appointments from being lost without asking, fixes import, export, drag and keyboard problems, makes install, update and uninstall recover cleanly, and moves the look and controls onto the shared KillerUI template.

### Security

- The calendar command pipe and the single-instance pipe accept only your own account, so another person signed in to the same PC cannot receive appointment details or stall Killendar by taking the pipe name first.
- Update and uninstall helpers load PowerShell modules only from Windows' own folder, so a module planted in your Documents cannot run inside them as administrator.
- The elevated part of an update compiles its helper code in a folder only administrators can open, so another program running as you cannot slip code into it.
- An update must be signed with the exact publisher name and pass a certificate revocation check. Only an unreachable revocation server falls back to an offline check. Update metadata downloads are capped at 1 MB.

### Fixed

- The file picker resolves place and drive icons in the background and restores its position on any monitor. Scrollbar buttons have localized screen-reader names.
- Appointments are saved in a fixed date format whatever the Windows region. On a Thai or Arabic region they were saved centuries off, and on a region that writes times with dots they could not be read back; those dotted times now load too, including dates deleted from a repeating appointment, and reminders dismissed before the upgrade stay dismissed.
- Exported .ics files carry Gregorian dates whatever the Windows region, and a description with several lines comes back whole when the file is imported again instead of stopping at the first line break.
- An unsaved appointment is no longer lost without asking. Clicking a reminder, opening Manage Killendars, Update now and the Install badge all ask first, Save in that prompt actually saves, closing a locked Killendar offers to discard rather than a Save that could not work, and renaming the open Killendar more than once in Manage Killendars keeps the appointment you were writing.
- Editing an appointment from the day list after dragging it keeps the new time. The list now refreshes when appointments change, and Edit used to load the old copy and put the appointment back where it was.
- An encrypted Killendar locks on idle or with Windows even while Manage Killendars is open. It used to stay unlocked behind the dialog and reopen without asking for the password. While locked, the dialog can no longer delete or rename the locked Killendar, and a password change that went through but could not reopen the file now locks it and asks for the new password.
- A failed backup re-encryption keeps the original backup instead of deleting it, a password change that went through is no longer reported as failed when reopening the file stumbles, the backups of a Killendar set aside after a forgotten password stay with it, and an import that is waiting to write holds off a lock instead of losing its calendar mid-way. When the file cannot be reopened after a password change, the previous copy is kept and its location is shown.
- The month view no longer crashes when the open Killendar and a shown one hold the same appointment spanning several days.
- A calendar no longer fails to open because another account holds its native library lock.
- Installing for all users verifies and copies the very same file, so nothing can swap the program between the check and the copy, and installing an older copy over a newer all-users install asks first.
- Uninstalling while Killendar is still open, for example in the tray, now waits up to 30 seconds for it to close and otherwise stops before removing anything. It used to take away the shortcuts and file links, then fail on the running program and leave the rest behind. Uninstall also removes leftover update files, its own helper copy, the old copy that an install over a running Killendar leaves behind and the empty .kcal registry key.
- A per-user or portable update that fails now reopens Killendar instead of leaving it closed, including when antivirus or a locked-down PowerShell stops the update helper from loading, a published copy that fails its final check is swapped back for the previous version and never started, and update downloads stream to disk with a size cap and an overall deadline.
- An all-users update reopens Killendar for the signed-in user at normal rights, whether it finishes or stops early. Windows was refusing that relaunch, so the app stayed closed. Files left behind by earlier failed updates are cleared when the next update is staged.
- A failed uninstall reports the real reason and retries when a copy is still running, a stalled update says so instead of silently doing nothing, and the window no longer freezes while an update is handed off.
- An update that finished is no longer reported as failed when the app cannot be reopened afterward.
- Updates keep working after the publisher's code signing certificate is renewed. A release must still carry a valid signature naming the same publisher, but no longer the exact certificate of the installed copy.
- Self-update runs on the family update code, which keeps Killendar's signature checks inside the helper and adds pinned publication folders, a publisher check on the published copy, same-publisher replacement and stable-only releases. Installed paths, registry keys and the exe name are unchanged.
- An update that cannot finish because the app did not close says so in its recovery report, the Update button stays off once an update has started, and a translation file can no longer give a setting the wrong kind of value.
- A locked Killendar no longer leaves you stuck with its password prompt. Canceling the prompt keeps it locked instead of quitting, the Killendars button can then open or create a different Killendar, and the padlock or any key brings the prompt back. After a cancel, Tab, Space and Enter reach the title bar buttons, Alt+F4 and Alt+Space work, and a calendar file opened while locked imports once you unlock instead of being dropped.
- Importing an .ics file with a huge list of extra or skipped dates, or hundreds of unknown time zones, stays quick and can be canceled. CREATED is no longer overwritten by the file's export time, an appointment cut off at the end of a file is counted as unreadable, a weekly repeat whose days leave out its first date is reported, and a timed exclusion on an all-day repeat keeps that day skipped.
- Dragging an appointment moves it by the distance dragged. Grabbing the second day of an overnight or multi-day appointment, or a long bar in the month view away from its first day, used to throw its start by hours or days.
- Category chips in the appointment panel can be reached and toggled from the keyboard and are named for screen readers, Alt+Left moves an all-day appointment back a day, and filling an appointment from a template no longer starts a location lookup on its own.
- A date-range CSV or HTML export over an endless repeat stops at the same limit as a whole-calendar export instead of freezing, time zone definitions in exported .ics files stay small, the slash date styles print slashes on regions that use dots, and an email whose invite is attached as an .ics file imports.
- The command line explains a bad option on stderr and exits with code 2 instead of exiting silently, and `killendar help agenda` shows help for the agenda command instead of an error.
- The agenda command and KillerMCP's agenda list a range day by day, so a limit drops the last days instead of the first appointments when all-day items fall later in the range, and repeats that move off days off follow your saved work week and holiday calendar there too.
- Hiding shortcut hints removes every hint, including single keys such as (T) and translated ones such as (Strg+D).
- The file picker no longer stalls on a disconnected network folder when it opens, a relative path typed in the path box is refused instead of being looked up against the wrong folder, and a full path typed in the name box can be accepted even when the shown folder failed to list.
- File dialogs stay responsive on slow or crowded folders, cancel stale loads and identify incomplete listings.
- The file picker's Accept button can no longer fire twice: it stays off while a folder is loading or an accept is already running. A mistyped folder path now returns to the last folder that worked instead of leaving an empty list, and a malformed typed path says to enter a valid file name or path instead of showing an internal message name, even while a folder is still loading. Changing the file type with such a name typed no longer crashes the picker.
- A finished all-users uninstall clears the files earlier failed attempts kept, so they no longer hold the install folder open after the restart.
- The file picker's recent locations and places lists are named for screen readers, and dragging the reminders or search window by its title bar can no longer crash after a quick tap.
- Keyboard focus cues respect the theme, Windows contrast refreshes are coalesced and restore frame colors, and picker selections keep text and icons shadow-free.
- Dialog title bars match the rest of the dialog in every theme except 98SE. They had their own band, a lighter strip in Light and a gradient in several dark themes, and the title bar painted its film grain twice.
- The tray icon's menu opens from the pointer upward, out of the notification area, the same way in every Killer app. A short menu no longer covers the tray icons and a taller one no longer floats about 40 pixels above them.
- Black table headers match the content surface, and softer grain keeps dark surfaces from looking gray.
- Opening one date of a repeating series no longer shows repeat settings that would be ignored on save.
- An all-day appointment whose end date is before its start date is refused with a message instead of being saved as one day.
- Agenda Previous and Next step by the chosen 7, 14 or 30 day range instead of skipping days.
- Week and Day mark today with an accent edge when another day is selected, matching Month.
- Screen readers announce untitled appointments as untitled instead of starting with a comma.
- Releasing a dragged appointment outside the calendar, or pressing Escape mid-drag, cancels the move instead of saving it at the nearest edge.
- A Month appointment stays visible while you drag it to another day.
- Opening a calendar while Killendar is closing no longer silently does nothing and reports success.
- After a machine-wide install, double-clicking a .kcal file opens the installed copy instead of an old portable copy.
- Installing for all users adds the desktop shortcut when Add a desktop shortcut is ticked.
- The window no longer freezes while an all-users install runs.
- Removing the second copy when Killendar is installed twice no longer leaves an orphaned program behind when that copy is running; you are told it is in use.
- Exporting to .ics no longer fails when the calendar holds a repeating appointment from Brazil, Chile, Argentina, Egypt, Iran, Pakistan or another zone whose clock changes fall at the end of a day. A repeat whose zone still cannot be written goes out in UTC, and the status line says how many did.
- Monthly-by-date and yearly repeats from Outlook, such as birthdays, import as repeats instead of a single appointment.
- A repeating appointment whose end date is given without a time imports as a repeat.
- A canceled day of an all-day repeating event from Outlook is removed from the series on import, and an extra date given with a time on an all-day repeat keeps its own date instead of moving a day early west of UTC.
- An imported appointment with a malformed reminder no longer makes later .ics exports invalid.
- A calendar file with an oversized time zone definition no longer stalls import, and Cancel stops it.
- CSV files exported by Outlook in the Windows code page keep their accented characters on import.
- A quick tap or click on the title bar of Manage Categories, Manage Killendars or the password prompt no longer closes Killendar.
- Delete and F2 inside the category rename box edit the name instead of opening the delete prompt or a second rename box.
- Escape on an open Reminder or Time zone list closes the list and keeps the appointment you are writing.
- Letter shortcuts no longer fire while a dropdown or check box in the appointment editor has focus.
- F12 while the keyboard map is open replaces it with About instead of stacking both.
- The density button's tooltip switches language with the rest of the app.
- The app size readout in the status bar is translated and no longer overwrites a newer status message.
- The Search button's tooltip shows its Ctrl+F shortcut.
- The keyboard map lights all four arrow keys for moving an appointment with Alt.
- The title-bar menu opens at the pointer instead of slightly below and to the right.
- Opening the appointment panel no longer pulls a narrow snapped window out of its snap position.
- Locking an encrypted Killendar shown beside the open one removes its appointments from the sidebar day list right away.
- When a backup fails, the status line gives the real reason, such as a full disk, instead of a misleading database message.
- A calendar that another program is holding open reports why it could not be opened instead of asking for a password it does not have.
- Renaming the open Killendar or changing the data folder while the settings file cannot be saved is undone and reported, instead of opening an empty calendar on the next launch. Deleting the open Killendar in that state says so too.
- If the safety backup taken before a password change or an import fails, the status line says so.
- The install, uninstall and installation conflict messages and their buttons follow the chosen language, including the uninstall prompt opened from Windows Settings, and the install prompt's three points sit on their own lines again.
- A click anywhere in a Month day opens it, not only near the date number.
- The toolbar no longer leaves its dividers behind beside New once the file buttons move into the overflow menu.

### Changed

- Themes, palettes and control styles now come from the shared KillerUI template, so buttons, caption buttons, menus and the 98SE frame look the same as in the other Killer apps.
- Accent buttons such as Save and OK rest with an accent outline, as in the other Killer apps, and the About card, the window menu and the signature check share the KillerUI template code.
- Confirm prompts use the family dialog frame shared with the other Killer apps, and their Cancel button follows the chosen language. The window chrome, toolbar, app size, tray, folder tree and update notices now run on the KillerUI template code with Killendar's own behavior kept.
- Menus opened from the rail now sit level with their own button, while the theme and language menus stay in the corner of the calendar pane. The accent dots in the theme menu can be reached with Tab and the arrow keys and are named for screen readers.
- Dates, times, month and day names and numbers follow the Windows region format instead of the interface language, as in the other Killer apps; the date format setting still applies, and its ISO, US and EU styles and the suggested export file name stay Gregorian under any region calendar.
- Toolbar captions are larger and set in the family sans, captions under the icons wrap instead of running wide, and the toolbar right-click menu stays open so display modes can be compared.
- The open, save and folder picker now uses the family file picker card and frame shared with the other Killer apps, and pictures keep their plain file icons.
- The footer's Install button runs on the shared KillerUI template code, with Killendar's all-users choice and calendar handling kept the same.
- The footer status text sits 16 pixels from the window edge, as in KillerNotes and KillerPDF, instead of 4. 98SE keeps its spacing.
- Week and Day keep their scroll position when you switch views.

### Added

- Translators can start Killendar with `--lang-file <file>` to try a translation file in the running app; it reloads on every save.
- About groups settings in two columns and can hide shortcut hints in menus and tooltips without disabling keyboard shortcuts. Hover tooltips clear large pointers while preserving custom placement.
- Alt+drag moves any Killendar window from anywhere inside it.
- When Killendar is already installed for all users, the install prompt ticks and locks the all-users box and says it will update that copy.
- The color picker and the new-category color swatch can be used entirely from the keyboard and are named for screen readers.
- The version line in About can be reached with Tab and opens the release notes with Enter.

## [1.4.0] - 2026-10-22

Killendar 1.4.0 adds reminders, undo, search, verified backups, holidays and time zones, and makes repeats and calendar files match other calendar programs.

### Fixed

- Failed password changes and removals keep the original calendar and its recovery copies, can be retried, and say what was left behind. A password change that worked no longer reports failure when the old copy cannot be deleted.
- Failed exports leave an existing calendar, CSV or HTML file untouched instead of truncating it.
- Failed calendar migrations, category changes and deletions of edited repeats keep what was there: the source calendar, the typed name and saved color, and existing edits and assignments. An interrupted settings save recovers the selected calendar location.
- Unsaved appointment input is no longer replaced silently. Starting a new appointment, opening another one or a day, switching or reloading the Killendar, hiding to the tray or quitting asks first and offers Save; Cancel, Esc and closing the panel still discard on purpose.
- Large imports no longer freeze the window. They run in the background with a Cancel card for anything slower than half a second, save about twice as fast, and add nothing when canceled. An import that would skip, lose or duplicate something shows what and asks first, and files over 50 MB or 100,000 appointments, or emails nested more than 10 levels deep, are refused with a clear message.
- Imported meetings keep their UID, organizer, attendee answers, reminders, free/busy setting, privacy, meeting link and revision when exported, so other calendars recognize them instead of adding duplicates or resetting every attendee to "needs action". Details Killendar cannot keep are counted in the import message. Canceled meetings are no longer imported as ordinary appointments, and a canceled date of a series removes that date.
- Repeating appointments keep their meaning through calendar files: time zones, wall-clock times, lengths across clock changes, week starts, counts, multi-day overlaps and exact exceptions all survive import and export. Exceptions no longer duplicate on reimport, whole-calendar exports include future and long series, and nonexistent times and invalid intervals are skipped or refused.
- Calendar file and CSV imports keep valid rows when another row is unsupported, and keep escaped text, the invite's character encoding, distinct CSV rows, and attendee addresses and time zones whose quoted names contain a colon, comma or semicolon. CSV rows with an unreadable time or an end before the start are skipped and counted instead of being guessed, and a blank end takes the default length.
- Exported calendar files write lengths in the iCalendar form (for example PT1H30M) that stricter calendar programs require. Files from earlier versions still import.
- An imported appointment that is moved and made to repeat in the same edit repeats from its new time, not the original one.
- Times in the calendar, agenda, day panel, editor and HTML export follow the chosen date format and the language's clock instead of always showing US month/day and 12-hour times, and changing the language or date format no longer shifts an appointment's dates, times, length or repeat end.
- Self-updates reject unsigned or invalid images, ask for administrator access through the signed Killendar, run their helper without a script in the temp folder, and keep complete recovery copies when an install or update fails.
- Installing waits for the previous Killendar to exit, and a silent install exits with 1618 so deployment tools retry. Installation cleanup and uninstall keep calendars and other files stored beside the app. Uninstall removes shortcuts, the command path and file types before the program, so a failed step leaves a working uninstall entry; an all-users uninstall waits for its confirmation and finishes cleaning up at the next restart. Uninstall and Chocolatey report canceled, failed or pending removal.
- Older calendars open without changing their files, a failed open leaves no stale appointments, and switching Killendars no longer reports "Opened" when nothing opened.
- Date limits and malformed paths no longer crash the editor, repeats or file pickers. Stalled or oversized command-line requests are refused without blocking the next one, forwarded file launches keep the requested path, and the native database cache is repaired safely when several copies start at once.
- In Week view, an appointment dropped near the left edge of a day no longer lands on the day before.
- Empty days, Week and Day column headers, agenda day headings and empty time slots work from the keyboard and screen readers. Tab reaches them, arrow keys move between days and slots, and Enter opens a day or creates at a slot.
- Screen readers name the color picker's Red, Green, Blue and Hex fields, the repeat interval with its unit, and the Attendees box. Date pickers, calendar names and address suggestions work from the keyboard, modal cards block background shortcuts, a closed appointment panel leaves no hidden controls enabled, and changing the language refreshes agenda headings and the repeat controls.
- Gray hint and footer text in Light, the version number, the close X on every title bar, and the Dark theme's red accent text and hovered buttons meet the contrast guidelines.
- In 98SE, long paths in the file dialog no longer run under the recent-locations arrow, and menu highlights follow the selected accent.
- Printed pages start on the first sheet and keep weeks and appointments whole instead of splitting them across pages.
- Window resizing respects small and scaled monitor work areas.

### Changed

- Month and Week draw much faster on big calendars, and holidays no longer slow them down.
- Light is shades of gray with nothing white: the background, sidebar, footer and title bar are the darkest gray (#cccccc), menus and bars a step lighter, then panes, then fields.
- Updated SQLCipher to 4.19.0, SQLite and JSON processing to 10.0.13, and SQLitePCLRaw to 3.0.5.
- The toolbar never cuts off the date: Export, Import, Deleted and Search go to the overflow menu first, then the view buttons drop their words, then the view buttons, Today and New. Dialogs are edged in the app border color, and the date settings flyout lays its groups out in three columns.
- Hovered menu and list items show their text in the theme accent, lightened or darkened only as far as needed to stay readable on the hover fill, and theme menu hover rows reach the right edge.
- Wordmarks and headings are raised with a subtle bevel in their own color instead of a dark outline. The Dark theme's red accent is the brand red again instead of a pink tint, Dark grid lines and pane borders are a little lighter, pane borders are softer in Blood, Greed and Cyanotic and stronger in Black, and hovering a day no longer shifts its film grain.
- Date headings are larger, and calendar-list contrast, grain, embedded fonts and file-picker colors are improved. Dialogs show app icons, with full bevels and compact fields in 98SE. Sidebar fades leave scrollbars clear, and the shortcuts card wraps long labels and shows overflow.
- HTML calendars support all themes and accents, accessible selection controls, saved preferences and clearer accent-colored tables.
- Grid density has one button, on the rail. Its right-click menu sets how much of each appointment the day panel shows, replacing the second density button beside the day heading.
- The lock button's menu opens in the content pane's top-right corner, mirroring the rail flyouts.
- The website shows twelve new screenshots with captions in every language, explains per-appointment time zones and what calendar files carry, and lists the new shortcuts. Its film grain is lighter, matching KillerTools, with a switch in the theme menu (Alt+Shift+G). The screenshot viewer keeps keyboard focus while open, the More button is named in every language, the top bar no longer leaves a gap on narrower windows, and the page no longer claims import and export have no external dependencies.

### Added

- Added reminders: none, at the start, 5, 10, 15 or 30 minutes, 1 hour or 1 day before, and 9:00 the day before for all-day appointments. A reminders window in the corner of the screen offers Snooze (5, 10, 15 or 60 minutes) and Dismiss, reminders missed while Killendar was closed show at the next launch, and a locked calendar shows only how many are due. With Killendar in the tray, the tray icon also shows a notification, which for an encrypted Killendar gives only the count. New appointments use the default reminder from the date settings menu (none until chosen). Calendar files and CSV files carry reminders both ways.
- Added undo and redo (Ctrl+Z, Ctrl+Y) for adding, editing, moving and deleting appointments and for imports, up to the last 50 changes.
- Added Recently deleted: a toolbar list of appointments and series deleted in the last 30 days, kept inside the calendar file (encrypted with it) and restored with a click.
- Added verified backups: each calendar is copied to a Backups folder beside it on the first open of each day and before imports and password changes, reopened and checked table by table, encrypted with the calendar's own password, and kept for 7 days and 4 weeks. Manage Killendars shows the last backup and restores one as a separate calendar. Deleting a calendar deletes its backups. The daily backup runs beside the app, so a large calendar opens without waiting for it.
- Added locking for encrypted Killendars: Ctrl+L or Lock now on the lock button's menu hides the calendar and asks for the password again, keeping any unsaved appointment input. A locked calendar keeps nothing readable in memory, and open search results close with it. The same menu can lock after 5, 15 or 30 idle minutes or when Windows locks; both are off by default. KillerMCP requests are refused while locked.
- Added search (Ctrl+F or the toolbar's Search button) across titles, locations, descriptions, attendees and categories, ignoring case and accents, with Any time, Upcoming and Past filters. It reads the open calendar in memory and writes no index. Typing a date in the same box, or pressing Ctrl+G, goes to that date.
- Added monthly repeats on a weekday: the first to fourth, or the last, Tuesday (or whichever day the appointment starts on) of each month. Calendar files with these rules, including Outlook's form, import as repeats instead of single appointments, and export the same way.
- Added "This and following" when changing or deleting one date of a repeating appointment. The series ends the day before, and the change goes to that date and every later one as a new series that keeps their single-date edits and skipped dates. One undo puts it back, and a split that fails changes nothing.
- Added extra dates to repeating appointments: "Also on" in the repeat section adds dates outside the pattern, at the series' time of day. Calendar files carry them both ways (RDATE), and a file's extra dates no longer import as a single date.
- Added a holiday calendar: in the date settings, choose a Killendar (one imported from a holiday .ics file, say) and its all-day appointments become holidays, shaded like days off and named on their day in Month and Week. Nothing is built in or downloaded. Repeats can move off days off: "On a day off" in the repeat section keeps the date, or moves it to the next workday or the workday before, past weekends you do not work and holidays. Weekly repeats also get an "Every workday" shortcut. Exported calendar files show each moved date where Killendar does, two years ahead.
- Added time zones, off until "Show time zones" is ticked in the date settings. An appointment can then be given a time zone, its times are typed on that clock and it keeps that clock when either place changes its clocks, and Week and Day can show a second zone's hours beside this computer's. Calendar files that define their own time zone (Outlook's "Customized Time Zone", for one) import instead of being refused, placing each appointment at the right moment and counting any repeat that keeps its first date's times.
- Importing a newer invite for a meeting already in the Killendar updates it instead of skipping it, keeping the categories and reminder chosen here; an older or identical one is still skipped. A cancellation removes the meeting (to Recently deleted) or the canceled date of a series. The import asks first whenever it would change or remove something, and one undo puts it all back.
- Added showing other Killendars beside the open one: right-click the Killendars button and tick them. Each shows in its own color in Month, Week and Day and is read only, so new appointments and changes go to the open one. An encrypted one stays locked until its own password is typed, one that cannot be read is marked and does not stop the rest, and locking the app locks them too.
- Added work days and hours to the date settings menu. The work week runs from the first workday to the last, so Sunday to Thursday works too, and days off and hours outside the working day are shaded in Week and Day; Month shades days off instead of always Saturday and Sunday.
- Added meeting details to the appointment panel for imported invitations: the organizer, each attendee's reply, whether the organizer marked it private, and a Join meeting button for its web link that shows where it goes and opens only http and https links, only when clicked. Any appointment can be shown as free instead of busy, which other calendars read from the exported file.
- Added Duplicate (Ctrl+D) to the appointment panel: the copy is a new appointment, without the original's imported identity or organizer details. Added templates: save an appointment's details (not its attendees or date) under its title from the panel's Templates button, and start one from that button or the New button's right-click. Alt+arrow keys move the appointment being edited by 15 minutes or a day. Saving an appointment that overlaps a busy one says so without stopping the save.
- The web page export prints Month, Week, Day or Agenda layouts, each appointment in its category color with a key at the top. The export menu chooses the layout and whether locations, descriptions and attendees go on the page; descriptions and attendees stay off unless chosen.
- Added a time format choice (Follow Windows, 12-hour or 24-hour) and an agenda length (7, 14, 30, 60, 90, 180 or 365 days) to the date settings menu.
- Added Slovenian across the app, installation prompts, website and technical help.
- Added Copy diagnostics in About: a short report for a support request with the version, Windows, settings and counts, and no appointment content, names, addresses, paths or links. Nothing is sent.
- Added a plain-language guide to importing, repeats, time zones, reminders, backups and restore, privacy and the command line.

## [1.3.1] - 2026-10-19

### Added

- Added a right-hand position for the appointment panel and its rail, picked from the rail's right-click menu or with Alt+Shift+Left and Alt+Shift+Right. The arrow of the side the panel is already on opens or closes it.
- Rail buttons can be dragged into any order, which is remembered. Theme, language and shortcuts stay together at the bottom, with date settings moved out of that group and the chevron at the top. Removed the option to swap those ends.
- Installed copies add their folder to PATH, so commands such as `killendar agenda` run from any new terminal. `killendar --help` lists commands, and `--cli` stays compatible. Portable copies run from their folder or full path. Uninstalling removes the installed PATH entry.
- Added a dropdown arrow beside the Month button that switches Month view between a calendar month and rolling weeks, and the R key to toggle it. The choice was only reachable by right-clicking the Month button.
- Killendar checks for updates on startup and offers a newer release in a dialog with an Always check on startup box. A Check for updates on startup checkbox at the top of the About options turns the check back on.
- Added Start with Windows to the About card. It starts Killendar minimized on the taskbar when you sign in, and stays off until you turn it on.
- Added Close to system tray to the About card. With it on, closing the window keeps Killendar running in the notification area, where its icon brings the window back or exits; it stays off until you turn it on.
- Alt+Shift+G turns Film Grain on or off. It is listed in the shortcuts overlay, and the keyboard map has a new Alt+Shift layer for it.
- The website's top bar has a Microsoft Store button on every page, and the MCP page has a Microsoft Store button for KillerMCP.
- The website's About page lists KillerBench and KillerShot under Also try, with larger app icons.

### Changed

- Film grain is softer and shows on every theme, Black included. Its specks come in many shades, and some lean toward a color opposite the theme accent, such as purple on green and orange-brown on blue and red.
- Rail buttons and the collapse arrow sit centered across the rail on either side, with the same space toward the panel and the edge, and the arrow is larger. The side choices in the rail's right-click menu show a picture of each position.
- The period header above the calendar, the sidebar date heading and the dialog headings use Lacquer, the wordmark's app-name face, in place of Killer Sans. 98SE keeps Killer Sans.
- Context menus use Killer Sans UI like the rest of the interface instead of Segoe UI.
- The Light theme draws a softer shadow under the toolbars.
- Toolbar buttons with text under the icon are 6 pixels shorter, without the extra space above and below the icon and label.
- Clicking a density button steps one level tighter and wraps back to the roomiest, the same way the density buttons in KillerNotes and KillerShell work.
- The website's top bar Download button uses the same font and height as the other top bar buttons.
- The keyboard focus outline is a thin one-pixel dotted ring in the theme accent instead of a thick dotted line, and uses the text color where the accent would be too faint to see.
- Primary, outline and secondary buttons sit raised on a soft shadow that lifts on hover and sets down when pressed. 98SE keeps its flat beveled buttons.

### Fixed

- ICS export now folds Unicode text at UTF-8 byte boundaries without breaking emoji or exceeding the line limit.
- An oversized or invalid ICS duration now skips that event and leaves the rest of the import intact.
- ICS duration applies correctly whether it appears before or after the event start.
- ICS imports now convert named time zones, including daylight time, and report unknown zone IDs instead of treating them as local time.
- ICS imports now link recurring exceptions to their series when the UID is text rather than a GUID.
- Failed appointment writes now restore the calendar from disk and keep the editor open with an error instead of reporting a save or deletion that did not happen.
- Windows contrast themes now use system colors, readable icons, complete month grid lines and clear hour rules.
- The window opens fully on screen on a small or high-scaling display, or when its saved size came from a bigger monitor, instead of running off the edges, and changing the app size no longer pulls a window on another monitor back to the main one.
- The command line accepts an optional trailing --json while keeping its existing JSON output.
- Checked check boxes take the chosen accent on Dark, Light and Black instead of staying green.
- On the Light theme's Yellow and Orange accents, buttons, links, tabs and accent icons keep their color with a thin gray outline around accent text and icons, instead of turning near-black or brown.
- Gray, menu and accent text reads at 4.5:1 contrast or better in every theme and accent except the Light theme's outlined Yellow and Orange, and the footer's version and copyright are slightly dimmed.
- The Dark theme's Red accent stays a true red on buttons, lines, scroll bars and the website's accent swatches, and only small red text uses a lighter red.
- The website's text and accent colors read at 4.5:1 contrast or better in every theme except the Light theme's outlined Yellow and Orange accents, and a narrow top bar shrinks the Microsoft Store and MCP buttons to their logos and moves links into a More menu instead of wrapping.
- On 98SE the title bar text beside the wordmark is readable on the green title bar.
- On the website's Light theme with the Yellow or Orange accent, links, headings and labels keep their color with a thin gray outline instead of turning olive or brown.
- The exe's file properties show Steve the Killer as the copyright holder instead of leaving it blank.
- Turning off Film Grain removes the grain from the calendar pane too.
- Screen readers no longer read toolbar, menu and title bar icons as unreadable symbols, and the appointment fields, the Repeats button, each calendar view, each day in Month view and each part of the scroll bars now have names. File-picker rows announce folder and file names.
- Opening the shortcuts or About card moves keyboard focus into it, Tab stays inside the card, and closing it returns focus to where it was.
- Tab no longer stops on hidden links, which screen readers announced as the window.
- Screen readers and accessibility checkers no longer report empty text or the hidden parts of scroll bars as items on screen.
- Screen readers name the start and end date and time boxes and their date buttons in the appointment editor instead of reading the date format.
- In Month view, screen readers tell apart the weekly bars of an appointment that spans weeks by the day each bar starts.
- Appointments read out their title, time and location to screen readers, open with Enter or Space, and show the focus ring instead of a dotted box.
- Tab no longer stops on the calendar pane itself, which drew a dotted outline around the whole view.
- The About card says Killendar has no telemetry instead of claiming nothing phones home, since it now checks for updates at startup.
- The About card's update notice and update dialog are translated instead of always showing English.
- Toolbar menus open leftward under their button so they stay inside the window instead of running off its right edge.
- Corrected the Hungarian name of the rolling weeks month mode.
- The file picker's close button turns red on hover without filling a red block, like the other dialogs, and keeps the classic caption button on 98SE.
- The website top bar no longer leaves extra space under the wordmark and buttons.
- Theme and language radio buttons and the Film Grain checkbox line up with their labels again.
- On the website, text on accent-colored buttons, tabs and highlights switches to white or near-black for contrast, so it is never white on yellow.
- Theme names, the date format choices, date hints, file type lists and the Hungarian event fields that were still in English are translated in every language.
- The website's MCP page, release details, contact cards, carousel controls, keyboard map and remaining labels are translated in every language.
- On phones, the website's logo and download hash fit the screen instead of making the home page scroll sideways.
- The calendar name in the title bar sits on the baseline of The in the wordmark instead of 2px below it.

## [1.3.0] - 2026-10-14

### Added

- Added a Film Grain switch to the theme menu. It is on by default, and turning it off removes the grain texture from every surface.
- Added the Windows 11 snap layouts menu to the maximize button.

### Changed

- The wordmarks use Punktype and Lacquer, and the interface text, headings and footers use Killer Sans, in place of the typewriter font in the app and on the website.
- Confirm, install and uninstall dialogs use the family setup dialog layout, with a notice icon, a larger Killer Sans heading and evenly sized buttons.
- The Malaise theme's calendar pane is a shade darker than the window around it, in the app and on the website.

### Fixed

- Silent installs return specific Windows error codes and reject a second concurrent install instead of reporting a general failure.
- A silent install started without admin rights, as the Microsoft Store runs it, installs for the current user with no prompt instead of failing.
- Chocolatey uninstalls run the silent uninstaller and fail with its exit code instead of opening the uninstall prompt.
- Icon-only buttons give screen readers the name from their tooltip, including tooltips with formatted text, and controls reached with the keyboard show a dotted focus outline, drawn just outside small icon buttons so it no longer covers the icon.
- Kept app icons sharp at fractional display DPI and app zoom by caching exact pixel sizes from a covering source and aligning the final draw to device pixels.
- On the Light theme, hovering a day in Month view uses its own gray instead of the weekend gray, and hovering a weekend day now shows a change.
- Rail right-click menus align 8 pixels from the side and bottom content edges.
- Submenus attach consistently to their opening rows, with native direction and screen-edge placement.
- Installed Apps lists Steve the Killer as the publisher after an update, instead of keeping the old "Steve / thekiller.net" label from the first install.
- Dialog title wordmarks keep the main window's dark halo behind the app name on the Light theme's Yellow accent, so the yellow no longer fades into the title bar.
- Active and hovered sidebar rail icons show the yellow accent with a dark halo on the Light theme's Yellow accent, instead of near-black.
- The wordmark has a soft shadow on the Light theme too, so it no longer looks thin on the light title bar, and the dark themes use the same lighter shadow as the rest of the Killer Tools family.
- The title bar icon is 27px and has its own 27px image, so it is a little larger and sharper.
- Button gradients start at the outline color and darken toward the bottom, so a hovered button no longer looks darker than its border.
- The "Install for everyone" checkbox in the install dialog wraps instead of being cut off.
- On 98SE, hovered dialog buttons keep the classic gray face instead of filling with a dark color that hid the label.
- Centered website logos with space above and below and corrected current features, runtime requirements and documentation.
- Menu separators draw their divider line instead of leaving an empty gap.

## [1.2.0] - 2026-10-07

### Added

- Added a silent uninstall, `/uninstall-silent`, registered as the quiet uninstall so WinGet and other package managers can remove Killendar without a prompt.
- Ukrainian localization.
- Norwegian (Bokmål) localization.
- Brazilian Portuguese localization.
- Added yellow and magenta accents to the neutral and 98SE themes.
- Link both shortcut views to the online guide and how-to, with more space beside the close button.

### Changed

- Improved menu and selection contrast, softened material-theme footers and 98SE footer text, matched the 98SE About bevel to the sidebar, and used subtle gray Delirium dividers.
- The two-column language menu now has room for all 19 languages.
- Filled buttons, hovered outline buttons, checked chips and accent picker pills use gradients outside 98SE, and accent swatches lift on hover.
- Menu rows now show accent text and icons on the theme's hover color.

### Fixed

- Opening a Killendar already in the active folder uses the existing file instead of creating a duplicate (#18).
- Opening an iCalendar file with Killendar now imports its events, and Killendar appears in the Windows Open with list for `.ics` files.
- Assigned categories are visibly selected, retain their primary color, and offer Make primary without clearing other selections (#16).
- A maximized window stays maximized after it is minimized and restored (#17).
- Pinned places in the file picker can be dragged into a new order.
- Ctrl+wheel changes picker views and icon sizes.
- App and website keyboard maps show appointment actions on separate Ctrl, Alt and Ctrl+Shift layers.
- The uninstaller has its own taskbar button, labeled "Uninstall Killendar", with the app icon.
- 98SE forms use gray panels, distinct white fields and recessed borders, with clearer dropdown arrows and readable highlights.
- Shared theme colors, control states, title styling and shadows match the family palette, including cream Blood, Greed and Cyanotic wordmarks. Rail flyouts sit 8 pixels from the rail and bottom content edges.
- Dropdown highlight text uses the right color in Dark/Yellow, Light/Purple and Light/Yellow.
- Selected language and theme rows keep the active accent on hover.
- Fixed picker and dialog borders, including square color picker corners in 98SE.
- Appointments show an open hand on hover and a closed hand while they are dragged, and so does the color picker's title.
- Align shortcut titles, view controls, and online help in one header row.
- Light/Yellow uses true yellow for the wordmark, accent lines and radio buttons, with neutral dark text instead of olive brown. Only the yellow wordmark accent has a shadow.
- 98SE Red is a true maroon again, so it no longer looks like Magenta.
- Delirium, Ectoplasm and Sepulchre footers match the window background again.
- The theme picker's divider updates when switching themes.
- The portable footer button reads PORTABLE, like KillerPDF and KillerScan, and its tooltip offers the install.
- Install and uninstall windows now show the film grain and the app icon in the title bar.
- Main window and dialog title bar icons now use sharp size-matched images.
- Added a faint divider with fading ends between the language menu columns.

## [1.1.4] - 2026-09-28

1.1.4 adds calendar access through KillerMCP and expands localization.

### Added

- KillerMCP can use the installed app to read the calendar agenda without changing it.
- KillerMCP can create appointments in the open, unlocked calendar.
- Vietnamese localization for the app and website.

### Fixed

- The 98SE calendar edge, toolbar buttons, and About controls now match the classic styling.
- Closing the appointment panel with Escape leaves the B shortcut ready to reopen it (#15).
- Standalone installation dialogs use the default Dark/Red theme, including uninstall and installation conflicts.
- The language picker lists its sixteen languages in two columns, so it no longer runs the height of the window or clips on a short one.
- On the 98SE theme the keyboard map's keys are white instead of button-face gray.
- Aligned the sidebar rail with the family footer spacing.
- Chocolatey package now installs the app instead of leaving the download in the Chocolatey tools folder.

## [1.1.3] - 2026-09-02

### Fixed

- Changing the week start immediately updates the calendar dates as well as the headings (#6).
- Rolling Month navigation has larger range arrows and distinct week and range tooltips (#11).

## [1.1.2] - 2026-09-02

1.1.2 improves the calendar layout.

### Added

- Agenda automatically shows two columns in wide windows and one column in narrow windows.
- Date format and week-start settings have their own flyout above the language button.

### Changed

- The calendar name sits beside the database controls in the title bar.
- Sepulchre uses brown for the selected calendar view button.

### Fixed

- The sidebar density button no longer overlaps the date and matches the other controls' size (#10).

## [1.1.1] - 2026-09-01

1.1.1 fixes the time box crash and the first round of user reports, and adds a crash log, two languages and optional address suggestions.

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
- First launch now picks the interface language from the Windows display language when it is one of the fifteen shipped, falling back to English.

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
