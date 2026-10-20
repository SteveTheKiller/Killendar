# Deployment and uninstall results

`Killendar.exe /silent` installs without a dialog. `Killendar.exe /uninstall-silent` requests removal without a confirmation dialog. A machine installation requests administrator access if it is not already elevated. Declining that request returns `1223` and leaves the installation untouched.

Uninstalling the running executable requires a separate process. The request returns `997` (`ERROR_IO_PENDING`) after a verified helper copy starts and a pending receipt is saved. This code means removal is pending. It does not mean removal succeeded, and it does not request a reboot. The original process cannot change its exit code after it exits.

## Finding the receipt

Each request has a fresh operation ID. The confirmation and diagnostic output identify its directory:

- Per-user: `%LOCALAPPDATA%\Killendar\Uninstall-<operationId>\status.json`
- Machine: `%ProgramFiles%\Killendar\Uninstall-<operationId>\status.json`

Deployment wrappers can supply their own fresh ID as the second argument: `/uninstall-silent <operationId>`. Use a GUID in its 32-character `N` format, without braces or hyphens. The same ID follows an elevated child. Never reuse an existing operation directory.

The receipt includes `OperationId`, `Machine`, `TargetDirectory`, `State`, `ExitCode`, `MetadataRemoved`, `RemainingFiles`, `Error` and `UpdatedUtc`.

| State | Meaning |
| --- | --- |
| `Pending`, code `997` | The helper has not published a final result. The executable or registrations may still be present. |
| `Completed`, code `0` | The owned executable was confirmed absent and required registration cleanup finished. |
| `Failed`, nonzero code | Cleanup did not complete. Read `Error` and `RemainingFiles`. |

A missing, malformed or still-pending receipt at the wrapper's deadline is an unconfirmed result. Do not turn it into success. The helper waits for its identified parent and retries sharing violations for bounded periods. Another running copy can keep the original executable mapped long enough to produce a failed result.

The operation directory retains `Killendar.Uninstall.exe`, its configuration if present, and the receipt as diagnostic evidence, including after completion. These files are not an installed application or a successful-removal signal. Cleanup does not recursively delete the installation directory, calendars, or other files stored beside the application.

## Package managers and scripts

The Chocolatey uninstall wrapper passes a fresh operation ID and waits for its matching receipt. It requires the machine scope, expected installation directory, `Completed`, code `0`, `MetadataRemoved: true`, no remaining owned files, and independently confirmed absence of `Killendar.exe`. Cancellation, failed removal, an invalid receipt or a timeout fails the package operation.

Do not add `997` to a package manager's unconditional success codes. The local WinGet manifest generator does not classify it as success. A consumer that only observes the original executable's exit code may report a pending request as an error even if its helper later completes. Such consumers need receipt-aware completion handling; signed package-manager behavior still needs validation in a disposable Windows environment.

## Recovering a failed request

Read the retained receipt and correct the reported cause, such as another running instance or denied access. A file-removal failure leaves the uninstall registration in place. If file removal succeeded but registration cleanup failed, the normal executable can be absent while the uninstall entry remains.

In that case, reinstall the original signed build to restore its normal executable and registrations, then retry a normal uninstall with a new operation ID. Keep the failed receipt for diagnosis. Do not change its state, treat a missing executable alone as success, or rerun the retained worker against a failed receipt.

Common nonzero results include `1` for a general failure, `5` for denied access, `1223` for cancellation, `1618` for an unfinished child or locked image, and `1625` for a rejected helper or path validation.
