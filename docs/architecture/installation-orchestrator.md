# Installation queue and execution orchestrator

## Purpose

Issue #7 introduces the application-layer execution boundary that turns a user-approved software selection into a controlled installation session.

The orchestrator does not execute shell commands and does not know WinGet arguments. It consumes only the typed `IPackageProvider` contract introduced by #4 and the normalized installed-software state introduced by #5.

```text
User-approved selection
        |
        v
InstallationOrchestrator
        |
        +--> IInstallationVerifier --> normalized software inventory
        |
        `--> IPackageProvider -------> WinGetProvider (Windows V1)
```

The UI remains outside this boundary. It observes session state and progress events and may request cancellation or an explicit retry.

## Approval boundary

Only selections with `Approved = true` enter the executable queue.

Non-approved selections are retained in the session report as `Skipped` with diagnostic code `selection.not-approved`. This makes the final report complete while preserving the security/product rule that AgenStart never installs a recommendation merely because it was suggested.

## Queue model

V1 executes sequentially.

Item states:

```text
Queued
Running
Succeeded
Failed
Skipped
Cancelled
```

Session states:

```text
Ready
Running
Cancelling
Completed
Cancelled
```

The session object is the in-memory source of truth for the active run. It preserves sequence, attempt count, provider result, verification result, retry eligibility, reboot requirement and timestamps.

Durable recovery across application restarts is deferred. "Persisted for the active session" in V1 means state is retained consistently in the active `InstallationSession`, not written to a long-lived machine database.

## Execution sequence

For each approved queued item the orchestrator performs:

1. mark item `Running` and increment attempt count;
2. refresh installed-state verification before provider execution;
3. if already installed, mark `Succeeded` with `AlreadyInstalled` and skip provider execution;
4. locate the registered provider by exact provider ID;
5. check provider availability;
6. resolve the exact trusted package reference;
7. call `IPackageProvider.InstallAsync` with the typed user-approved request;
8. normalize cancellation/failure results;
9. refresh installed-state verification after a verifiable provider completion;
10. derive the final queue item state and publish progress.

No fuzzy package lookup, source fallback, arbitrary command or shell execution is introduced by the orchestrator.

## Post-install verification

Provider exit status alone is not enough to claim installation success.

`SoftwareInventoryInstallationVerifier` refreshes `IInstalledSoftwareInventoryProvider` and resolves the application through `SoftwareStateResolver`.

Mapping:

```text
Installed -> Verified
Missing   -> NotInstalled
Unknown   -> Unknown
```

A provider result of `Succeeded`, `AlreadyInstalled` or `RebootRequired` becomes final `Succeeded` only when installed state is verified.

If the provider completed but inventory proves the application is still missing, the item becomes `Failed` and may be retried.

If inventory is incomplete/ambiguous after a provider completion, the item becomes `Failed` with retry disabled. AgenStart deliberately avoids repeating an installation that may already have succeeded.

## Retry policy

The provider layer performs no implicit retry. Retry policy belongs here.

Initial retryable provider states are limited to conditions that may reasonably change without changing package identity:

```text
NetworkFailure
SourceUnavailable
TimedOut
ProviderUnavailable
Failed
```

Retryable resolution states follow the same conservative principle.

`RequiresElevation`, agreement failures, policy blocks, integrity failures, ambiguous packages and unsupported installers are not automatically retryable.

A retry is always explicit. Before invoking the provider again, the orchestrator re-runs installed-state verification. If the package is now detected, the retry finishes as `Succeeded / AlreadyInstalled` without a second provider installation call.

This protects against duplicate side effects when the previous installer completed but its original operation result or immediate verification was inconclusive.

## Cancellation semantics

Cancellation is cooperative at the application layer and is propagated to provider operations through a linked `CancellationToken`.

When cancellation is requested:

- the active provider receives cancellation;
- the current item becomes `Cancelled` when provider execution/resolution reports cancellation or throws due to the linked token;
- all not-yet-started `Queued` items become `Cancelled`;
- already `Succeeded`, `Failed` or `Skipped` items remain unchanged;
- the session ends as `Cancelled`.

A cancelled session cannot be resumed or retried in V1. The user creates a new session from a fresh approved selection instead.

## Progress and observability

`InstallationSession.ProgressChanged` publishes structured `InstallationProgressEvent` values containing:

- session ID and state;
- item snapshot when applicable;
- stable diagnostic/event code;
- human-readable message;
- UTC timestamp.

These events are suitable for the future Avalonia UI and for privacy-minimal local diagnostics. They are not telemetry by themselves.

## Final report

`InstallationReport` includes every original selection, including skipped items, with:

- deterministic sequence;
- application/package identity;
- final state;
- attempt count;
- last normalized provider status;
- diagnostic code/message;
- installed version when verified;
- retry eligibility;
- reboot requirement;
- start/completion timestamps.

Summary counters expose succeeded, failed, skipped and cancelled totals.

## Security properties

The orchestrator preserves ADR-0002 and the #4 provider boundary:

- no `cmd.exe` or PowerShell command construction;
- no arbitrary provider arguments;
- no fuzzy execution-time package selection;
- no automatic source fallback;
- no automatic elevation broker;
- no implicit retry;
- no installation of unapproved recommendations;
- no success claim without post-install verification.

## Verification gate

`Installation tests` runs on every relevant `push` and `pull_request`, plus manual dispatch.

The test suite covers:

- approved-only queue execution;
- deterministic sequential order;
- skipped non-approved items;
- normalized retryable provider failure;
- retry without duplicate installation;
- session cancellation and cancellation of remaining queued items;
- post-install unknown state not being claimed as success;
- reboot-required success after verification;
- real `SoftwareInventoryInstallationVerifier` mapping for installed and incomplete inventory states.
