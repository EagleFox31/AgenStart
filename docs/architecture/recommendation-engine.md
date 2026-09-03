# Explainable recommendation engine

Status: Implemented for MVP rules

Related issue: #6

## Purpose

The recommendation engine decides which curated applications are appropriate for the selected AgenStart profile using only normalized domain state:

- selected user profile;
- cross-platform machine capabilities;
- normalized installed-software state;
- curated catalogue recommendation metadata and constraints.

The engine is pure domain logic. It does not launch installers, invoke WinGet, access the Registry, show UI, or mutate the machine.

## Dependency boundary

```text
AgenStart UI / Application
        |
        v
RecommendationEngine
   |            |
   v            v
AgenStart.Core  AgenStart.SoftwareInventory
   |
   +-- MachineSnapshot
   `-- catalogue recommendation contracts
```

`AgenStart.Core` has no dependency on Avalonia, WinGet, Windows APIs or the software-inventory implementation.

## Supported profiles

The initial profiles match the catalogue contract:

```text
Personal
Development
Business
Creation
Training
```

A catalogue application participates in a plan only when it has exactly one recommendation rule for the selected profile.

Recommendation levels are:

```text
Essential
Recommended
Optional
```

Essential and Recommended applications are preselected when all safety/compatibility checks pass. Optional applications remain visible but are not preselected.

Preselection is only a recommendation. The user validates and may change the final selection before installation; execution is owned by later installation-planning/orchestration work.

## Explainability

Every decision keeps the catalogue `reasonKey` and exposes human-readable reasons describing why the application is present and why it was selected, excluded or withheld.

Examples:

- profile fit;
- already installed;
- installed state could not be verified;
- insufficient RAM;
- insufficient storage;
- unsupported architecture;
- GPU required but unavailable;
- compatibility value unknown;
- catalogue lifecycle unavailable;
- conflict with an installed application;
- conflict with a higher-priority recommendation;
- machine meets minimum but not recommended capability.

The engine does not rely on opaque scores for MVP decisions.

## Deterministic rule order

For an application that belongs to the selected profile, the engine applies rules in a stable order:

1. handle normalized installed-software state;
2. reject deprecated/blocked catalogue entries for new installation;
3. verify platform support;
4. enforce architecture constraints;
5. enforce minimum RAM;
6. enforce minimum free storage on the system drive;
7. enforce required GPU capability;
8. add non-blocking advisories for recommended capabilities;
9. resolve conflicts deterministically;
10. produce the suggested default selection.

The same normalized input must produce the same ordered plan.

## Installed software behavior

The engine consumes the states produced by issue #5:

### Installed

The application is returned as `AlreadyInstalled` and is never preselected for another installation.

### Missing

The engine may recommend the application when catalogue and machine rules pass.

### Unknown

The application is returned as `InventoryUnknown` and is not preselected. AgenStart does not guess that an application is missing and thereby create a duplicate install proposal.

If the recommendation engine receives no normalized software state at all for a profile application, it uses the same fail-conservative behavior.

## Capability behavior

Minimum catalogue requirements are hard constraints.

### Known below minimum

The decision is `Incompatible`.

### Required value unknown

The decision is `CompatibilityUnknown` and is not preselected. This follows the machine-inventory rule that unknown capability values are never silently interpreted as compatible.

### Recommended value not reached

If minimum requirements pass but a known value is below the catalogue's recommended threshold, the application remains recommendable with an explanatory advisory.

## Platform and architecture

Catalogue platform support must be explicitly `Supported` for the detected platform.

`Planned`, `Unsupported`, or absent platform support cannot enter the default installation selection.

Machine architecture must satisfy both the platform-support architecture list and the application's minimum architecture list. An unknown architecture produces `CompatibilityUnknown`.

## GPU capability

GPU requirements use an explicit normalized state:

```text
Available
Unavailable
Unknown
```

A required unavailable GPU is incompatible. A required unknown GPU state is unverified and therefore not preselected.

## Conflict handling

Catalogue conflicts use canonical AgenStart application IDs.

### Conflict with installed software

A recommendation conflicting with an already-installed application is marked `Conflict` and withheld from default selection.

### Conflict between recommendations

The engine resolves conflicting candidates deterministically:

1. Essential beats Recommended;
2. Recommended beats Optional;
3. equal levels use canonical application ID as the stable tie-breaker.

The losing recommendation remains visible as `Conflict` with an explanation.

This prevents contradictory default plans while preserving user visibility into the decision.

## Invalid catalogue state

The engine fails closed when it receives invalid normalized catalogue input, including:

- duplicate canonical application IDs;
- duplicate recommendation rules for the same application/profile;
- self dependencies/conflicts;
- dependency/conflict references to unknown canonical IDs;
- duplicate platform support rules for the active platform;
- negative capability requirements.

Full JSON Schema and catalogue cross-entry validation remain part of the catalogue boundary; the engine still rejects invalid domain input rather than relying on callers to be perfect.

## Dependencies

Catalogue dependency references are validated in this issue, but dependency expansion into an executable installation plan is intentionally not performed here.

Why:

```text
recommendation = what is appropriate for the user
installation plan = exact ordered work that will be executed
```

Dependency expansion, execution ordering, retries and provider operations belong with installation orchestration (#7). This avoids making the recommendation engine an installer in disguise.

## Machine model initialization

Issue #6 initializes `AgenStart.Core` with the cross-platform machine contracts specified by the machine-inventory architecture document:

- PlatformSnapshot
- CpuSnapshot
- MemorySnapshot
- GpuSnapshot
- StorageSnapshot
- PackageManagerSnapshot
- CapabilitySnapshot
- MachineSnapshot

Windows collectors remain outside Core. Future Windows machine-inventory implementation maps platform APIs into these contracts; future macOS support can do the same without changing recommendation rules.

## Testing

The recommendation test suite is platform-independent and runs on Linux with .NET 10 because the engine has no Windows dependency.

Representative scenarios cover:

- all five initial profiles;
- human-readable reasons;
- already-installed applications;
- unknown installed state;
- insufficient and unknown RAM;
- GPU minimum requirements;
- optional/preselection behavior;
- installed conflicts;
- deterministic recommendation conflicts;
- recommended-capability advisories;
- duplicate catalogue data;
- applications outside the selected profile.

No test launches an installer or package manager.
