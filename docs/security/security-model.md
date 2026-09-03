# AgenStart security model

Status: Proposed for Windows MVP

Related: #11, #2, #3, #15, ADR-0001, ADR-0002

## Purpose

AgenStart inspects a machine, recommends software and orchestrates installation. That combination gives the product access to operating-system information and the ability to trigger changes on the machine, so security boundaries must be explicit before the package execution layer is implemented.

The Windows MVP follows four rules:

1. **The main AgenStart application runs as a standard user.**
2. **WinGet is invoked non-elevated; Windows/installer UAC handles elevation when an installer requires it.**
3. **AgenStart never executes arbitrary command strings from UI input, catalogue data, remote configuration or telemetry.**
4. **Only curated application identities mapped to exact trusted provider identifiers may be installed.**

The design is fail-closed: if identity, source, command construction, catalogue integrity or privilege requirements cannot be established safely, AgenStart must refuse the operation and explain why.

## Security objectives

AgenStart must protect against:

- command/argument injection;
- accidental permanent administrator execution;
- privilege escalation through a broad helper API;
- package substitution caused by ambiguous search results;
- malicious or compromised catalogue metadata;
- unsafe WinGet options that bypass integrity/security controls;
- path hijacking or unintended executable resolution;
- leakage of sensitive machine/user data through logs or diagnostics;
- silent destructive recovery or rollback behaviour;
- untrusted custom package sources being treated as AgenStart-approved sources.

## Trust boundaries

```text
┌────────────────────────────────────────────────────────────┐
│ User / Avalonia UI                                         │
│ Standard-user process                                      │
└──────────────────────┬─────────────────────────────────────┘
                       │ typed application commands
                       ▼
┌────────────────────────────────────────────────────────────┐
│ AgenStart.Application / Core                               │
│ canonical app IDs, policy, catalogue, plans, validation    │
└──────────────────────┬─────────────────────────────────────┘
                       │ typed provider request
                       ▼
┌────────────────────────────────────────────────────────────┐
│ WinGetProvider                                              │
│ exact package ID + exact source + allow-listed arguments   │
└──────────────────────┬─────────────────────────────────────┘
                       │ ProcessStartInfo, no shell
                       ▼
┌────────────────────────────────────────────────────────────┐
│ winget.exe / Windows package ecosystem                     │
│ runs non-elevated from AgenStart                           │
└──────────────────────┬─────────────────────────────────────┘
                       │ installer-specific UAC if required
                       ▼
┌────────────────────────────────────────────────────────────┐
│ Windows Installer / application installer                  │
└────────────────────────────────────────────────────────────┘

Future AgenStart-owned privileged OS operations:

AgenStart.App (standard user)
        │
        │ explicit user action + typed request
        ▼
AgenStart.Elevated (one-shot helper)
        │
        ▼
allow-listed privileged Windows operation
```

The elevated helper is **not required for normal WinGet installation in the MVP** and must not be introduced merely to wrap WinGet.

## Process privilege policy

### Main application

`AgenStart.App` must be manifested as a normal `asInvoker` desktop application. The main process must never request `requireAdministrator` for routine startup.

Standard-user operations include:

- machine inventory that does not require elevation;
- reading AgenStart configuration and the trusted catalogue;
- generating recommendations;
- detecting installed applications where accessible to the user;
- constructing installation plans;
- invoking WinGet without self-elevation;
- verifying post-installation state;
- writing AgenStart user-scoped logs/configuration;
- opt-in telemetry described by #15.

### Installer elevation

AgenStart starts WinGet non-elevated. If the selected package installer requires administrator rights, elevation is owned by Windows/the installer and must remain visible to the user through the normal UAC experience.

AgenStart must treat cancellation/denial of UAC as a normal installation failure category rather than attempting to bypass or automatically repeat elevation.

### Future AgenStart elevated helper

A separate elevated helper may be added only when AgenStart itself must perform an operation that cannot safely be delegated to WinGet or an existing Windows mechanism, for example selected machine-wide configuration tasks.

