# Killendar encryption + .kcal file format

Draft plan, 2026-07-29. Nothing here is built yet. Decisions taken so far: SQLite with
SQLCipher (the KillerNotes mechanism, not a bespoke one), `.kcal` as the file format,
each file referred to as "a Killendar", opt-in encryption with the family title-bar lock
button.

## 1. The format

`.kcal` is a SQLCipher-capable SQLite database. One file is one Killendar. Plaintext until
a password is set, encrypted at rest afterwards - the same model as `.kndb`, and the reason
KillerNotes' lock button has an unlocked state at all.

`.kcal` appears clear: it is absent from the usual filename-extension registries, and the
adjacent names that ARE taken are `.ics`/`.ical` (iCalendar), `.cal` (Windows Calendar and
CALS raster) and `.kal`.

Do not confuse the two file jobs:

| File | Purpose |
| --- | --- |
| `.kcal` | A Killendar. The live database, opened and written in place. |
| `.ics` | Interchange only. Import and export, already built, unchanged by this. |

### Schema

```sql
CREATE TABLE events (
  id          TEXT PRIMARY KEY,   -- the existing Guid, as text
  title       TEXT NOT NULL,
  start_utc   TEXT NOT NULL,      -- ISO 8601, UTC
  end_utc     TEXT NOT NULL,
  all_day     INTEGER NOT NULL,
  location    TEXT NOT NULL DEFAULT '',
  description TEXT NOT NULL DEFAULT '',
  attendees   TEXT NOT NULL DEFAULT '',  -- newline separated
  created_utc TEXT NOT NULL,
  modified_utc TEXT NOT NULL
);
CREATE INDEX events_range ON events (start_utc, end_utc);

CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
-- meta: schema_version, app_version_created
```

Times stored UTC, converted at the edges. The current JSON stores local `DateTime` with no
offset, which is already a latent bug for anyone who changes timezone; moving to a database
is the moment to fix it rather than carry it forward.

`events_range` exists because `GetInRange` is the only hot query - every view calls it.

## 2. Encryption

- **Cipher**: SQLCipher default, AES-256-CBC with per-page HMAC-SHA512.
- **Key derivation**: SQLCipher's own PBKDF2 (256k iterations in SQLCipher 4). Do not
  hand-roll a KDF on top; passing the passphrase to `PRAGMA key` is the supported path.
- **Opt-in**: no password means a plain SQLite file. Setting one runs
  `PRAGMA rekey`; removing one rekeys back to empty.
- **In memory**: the passphrase is held for the session so the Manage dialog can reopen the
  file without re-prompting. It is not written to `settings.json`, ever.

### What this does NOT protect

Worth stating plainly in the README so the claim is honest:

- Nothing is protected while the app is open and unlocked.
- No protection against a keylogger, a memory dump, or an attacker with your password.
- `settings.json` stays plaintext - it holds theme, locale, window rect and the recent-file
  list. **The recent list leaks Killendar file paths and therefore their names.** Decide
  whether that list is worth having.
- iCalendar export is plaintext by definition. Exporting an encrypted Killendar to `.ics`
  writes an unencrypted copy, and the UI should say so at the moment of export.

## 3. Migration from events.json

On first run after the update, if `%LOCALAPPDATA%\Killendar\events.json` exists and no
`.kcal` does:

