# ADR-0003: Data-driven usage-profile registry

- Status: Accepted
- Date: 2026-10-11 (proposed and accepted)
- Decision owners: AgenStudio / AgenStart
- Related: ADR-0001, ADR-0002, `docs/product/software-catalogue.md`, `docs/product/setup-profile.md`

## Context

Usage profiles (Personal, Development, Business, Creative, Learning, Gaming) are modelled as a `[Flags] enum UserProfile` in `AgenStart.Core`. Every layer must know each profile by name:

| Surface | Hard-coded knowledge |
|---|---|
| `AgenStart.Core/Catalogue/ApplicationDefinition.cs` | enum member and bit position |
| `AgenStart.Catalogue/SoftwareCatalogueLoader.cs` | string → enum `switch`, aliases |
| `docs/product/software-catalogue.schema.json` | JSON Schema `enum` of profile ids |
| `AgenStart.Recommendations/RecommendationEngine.cs` | supported-profile list and label `switch` |
| `AgenStart.Desktop/Views/UsageProfilesView.axaml(.cs)` | one checkbox per profile, three `if` chains |
| `AgenStart.Desktop/Views/MainWindow.axaml(.cs)` | legacy single-select radio list (replaced at runtime, still maintained) and setup import/export mapping |
| `site/index.html`, `site/app.js` | active and upcoming profiles with translations |
| tests | per-profile assertions |

Adding a profile therefore touches about eight places in four layers. Four more profiles are already announced on the public site (Cybersecurity, Data & AI, Cloud / DevOps, IT / System Administration).

### Defect found while analysing the brownfield state

Setup import/export derives the portable `profileId` from `UserProfile.ToString().ToLowerInvariant()` (`MainWindow.axaml.cs`, `ToProfileId` / `TryParseProfileId`). Verified on 2026-10-11 against the compiled `AgenStart.Core`:

| Selection | Exported `profileId` | Result |
|---|---|---|
| Personal | `personal` | round-trips |
| Creative | `creation` (alias name wins) | round-trips only through the legacy alias |
| Learning | `training` (alias name wins) | round-trips only through the legacy alias |
| Gaming | `gaming` | **import rejects it** (`TryParseProfileId` has no `gaming` case) |
| Personal + Development | `personal, development` | **fails** `SetupProfileValidator` (`^[a-z0-9][a-z0-9._-]{0,99}$`) |

Setup profile v1 also stores a single `profileId`, which cannot represent the multi-select model shipped in v0.4.

### External guidance (researched 2026-10-11)

- Microsoft Framework Design Guidelines — *"DO NOT use an enum for open sets"*; enums are for small closed sets. <https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/enum>
- Google AIP-126 — enums only when values change roughly no more than once a year; otherwise a documented string field using a uniform case such as `kebab-case`; use existing standards (e.g. language tags) for well-known value sets. <https://google.aip.dev/126>
- Windows 11 "Let's customize your experience" / *Device usage* — a multi-select set of usage intents (Development, Gaming, Family, Creativity, School, Entertainment, Business), changeable later in Settings. <https://support.microsoft.com/en-us/accessibility/windows/use-a-screen-reader-to-set-up-windows>
- WinGet Configuration (DSC v3) — Microsoft's declarative desired-state format for reproducible machine setup; can include script resources. <https://learn.microsoft.com/en-us/windows/package-manager/configuration/create-v3>
- UniGetUI bundles — explicit package lists that may carry custom CLI arguments and pre/post commands; no usage-profile taxonomy. <https://github.com/marticliment/UniGetUI>

These are design guidelines and benchmarks, not formal standards.

## Decision

### 1. Profiles become catalogue data

A versioned registry `docs/product/profiles.json`, validated by `docs/product/profiles.schema.json`, is shipped alongside the catalogue and embedded the same way. It is the single source of truth for profile identity, status, order and display text.