The helper must:

- be a separate executable with the minimum implementation surface;
- launch only for a concrete user-approved privileged operation;
- terminate after the requested operation or short operation batch;
- expose typed, allow-listed operations rather than a generic command executor;
- validate every request again inside the elevated process;
- refuse unknown operation types/parameters;
- use a restricted IPC channel with explicit Windows ACLs;
- authenticate/bind the request to the expected AgenStart installation/session as far as practical;
- never become a permanent privileged Windows service for the MVP.

Examples of acceptable future broker APIs:

```text
EnableKnownWindowsFeature(featureId)
ApplyKnownMachineSetting(settingId, value)
RunApprovedConfiguration(configurationId)
```

Unacceptable APIs:

```text
Execute(command)
RunPowerShell(script)
WriteRegistry(path, name, arbitraryValue)
StartProcess(path, arguments)
```

## Command execution policy

AgenStart must not use a command shell for package-provider execution.

For WinGet, the provider must use `System.Diagnostics.ProcessStartInfo` with:

```text
UseShellExecute = false
```

and populate `ArgumentList` one argument at a time. The provider owns command construction; the catalogue never stores an executable command line.

Conceptually:

```csharp
var psi = new ProcessStartInfo
{
    FileName = resolvedWingetPath,
    UseShellExecute = false,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    CreateNoWindow = true
};

psi.ArgumentList.Add("install");
psi.ArgumentList.Add("--id");
psi.ArgumentList.Add(packageId);
psi.ArgumentList.Add("--exact");
psi.ArgumentList.Add("--source");
psi.ArgumentList.Add(sourceId);
```

The real implementation may add approved non-interactive/licence flags as required, but only through typed provider policy.

The following must not be used by the normal AgenStart install path unless a future security review explicitly changes the policy:

- `cmd.exe /c`;
- PowerShell `-Command`/arbitrary script execution;
- catalogue-provided command strings;
- WinGet `--override`;
- WinGet `--custom`;
- WinGet `--ignore-security-hash`;
- WinGet `--ignore-local-archive-malware-scan`;
- local manifests supplied by users/remote catalogue data;
- `--force` as a default recovery mechanism;
- automatic `--allow-reboot`;
- arbitrary/custom WinGet sources.

## Executable resolution

AgenStart must not trust an arbitrary `winget.exe` discovered by PATH alone if a safer Windows-supported resolution mechanism is available.

The provider must have an explicit executable-resolution strategy and validate that the resolved command is the expected WinGet client before execution. If WinGet cannot be safely resolved, the provider reports `Unavailable`/`UntrustedResolution` rather than falling back to a random same-named executable.

No executable path may come directly from catalogue metadata.

## Trusted package policy

The AgenStart catalogue defines product-level trust. A package being discoverable through WinGet does not automatically make it AgenStart-approved.

Each installable application must have:

- a canonical AgenStart application ID;
- an allowed platform;
- an allowed provider;
- an exact provider package ID;
- an exact allowed provider source;
- an active trust state (`active`, `deprecated`, `blocked` or equivalent);
- sufficient verification metadata for AgenStart's post-installation checks.

For the Windows MVP, approved sources are limited to sources explicitly configured by AgenStart policy, initially the official WinGet community source (`winget`) and Microsoft Store source (`msstore`) where a curated catalogue entry requires it.

A user-added/custom source must never silently satisfy an AgenStart package mapping even if it publishes an identical package name.

Install resolution is therefore:

```text
AgenStart canonical ID
        ↓
trusted catalogue entry
        ↓
provider = winget
package ID = exact configured ID
source = exact configured source
        ↓
WinGetProvider
```

There is no fuzzy package search at execution time.

## Catalogue as a security boundary

Catalogue metadata can influence what software AgenStart installs. It must therefore be treated as security-sensitive policy, not ordinary UI content.

### Bundled catalogue

The production application must ship with a known-good versioned catalogue. Runtime installation must never depend on an unvalidated arbitrary JSON file placed in a user-writable directory.

