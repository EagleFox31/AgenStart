# Windows Machine Inventory Boundary

- Status: Proposed
- Scope: AgenStart Windows V1
- Related issue: #2
- Architecture: Avalonia 12 + .NET 10 LTS + C#

## 1. Purpose

AgenStart needs enough information about a machine to make safe, explainable software recommendations without turning machine inspection into surveillance.

The inventory layer therefore has a deliberately narrow contract:

> Collect only technical capability data that is necessary to determine compatibility, installation readiness and recommendation quality.

The inventory layer is **read-only**, **local-first**, **best-effort**, and must run as a **standard user**.

AgenStart must never request elevation merely to inspect the machine.

---

## 2. Architectural boundary

The inventory subsystem belongs behind a platform abstraction.

```text
AgenStart.UI
    |
    v
AgenStart.Application
    |
    v
IMachineInventoryProvider
    |
    +-- WindowsMachineInventoryProvider   (V1)
    |
    `-- MacOsMachineInventoryProvider     (future)
```

The application and recommendation engine consume a normalized `MachineSnapshot`; they must not depend directly on WMI, the Windows Registry, Win32 APIs or Windows-specific DTOs.

Windows-specific collection code belongs in `AgenStart.Platform.Windows`.

---

## 3. Non-negotiable rules

1. **No UAC prompt for inventory.** If a value cannot be obtained as a standard user, return it as unavailable or omit it.
2. **No personal-content inspection.** Do not enumerate user documents, browser data, downloads, photos, source-code folders or file contents.
3. **No persistent device fingerprint.** Do not collect or derive hardware serial numbers, motherboard UUIDs, MAC addresses or similar stable identifiers.
4. **No account identity requirement.** User name, Microsoft account, email address and machine name are not needed for recommendation logic.
5. **Partial results are valid.** A failed GPU query must not invalidate CPU, RAM or storage results.
6. **Collectors must have timeouts.** WMI/process-based collection may stall and cannot be allowed to block application startup indefinitely.
7. **Raw provider errors stay internal.** The product exposes normalized availability/diagnostic states rather than leaking implementation-specific exception details into business logic.
8. **Inventory is a snapshot, not telemetry.** V1 performs on-demand/local inspection; it is not a continuous monitoring agent.

---

## 4. V1 inventory fields

### 4.1 Operating system

Required fields:

- Platform: `Windows`
- Windows edition / product name
- Display version where available
- OS version
- Build number
- Update Build Revision (UBR) where available
- OS architecture (`X64`, `Arm64`, etc.)
- Process architecture

Preferred Windows sources:

- `.NET Environment.OSVersion`
- `.NET RuntimeInformation.OSArchitecture`
- `.NET RuntimeInformation.ProcessArchitecture`
- Read-only registry values under:
  - `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion`

Relevant registry values include `ProductName`, `EditionID`, `DisplayVersion`, `CurrentBuildNumber` and `UBR`.

Rationale:

- .NET 5+ reports the actual Windows OS version in supported compatibility scenarios.
- `RuntimeInformation.OSArchitecture` represents the underlying OS architecture and is preferable to inferring it from the current process.
- Registry metadata gives product/edition labels that are useful to the UI but are not required for core compatibility decisions.

Privilege requirement: **standard user / read-only**.

Failure policy:

- OS platform and architecture are required.
- Edition/display labels are optional and may be `Unknown`.
- Recommendation rules must rely primarily on normalized version/build, not marketing strings such as `Windows 11 Pro`.

---

### 4.2 CPU

Required fields:

- CPU model/name when available
- Logical processor count
- Architecture

Optional future fields:

- Physical core count
- Virtualization capability
- instruction-set/capability flags only when a real product requirement appears

Preferred Windows sources:

- `Environment.ProcessorCount` for logical processor count
- read-only registry value `ProcessorNameString` under:
  - `HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0`
- WMI `Win32_Processor` only where additional properties are genuinely required

Privilege requirement: **standard user / read-only**.

Design note:

WMI must not be the only source for basic CPU capability. Basic inventory should continue to work when WMI is slow or unavailable.

Do not collect CPU serial identifiers or asset tags.

---

### 4.3 Memory

Required fields:

- Total physical RAM bytes
- Available physical RAM bytes at snapshot time

Derived values may include:

- Total RAM GiB for presentation
- Memory pressure category for recommendation logic

Preferred Windows source:

- Win32 `GlobalMemoryStatusEx` via a small P/Invoke wrapper

Rationale:

`GlobalMemoryStatusEx` directly reports current physical/virtual memory state and avoids using WMI for a simple system capability query.

Privilege requirement: **standard user**.

Important:

Available memory is volatile. Recommendation rules should use **total physical memory** for hardware compatibility and treat available memory as contextual information only.

---

### 4.4 GPU

V1 required fields:

- zero or more GPU/display adapter names
- provider/vendor text when available

V1 explicitly does **not** require reliable VRAM capacity.

Initial source:

- WMI `Win32_VideoController`, best-effort

Why best-effort:

Microsoft documents that some `Win32_VideoController` properties can be inaccurate depending on driver/model support. AgenStart must therefore not build hard compatibility rules on questionable fields such as WMI-reported VRAM without a later, more robust collector.

Future hardening option:

- DXGI adapter enumeration for more reliable GPU capability information if Creation/AI profiles need it.

Privilege requirement: **standard user**.

Failure policy:

- GPU state may be `Unknown`.
- A GPU inventory failure never aborts the full machine snapshot.

Do not collect GPU device instance identifiers unless a concrete compatibility use case requires them.

---

### 4.5 Storage

Required fields for local fixed drives:

- root/mount path (for example `C:\`)
- drive type
- total size bytes
- available free space bytes
- whether the drive is the system drive

Preferred source:

- `.NET System.IO.DriveInfo`

Only ready, relevant local drives are used for installation planning.

Privacy rule:

- Do **not** read directory listings.
- Do **not** read file names.
- Do **not** collect volume labels because labels are user-defined and unnecessary for recommendation logic.

Recommendation logic should primarily use free space on the **system drive** and, later, a user-selected installation destination where a provider supports one.

Privilege requirement: **standard user**.

---

### 4.6 WinGet capability

Required fields:

- availability state
- WinGet version when available

Normalized states:

```text
Available
Unavailable
TimedOut
Failed
```

Initial detection strategy:

1. Attempt to invoke `winget --version` as the current standard user.
2. Apply a short timeout.
3. Capture exit code and sanitized stdout/stderr.
4. Parse only the version value required by AgenStart.

WinGet officially supports `-v` / `--version`; `--disable-interactivity` is available for commands where interaction must be prevented.

The inventory layer does not install or repair WinGet. Remediation belongs to package-provider/onboarding logic.

Privilege requirement: **standard user**.

---

## 5. Runtime and capability prerequisites

Because AgenStart is intended for fresh PCs, the application must avoid unnecessary prerequisites.

V1 capability snapshot may expose:

- WinGet available/version
- Windows PowerShell available/version (optional)
- PowerShell 7 (`pwsh`) available/version (optional)
- network connectivity state only when needed immediately before online package operations

These are **capabilities**, not identity data.

Do not add a prerequisite collector merely because a technology exists. A prerequisite enters this model only when AgenStart has a real feature that depends on it.

---

## 6. Explicit privacy exclusions

AgenStart machine inventory must not collect the following:

### Identity

- Windows user name
- Microsoft account email
- domain identity unless a future enterprise policy feature explicitly requires it
- computer/device name

### Stable hardware identifiers

- motherboard serial number
- BIOS serial number
- device serial number
- hardware UUID
- TPM identity
- MAC addresses
- disk serial numbers

### Network identity

- public IP address
- local IP addresses by default
- Wi-Fi SSID/history
- saved wireless profiles

### User content

- browser history
- bookmarks
- cookies
- passwords or credential stores
- document names/content
- source-code files
- Downloads/Desktop/Documents contents
- photos/videos
- clipboard contents
- recent-file lists

### Behavioral data

- application usage history
- keyboard/mouse activity
- browsing activity

### Derived fingerprinting

AgenStart must not combine otherwise harmless hardware properties into a persistent device fingerprint for tracking.

---

## 7. Normalized machine model

The cross-platform core should consume a model conceptually equivalent to:

```csharp
public sealed record MachineSnapshot(
    PlatformSnapshot Platform,
    CpuSnapshot Cpu,
    MemorySnapshot Memory,
    IReadOnlyList<GpuSnapshot> Gpus,
    IReadOnlyList<StorageSnapshot> Storage,
    PackageManagerSnapshot PackageManager,
    CapabilitySnapshot Capabilities,
    IReadOnlyList<InventoryDiagnostic> Diagnostics,
    DateTimeOffset CapturedAtUtc);