```json
{
  "schemaVersion": "1.0.0",
  "profiles": [
    {
      "id": "gaming",
      "status": "active",
      "order": 60,
      "icon": "gamepad",
      "aliases": [],
      "label": { "en": "Gaming", "fr": "Gaming" },
      "description": {
        "en": "Game launchers, voice chat, monitoring and streaming.",
        "fr": "Lanceurs de jeux, chat vocal, monitoring et streaming."
      },
      "reasonLabel": { "en": "gaming", "fr": "le jeu" }
    },
    {
      "id": "creative",
      "status": "active",
      "order": 40,
      "aliases": ["creation"],
      "label": { "en": "Creative", "fr": "Créatif" }
    },
    {
      "id": "data-ai",
      "status": "planned",
      "order": 70,
      "label": { "en": "Data & AI", "fr": "Data & IA" }
    }
  ]
}
```

- `id`: stable, lowercase `kebab-case` (`^[a-z][a-z0-9-]{1,39}$`), never reused for a different meaning.
- `status`: `active` (selectable), `planned` (known, not selectable, may be shown as upcoming), `retired` (not selectable, still accepted on import so old files remain readable).
- `aliases`: legacy ids accepted on read and normalized to `id`; never written.
- Localized text is keyed by BCP 47 language tags; `en` is mandatory.

### 2. Profile identity is a validated string, not an enum

`UserProfile` is replaced by `ProfileId` (a validated value type) and `ProfileDefinition`. A user selection is an ordered, de-duplicated set of `ProfileId`, not a bit mask. The recommendation engine takes ordering and labels from the registry; its ranking logic is unchanged.

### 3. Fail closed on load

Loading refuses the catalogue when any of these hold, consistent with ADR-0002 section 9:

- duplicate profile `id`, or an alias colliding with an `id` or another alias;
- an application recommendation references an unknown or alias-only profile;
- an `active` profile has no application recommendation;
- a required `en` label is missing.

The catalogue JSON Schema replaces its profile `enum` with the id pattern; referential integrity is enforced by the loader and tests, because JSON Schema cannot cross-reference two files.

### 4. Setup profile contract is versioned, not silently changed

Setup profile **v2** stores `profileIds: string[]` (minimum one, registry ids). Readers keep accepting **v1** `profileId`, resolving it through aliases (`creation`, `training`, `study`). Writers emit v2 only. Export derives ids from the registry, never from `Enum.ToString()`.

### 5. UI renders profiles from the registry

`UsageProfilesView` binds an `ItemsControl` to the active profiles (`ProfileOptionViewModel` with `IsSelected`). The legacy single-select radio list in `MainWindow.axaml` and its handlers are removed.

### 6. Delivery gates

- **Golden non-regression test** (written first, against the current enum implementation): a fixed machine snapshot and installed-software state; the full `RecommendationPlan` for each of the six profiles and representative combinations is serialized and must remain byte-identical after the migration.
- **Round-trip tests**: export → import for every active profile and for multi-profile selections; v1 fixtures (`creation`, `training`, `study`, `gaming`) must import.
- **Impact-aware CI**: `docs/product/profiles.json` and its schema are cross-cutting and must trigger catalogue, recommendation, desktop and landing workflows, and nothing else. Test steps run `dotnet test --project <csproj>` (see `docs/engineering/lessons-learned.md`, LL-001).

### 7. Profile taxonomy: Family and Entertainment

Aligned with the Windows 11 Device-usage intents, the registry adds two profiles:

| id | Initial status | Scope |
|---|---|---|
| `family` | `planned` | shared or children's PCs: parental controls, safe browsing, learning-friendly defaults |
| `entertainment` | `active` (2026-10-11) | music, video and media-center use |

`personal` is **kept** as the everyday baseline (archives, media playback, browser, password manager, PDF). Windows intents are additive personalization on top of a working system, and AgenStart still needs a baseline that applies to every machine. Entertainment-type entries currently under `personal` (e.g. Discord) are re-evaluated when `entertainment` becomes active; until then current `personal` recommendations stay unchanged so the golden test remains valid.

Activation criteria (both profiles): the registry is in place (rollout step 2), at least six curated applications with verified exact WinGet ids, explained rules, and tests.

