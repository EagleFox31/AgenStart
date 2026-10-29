# Walkthrough — Catalogue 0.3.0 and setup profile v2 (2026-10-11, Cursor agent)

## Scope

Continue development without touching the frontend extraction WIP (`InstallationView`, `ReportView`, `MainWindow.axaml(.cs)`). Two unblocked product slices: curated runtimes already packaged as icons, and the Application-layer half of ADR-0003 step 4.

## Changed paths

- `docs/product/catalogue.fixtures.json` — catalogue `0.3.0`; `nodejs`, `postgresql`
- `tests/AgenStart.Catalogue.Tests/SoftwareCatalogueLoaderTests.cs`
- `src/AgenStart.Application/AgenStart.Application.csproj` — references Core
- `src/AgenStart.Application/Profiles/SetupProfileContracts.cs`
- `src/AgenStart.Application/Profiles/SetupProfileValidator.cs`
- `src/AgenStart.Application/Profiles/SetupProfileSerializer.cs`
- `tests/AgenStart.Application.Tests/SetupProfileSerializerTests.cs`
- `docs/product/setup-profile.md`, `docs/product/setup-profile.schema.json`
- `docs/engineering/lessons-learned.md` (LL-003)

## Evidence actually gathered

- Catalogue tests: 24/24 (test executable).
- Recommendation golden tests: 22/22 (frozen 0.2.0 catalogue unchanged).
- Application tests: 30/30 after restore with `NUGET_PACKAGES` pointed at the user cache (sandbox NuGet cache still NU5037).
- Desktop Release `--no-restore`: 0 warnings, 0 errors.
- `nodejs` / `postgresql` icons were already mapped in `AppIconService`; no new artwork.
- WinGet ids used: `OpenJS.NodeJS.LTS`, `PostgreSQL.PostgreSQL.17` (verified 2026-10-11 earlier in this session). Installation not re-tested.

## Not done

- Desktop still builds setup documents with `ToProfileId(UserProfile)` (LL-003). v2 files with a single canonical `profileId` will import on this build; multi-select export from the UI still fails until MainWindow is updated.
- No new logos, so Chrome / Python / game launchers were not added (icon smoke requires packaged artwork).
- `entertainment` / `family` remain planned.

## Handoff

- Frontend: when wiring export, pass `ProfileIds` from `UsageProfilesView` / `ProfileRegistry.Expand` instead of `Enum.ToString()`.
- Next catalogue adds need an SVG/PNG in `AppIconService` before they can ship.