```

### Platform

```csharp
public sealed record PlatformSnapshot(
    PlatformKind Kind,
    string? Edition,
    string? DisplayVersion,
    Version? Version,
    string? Build,
    string? Revision,
    MachineArchitecture Architecture,
    MachineArchitecture ProcessArchitecture);
```

### CPU

```csharp
public sealed record CpuSnapshot(
    string? Model,
    MachineArchitecture Architecture,
    int LogicalProcessorCount);
```

### Memory

```csharp
public sealed record MemorySnapshot(
    ulong? TotalPhysicalBytes,
    ulong? AvailablePhysicalBytes);
```

### GPU

```csharp
public sealed record GpuSnapshot(
    string? Name,
    string? Vendor);
```

### Storage

```csharp
public sealed record StorageSnapshot(
    string Root,
    StorageKind Kind,
    long? TotalBytes,
    long? AvailableBytes,
    bool IsSystemDrive);
```

### Package manager

```csharp
public sealed record PackageManagerSnapshot(
    PackageManagerKind Kind,
    CapabilityState State,
    Version? Version);
```

The exact implementation may evolve, but these boundaries should remain stable: **domain-facing models do not expose RegistryKey, ManagementObject, process output or other Windows implementation types.**

---

## 8. Availability model

Do not use fake defaults such as `0 GB RAM` or `0 cores` to represent failed collection.

Collectors distinguish:

```text
Known        -> collected and valid
Unavailable  -> capability does not exist
Unknown      -> could not determine
Failed       -> collector encountered an error
TimedOut     -> provider exceeded allowed duration
```

Where practical, nullable values represent absence while `InventoryDiagnostic` records why a collector could not provide information.

Business rules must explicitly define their behaviour when a required property is unknown.

Example:

- If total RAM is unknown, AgenStart can still recommend lightweight general software.
- It must not confidently recommend software whose minimum RAM requirement cannot be validated without explaining that compatibility is unverified.

---

## 9. Collector architecture

Prefer multiple focused collectors over one large `GetEverything()` implementation.

```text
WindowsMachineInventoryProvider
    |
    +-- WindowsPlatformCollector
    +-- WindowsCpuCollector
    +-- WindowsMemoryCollector
    +-- WindowsGpuCollector
    +-- WindowsStorageCollector
    `-- WinGetCapabilityCollector
```

