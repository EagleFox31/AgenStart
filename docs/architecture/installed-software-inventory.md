# Installed software inventory

Status: Implemented for Windows MVP

Related issue: #5

## Purpose

AgenStart must know which curated applications are already installed before it recommends or installs software. Detection is local, read-only, privacy-minimal, and conservative: uncertainty becomes `Unknown`, never a guessed `Installed` state.

## Windows inventory sources

The Windows MVP combines two complementary sources.

### WinGet structured export

AgenStart runs WinGet directly with a typed argument list and exports installed packages separately for the trusted `winget` and `msstore` sources.

The command shape is:

```text
winget export --output <AgenStart temp json> --source <trusted source> --include-versions --disable-interactivity
```

The JSON export is parsed structurally. AgenStart records:

- provider ID (`winget`);
- package ID;
- trusted package source;
- installed version when present;
- installation scope when WinGet supplies it.

AgenStart never parses the human-readable `winget list` table.

Only the trusted MVP sources `winget` and `msstore` are queried. User-added/custom WinGet sources are not automatically trusted.

### Windows uninstall registry

AgenStart reads the standard uninstall inventory in read-only mode from:

```text
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall
HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall
```

Both 32-bit and 64-bit registry views are inspected. The collector reads only software metadata required for detection:

- `DisplayName`;
- `Publisher`;
- `DisplayVersion`;
- `SystemComponent` to omit hidden system components.

No personal user files, browser data, document folders, file names, serial numbers, MAC addresses, or unrelated registry data are inspected.

## Snapshot model

Collectors return normalized `InstalledSoftwareRecord` values plus one `InventorySourceStatus` per source. The composite inventory provider merges and deduplicates records into a timestamped `InstalledSoftwareSnapshot`.

Source states are explicit:

- `Complete`
- `Partial`
- `Unavailable`
- `Failed`
- `TimedOut`

A partial source does not invalidate records successfully collected from other sources.

## Catalogue matching

Runtime catalogue entries are represented to the resolver as `SoftwareDetectionTarget` values containing:

- canonical AgenStart application ID;
- display name;
- expected publisher;
- exact provider package references;
- optional exact registry display-name aliases.

Matching intentionally avoids fuzzy execution-time guesses.

### Provider identity

The strongest match is the tuple:

```text
provider ID + package source + package ID
```

For example:

```text
winget + winget + Microsoft.VisualStudioCode
```

A provider identity mapped to more than one canonical application is ambiguous and does not silently confirm installation.

### Registry identity

Registry evidence requires an exact normalized display-name match and a compatible expected publisher. Explicit registry aliases may be supplied for curated products whose Windows display name differs from the catalogue name.

If one registry record matches more than one catalogue application, every affected application remains `Unknown` unless independent unambiguous evidence confirms it.

## Presence states

### Installed

An application is `Installed` when at least one unambiguous exact provider or registry identity is observed.

When exactly one installed version is observed, AgenStart reports it. If multiple different versions are present, the application remains `Installed` but the normalized version is left empty and a diagnostic is attached.

### Missing

An application is `Missing` only when the inventory source required to prove absence completed successfully and no matching identity was observed.

### Unknown

An application is `Unknown` when AgenStart cannot safely prove either presence or absence, including:

- ambiguous identity;
- unavailable provider;
- partial source read;
- failed or timed-out source;
- insufficient trustworthy evidence.

This fail-conservative behavior prevents duplicate install proposals caused by guessed absence.

## Unknown software

Software that does not map to the curated catalogue is retained in the raw snapshot but does not break detection. `SoftwareDetectionResult.UnmappedRecordCount` makes that condition observable without treating arbitrary installed software as an error.

## Failure and cleanup behavior

WinGet exports are written only to AgenStart-generated temporary paths and removed on a best-effort basis after parsing. The collector never targets user-owned files for deletion.

Expected registry access failures degrade the registry source to `Partial` rather than failing the whole inventory.

Cancellation is propagated to the caller.

## Testing

Fixture-based tests cover representative WinGet exports and edge cases including:

- exact package identity;
- version and scope capture;
- source separation;
- unknown packages;
- exact registry matching;
- ambiguous registry matches;
- `Missing` versus `Unknown` based on source completeness;
- multiple installed versions;
- collector aggregation and deduplication.

## Boundary with later features

Issue #5 detects and normalizes installed state. It does not decide what should be recommended. The recommendation engine consumes these normalized states in issue #6 so already-installed applications can be excluded or explained correctly.