1. Create `Default.kcal` in `%LOCALAPPDATA%\Killendar\`, plaintext.
2. Insert every appointment, converting local times to UTC using the current zone.
3. Rename the old file to `events.json.migrated` rather than deleting it.
4. Report the count in the status bar.

Non-destructive, and reversible by hand. The `.migrated` suffix rather than a delete is
deliberate: the timezone conversion in step 2 is the one lossy step, and if it is wrong the
original is still there.

## 4. UX

- **Title-bar lock button**, left of the caption buttons: `0xE72E` locked, `0xE785`
  unlocked, char casts, matching KillerNotes exactly. Click to set / change / remove the
  password.
- **Unlock on launch** when the active Killendar is encrypted. Wrong password re-prompts;
  cancel exits. A "New Killendar" escape hatch on that screen for a forgotten password -
  the locked file is left alone, not destroyed.
- **New Killendar... / Load Killendar...** in the toolbar or a rail button. The name in the
  title bar or footer, so it is obvious which Killendar is open.
- **Manage Killendars** dialog, ported from `DatabasesDialog`: every `.kcal` in the data
  folder with size, modified, encrypted flag, rename inline, reveal in Explorer, switch to.
- **Shell association** for `.kcal`, so double-click opens it. That needs the association
  registration and cleanup code from KillerShell's `Associations.cs`, which I deliberately
  did NOT port in Phase 5 because Killendar had no formats. It comes back for this.
- **No recent-files list** (Steve, 2026-07-29). It was a KillerNotes assumption that does not
  survive contact with a calendar: you do not open a calendar the way you open documents, and
  Manage Killendars already lists every `.kcal` in the data folder, which makes "recent"
  redundant. Dropping it also removes the plaintext leak of Killendar names from settings.
- **Export warning**: exporting an encrypted Killendar to `.ics` shows a one-time confirm with
  a "Don't remind me again" checkbox, persisted in `settings.json`. KillerShell's
  `ConfirmDialog` already takes up to two optional checkbox labels, so this wants that
  generalized version ported over KillerUI's simpler three-argument one. Note KillerShell's
  copy calls `MainWindow.ApplyThemeBorder(this)`, which Killendar's Chrome.cs does not have -
  either port that too or drop the call.

## 5. The trap to plan for

KillerNotes' csproj documents it: use `SQLitePCLRaw.provider.e_sqlcipher` (the **static**
provider), never the bundle. The bundle's `Batteries_V2` dynamic loader probes
`Assembly.Location`, which is empty for Costura's in-memory assemblies, and crashes at
startup. The static provider binds by plain `DllImport`, satisfied by a `SqlCipherBootstrap`
that self-extracts an embedded x64 native at runtime - same pattern as KillerPDF's OCR
natives. That bootstrap and the csproj incantation get ported verbatim, not reinvented.

Also inherited: the transitive pins for `System.Net.Http` 4.3.4 and
`System.Text.RegularExpressions` 4.3.1 with `ExcludeAssets=all`, which Costura's dependency
graph otherwise resolves to versions with advisories.

### The second trap, found while verifying step 1: connection pooling

`Microsoft.Data.Sqlite` pools connections, and the pool key is the **whole connection
string**. Two consequences, both load-bearing:

1. **Never key a connection with a raw `PRAGMA key` after `Open()`.** A pooled connection
   comes back already keyed, so the second `PRAGMA key` is silently ignored and a **wrong
   password reads the database successfully**. Verified: the smoke harness did exactly this
   and read the plaintext row back with `PRAGMA key = 'wrong password'`. The key must ride in
   the connection string via `SqliteConnectionStringBuilder.Password`, which makes it part of
   the pool key, so a wrong password gets its own handle and fails with `SQLite Error 26:
   'file is not a database'`. This is what KillerNotes already does, and now the reason is
   written down.
2. **`Dispose()` does not release the file handle - `SqliteConnection.ClearAllPools()` does.**
   Anything that swaps the file underneath (password set/change/remove via
   `sqlcipher_export`, switching the active Killendar, archiving a locked one) has to clear
   pools first or it hits a sharing violation. KillerNotes calls it in `Close()` and in the
   `finally` of `IsEncryptedFile`.

Also copy KillerNotes' habit of forcing the key check immediately:
`SELECT count(*) FROM sqlite_master` right after `Open()`, rather than trusting `Open()` to
have validated anything.

### Step 1 verification result (2026-07-29)

Ran from a copy of the single exe alone in an empty directory, with
`%LOCALAPPDATA%\Killendar\native` deleted first to prove a cold extract:

```
assembly location: ''                      (empty, as expected under Costura)
PASS bootstrap: EnsureLoaded returned
PASS provider: SQLite3Provider_e_sqlcipher set, sqlite version 3.39.2
PASS create: encrypted database created and written
PASS ciphertext: header is not "SQLite format 3"
PASS roundtrip: read the row back with the right key
PASS wrongkey: SqliteException, SQLite Error 26: 'file is not a database'
PASS nokey: same rejection for an unkeyed connection
PASS plaintext: an unencrypted .kcal still opens and reads (encryption stays opt-in)
PASS plaintext header: "SQLite format 3"
```

Output directory is `Killendar.exe` + `.exe.config` + `.pdb` only; the native lands as the
`Killendar.SqlCipherNative.e_sqlcipher.dll` manifest resource (1,852,928 bytes) and extracts
to `%LOCALAPPDATA%\Killendar\native\<version>\`. Step 1 is done.

## 6. Scope and sequencing

This is a phase of its own, and it lands **before** the landing page, because the og-image
now says "Free Encrypted Calendar" and the site should not make a claim the exe cannot back.

1. ~~csproj packages, `SqlCipherBootstrap`, native embed. Verify a plain SQLite open works
   under a Costura single-file build - this is the step most likely to fight back.~~
   **Done, verified end to end - see section 5.**
2. ~~`EventStore` reimplemented on SQLite behind the existing method signatures, so the views
   and the sidebar need no changes. Migration from `events.json`.~~
   **Done, verified end to end - see section 9.**
3. ~~Password: set / change / remove, unlock on launch, lock glyph.~~
   **Done, verified end to end - see section 10.**
4. ~~New / Load / Manage Killendars, active-file display.~~
   **Done, verified end to end - see section 11.**
5. ~~`.kcal` shell association, `Associations.cs` ported.~~
   **Done, verified end to end - see section 12.**
6. Strings for all of the above in ten locales, README and CHANGELOG.

## 7. Decisions taken (Steve, 2026-07-29)

- **One password per Killendar.** Each `.kcal` is a database that can be locked, and that is
  the only unit of locking. No per-day or per-group sub-locking like KillerNotes has for
  notes - a calendar has no equivalent boundary worth defending.
- **No recent-files list.** See section 4.
- **Export warns once**, with a "Don't remind me again" checkbox. See section 4.
- **Install scope stays the KillerShell model**: the user chooses per-user or all-users at
  install time. Already built in Phase 5.

## 8. Where the Killendar file lives

**Per-user data, always.** A machine-wide install makes the app available to everyone on the
box; each Windows user still gets their own Killendar, and a new user who signs in starts with
a fresh one. Standard behaviour, and the install-scope choice from Phase 5 is unchanged by any
of this - it governs where the exe goes, nothing else.

**`%APPDATA%\Killendar\`** (roaming), matching KillerNotes, so a calendar follows the user
between machines on a domain. `settings.json` moves there too.

The migration in section 3 checks BOTH `%APPDATA%` and `%LOCALAPPDATA%`, because builds up to
this point have already written `events.json` to the local one.

A shared calendar (front desk, team) is not tied to install scope: point "New Killendar..." at
a network path or `C:\ProgramData` and the `.kcal` format handles it with no extra machinery.

## 9. Step 2 verification result (2026-07-29)

`Services/EventStore.cs` now writes SQLite and keeps an in-memory mirror for reads, so every
existing signature the views call (`GetInRange`, `GetOnDay`, `GetUpcoming`, `GetById`, `Add`,
`Update`, `Delete`, `ImportEvents`, `Events`, `LoadError`, `Changed`) behaves exactly as it did
on JSON. No view or sidebar file changed. New surface for the later steps: `Open(path,
password)`, `Close()`, `NeedsPassword`, `HasPassword`, `FilePath`, `DisplayName`,
`IsEncryptedFile(path)`, `ActiveIsEncrypted()`, `MigratedCount`.

Verified by a throwaway probe compiled against the **built** `Killendar.exe`, so it ran the real
Costura-embedded `Microsoft.Data.Sqlite` and the real SQLCipher native, not a test double.
A seeded pre-database `events.json` (a timed appointment, an all-day holiday, a multi-day
overnight cutover, attendees, an embedded newline) was migrated by launching the app, then
read back:

```
migration:  Default.kcal created in %APPDATA%\Killendar, events.json -> events.json.migrated
on disk:    14:00 local (PDT, -07:00)          -> 2026-08-03T21:00:00.0000000Z
            22:00 Aug 14 local                 -> 2026-08-15T05:00:00.0000000Z
            all-day Sep 7                      -> 2026-09-07T00:00:00.0000000  (no Z, unconverted)
            attendees                          -> newline separated
            schema_version=1, app_version_created=1.0.0.0, events_range index present
