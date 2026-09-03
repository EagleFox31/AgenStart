# Package provider architecture

## Purpose

AgenStart must not couple product/application logic directly to WinGet.

The package-provider boundary translates a trusted, typed provider reference into provider-specific operations while keeping canonical AgenStart application identity independent from package-manager identifiers.

```text
AgenStart catalogue / application layer
        │
        │ canonical application ID
        │ + validated provider mapping
        ▼
IPackageProvider
        │
        ├── Windows → WinGetProvider
        │
        └── macOS   → future HomebrewProvider
```

This document defines the executable contract introduced by Issue #4.

---

## Projects

```text
src/
├── AgenStart.PackageManagement
│   └── provider-neutral contracts
│
└── AgenStart.Platform.Windows
    └── WinGet adapter and Windows execution boundary

tests/
└── AgenStart.Platform.Windows.Tests
```

`AgenStart.PackageManagement` has no dependency on Avalonia, WinGet or Windows APIs.

`AgenStart.Platform.Windows` depends on `AgenStart.PackageManagement` and owns the WinGet-specific translation/execution rules.

---

## Provider contract

The V1 contract exposes three operations:

```csharp
public interface IPackageProvider
{
    string ProviderId { get; }

    Task<PackageProviderAvailability> CheckAvailabilityAsync(
        CancellationToken cancellationToken = default);

    Task<PackageResolutionResult> ResolveAsync(
        ProviderPackageReference package,
        CancellationToken cancellationToken = default);

    Task<PackageOperationResult> InstallAsync(
        PackageInstallRequest request,
        CancellationToken cancellationToken = default);
}
```

The interface deliberately does **not** expose:

- arbitrary command execution;
- raw CLI argument passthrough;
- shell fragments;
- arbitrary provider/source switching;
- generic installer overrides.

Those exclusions are security properties, not missing convenience features.

---

## Typed provider reference

The catalogue owns canonical AgenStart identity and stores provider mappings.

A provider receives a typed mapping such as:

```csharp
new ProviderPackageReference(
    ProviderId: "winget",
    PackageId: "Microsoft.VisualStudioCode",
    Source: "winget",
    ScopePreference: PackageScope.User);
```

The WinGet adapter validates the reference again before execution.

For the Windows MVP, trusted source names are:

```text
winget
msstore
```

A custom/user-added source is not trusted merely because it exists in the user's WinGet configuration.

---

## WinGet executable resolution

AgenStart does not execute `winget` through an arbitrary `PATH` lookup.

The V1 locator checks the current user's known App Execution Alias locations under:

```text
%LOCALAPPDATA%\Microsoft\WindowsApps\winget.exe

%LOCALAPPDATA%\Microsoft\WindowsApps\
Microsoft.DesktopAppInstaller_8wekyb3d8bbwe\winget.exe
```

If neither trusted alias is available, WinGet is reported as unavailable for the session.

AgenStart does not crawl `Program Files\WindowsApps`, invoke PowerShell to discover WinGet, or execute another same-named binary from PATH as an automatic fallback.

This is intentionally conservative and can be revisited if telemetry/support evidence shows legitimate installations are missed.

---

## Availability and provider version

Provider availability is checked with a bounded direct process call equivalent to:

```text
winget --version
```

The result captures:

- provider available/unavailable state;
- best-effort WinGet version;
- stable diagnostic code.

Raw provider output is not the UI contract.