### Future remote catalogue updates

If remote catalogue updates are introduced, the activation pipeline must be conceptually:

```text
download candidate
      ↓
authenticity/integrity verification
      ↓
JSON/schema validation
      ↓
semantic policy validation
      ↓
version/compatibility checks
      ↓
write candidate separately
      ↓
atomic activation
      ↓
retain last-known-good catalogue
```

The exact signing/key-rotation mechanism requires a dedicated implementation design before remote catalogue updates ship. A network TLS connection alone is not considered sufficient catalogue authenticity for a privileged package recommendation product.

Remote catalogue data must never introduce raw shell commands, executable paths, arbitrary provider flags or new custom sources.

A catalogue entry that fails validation is rejected; AgenStart must not partially interpret it.

## Supply-chain threat scenarios

### Malicious/compromised catalogue entry

Threat: a package mapping is changed from the intended application to another package.

Controls:

- versioned curated catalogue;
- schema + semantic validation;
- exact provider package ID and source;
- signed/authenticated remote updates before remote updates are supported;
- `blocked` state/kill-switch capability for known-bad mappings;
- last-known-good fallback.

### Package-name ambiguity/substitution

Threat: a fuzzy search installs the wrong similarly named package.

Controls:

- no fuzzy execution-time install resolution;
- exact package ID;
- `--exact`;
- exact source.

### Argument injection

Threat: malicious metadata inserts additional command-line operations.

Controls:

- provider-owned typed arguments;
- `ArgumentList`;
- no shell;
- no catalogue command strings;
- allow-listed argument policy.

### Hash/security bypass

Threat: an operator or compromised catalogue disables integrity checks to make an install succeed.

Controls:

- security-bypass WinGet flags prohibited;
- no generic provider flag passthrough.

### Privilege confusion

Threat: an untrusted request reaches a broad elevated process and executes arbitrary machine-level actions.

Controls:

- no elevated main application;
- no custom broker for WinGet;
- future helper is one-shot and allow-listed;
- request validation occurs inside the elevated boundary;
- restricted IPC ACLs.

### Executable/path hijack

Threat: AgenStart launches an attacker-controlled binary named `winget.exe`.

Controls:

- explicit provider executable resolution;
- no executable paths from catalogue/UI;
- provider validates availability/trust before execution.

### Compromised upstream package

Threat: an upstream publisher or repository serves a malicious but otherwise valid package.

Controls are necessarily limited. AgenStart reduces exposure through curation, exact source/ID binding, WinGet's own package integrity mechanisms and the ability to block affected catalogue entries. AgenStart does not claim to cryptographically establish the intent of every upstream publisher.

## Provider argument model

The application layer should request domain operations rather than CLI syntax.

Example:

```csharp
InstallRequest(
    ApplicationId: "visual-studio-code",
    Scope: InstallScope.Default,
    Interaction: InstallInteraction.Normal
)
```

The provider resolves that through the trusted catalogue to an internal execution plan such as:

```text
Provider: WinGet
PackageId: Microsoft.VisualStudioCode
Source: winget
Exact: true
```

No public application API should expose `ExtraArguments: string` for the MVP.

## Logging and diagnostics

Security logging must be useful without becoming a machine/user data collection channel.

Normal structured logs may include:

- canonical AgenStart application ID;
- provider name;
- provider package ID/source;
- AgenStart version;
- operation type;
- timestamps/duration;
- normalized exit/result category;
- retry count;
- high-level verification result.

Normal logs must not intentionally include:

- usernames;
- hostname/device name unless explicitly required for a diagnostic export;
- serial numbers or MAC addresses;
- credentials/tokens/auth headers;
- personal file contents/names;
- full environment-variable dumps;
- arbitrary command lines containing unreviewed external values;
- telemetry installation UUID unless a specific diagnostic design requires correlation and documents it.

Provider stdout/stderr may contain paths/usernames or installer-specific data. It must therefore not be uploaded automatically. If retained locally for troubleshooting, retention and redaction must be bounded; explicit diagnostic export is preferred for detailed raw logs.