read back:  every local time identical to the JSON it came from
            all-day date did not drift
            GetOnDay / GetInRange / GetUpcoming counts all correct
write path: add -> reopen -> update -> reopen -> delete -> reopen all persisted
```

20 assertions, all pass. Test data and probe deleted afterwards; `%APPDATA%\Killendar` was left
empty so a first real launch is clean.

Two things worth remembering from this pass:

- **All-day events must not be converted to UTC.** They are floating calendar dates. Converting
  them slides an all-day appointment to the previous day for everyone east of UTC. `ToStore` and
  `FromStore` take an `allDay` flag for exactly this, and the stored value has no `Z`.
- **Migration stamps the JSON's naive `DateTime`s as `Local` before converting.** Without the
  `SpecifyKind`, `ToUniversalTime()` sees `Unspecified`, treats it as already-UTC and shifts
  every appointment by the machine's offset. This is the one lossy step in the feature, which is
  why the original is renamed `.migrated` rather than deleted.

### Not a Killendar bug, but it cost time

A WPF process launched from a shell with no `%windir%` dies before `MainWindow` exists:
`UriFormatException` inside `MS.Internal.FontCache.Util..cctor()`, surfacing as exit code
`-532462766` (`0xE0434352`). Any WPF app does this; the fix is to restore `windir` in the
launching shell, not to change anything in the app.

## 10. Step 3 verification result (2026-07-29)

New files: `Security.cs` (a MainWindow partial: unlock-on-launch, the lock button, the
forgotten-password escape hatch) and `PasswordDialog.xaml(.cs)`, both ported from KillerNotes.
`EventStore` gained `SetPassword`, `ReplaceWithRetry` and `ArchiveActive`, ported including the
two hard-won bits: `GC.Collect()` + `WaitForPendingFinalizers()` before the file swap, and the
retry-then-move fallback (KillerNotes issue #3). Title bar gained `LockButton`; its glyph and
tooltip are set only from `UpdateLockGlyph`, never hardcoded in XAML.

Verified twice. First a probe compiled against the built `Killendar.exe` - 34 assertions, all
pass: set, ciphertext on disk, wrong password rejected, no password rejected, `IsOpen` stays
false after a rejected open, encrypted writes persist, change, old password stops working,
remove, back to a plain SQLite header, no `.rekey` or `.bak` residue on any path, and
`ArchiveActive` renaming to `<name>-locked-<stamp>.kcal` rather than deleting.

Then the real UI, driven on the desktop: lock button -> Encrypt -> status line "This Killendar is
now encrypted" and the glyph flips from `0xE785` to `0xE72E`; relaunch prompts with "Default.kcal
is encrypted"; a wrong password re-prompts with "That password did not work. Try again." and
clears the box; the right password opens; the lock button then offers change-or-remove, and
leaving both boxes empty removes encryption, restores the open-padlock glyph and reports
"Password removed - this Killendar is no longer encrypted".

Two real bugs this pass, both found by verifying rather than by reading:

- **`IsOpen` lied.** `Open()` assigned `_db` before calling `Open()` on it, so a connection that
  failed to open - or failed its key check - still reported `IsOpen == true`, and every caller
  keys off `IsOpen` to decide whether to prompt. The connection is now built into a local and
  only published to `_db` once it has opened AND passed `SELECT count(*) FROM sqlite_master`.
- **The unlock prompt cannot run in the constructor.** `Owner = this` on a window that has not
  been shown throws "Cannot set Owner property to a Window that has not been shown previously",
  and cancelling the prompt calls `Close()`, which throws again if it reenters `Show()`. Opening
  moved out of `InitCalendar` into `OpenCalendarData`, dispatched from `Loaded` at `Background`
  priority - exactly what KillerNotes already does, with the same comment.

### Harness note

A probe compiled against `Killendar.exe` must NOT reference `Microsoft.Data.Sqlite` itself. A
loose copy in the probe's output folder wins normal assembly probing and shadows the
Costura-embedded one, and the provider then looks unset: "You need to call
SQLitePCL.raw.SetProvider()". Talk to the store through `EventStore` only.

## 11. Step 4 verification result (2026-07-29)

New file: `KillendarsDialog.xaml(.cs)`, ported from KillerNotes' `DatabasesDialog`. `EventStore`
gained the file-level statics: `ListKillendars`, `SetActive`, `CreateKillendar`,
`RenameKillendar`, `DeleteKillendar`, `ImportKillendar`. `Security.cs` gained
`KillendarsButton_Click` (closes the store, shows the dialog, reopens, falls back to the previous
file if the chosen one's unlock is cancelled) and `UpdateActiveKillendarLabel`.

### Placement (Steve, 2026-07-29)

Both file buttons live in the **title bar**, not on the sidebar rail, matching KillerNotes:
Killendars, then the lock, then the caption buttons. They are about the open Killendar, not about
the view - theme and language stay on the rail. Killendar's glyphs are deliberately **bigger than
the kit's `ChromeButton` FontSize 10**, which is sized for caption glyphs and makes these read as
specks: Killendars at 16, lock at 14. The Library glyph is drawn narrower than the padlock, hence
the extra 2. Set locally on the buttons so `Controls.xaml` stays byte-identical across the family.
**KillerNotes wants the same bump** - Steve flagged its icons as too small in the same breath.

The name of the open Killendar shows in the footer's centre column, which was already reserved and
empty. Hidden while there is only the default file, so the common case stays quiet. Clicking it
opens the same dialog.

Glyph picks, all found by rendering candidates rather than trusting the docs:

| Where | Glyph | Why |
| --- | --- | --- |
| Title bar, Killendars | `E8F1` Library | `E8B7` was tried first and draws a blank page |
| Dialog, new | `E710` | |
| Dialog, delete | `E711` | |
| Dialog, load from a file | `E8DA` | arrow points INTO the page; `E8E5` points out and reads as export |
| Dialog, data folder | `E838` | same folder KillerNotes' equivalent button uses |

### Deliberately not ported

The **change-data-folder** picker (KillerNotes issue #6). Killendars are per-user under `%APPDATA%`
by design, and it would need `FolderPicker.cs` ported for a feature nobody asked for.

**Load copies rather than opening in place.** A Killendar is written to constantly; silently
writing into someone's Downloads folder or a network share is not what "Load" should mean. The
source file is left untouched and the copy lands in the data folder with a uniquified name.

### Results

26 headless assertions against the built exe, all pass: create uniquifies
(`Killendar-new.kcal`, `Killendar-new-2.kcal`), a zero-byte file is a valid empty Killendar and
takes its schema on first open, switching is isolated (no appointment leaks between files),
renaming the **active** file retargets the setting while renaming another leaves it alone,
encryption travels with the file and the list flags it, import copies and uniquifies, delete
removes exactly one, and the listing is alphabetical and `.kcal`-only.

Then the real UI: the dialog lists every file with size, modified, `[encrypted]` and `[open]`;
selecting Default and pressing Open switched the store, wrote `ActiveKillendar` to settings, moved
the `[open]` flag and updated both the status line and the footer label; New created a file and
dropped straight into inline rename with the stem preselected; renaming committed on Enter and
auto-appended `.kcal`.

### Two things that looked like bugs and were not

- **A second dialog appeared after switching.** Enumerating the process's visible windows proved
  one click gives exactly one dialog and closing it leaves only the main window. The extra dialog
  came from the automation double-firing, not from `KillendarsButton_Click`.
- **Inline rename produced `Killendar-new.kOncallcal.kcal`.** The automation's type action clicks
  at a coordinate first, which collapses the `Select(0, stem)` preselection and types at a caret.
  Driving it with an explicit clear-first produced `Oncall.kcal` correctly. Worth knowing before
  reading a mangled name as an app defect.

## 12. Step 5 verification result (2026-07-29)

Three new files: `Associations.cs` (register/unregister `.kcal`, capture the command-line path),
`SingleInstance.cs` (mutex + named pipe, ported from KillerNotes), `OpenFile.cs` (the receiving
side). `App.OnStartup` captures the argument, claims the single-instance slot, then registers;
`Uninstall` unregisters; `MainWindow`'s dispatched Loaded block drains the pending file after the
Killendar has opened.

### Simpler than KillerShell's Associations.cs, on purpose

Killendar **owns** `.kcal`. There is no existing default action to displace, no UserChoice hash to
forge, and no question of whether taking the open verb changes what a double-click *does* - which
is what all of KillerShell's caution is about. So this follows KillerNotes' `.kndb` model: HKCU
only, no elevation, best-effort, registered on **every launch** so the association follows the exe
if it moves. Not an opt-in card.

DefaultIcon is `<exe>,0` rather than a dedicated extracted `.ico`. KillerNotes extracts per-type
icons because a note and a database should look different; there is no second Killendar artwork yet,
so extracting a copy of the app icon would buy nothing.

### Double-click COPIES, it does not open in place

Consistent with the Killendars dialog's Load button, and with what KillerNotes does for a
double-clicked `.kndb`. Two reasons: a Killendar is written to constantly, and SQLite over SMB is a
well-known way to corrupt a database - so silently making someone's network share or Downloads
folder the live store is not what a double-click should mean. The confirm dialog says exactly that,
the source file is left untouched, and the copy lands in the data folder with a uniquified name.

### Single instance is load-bearing here

Two processes with the same `.kcal` open are two SQLite writers on one file. SQLite allows it, the
user never notices the double launch, and the password-change file swap then fails with "in use by
another process" - KillerNotes issue #3 all over again. Mutex is `Local\`-scoped and the pipe name
carries the session id, so a terminal server gets one instance per user rather than one per machine.

### Results

```
before          HKCU\Software\Classes\.kcal absent, ProgID absent
after one run   .kcal            (default)='Killendar.Killendar'
                ProgID           (default)='Killendar'  FriendlyTypeName='Killendar'
                DefaultIcon      '<exe>,0'
                shell\open       FriendlyAppName='Killendar'
                shell\open\command '"<exe>" "%1"'
