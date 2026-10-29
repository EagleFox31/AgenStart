# Walkthrough — Usage-profile registry rollout steps 2–3 (2026-10-11, Cursor agent)

## Scope

ADR-0003 rollout step 2 (registry, `ProfileId`, loader, engine; `UserProfile` kept as adapter) and step 3 (`UsageProfilesView` generated from active registry profiles). Frontend refactor files under `PASSATION.md` were not rewritten; `UsageProfilesView` public events and `SetSelection` / `SelectedProfiles` stay compatible with `MainWindow.MultiProfiles.cs`.

## Changed paths (this increment plus already-present step 2 sources)

- `docs/product/profiles.json`, `docs/product/profiles.schema.json`
- `docs/product/software-catalogue.schema.json` (profile field is a kebab-case pattern, not a closed enum)
- `src/AgenStart.Core/Profiles/ProfileId.cs` path: `ProfileDefinition.cs`, `ProfileRegistry.cs`
- `src/AgenStart.Core/AgenStart.Core.csproj` (embeds `profiles.json`)
- `src/AgenStart.Catalogue/SoftwareCatalogueLoader.cs`
- `src/AgenStart.Recommendations/RecommendationEngine.cs`
- `src/AgenStart.Desktop/ViewModels/ProfileOptionViewModel.cs`
- `src/AgenStart.Desktop/Views/UsageProfilesView.axaml(.cs)`
- `tests/AgenStart.Catalogue.Tests/ProfileRegistryTests.cs`
- `tests/AgenStart.Catalogue.Tests/SoftwareCatalogueLoaderTests.cs`
- `.github/workflows/catalogue-tests.yml`, `recommendation-tests.yml`, `desktop-ui-build.yml`, `landing-pages.yml`
- `docs/engineering/lessons-learned.md` (LL-001 catalogue workflow)

## Evidence actually gathered

- `AgenStart.Catalogue.Tests`: 24/24 passed (test executable).
- `AgenStart.Recommendations.Tests`: 22/22 passed, including golden files (byte-identical after engine switched to registry labels).
- `AgenStart.Application.Tests`: 28/28 passed.
- `dotnet format --verify-no-changes` exit 0 on Core, Catalogue, Recommendations, Desktop, Catalogue.Tests.
- `dotnet build src/AgenStart.Desktop/AgenStart.Desktop.csproj -c Release --no-restore`: 0 warnings, 0 errors.
- Desktop restore inside the agent sandbox still hits corrupted NuGet cache (`NU5037` on Avalonia); not a repository defect (LL-004 adjacent).

## RAIDER notes

- Reusable / Agnostic: profile identity, order, labels and aliases come from data.
- Durable: golden plan output unchanged; catalogue still requires canonical (non-alias) ids.
- Scoped CI: `profiles.json` now fans out to catalogue, recommendation, desktop and landing only.
- Retroactive: aliases resolve on read; activating a planned profile without a `UserProfile` mapping is refused until step 4.
- Reuse-first: Build (no suitable library); Learn from Microsoft / AIP-126 / Windows Device usage (already in ADR-0003).

## Not done / unresolved risks

- Step 4 (setup profile v2, Desktop import/export LL-003, remove `UserProfile`) still blocked on the frontend refactor.
- Step 5 (generate landing profile cards from `profiles.json`) not started; landing CI now wakes on registry edits so that step is not silent.
- `family` still blocked on `msstore` policy.
- No dedicated Desktop UI test project; UsageProfilesView coverage is compile-time only.
- Nothing committed.

## Handoff

- Next product-safe work: catalogue expansion for `entertainment`, or step 5 site generation.
- Frontend agent: `UsageProfilesView` now binds `Options` from `ProfileRegistry.Default`. Do not reintroduce named per-profile checkboxes.
