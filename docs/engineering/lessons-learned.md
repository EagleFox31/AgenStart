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
- **Prevention:** exit code 5 is non-zero, so CI fails loudly rather than passing falsely. `catalogue-tests.yml`, `installation-tests.yml` and `recommendation-tests.yml` now build explicitly and run `dotnet test --project … --no-build` (2026-10-11); `package-management-tests.yml` still uses the positional form and should follow when next touched.
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
- **Prevention:** export → import round-trip tests for every active profile and for multi-profile selections; v1 fixtures must keep importing. Application serializer now writes schema v2 `profileIds`, accepts v1, and normalizes aliases via `ProfileRegistry`. Desktop `ToProfileId` / `TryParseProfileId` in `MainWindow.axaml.cs` is still the remaining hole (ADR-0003 step 4).
- **Generalized lesson:** never persist `ToString()` of an enum; persisted identifiers need an explicit, tested mapping. Any new profile, flag or option must come with a round-trip test.
- **Ownership note:** the affected file is under the frontend refactor in progress (`PASSATION.md`); the fix is sequenced after that refactor (ADR-0003, rollout step 4).

## LL-004 — Build verified on a shared dirty working tree (near miss)

- **Context:** another agent's uncommitted frontend refactor was present in the working tree.
- **Symptom:** `dotnet build` of `AgenStart.Desktop` failed with CS0120 errors; this could have been reported as a defect on `main`.
- **Root cause:** verification ran against the working tree, not against the committed revision.
- **Fix:** built `HEAD` in a temporary `git worktree`: 0 errors, 0 warnings. The errors belonged to work in progress.
- **Prevention:** before attributing a failure to the codebase, check `git status`; verify committed state in an isolated worktree when the tree is dirty.
- **Generalized lesson:** in multi-agent repositories, distinguish "committed state" from "working tree" in every verification report.

## LL-005 — UserControl `DataContext = this` broke the host's `IsVisible` binding

- **Context:** `UsageProfilesView` set `DataContext = this` in its constructor; `MainWindow` hosts it with `IsVisible="{Binding IsUsageProfile}"`.
- **Symptom:** the Usage profile step stayed visible on every step, drawn over Overview at startup.
- **Root cause:** a binding on the host element resolves against that element's own `DataContext`. Once the control replaced it with itself, `IsUsageProfile` no longer resolved and `IsVisible` kept its default (`true`). Compiled bindings did not flag it because the binding lives in `MainWindow.axaml`, typed on `MainWindowViewModel`.
- **Fix:** scope the self-context to the control's root element (`RootGrid.DataContext = this`).
- **Prevention:** never assign `DataContext` on a UserControl itself; assign it on an inner root. Check every step at startup with a window capture after any view extraction.
- **Generalized lesson:** a control's own `DataContext` belongs to its parent; a view that needs a private context sets it one level down.

## LL-006 — Pushed without running the CI formatting gate

