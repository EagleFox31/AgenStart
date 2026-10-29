# Walkthrough — Usage-profile registry: decision and rollout step 1 (2026-10-11, Cursor agent)

## Scope

ADR-0003 accepted by the product owner, Family / Entertainment taxonomy integrated, rollout step 1 (golden and setup-contract tests) implemented. No production code changed. Frontend files under refactor by another agent (`PASSATION.md`) were not touched.

## Changed paths

- `.gitignore` (new)
- `docs/adr/0003-data-driven-usage-profile-registry.md` (new, Accepted; decision 7 Family / Entertainment)
- `docs/engineering/lessons-learned.md` (new)
- `docs/engineering/README.md` (index entry)
- `docs/walkthroughs/2026-10-11-cursor-usage-profile-registry.md` (this file)
- `tests/AgenStart.Recommendations.Tests/AgenStart.Recommendations.Tests.csproj` (references `AgenStart.Catalogue`)
- `tests/AgenStart.Recommendations.Tests/RecommendationGoldenTests.cs` (new)
- `tests/AgenStart.Recommendations.Tests/Golden/catalogue-0.2.0.frozen.json` (byte-identical copy of `docs/product/catalogue.fixtures.json`, SHA-256 compared)
- `tests/AgenStart.Recommendations.Tests/Golden/capable-machine.golden.txt`, `constrained-machine.golden.txt` (generated)
- `tests/AgenStart.Application.Tests/SetupProfileSerializerTests.cs` (10 new cases)
- `.github/workflows/recommendation-tests.yml` (Catalogue + `global.json` path triggers; explicit build; `dotnet test --project --no-build`)
- `.github/workflows/installation-tests.yml` (explicit build; `dotnet test --project --no-build`)

## Golden test design

- Frozen catalogue so future catalogue additions do not invalidate the baseline.
- Ten selections (six single profiles, three pairs, all six) on two machines: capable with partial inventory (git and 7-Zip installed, Docker unknown) and constrained (4 GiB RAM, ~8 GB free, no GPU).
- Output records id, name, level, disposition, default selection, reason key, matched profiles and every reason message. Profile ids are mapped explicitly in the test, never via `Enum.ToString()`.
- Dispositions covered: Recommended, AlreadyInstalled, InventoryUnknown, Incompatible. Conflict, CompatibilityUnknown and Unavailable do not occur with the current catalogue; existing unit tests cover them.
- Regenerate only deliberately: `AGENSTART_UPDATE_GOLDEN=1`, then review the diff.
- Reuse decision: Build (minimal file comparison) rather than Adopt Verify/snapshot libraries, to avoid a new test dependency for two baseline files. Revisit if snapshot tests multiply.

## Evidence actually gathered

- `AgenStart.Recommendations.Tests`: 22/22 passed (test executable and `dotnet test --project … -c Release --no-build`); golden files stable across a second run.
- `AgenStart.Application.Tests`: 28/28 passed (test executable).
- `dotnet format --verify-no-changes` exit 0 on both test projects.
- Earlier in the session: 93/93 baseline tests; `HEAD` desktop build 0 warnings / 0 errors in an isolated worktree; LL-003 defect reproduced on compiled `AgenStart.Core`.
- `RAIDER.md` read from `EagleFox31/project-registry`, blob `db1b349e129c8f147f91551ba79f6c1799481756`.
- WinGet ids checked with `winget show --id <id> --exact --source winget`: Entertainment candidates found (Spotify, Kodi, Plex, Plexamp, Stremio, Audacious); MusicBee and foobar2000 not found; Microsoft Family Safety only on `msstore`. Installation not tested.

## Sources

- <https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/enum>
- <https://google.aip.dev/126>
- <https://support.microsoft.com/en-us/accessibility/windows/use-a-screen-reader-to-set-up-windows>
- <https://learn.microsoft.com/en-us/windows/package-manager/configuration/create-v3>
- <https://github.com/marticliment/UniGetUI>

## Not done / unresolved risks

- Workflow changes are not validated on GitHub-hosted runners; first PR run will tell.
- Local NuGet restore inside the agent sandbox hit corrupted cached packages (`NU5037`); builds used existing restore assets (`--no-restore`). Environment issue, not a repository defect.
- LL-001 root cause not established.
- LL-003 defect still present in `MainWindow.axaml.cs` (frontend-owned; ADR-0003 step 4).
- `family` blocked on an `msstore` source-policy decision; `entertainment` needs catalogue entries before activation.
- Nothing committed.

## Handoff

- Next: rollout step 2 (`profiles.json` + schema, `ProfileId`, loader, engine) — the golden files must stay byte-identical.
- Frontend agent: LL-003 and the `dotnet test` workaround (LL-001).
