# ADR-0004: Uninstall execution and elevation model

- Status: Proposed
- Date: 2026-11-18
- Decision owners: AgenStudio / AgenStart
- Related: #119, ADR-0002, `docs/design/2026-11-18-uninstall-module.md`

## Context

The uninstall module (#119) must run uninstallers and, later, remove machine-scope leftovers.
These leftovers include `ProgramData` folders, `HKLM\SOFTWARE` keys, scheduled tasks and firewall
rules.

This is the first AgenStart feature that conflicts with two ADR-0002 rules:

- **Commands from strings.** Uninstall commands come from the registry (`UninstallString`,
  `QuietUninstallString`). Third-party installers write those values, so they are untrusted. ADR-0002
  §3–§4 forbid turning arbitrary strings into executable commands.
- **Privileged work owned by AgenStart.** Removing machine-scope leftovers needs administrator
  rights. ADR-0002 §7 allows a separate one-shot elevated helper "until a concrete privileged
  AgenStart-owned operation requires it". This is that operation.

There is also a Windows-specific trap. When a standard user approves UAC with a *different*
administrator account, the elevated process gets that administrator's `HKCU` and `%APPDATA%`. They are
not the user's.

## Decision

### 1. The main application stays `asInvoker`

ADR-0002 §1 is unchanged. Listing programs, computing the plan and removing user-scope leftovers all
run as the standard user.

### 2. Official uninstall strategies are typed, in a fixed priority order

`OfficialStrategySelector` picks the first strategy that applies:

1. **WinGet.** `winget uninstall --id <id> --exact --source <source> --silent
   --disable-interactivity --accept-source-agreements [--scope]`.
   - It is built by `WinGetCommandBuilder.BuildUninstall`.
   - The id and source come from the existing `winget export` correlation, never from free text.
   - The prohibited options of ADR-0002 §6 still apply.
2. **Windows Installer.** `msiexec.exe /x {ProductCode} /qn /norestart`.
   - `msiexec.exe` is resolved from `%WINDIR%\System32`.
   - The product code must parse as a GUID and `MsiQueryProductStateW` must report it as installed.
3. **Registry uninstall string.** `QuietUninstallString` first. Failing that, `UninstallString` with
   flags for a detected installer kind (Inno: `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`; NSIS:
   `/S _?=<dir>`). Failing that, the interactive `UninstallString`, labelled as such in the UI.

### 3. Registry uninstall strings are parsed, never shelled

A registry string is accepted only if it is reduced to a resolved executable plus a discrete
argument list. All of these must hold:

- It is parsed with `CommandLineToArgvW` semantics. It is never passed to `cmd.exe`,
  `powershell.exe` or `UseShellExecute = true`.
- The executable path is absolute and exists.
- The executable is not a shell or script host, nor an interpreter beyond `msiexec.exe`:
  `cmd`, `powershell`, `pwsh`, `wscript`, `cscript`, `mshta`, `rundll32` and `regsvr32` are refused.
- The executable sits under the program's `InstallLocation`, under a `Program Files` root, or in
  `%WINDIR%\Installer`. Otherwise the strategy is refused and the UI explains why.
- A `MsiExec.exe /I{GUID}` or `/X{GUID}` string is rewritten to strategy 2 using the parsed GUID.
  The original string is not used.
- AgenStart adds flags only from its own allow-list for the detected installer kind. It never
  forwards flags taken from rule files or the UI.

A string that fails these checks is a structured `uninstall.command-rejected` outcome. It does not
fall back to the shell. This keeps ADR-0002 §9 (fail closed).

### 4. Third-party uninstallers own their own UAC

As with installs, a WinGet uninstall or a vendor uninstaller that needs elevation triggers its own
UAC prompt. AgenStart does not wrap them in an elevated broker. The only exception is
`msiexec /qn` for a machine-scope product: it cannot prompt by itself, so it goes through the
helper (§5).

### 5. Machine-scope leftovers use the one-shot helper `AgenStart.Uninstall.Elevated`

This applies ADR-0002 §7.

- **Process.** A separate console executable, started with `runas` only when the user confirms the
  plan, never at startup. There is at most one UAC prompt per session.
- **Input.** A typed request that lists the plan id, item ids, targets, kinds and fingerprints. The
  helper has no generic process, shell or registry API.
- **Revalidation.** The helper does not trust its caller. Before each action it reloads the
  hard-coded safety floor and the rule set (ADR-0006), then checks the target against
  `PathSafetyPolicy` and the recorded fingerprint again. An item that fails is skipped and reported.
- **Scope.**
  - The helper refuses any user-scope item: `HKCU`, any per-user known folder, any path under a
    user profile.
  - This removes the "different administrator account" trap. User-scope items always run in the
    standard-user process.
- **IPC.** The request goes in a file under `%LOCALAPPDATA%\AgenStart\uninstall\requests\`. The ACL
  of that file grants access to the invoking user and to `SYSTEM`/`Administrators` only. The helper
  writes its results to the journal (ADR-0007), never to a channel the UI could feed back as input.
- **Lifetime.** The helper exits when the request is processed. It installs no service, scheduled
  task or persistent component.

### 6. Running processes are closed only with consent

The UI lists the processes that Restart Manager (`RmGetList`) reports, plus running images under
`InstallLocation`. Each one needs its own approval. AgenStart first tries `CloseMainWindow`. It
calls `Kill` only after a timeout and a second approval.

### 7. Exit codes map to the existing statuses

Uninstall results reuse `PackageOperationStatus`:

| Exit code | Status |
|---|---|
| MSI `1605`, WinGet "not installed", missing ARP key | `NotFound` (idempotent no-op) |
| MSI `3010` | `RebootRequired` |
| MSI `1602` | `CancelledByUser` |
| MSI `1618` | retry later |
| UAC denial | `RequiresElevation` |

## Alternatives considered

- **Elevate the whole application for uninstall sessions.** Rejected, for the same reasons as
  ADR-0002 §1.
- **Run `UninstallString` through `cmd.exe /c`.** This is what most uninstall tools do. It is
  rejected because it makes third-party registry content part of the shell boundary.
- **A long-lived elevated service.** Rejected: it would be a persistent local privilege primitive,
  and ADR-0002's revisit triggers exclude it.
- **The WinGet COM API (`Microsoft.Management.Deployment`).** Rejected for v1. It needs a Windows TFM
  and COM activation, has limits in elevated contexts, and would duplicate the existing CLI path.

## Consequences

- Some legitimate uninstallers are refused because they live outside the allowed roots or are
  script-based. The UI reports them as "uninstall manually from Windows Settings" instead of running
  them.
- The helper adds a second executable to build, package and eventually sign. Until signing exists,
  the UAC prompt shows "unknown publisher".

## Verification

This ADR is implemented when tests prove that:

- shell hosts, script hosts, relative paths and executables outside the allowed roots are rejected;
- MSI strings are rewritten to the typed `msiexec /x {GUID}` form;
- no flag from a rule file or the UI reaches an argument list;
- the helper refuses user-scope items, items outside the policy and items whose fingerprint changed;
- UAC denial and every exit code in §7 map to the documented status.