Each collector:

- is independently testable;
- has a timeout where external providers are involved;
- returns normalized data + diagnostics;
- cannot mutate machine state;
- does not require administrator privileges.

The provider aggregates successful results into one snapshot.

---

## 10. Performance expectations

Target for normal supported hardware:

- basic snapshot should feel effectively immediate to the user;
- fast synchronous/native calls should complete first;
- WMI and process checks should run asynchronously where appropriate;
- slow optional collectors must not freeze the UI thread;
- external process checks such as WinGet must use explicit timeouts and cancellation.

A later implementation issue should define measured startup/inventory budgets rather than inventing premature hard numbers here.

---

## 11. Security expectations

Inventory is read-only.

No inventory collector may:

- execute arbitrary shell provided by catalogue data;
- write Registry values;
- install software;
- enable Windows features;
- request elevation;
- modify services;
- modify environment variables.

Those responsibilities belong to later execution/package-management modules with separate privilege boundaries.

---

## 12. Cross-platform continuity

The V1 implementation is Windows-specific, but the normalized model must allow a future macOS provider.

Example mapping:

```text
MachineSnapshot
    |
    +-- Windows
    |     +-- WinGet
    |     +-- Windows Registry / Win32
    |
    `-- macOS (future)
          +-- Homebrew / Cask
          +-- macOS native/system APIs
```

Core recommendation rules should operate on capabilities such as:

- architecture
- RAM
- available storage
- package manager availability
- platform kind/version

rather than directly checking Windows-specific APIs.

---

## 13. V1 decisions

Accepted for V1:

- Windows 10/11 x64 initially; architecture model remains extensible for Arm64
- standard-user inspection only
- Registry + .NET APIs + minimal Win32 P/Invoke
- WMI as a best-effort specialized provider, not the universal inventory mechanism
- `winget --version` process probe for WinGet capability
- no persistent hardware identifiers
- no file/content scanning
- no installed-application enumeration in this module (tracked separately by #5)

Deferred:

- reliable GPU VRAM/capability via DXGI
- battery/portable-device capability
- virtualization/WSL inventory
- network adapter inventory
- enterprise/domain state
- macOS implementation

---

## 14. Implementation impact

This document establishes the future project boundary:

```text
src/
├── AgenStart.Core/
│   └── Machine/
├── AgenStart.Application/
│   └── Inventory/
├── AgenStart.Platform.Windows/
│   └── Inventory/
└── tests/
    ├── AgenStart.Core.Tests/
    └── AgenStart.Platform.Windows.Tests/
```

The exact solution structure may be finalized when source initialization begins, but Windows APIs must not leak into `AgenStart.Core`.

---

## 15. References

- Microsoft .NET `Environment.OSVersion`: https://learn.microsoft.com/dotnet/api/system.environment.osversion
- Microsoft .NET `RuntimeInformation.OSArchitecture`: https://learn.microsoft.com/dotnet/api/system.runtime.interopservices.runtimeinformation.osarchitecture
- Microsoft .NET `Microsoft.Win32.Registry`: https://learn.microsoft.com/dotnet/api/microsoft.win32.registry
- Microsoft .NET `System.IO.DriveInfo`: https://learn.microsoft.com/dotnet/api/system.io.driveinfo
- Win32 `GlobalMemoryStatusEx`: https://learn.microsoft.com/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex
- WMI `Win32_Processor`: https://learn.microsoft.com/windows/win32/cimwin32prov/win32-processor
- WMI `Win32_VideoController`: https://learn.microsoft.com/windows/win32/cimwin32prov/win32-videocontroller
- Windows Package Manager / WinGet: https://learn.microsoft.com/windows/package-manager/winget/