- **Context:** passe 9 (PR #117) was verified locally with `dotnet build` and the test suite before push.
- **Symptom:** CI `build` failed on `Verify formatting` (`WHITESPACE` in `RecommendationsViewModel.cs`, `=>Recommendations`).
- **Root cause:** the local check list stopped at build + tests; `desktop-ui-build.yml` also runs `dotnet format --verify-no-changes`, which neither of them covers.
- **Fix:** inserted the missing space; `dotnet format ... --verify-no-changes` exits 0.
- **Prevention:** before any push touching `src/AgenStart.Desktop`, run the same command as CI: `dotnet format src/AgenStart.Desktop/AgenStart.Desktop.csproj --verify-no-changes`.
- **Generalized lesson:** local verification must replay every CI gate for the touched surface, not only build and tests.

## LL-007 — Wrapping views in panels silently disabled History and Settings

- **Context:** passe 9 (PR #117, f489980) wrapped each step view in a `Panel` so it could bind directly to its step view model.
- **Symptom:** History and Settings stayed disabled in the side bar. Nothing was logged and the build stayed green.
- **Root cause:** `InstallUtilityViews` found the page grid through `OverviewControl.Parent is Grid`. After the wrap, the parent became a `Panel`, so the method returned early without saying anything. Code that walks the visual tree depends on the layout, and the compiler cannot check that dependency.
- **Fix:** the page grid is now named (`x:Name="PageHost"`) and resolved by name (8fd6608).
- **Prevention:** after any change to `MainWindow` layout, click every side-bar item, including History and Settings, as part of the manual smoke pass. Resolve hosts by `x:Name`, never through `.Parent` chains.
- **Generalized lesson:** a guard that returns early in silence turns a layout change into a hidden feature loss. Resolve dependencies by name, and make a missing dependency fail loudly.

## LL-008 — Redesign crashed at startup on an undecodable brand PNG

- **Context:** the inset grouped redesign (PR #118, 269fa38) put `Assets/agenstart-app-icon.png` in the side bar and the Settings About row.
- **Symptom:** `dotnet run` returned immediately with no window. Running the exe directly showed `ArgumentException: Unable to load bitmap from provided data` from `MainWindow.axaml` line 20.
- **Root cause:** that PNG (96×96, 8-bit colormap) had never been loaded at runtime; Skia rejects it. XAML compilation only checks the URI, so build, format and tests stayed green. The pre-push check had no launch step.
- **Fix:** replaced the bitmap with a vector brand mark using theme brushes (`AgenAccentBrush` / `AgenOnAccentBrush`); the original asset is untouched.
- **Prevention:** after any XAML change, launch the built exe for ~15 s (`timeout 15 ./AgenStart.Desktop.exe`, exit 124 = still running) and read stderr before pushing. Before using a bitmap asset in XAML for the first time, confirm it loads at runtime.
- **Generalized lesson:** a green build does not prove a UI starts. Resources referenced from XAML fail only at runtime, so the startup check is a gate in its own right.
- **Update (2026-11-05):** the PNG is not just an unusual encoding: its data stream is corrupt (Pillow also fails with "broken data stream"), and the same broken file exists under `site/assets/generated-brand/favicon.png`. The `.ico` frames decode fine, so `agenstart-app-icon.png` was regenerated from the 64×64 `.ico` frame (RGBA) and the real logo is back in the side bar and About row. Extra prevention: decode a bitmap with an independent tool (e.g. `python -c "from PIL import Image; Image.open(p).load()"`) before committing it.

## LL-009 — A new settings flag would have shown the onboarding to existing users

- **Context:** guided first-run onboarding (PR #118, 38a6181) keyed on `DesktopSettings.OnboardingCompleted`, a `bool` defaulting to `false`.
- **Symptom (near miss, caught before merge):** `settings.json` files written by released versions have no `onboardingCompleted` field, so they deserialize to `false` and every existing user would have been sent back through the first-run flow. A branch test even asserted that outcome.
- **Root cause:** a field's default value was read as user intent. "Field absent" (file written before the feature existed) and "explicitly false" (onboarding left midway) were the same value after deserialization.
- **Fix:** `LocalExperienceStore.LoadSettingsAsync` checks whether the property is present; an existing file without it loads as `OnboardingCompleted = true`. An explicit `false` still restarts the flow.
- **Prevention:** `DesktopSettingsTests` covers both cases (legacy file → onboarded; explicit `false` → restart). When adding a persisted flag, write a test that loads a settings file from the previous release before writing the feature.
- **Generalized lesson:** for persisted data, a new field's default must be chosen for the data that already exists, not for a fresh install. Absent and false are different states; keep them apart at load time.

## LL-010 — A string key ending in `.Other` was read as half of a plural pair

- **Context:** uninstall inventory P1 (`feat/uninstall-inventory`) added an installer-kind label `Uninstall.Installer.Other`.
- **Symptom (near miss, caught by tests):** the localisation test failed because it treats any key ending in `.Other` as the plural form of a `.One`/`.Other` pair and requires its sibling.
- **Root cause:** the plural convention is inferred from a key suffix, so an ordinary label using the word "Other" collides with it.
- **Fix:** renamed the key to `Uninstall.Installer.Generic`.
- **Prevention:** the existing parity/plural test is the guardrail; do not end a non-plural key with `.One` or `.Other`.
- **Generalized lesson:** suffix-based conventions reserve those suffixes; check the reserved suffixes before naming a key.

## LL-011 — `kill $!` in Git Bash left the startup-smoke window running

- **Context:** LL-008 startup smoke rerun during the P1 review, launching `AgenStart.Desktop.exe &` from Git Bash and stopping it with `kill $!`.
- **Symptom (near miss):** the check printed "alive" and `kill` returned, but the Windows process (another PID) was still running from the worktree.
- **Root cause:** `$!` is the MSYS process id, not the Windows PID; the signal did not reach the native GUI process.
- **Fix:** found the process by its executable path under the worktree, checked its start time matched the smoke run, and stopped that PID only.
- **Prevention:** after a smoke run, confirm with `Get-Process AgenStart.Desktop | Where-Object Path -like '<worktree>*'` that the count is 0; stop by the Windows PID (`Stop-Process -Id`), never by process name, so the user's own instance is untouched.
- **Generalized lesson:** on Windows, verify a background process is actually gone by querying the OS, not by trusting the shell's exit status.

## LL-012 — Shell-based edits mangled Windows paths and multi-line code

- **Context:** uninstall plan P2 (`feat/uninstall-plan`), edits made from Git Bash with heredocs, `sed` and inline Python.
- **Symptom (near miss, caught before commit):** a heredoc running Python failed with "unexpected EOF", and backslashes in Windows paths were silently altered by `sed`/Python/heredoc quoting more than once.
- **Root cause:** several quoting layers (Bash, heredoc, the interpreter's own escapes) each reinterpret `\`, so the text written differs from the text intended.
- **Fix:** redid the affected edits with the editor tools and rebuilt; tests caught the remaining divergences.
- **Prevention:** use the file edit/write tools, not shell pipelines, for multi-line or backslash-heavy edits; keep shell edits to simple, single-line, backslash-free substitutions.
- **Generalized lesson:** every quoting layer is a translation; when the content contains escape characters, remove layers instead of adding escapes.

## LL-013 — A capability-reachability test rooted too high raised false alarms

- **Context:** P2 test proving the Uninstall page cannot reach any write/execute port.
- **Symptom (near miss):** rooted at the page view model, the object-graph walk escaped through a shared test localizer's event handlers to the whole shell and flagged unrelated types (e.g. `WinGetProcessRunner`).
- **Root cause:** event subscriptions and shared services connect every view model to the shell, so "reachable from the page" meant "reachable from the app".
- **Fix:** root the walk at the page's injected port fields and assert the root count (`Assert.Equal(2, ports.Length)`) so a new port cannot slip past the test unnoticed.
- **Prevention:** reachability/allowlist tests start from the injected capability fields and pin how many there are.
- **Generalized lesson:** a security property test must start from the capability boundary, not from a node connected to everything.

## LL-014 — LL-012 recurred twice in one segment, plus a CRLF miss

- **Context:** official uninstall P3 (`feat/uninstall-official`), edits scripted from Git Bash with heredocs, `sed` and Python after LL-012 was recorded.
- **Symptom (near misses, caught before commit):** a Python heredoc turned `\` into an invalid C# escape; `sed` on a backslash path matched nothing and exited 0; a Python replacement on a CRLF file found 0 matches (its own `assert` caught it); a Python edit script for a CI file had a syntax error.
- **Root cause:** LL-012's prevention was a guideline, not a check; scripted replacements fail silently when the pattern does not match (quoting, line endings).
- **Fix:** switched to the editor tools for those edits and rebuilt.
- **Prevention (stronger, repeated error):** any scripted replacement must assert its match count (`count == expected`, else exit non-zero); multi-line, backslash or CRLF content goes through the editor tools only.
- **Generalized lesson:** a replacement that can match zero times must be made to fail loudly when it does.

## LL-015 — A guard test went red at intermediate commits; a pipe hid a format failure

- **Context:** P3, seven commits in sequence, each gated by the suites the commit was thought to affect.
- **Symptom (near miss, fixed before push):** the Platform.Windows suite was not rerun after `fac3063`, so its process-start guard test was red at `fac3063` and `7e3283d` (fixed in `c0364c6` with a pinned allowlist). Separately, a `dotnet format` loop piped to `head` reported success while hiding the exit code.
- **Root cause:** suites were chosen per commit by intuition, not by dependency; in a pipeline the shell reports the exit code of the last command.
- **Fix:** reran every suite on the final commit; reran the format gate without a pipe.
- **Prevention:** before each commit run all test executables (they take seconds); never pipe a gate command — capture its exit code (`cmd >log 2>&1 || rc=1`) and print the log tail separately.
- **Generalized lesson:** cheap gates are run in full, and a gate's exit code must reach the decision unfiltered.

## LL-016 — A catalogue entry passed every unit suite but failed the packaged icon smoke

- **Context:** adding JDownloader 2 to the catalogue (PR #125).
- **Symptom:** all six test suites were green locally, but the CI `build` job failed at "Validate packaged recommendation icons" (`--icon-smoke` exit 2) because the new application had no packaged logo.
- **Root cause:** the rule "every catalogue application has packaged artwork" is enforced only by the published app's `--icon-smoke`, which the local gate (test executables) does not run.
- **Fix:** added `Assets/AppLogos/jdownloader.svg`, its `AppIconService` mapping and its `THIRD_PARTY_NOTICES.md` entry; reproduced the CI step locally (publish win-x64, `--icon-smoke` exit 0).
- **Prevention:** any change to `docs/product/catalogue.fixtures.json` that adds an application also adds its logo, mapping and notice, and is gated locally by `dotnet publish src/AgenStart.Desktop/AgenStart.Desktop.csproj -c Release -r win-x64 --self-contained true -o <dir>` then `<dir>/AgenStart.Desktop.exe --icon-smoke` (exit 0).
- **Generalized lesson:** before calling a change verified, list the CI steps its surface triggers and reproduce the ones the unit suites do not cover.
