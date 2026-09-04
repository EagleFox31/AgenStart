# Installation queue, package preparation and execution orchestrator

## Purpose

The application-layer installation boundary turns a user-approved software selection into a controlled installation session.

The orchestrator does not construct shell commands and does not know WinGet command-line arguments. It consumes typed package-provider contracts and normalized installed-software state.

```text
User-approved selection
        |
        v
InstallationOrchestrator
        |
        +--> IInstallationVerifier --> normalized software inventory
        |
        `--> IPackageProvider
                |
                +--> Resolve exact trusted package
                +--> optional bounded preparation
                `--> sequential installer execution
```

## Approval boundary

Only selections with `Approved = true` enter the executable queue. Non-approved selections remain in the final report as `Skipped`.

AgenStart never installs a recommendation merely because it was suggested.

## Two-layer state model

Terminal queue state remains intentionally small:

```text
Queued
Running
Succeeded
Failed
Skipped
Cancelled
```

A separate activity state explains what an item is doing while it is still in the queue:

```text
Waiting
Resolving
Downloading
Ready
Installing
Verifying
Completed
Failed
Skipped
Cancelled
```

This separation lets the UI expose `Downloading`, `Ready` and `Installing` without weakening the deterministic queue/report model.

## Bounded preparation pipeline

AgenStart v0.2 can prepare trusted packages ahead of installation when a provider implements `IPreparablePackageProvider`.

The default preparation concurrency is **3** and is constrained to **1–3**. Installer execution concurrency remains **exactly 1**.

```text
approved items
     |
     v
pre-verification
     |
     v
exact resolve
     |
     +---- preparation worker 1 ---- Downloading -> Ready
     +---- preparation worker 2 ---- Downloading -> Ready
     `---- preparation worker 3 ---- Downloading -> Ready
                                      |
                                      v
                           deterministic sequence
                                      |
                                      v
                             Installing (one only)
                                      |
                                      v
                                  Verifying
```

Preparation failures are item-local. A failed download does not corrupt or reorder other queue items.

Providers that cannot safely prepare a package return `Unsupported`; the item remains eligible for the provider's normal trusted sequential installation path.

## Windows / WinGet preparation

For the public `winget` source, `WinGetProvider` may use `winget download` with:

- exact package ID;
- exact configured trusted source;
- AgenStart-owned absolute download directory;
- explicit package/source agreement flags only when the user approved them;
- no force, override or hash-bypass flags.

The Microsoft Store source is deliberately excluded from direct preparation in this version because its account/licensing flow remains owned by WinGet.

After WinGet downloads a package, AgenStart:

1. reads the WinGet-emitted merged manifest;
2. matches the expected exact package identifier;
3. requires the manifest installer SHA-256;
4. independently re-hashes the downloaded installer;
5. allows prepared execution only for a conservative set of installer types (`exe`, Inno, Nullsoft, Burn, MSI/Wix);
6. falls back to normal WinGet installation when dependencies or safe silent invocation cannot be preserved;
7. re-checks the installer SHA-256 immediately before execution.

The public preparation result contains an opaque preparation ID, not an arbitrary executable path supplied by UI/user input.

## Sequential installation guarantee

Even while later packages are downloading, the orchestrator consumes ready items only in the original approved sequence.

At most one call to `InstallAsync` or `InstallPreparedAsync` is active at a time. This avoids MSI/UAC/PATH/reboot-sensitive installer contention.

## Execution sequence

For each approved item:

1. increment attempt count;
2. refresh installed-state verification;
3. skip provider execution if already installed;
4. check the registered exact provider;
5. resolve the exact trusted package reference;
6. optionally prepare/download it using the bounded preparation pool;
7. mark it `Ready`;
8. execute it only when every earlier executable queue position has been consumed;
9. mark `Verifying` and refresh installed state;
10. derive the terminal queue state and publish progress.

No fuzzy package lookup, automatic source fallback or arbitrary provider argument injection is introduced by the orchestrator.

## Post-install verification

Provider exit status alone is not enough to claim success.

`SoftwareInventoryInstallationVerifier` refreshes `IInstalledSoftwareInventoryProvider` and resolves the application through `SoftwareStateResolver`.

```text
Installed -> Verified
Missing   -> NotInstalled
Unknown   -> Unknown
```

A provider result becomes final `Succeeded` only after installed state is verified.

## Retry policy

There is no implicit retry.

Transient preparation/provider failures such as network failure, source unavailability, timeout or provider unavailability can be marked retryable. Retry remains explicit.

A prepared package may be retained for an explicit retry when the installer failed after preparation. Before any retry, installed-state verification runs again to prevent duplicate side effects.

Integrity failures, policy blocks, ambiguous packages and agreement/elevation requirements are never silently bypassed.

## Cancellation semantics

Cancellation is propagated through the linked session token to active preparation and provider operations.

When cancellation is requested:

- active downloads/preparation receive cancellation;
- active installer execution receives cancellation;
- not-yet-consumed queued items become `Cancelled`;
- completed terminal items remain unchanged;
- the session ends as `Cancelled`.

Prepared files that have already completed can be retained only while the active orchestrator needs them safely; successful verified installations release their preparation cache best-effort.

## Progress and UI contract

`InstallationSession.ProgressChanged` emits structured snapshots with activity, downloaded/required bytes when known, terminal state, retry eligibility and timestamps.

The desktop UI can therefore distinguish:

```text
Resolving
Downloading
Ready
Installing
Verifying
Verified / Failed / Cancelled
```

No fake percentage is required when a provider cannot expose trustworthy byte totals.

## Security properties

The pipeline preserves the existing trust boundary:

- approved applications only;
- exact typed provider/package/source identity;
- trusted WinGet sources only;
- no arbitrary installer URLs;
- no `cmd.exe` or PowerShell command construction;
- no `--override`, `--force` or security-hash bypass;
- independent SHA-256 verification of prepared installers;
- SHA-256 checked again immediately before prepared execution;
- unsupported or unsafe preparation falls back to the normal WinGet path;
- installer execution remains sequential;
- no automatic elevation broker;
- no success claim without post-install verification.

## Verification gate

Installation and Windows provider tests cover:

- approved-only execution;
- bounded concurrent preparation with at least two overlapping preparations;
- sequential deterministic installation despite concurrent downloads;
- isolated preparation failure;
- cancellation before installer execution;
- exact WinGet download command construction;
- no security-bypass arguments;
- prepared installer SHA-256 verification and tamper rejection;
- Store preparation fallback;
- existing retry, cancellation and post-install verification behavior.
