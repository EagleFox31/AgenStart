# Lessons learned

Searchable failure memory required by the RAIDER standard (Durable / Failure Memory). Consult before changing a related area. Near misses count.

Entry format: context, symptom, root cause, fix, prevention, generalized lesson. Status is `open` until a proportionate guardrail exists.

| ID | Date | Class | Title | Status |
|---|---|---|---|---|
| LL-001 | 2026-10-11 | tests / tooling | `dotnet test` reports zero tests (exit 5) locally | open |
| LL-002 | 2026-10-11 | repository hygiene | No `.gitignore`: build output exposed to commits | mitigated |
| LL-003 | 2026-10-11 | data contract | Setup profile id derived from `Enum.ToString()` | open |
| LL-004 | 2026-10-11 | process / multi-agent | Build verified on a shared dirty working tree | mitigated |

---

## LL-001 — `dotnet test` reports zero tests (exit 5) locally

- **Context:** .NET SDK 10.0.401, `global.json` selects `Microsoft.Testing.Platform`, test projects use xUnit v3 4.0.0.
- **Symptom:** `dotnet test tests/<Project>.csproj` prints "Zero tests ran" and exits with code 5 for all four test projects. Running the test executable directly (`bin/<cfg>/net10.0/<Project>.exe`) runs everything: 93/93 passed (Application 18, Catalogue 7, Platform.Windows 48, Recommendations 20).
- **Observations:**
  - `dotnet test --project <csproj> --no-build` after a separate build: tests discovered and passed (Debug and Release).
  - `dotnet test --project <csproj>` letting `dotnet test` build: zero tests in one observed run.
  - The CI workflows use the positional form with `--no-restore`; the last observed CI runs (2026-09-16) reported `total: 7` for Catalogue tests. Behaviour on the current hosted-runner SDK is not verified.
- **Root cause:** not established. The positional form is not the documented invocation for Microsoft.Testing.Platform mode in .NET 10 (`--project` is); the build-inside-test case needs further diagnosis.
- **Fix / workaround:** build first, then `dotnet test --project <csproj> --no-build`.
- **Prevention:** exit code 5 is non-zero, so CI fails loudly rather than passing falsely. `installation-tests.yml` and `recommendation-tests.yml` now build explicitly and run `dotnet test --project … --no-build` (2026-10-11); `catalogue-tests.yml` and `package-management-tests.yml` still use the positional form and should follow when next touched.
- **Generalized lesson:** a green test step must assert that tests were discovered; treat "zero tests" as a failure, never as success.

## LL-002 — No `.gitignore`: build output exposed to commits

- **Context:** the repository had no `.gitignore`.
- **Symptom:** the first local build produced untracked `bin/` and `obj/` directories under every project.
- **Root cause:** builds had only run in CI, where output is discarded, so the gap was invisible.
- **Fix:** standard .NET + Node/Vercel `.gitignore` added at repository root (`bin/`, `obj/`, `artifacts/`, `TestResults/`, IDE files, `node_modules/`, `.vercel/`). Verified `git ls-files -ci --exclude-standard` returns nothing (no tracked file newly ignored).
- **Prevention:** the file itself. Remaining step: commit it.
- **Generalized lesson:** bootstrap checklists should include `.gitignore` even for CI-only repositories.

## LL-003 — Setup profile id derived from `Enum.ToString()`

- **Context:** setup export/import in `src/AgenStart.Desktop/Views/MainWindow.axaml.cs` (`ToProfileId`, `TryParseProfileId`).
- **Symptom (verified against compiled `AgenStart.Core`, 2026-10-11):**
  - Creative exports as `creation`, Learning as `training` (alias names win in `ToString()`).
  - Gaming exports as `gaming` but import has no `gaming` case → "not supported by this AgenStart build".
  - Multi-profile selections export as `personal, development`, which fails `SetupProfileValidator` before the file is written.
- **Root cause:** a presentation API (`Enum.ToString()` on a `[Flags]` enum with aliases) used as a persisted contract, plus a hand-written parse table that drifted from the enum when Gaming and multi-select were added. Setup profile v1 stores one `profileId`, which cannot represent multi-select.
- **Fix:** not applied yet. Defined in ADR-0003 (registry-based ids, setup profile v2 with `profileIds[]`, v1 read compatibility).
- **Prevention:** export → import round-trip tests for every active profile and for multi-profile selections; v1 fixtures must keep importing. In place since 2026-10-11 at serializer level (`SetupProfileSerializerTests`): every known v1 id, including `creation` / `training` / `study`, must deserialize, and the multi-select `ToString()` output is shown to be rejected. The Desktop mapping itself has no test project yet; covered when ADR-0003 step 4 moves it out of `MainWindow.axaml.cs`.
- **Generalized lesson:** never persist `ToString()` of an enum; persisted identifiers need an explicit, tested mapping. Any new profile, flag or option must come with a round-trip test.
- **Ownership note:** the affected file is under the frontend refactor in progress (`PASSATION.md`); the fix is sequenced after that refactor (ADR-0003, rollout step 4).

## LL-004 — Build verified on a shared dirty working tree (near miss)

- **Context:** another agent's uncommitted frontend refactor was present in the working tree.
- **Symptom:** `dotnet build` of `AgenStart.Desktop` failed with CS0120 errors; this could have been reported as a defect on `main`.
- **Root cause:** verification ran against the working tree, not against the committed revision.
- **Fix:** built `HEAD` in a temporary `git worktree`: 0 errors, 0 warnings. The errors belonged to work in progress.
- **Prevention:** before attributing a failure to the codebase, check `git status`; verify committed state in an isolated worktree when the tree is dirty.
- **Generalized lesson:** in multi-agent repositories, distinguish "committed state" from "working tree" in every verification report.
