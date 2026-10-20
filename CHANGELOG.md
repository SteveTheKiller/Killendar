# Changelog

All notable changes to Killendar are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.3.2] - Unreleased

### Fixed
- Failed deletion of an edited recurring appointment preserves its edits and the original series.
- Failed legacy calendar migrations preserve the source and can retry without leaving an empty calendar.
- Failed password changes preserve the original calendar and complete recovery copies, and report temporary copies that cannot be removed.
- Failed self-updates retain complete executable copies and a recovery report.
- Settings recover the selected calendar location from complete backup or temporary files after an interrupted save.
- Category renames and deletions preserve all assignments when a save fails and show the error.
- A calendar that fails to load closes cleanly instead of showing stale appointments.
- Large recurrence intervals and dates at the calendar limits no longer crash recurrence queries.
- Calendar imports retain valid appointments when another row exceeds the supported date range.
- Email invites preserve text encoded in their declared character set.
- Updated SQLCipher to 4.19.0 and SQLite and JSON processing to 10.0.13.
- Installing and reopening waits for the previous app to exit so the installed copy opens reliably.
- Window resize minimums stay within the current monitor's work area on small or scaled displays.
- Calendar command clients that stop sending or reading no longer block later commands.
- Sidebar fades disappear at the scroll limits, leave scrollbars clear, and stay off in 98SE.
- The shortcuts card puts its scroll bar at the outer edge and shows when more content is out of view.
- Title bar buttons stay square when highlighted, like the caption buttons in the other apps, instead of rounding their top corner.
- The Week and Day grid sits flat under the day headers instead of curving at its top corners.
- The footer copyright sits a few pixels further from the corner grip instead of running into it.
- A silent install or update that finds Killendar running exits with 1618, which Intune, Configuration Manager and RMM tools retry, instead of a general failure.
- Chocolatey publishing reports failure when validation is still failing after a retry.
- The website's More button uses the selected language for its accessible name.

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