double-click    ShellExecute on "Field Work.kcal" (note the space) launched a second process,
                which forwarded the path and exited - instance count stayed at 1
                the running window came forward with "Open Field Work.kcal in Killendar?"
after Add       copy in the data folder, ActiveKillendar='Field Work.kcal', footer reads
                "Field Work", status "Opened Field Work", source file still 24,576 bytes
```

`assoc .kcal` and `ftype` report nothing, which is correct and not a defect: those commands read
the machine half of HKCR only and never see an HKCU-only registration.

### Also in this pass, from Steve's review

- **`ConfirmDialog`'s OK button was `PrimaryButton`, not `OutlineButton`.** The family standard is
  an accent outline at rest that fills solid on hover; `PrimaryButton` is already solid, so hovering
  shifts one shade and the button reads as dead. Now `OutlineButton` with `MinWidth` rather than a
  fixed 80 (German "Hinzufügen" does not fit). **The KillerUI kit's copy of ConfirmDialog.xaml still
  has PrimaryButton - fix it there too.**
- **The time-grid day-name strip had square top corners.** It was a `Grid` with a themed background
  plus a separately overlaid rule; a Grid cannot carry a `CornerRadius`, so it is now a `Border`
  holding that Grid, and the Border carries the fill, the radius and the bottom rule. MonthView's
  weekday header already had `4,4,0,0`.
- **In-month day cells hid the film grain.** They painted `PaneBrush` - the same colour as the card
  underneath - so they looked identical while covering the card's grain, and only the
  semi-transparent out-of-month cells (`RowAltBrush` is `#14FFFFFF`) showed any texture. In-month
  cells now carry no fill. `Brushes.Transparent`, NOT null: a Border with a null Background receives
  no mouse events and the cell has to stay clickable.

### The "app launched in Week view" scare

Not a bug. The screenshot harness called `SetForegroundWindow`, which pulled focus off Steve
mid-typing, and Killendar's single-key shortcuts then ate his keystrokes - the `w` in a sentence he
was typing switched the view. Capturing without activating shows Month every time. The harness no
longer steals focus.