Telemetry remains governed by #15 and must use a restricted telemetry DTO rather than serializing machine inventory or logs wholesale.

## Installation verification

A provider process exiting with code `0` is evidence, not the sole source of truth.

Where practical, AgenStart verifies the resulting application state after installation using the installed-app detection layer. The final result model should distinguish at least:

```text
SucceededVerified
SucceededUnverified
Failed
CancelledByUser
ElevationDenied
TimedOut
ProviderUnavailable
PackageNotFound
SourceUnavailable
PolicyBlocked
VerificationFailed
```

Exact enum names are an implementation detail, but provider exit codes must be normalized before they reach the UI/domain model.

## Failure, retry and rollback

AgenStart must not describe a multi-package setup as an atomic transaction.

If packages A and B install successfully and package C fails, AgenStart records A/B as successful and C as failed. It may offer retry/skip/continue and, where supported, an explicit user-driven uninstall action.

The MVP must not automatically uninstall previously successful packages in an attempt to simulate a global rollback. Third-party Windows installers are too heterogeneous for AgenStart to guarantee lossless reversal.

Recovery rules:

- retries re-evaluate installed state before running again;
- destructive actions require explicit intent;
- failures preserve a structured session result;
- reboot-required states are surfaced to the user rather than silently forcing reboot;
- interrupted sessions should be resumable where practical from observed machine state, not from blind assumption.

## Network policy

AgenStart-controlled network clients must use normal TLS validation and must not ship a generic "ignore certificate errors" path.

Provider/network failures are surfaced as failures; they must not cause AgenStart to silently switch to an untrusted mirror or custom source.

## Security-sensitive configuration

The following are security policy and must not be casually user-editable in the MVP:

- trusted provider source allow-list;
- package ID mappings used for execution;
- security-bypass flags;
- elevated helper operation allow-list;
- catalogue signing trust roots/keys when remote updates exist.

User preferences may control recommendation/interaction behaviour, but cannot downgrade these security controls through a normal settings toggle.

## Testing requirements

Before the installation engine is considered releasable, automated tests must cover:

- catalogue package IDs cannot become arbitrary command strings;
- command construction uses discrete arguments;
- prohibited WinGet flags cannot be generated by normal requests;
- unapproved/custom sources are rejected;
- blocked/deprecated package policy behaves as defined;
- malformed catalogue entries fail closed;
- provider exit results are normalized;
- logging redaction does not emit forbidden values in representative cases;
- manual cancellation/UAC denial are represented as safe failure states;
- any future elevated helper rejects unknown operations and malformed parameters.

Security-critical provider policy tests should be fast enough to run in CI without requiring actual third-party software installation. End-to-end Windows tests can then validate selected safe fixtures separately.

## Windows MVP implementation boundary

Issue #4 (`WinGetProvider`) may proceed once this model is accepted. It must implement only the non-elevated provider boundary described here.

The first provider implementation does **not** need:

- an elevated AgenStart helper;
- a PowerShell abstraction;
- arbitrary command execution;
- custom WinGet source management;
- remote catalogue update execution;
- automatic rollback.

Those capabilities require separate explicit requirements/security review if introduced later.

## Future macOS implication

The principles are platform-independent:

```text
standard-user application
        ↓
typed platform/provider operation
        ↓
trusted package identity/source
        ↓
platform-native package/elevation mechanism
```

A future macOS provider may use Homebrew/Cask for curated packages and Apple-supported authorization/service mechanisms for AgenStart-owned privileged operations. macOS must receive its own platform-specific security review rather than mechanically copying Windows implementation details.

## Review triggers

Re-review this security model before shipping any of the following:

- AgenStart-owned elevated helper;
- remote catalogue updates;
- custom/private package sources;
- direct-download fallback installers outside trusted package managers;
- arbitrary scripts/configuration recipes;
- Windows service installation;
- enterprise policy management;
- self-update mechanism;
- macOS privileged operations.
