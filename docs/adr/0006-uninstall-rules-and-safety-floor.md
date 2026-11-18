# ADR-0006: Uninstall leftover rules and hard-coded safety floor

- Status: Accepted
- Date: 2026-11-18
- Decision owners: AgenStudio / AgenStart
- Related: #119, ADR-0002 §8, ADR-0003, ADR-0005, `docs/design/2026-11-18-uninstall-module.md`

## Context

Finding leftovers means guessing which folders, keys and shortcuts belong to a program. Guessing in
code makes every new product a code change. Bulk Crap Uninstaller detects leftovers in imperative
code. Guessing from configuration alone lets one bad rule target `%APPDATA%` or `Program Files`.

ADR-0003 already treats data-driven registries as product configuration, loaded with a schema.
ADR-0002 §8 treats shipped policy data as a trust boundary.

## Decision

### 1. Rules are declarative and versioned

- **Files.** Rules live in `docs/product/uninstall-rules.json` and are validated against
  `docs/product/uninstall-rules.schema.json`.
- **Packaging.** They are packaged like the software catalogue: Content plus EmbeddedResource.
- **Versions.** Each file declares a `schemaVersion` and a `rulesVersion`.
- **Loading.** The loader uses the same pattern as `SoftwareCatalogueLoader`. It fails closed on an
  unknown schema version, an unknown field, an unknown placeholder or an invalid `kind`/`dataClass`.
  With no valid rule set, the module shows the official uninstall only, with no leftovers.

The file holds:

- `genericRules`: patterns built from `{Publisher}` / `{Product}` under known-folder roots, each with
  a base confidence;
- `productRules`: exact matches on publisher and display name, with explicit leftover paths and data
  classes;
- `extraForbiddenPaths` and `genericNameStoplist`;
- volume thresholds (ADR-0005 §8).

Placeholders such as `{LocalAppData}`, `{RoamingAppData}`, `{LocalLow}`, `{ProgramData}` and
`{Documents}` resolve through `SHGetKnownFolderPath` at runtime, never through hard-coded paths.

### 2. The safety floor is code, and configuration can only harden it

`PathSafetyPolicy` contains a hard-coded floor that no rule file can relax. Rule files can only add
forbidden paths and stop-words.

The floor forbids these locations, both as targets and as ancestors of targets:

- **System and program roots:** drive roots, `%WINDIR%`, `Program Files`, `Program Files (x86)`,
  `Common Files`, `ProgramData`, `Users`.
- **The user's own folders:** the user profile, and the root of every known folder (Documents,
  Desktop, Local, Roaming, LocalLow, Saved Games, OneDrive), plus `Temp`.
- **Package and AgenStart folders:** `WindowsApps`, `Packages`, `%LOCALAPPDATA%\AgenStart`.
- **Registry:** `HKLM\SOFTWARE` at depth < 2, and `Microsoft`, `Classes` and `Policies` keys.

The floor also requires:

- **Depth.** A target is at least one level below an allowed root. A publisher-level folder is
  removed only when it is empty, or when all of its children are selected.
- **Strict matching.** Names are compared with case-insensitive ordinal equality, after
  normalisation (version, `(x64)` and architecture suffixes stripped). There is no substring or regex
  matching. A token is at least 4 characters and not in the stoplist.
- **No shared folders.** A target is excluded (score −100, shown as "shared, ignored") when it, or a
  child, matches the publisher, name or `InstallLocation` of another installed program.
- **Shortcuts.** A shortcut is a candidate only if its resolved target is under the removed
  `InstallLocation`, or is the uninstalled executable.

### 3. Confidence scoring follows BCU's method, not its code

The scoring adopts the signed-weight approach of BCU's `ConfidenceRecords`:

| Signal | Weight |
|---|---|
| Uninstall key | +20 |
| Publisher match | +4 |
| Directly under a known folder | −1 |
| Executables present | −4 |
| Publisher still used by another program | −4 |
| Folder still in use | −7 |
| Store app | −10 |

The total sets the confidence level:

| Score | Level | Default selection |
|---|---|---|
| ≥ 8 | high | pre-selected, unless `UserData` |
| 4–7 | medium | not selected |
| < 4 | low | not shown; only in the exported JSON plan |

No BCU source is copied. If a file is ever ported, an Apache-2.0 NOTICE and a superseding ADR are
required.

### 4. Each item carries the id of its rule

Every `PlanItem` records the `RuleId` that produced it and the reasons behind its score. The UI shows
them in the item's expandable justification, so any false positive can be traced to a single rule.

## Alternatives considered

- **Embed BCU's `UninstallTools`.** Rejected. It targets `net8.0-windows` with WinForms, has no NuGet
  package, pulls in `KlocTools`, `ObjectListView` and `System.Management`, and its detection is
  code, not configuration.
- **Configuration only, with no code floor.** Rejected. One malformed or malicious rule could target
  a root folder.
- **Regex patterns in rules.** Rejected. They are too permissive for a deletion boundary and hard to
  review.

## Consequences

- New products are supported by editing a rule file, but the floor itself changes only in code, with
  tests and review.
- Some genuine leftovers stay undetected, for example under generic names in the stoplist or in
  shared folders. This is accepted: a missed leftover is cheaper than deleted user data.

## Verification

This ADR is implemented when tests prove that:

- the loader rejects an unknown schema version, unknown fields and unknown placeholders;
- property tests find no generated plan item that is a forbidden root, an ancestor of one, shared, or
  reached through a reparse point;
- no rule file content can remove an entry from the floor;
- golden plans on anonymised machine snapshots stay stable across rule changes unless their expected
  output is updated deliberately.
