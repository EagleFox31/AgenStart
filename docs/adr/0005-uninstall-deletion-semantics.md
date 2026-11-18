# ADR-0005: Uninstall deletion semantics

- Status: Accepted
- Date: 2026-11-18
- Decision owners: AgenStudio / AgenStart
- Related: #119, ADR-0004, ADR-0006, `docs/design/2026-11-18-uninstall-module.md`

## Context

Leftover removal is the riskiest part of the uninstall module. A false positive can destroy user
data such as game saves, configuration or documents. Windows also has several traps that make
"delete" less reversible than it looks:

- **The Recycle Bin is not guaranteed.** When an item exceeds the volume's bin quota, or sits on a
  network or removable drive, the shell deletes it permanently. `Microsoft.VisualBasic.FileSystem`
  does this silently.
- **OneDrive syncs deletions.** Deleting a OneDrive placeholder ("Files On-Demand") removes the
  cloud copy too.
- **Links can lead outside the target.** Junctions and symbolic links can redirect a recursive delete
  outside the intended folder.
- **Registry and scheduled-task deletions have no bin.**

## Decision

### 1. Only listed and approved items are deleted

The only deletion input is `UninstallApproval.SelectedItemIds`, checked against the reviewed plan.
Deletion never globs, never expands a wildcard at apply time, and never deletes "everything that
matches".

### 2. Files and folders go to the Recycle Bin

- **Mechanism.** Removal uses the shell's `IFileOperation` with `FOFX_RECYCLEONDELETE` and
  `FOF_WANTNUKEWARNING`.
- **Pre-check.** Before applying, AgenStart checks the bin's quota and free space on each volume. It
  marks an item `requires-permanent-delete` when it exceeds them, or when the volume has no bin
  (network or removable drive).
- **Permanent deletion.** A `requires-permanent-delete` item is deleted only if the user gives a
  separate, explicit second approval (`AllowPermanentDelete = true`, never on by default). Without
  that approval the item is skipped and reported.

### 3. Registry keys and values are exported before deletion

Before deleting a key or value, AgenStart exports it with `reg export` to
`%LOCALAPPDATA%\AgenStart\uninstall\backups\<sessionId>\<itemId>.reg`. Deletion is refused if the
export fails. The report shows the backup path, and restoring it is a double-click on the `.reg`
file.

### 4. Scheduled tasks are exported before deletion (P5)

AgenStart exports the task XML to the same backup folder before removing the task. Firewall rules
are recorded in the journal with all their properties, so they can be recreated.

### 5. Protected data classes

- **Default.** `UserData` items are never pre-selected. This covers saves, documents, anything under
  OneDrive, and anything a rule classifies as user data.
- **Opting in.** Selecting one shows a warning naming the folder.
- **OneDrive.** Items under a OneDrive root are always `UserData`, whatever a rule says.

### 6. Links are never followed

- **Scope.** Size computation and deletion both stop at reparse points: junctions, symbolic links
  and mount points. A reparse point inside a target is removed as a link; its destination is never
  touched.
- **Long paths.** These use the `\\?\` prefix.

### 7. Items changed since review are skipped

Each item carries a fingerprint: path, kind, size and last write time. If the fingerprint differs at
apply time, the item is skipped with `leftover.changed-since-review`, and the user can recompute the
plan.

### 8. Volume thresholds need extra confirmation

Above a configured size or file count, the confirmation screen asks for one more explicit approval.
The defaults are set in the rule file (ADR-0006).

### 9. AgenStart's own data

The journal and backups live under `%LOCALAPPDATA%\AgenStart\uninstall\`. They are excluded from
leftover detection by the hard-coded floor. "Clear local data" does not delete them unless the user
confirms that separately.

## Alternatives considered

- **Permanent deletion with a pre-made zip backup.** Rejected. It doubles disk usage for large game
  folders, and a zip restore is less discoverable than the Recycle Bin.
- **`Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory` with `RecycleOption.SendToRecycleBin`.**
  Rejected. It falls back to permanent deletion silently, with no reliable way to detect it
  beforehand.
- **A private quarantine folder instead of the Recycle Bin.** Deferred. The Recycle Bin is the
  standard Windows undo that users already understand, and a quarantine would need its own retention
  and cleanup policy.

## Verification

This ADR is implemented when tests prove that:

- with a fake file system, no item outside `SelectedItemIds` is ever passed to the remover;
- an item over the quota, or on a bin-less volume, is never deleted without `AllowPermanentDelete`;
- a failed `reg export` blocks deletion of that key;
- deletion of a target that contains a junction leaves the junction's destination intact;
- `UserData` items are never pre-selected;
- a changed fingerprint skips the item.