Installed-application inventory and installed package-version reconciliation remain separate concerns handled by the installed-application workstream (#5). Issue #4 detects the provider's own version, not the complete machine software inventory.

---

## Exact resolution

The runtime provider does not perform fuzzy search before installation.

Resolution is equivalent to:

```text
winget show \
  --id <exact-package-id> \
  --exact \
  --source <trusted-source> \
  --disable-interactivity
```

A stale package mapping therefore fails closed instead of silently selecting a similarly named package.

General package search may be useful for catalogue-maintenance tooling later, but it is intentionally excluded from the V1 runtime execution contract.

---

## Installation command

The V1 command builder generates an argument vector equivalent to:

```text
winget install \
  --id <exact-package-id> \
  --exact \
  --source <trusted-source> \
  --disable-interactivity \
  --no-upgrade
```

Optional typed additions are:

```text
--scope user|machine
--silent
--accept-package-agreements
--accept-source-agreements
```

Agreement flags are added only when the request explicitly records consent.

The following capabilities are not exposed through the provider:

```text
--override
--custom
--force
--ignore-security-hash
--ignore-local-archive-malware-scan
--allow-reboot
arbitrary local manifests
arbitrary custom sources
```

The command builder owns provider arguments. Catalogue records never contain a complete executable command.

---

## Process execution boundary

WinGet is launched directly through `ProcessStartInfo` with:

```text
UseShellExecute = false
ArgumentList.Add(...)
RedirectStandardOutput = true
RedirectStandardError = true
CreateNoWindow = true
```

AgenStart does not use:

```text
cmd.exe /c
powershell.exe -Command
raw concatenated command strings
```

Output and error streams are consumed asynchronously to avoid pipe deadlocks.

Cancellation and timeout attempt to terminate the complete provider process tree and return structured `Cancelled` / `TimedOut` results.

Default V1 operation budgets:

```text
availability check  8 seconds
exact resolution   45 seconds
installation       30 minutes
```

These values are provider policy and may become configuration after runtime evidence exists.

---

## Retry semantics

`WinGetProvider` performs **no implicit retry**.

Reasons:

- retries can repeat installer side effects;
- a package may have partially installed before the process returns failure;
- retryability depends on the normalized failure category;
- queue/orchestration policy belongs above the provider layer.

The future installation orchestrator (#7) may retry only explicit retryable states after re-checking installed state.

A retry must reuse the same trusted package ID and source unless the catalogue itself changes through a separately validated update.

---

## No source fallback

Provider/source fallback is explicit policy.

Example:

```text
catalogue mapping = Git.Git @ winget
winget source unavailable
        ↓
SourceUnavailable
```

AgenStart does **not** then try:

```text
msstore
another custom source
fuzzy package search
direct-download mirror
```

A different provider/source may only be attempted when the catalogue contains an explicitly trusted mapping and an upper-layer policy deliberately selects it.

---

## Normalized result model

The UI/orchestrator consumes AgenStart states rather than WinGet HRESULTs.

Initial operation states:

```text
Succeeded
AlreadyInstalled
NotFound
Ambiguous
NoApplicableInstaller
SourceUnavailable
AgreementRequired
RequiresElevation
BlockedByPolicy
IntegrityFailure
NetworkFailure
RebootRequired
CancelledByUser
Cancelled
TimedOut
ProviderUnavailable
Failed
```

Unknown WinGet return codes map to `Failed` with diagnostic code:

```text
winget.unmapped-error
```

They are never guessed into a success/retry state.

The normalizer currently covers stable WinGet CLI/install error families including:

- package not found / multiple matches;
- no applicable installer;
- missing/unavailable source;
- package/source agreements;
- policy blocks;
- hash/security/integrity failures;
- network/service failures;
- reboot requirements;
- user cancellation;
- already-installed state.

Reference definitions:

- WinGet return codes: https://github.com/microsoft/winget-cli/blob/master/doc/windows/package-manager/winget/returnCodes.md
- WinGet error constants: https://github.com/microsoft/winget-cli/blob/master/src/AppInstallerSharedLib/Public/AppInstallerErrors.h

---

## Elevation

The main AgenStart process remains `asInvoker` and launches WinGet non-elevated.

AgenStart does not create an elevated broker for ordinary WinGet installs.

When the selected installer requires elevation, Windows/the installer owns the UAC flow.

A WinGet result indicating that the command itself requires administrator context is normalized to:

```text
RequiresElevation
```

It is not automatically retried by relaunching AgenStart as administrator.

This follows ADR-0002.

---

## Agreements and interactivity

The provider always disables WinGet interactive prompts so a background install queue cannot stall on hidden console input.

If a package/source agreement has not been accepted, the provider returns:

```text
AgreementRequired
```

An upper UI layer can then display an explicit user decision and retry with the corresponding typed consent flag.

AgenStart must not infer legal consent from merely selecting an application.

---

## Output and privacy

Provider stdout/stderr may contain paths, usernames or installer-specific details.

Therefore raw provider output is not returned as the normal UI result and must not be sent directly to telemetry.

The stable result contract exposes:

```text
AgenStart status
canonical application ID
provider/package/source identity
native exit code
stable diagnostic code
safe product message
```

Future diagnostic logging must follow `docs/security/security-model.md` redaction and retention rules.

---

## Verification

The first provider test suite proves that:

- only exact package ID + source commands are built;
- untrusted sources are rejected before process start;
- package IDs that resemble CLI flags/command injection are rejected;
- prohibited security-bypass flags are not generated;
- agreement flags require explicit request values;
- provider failures do not trigger source fallback;
- known WinGet HRESULTs become normalized AgenStart states;
- unknown HRESULTs fail closed;
- cancellation/timeout override misleading native exit codes;
- raw provider output is not copied into the structured result message.

The test suite runs on Windows through `.github/workflows/package-management-tests.yml`.

---

## Follow-up boundaries

Issue #4 does not implement:

- installation queue/orchestration (#7);
- installed application inventory (#5);
- recommendation logic (#6);
- UI progress surfaces (#8);
- automatic global rollback;
- self-update;
- custom enterprise WinGet sources;
- an AgenStart-owned elevated helper.

These concerns build on the provider contract rather than being folded into it.