Coverage check on 2026-10-11 (`winget show --id <id> --exact --source winget`):

- `entertainment` — verified on the `winget` source: `Spotify.Spotify`, `XBMCFoundation.Kodi`, `Plex.Plex`, `Plex.Plexamp`, `Stremio.Stremio`, `Audacious.MediaPlayer`; plus existing VLC. Not found: `MusicBee.MusicBee`, `foobar2000.foobar2000`.
- `family` — **blocked on source policy**: Microsoft Family Safety is only available from the `msstore` source (`9PDJDJS743XF`), which AgenStart does not trust (ADR-0002 section 5). Activating `family` requires either a separate security decision on allowing `msstore` for named Microsoft-published packages, or a credible `winget`-only catalogue. Until then it stays `planned`.

### Reuse decision (RAIDER)

- **Build** the registry: small, domain-specific, no maintained library models "usage intent → curated recommendations".
- **Learn** from Microsoft and Google enum guidance and from the Windows 11 Device-usage intent model.
- **Adapt later** WinGet Configuration as an *export* format for setups (roadmap phase 5), restricted to `WinGetPackage` resources; never as the internal profile model, and never importing script resources (ADR-0002).
- UniGetUI-style bundles are not adopted: custom arguments and pre/post commands conflict with ADR-0002.

## Rollout

| Step | Scope | Ownership constraint |
|---|---|---|
| 1 | Golden and round-trip tests against current behaviour | none |
| 2 | `profiles.json` + schema, `ProfileId`, loader, engine; enum kept as a thin adapter until step 4 | Core / Catalogue / Recommendations only |
| 3 | `UsageProfilesView` generated from the registry | file not part of the frontend refactor in progress |
| 4 | Setup profile v2, import/export fix, removal of legacy radio list and `UserProfile` | touches `MainWindow.axaml(.cs)`: starts only after the frontend refactor (`docs/engineering/frontend-audit-2026-10.md`) is merged |
| 5 | Optional: generate site profile section from `profiles.json` via `site/build-assets.mjs` | landing surface only; reruns must produce no diff |

Steps 1–3 are independently releasable. Each step keeps all existing tests green.

## Consequences

### Positive

- Adding a profile is a data change (`profiles.json` + catalogue rules + tests), with no C#, XAML or translation edits spread across layers.
- Planned profiles can be declared before they are selectable, matching the public site.
- Fixes the multi-profile and Gaming setup import/export defect.
- One source of truth for app, catalogue validation, tests and (optionally) the site.

### Negative / trade-offs

- Loses compile-time checking of profile names; mitigated by fail-closed load validation and tests on the shipped registry.
- Setup profile v2 is a contract change; older AgenStart builds cannot read v2 files.
- Profile display text moves from XAML into data; desktop localization strategy must handle it.
- Migration touches the core domain type used by every layer; the golden test is mandatory, not optional.

## Alternatives considered

### Keep the enum and add bits per profile
Rejected. Contradicts the "no enum for open sets" guidance, keeps the eight-place change surface, and the flags `ToString()` behaviour is what produced the import/export defect.

### Keep the enum, centralize metadata in a static C# class
Cheaper and keeps compile-time names, but every profile still needs a code change and release, and the site still duplicates the list. Acceptable only as an interim step; not the target.

### Use WinGet Configuration files as the profile model
Rejected. They describe a desired machine state, not a usage intent with ranked, explained recommendations, and they can carry script resources that ADR-0002 forbids.

### Mirror Windows Device-usage intents exactly (replace Personal)
Rejected. Windows has no baseline intent; AgenStart needs one, and removing `personal` would break existing setup files and catalogue rules for no user benefit. Family and Entertainment are added alongside it (decision 7).

## Open questions

- Security: allow the `msstore` source for named Microsoft-published packages (needed for `family`)? Requires its own review under ADR-0002.
- Can AgenStart read the Windows *Device usage* selection locally to preselect profiles? Storage location, stability and privacy impact are not yet verified.
- Desktop localization approach for registry text (current UI is English-only).
