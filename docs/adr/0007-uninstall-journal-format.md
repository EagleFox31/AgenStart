# ADR-0007: Uninstall journal format and persisted enum mapping

- Status: Accepted
- Date: 2026-11-18
- Decision owners: AgenStudio / AgenStart
- Related: #119, ADR-0004, ADR-0005, LL-003, LL-009, `docs/design/2026-11-18-uninstall-module.md`

## Context

Uninstall sessions change the machine in several steps, and a session can be interrupted at any
point: crash, reboot, killed process, or UAC denied halfway. AgenStart needs a durable record for
three purposes:

- **Reporting:** show the user what was done;
- **Resuming:** detect an interrupted session;
- **Restoring:** point to backups.

Lessons that apply:

- LL-003: identifiers derived from `Enum.ToString()` break silently when a member is renamed.
- LL-009: new persisted fields need a deliberate default for existing data.

## Decision

### 1. Append-only JSON Lines, one file per session

- **Location.** `%LOCALAPPDATA%\AgenStart\uninstall\journal\<sessionId>.jsonl`.
- **Session id.** A UUID created when the plan is confirmed.
- **Writing.** Lines are only appended, and each line is flushed before the action it describes
  starts. A file is never rewritten. A truncated last line is tolerated on read and reported.

### 2. Record types

Every line has `schemaVersion`, `sessionId`, `timestampUtc` and `type`.

| `type` | When | Main fields |
|---|---|---|
| `session-started` | after confirmation | `planId`, `programId`, `displayName`, `stateFingerprint`, `simulation` |
| `intent` | before each action | `itemId`, `kind`, `target`, `fingerprint`, `strategy` |
| `result` | after each action | `itemId`, `outcome`, `exitCode?`, `backupPath?`, `message` |
| `session-finished` | at the end | `outcome`, `counts`, `bytesFreed` |

### 3. Outcomes and kinds use explicit string mappings

Persisted enums go through an explicit two-way mapping, never `Enum.ToString()`. This covers
`outcome`, `kind`, `strategy`, `dataClass` and `session outcome`. Round-trip tests cover every
member. Unknown values read from disk map to `Unknown`; they do not throw.

`outcome` values for v1:

- `removed`
- `recycled`
- `exported-and-removed`
- `no-op`
- `already-removed`
- `skipped-changed-since-review`
- `skipped-not-approved`
- `skipped-requires-permanent-delete`
- `rejected-by-policy`
- `failed`
- `cancelled`

### 4. No secrets, no environment

- **Command lines.** These are logged as the resolved executable plus its argument list. Environment
  variables, tokens and the user's environment block are never logged.
- **Paths.** These are logged in full, because the user needs them to restore an item.

### 5. Resume rule

- **Single session.** A global named mutex allows one uninstall session at a time.
- **Interrupted sessions.** At startup, a journal with `session-started` and no `session-finished`
  is shown as "interrupted".
- **Recovery.** The plan is **recomputed from the current machine state**, never replayed from the
  journal. Actions whose preconditions already hold come out as no-ops, so only the remaining work
  appears.

### 6. Retention

- **Lifetime.** Journals and backups are kept until the user deletes them.
- **Clearing.** "Clear local data" leaves them in place, unless the user confirms their removal
  separately.
- **Location.** They are not added to `history.json` in v1. Linking them is an open question in the
  design document, and any change to `history.json` must stay backward-compatible (LL-009).

### 7. Simulation mode

A simulation session writes the same records with `simulation: true`. Its `result` lines carry the
outcome that would have happened, and no remover or uninstaller is called.

## Alternatives considered

- **A single mutable JSON file per session.** Rejected. A crash during a rewrite can lose the whole
  record.
- **SQLite.** Rejected for v1. It adds a dependency for a write-mostly log that is read rarely.
- **Windows Event Log.** Rejected. Writing a custom source needs administrator registration, and
  users rarely look there.

## Verification

This ADR is implemented when tests prove that:

- every persisted enum member round-trips through its string mapping;
- an unknown string value reads as `Unknown`;
- a journal truncated mid-line is read without throwing, and the truncation is reported;
- an interrupted journal leads to a recomputed plan, not a replay;
- no `result` line contains environment variables, using a fake process runner that injects a token
  into the environment;
- a second `apply` of a finished plan journals only `no-op` or `already-removed` outcomes.
