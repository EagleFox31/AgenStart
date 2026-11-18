# Desktop: guided onboarding, FR/EN localization, top tabs

Status: accepted, in progress on `feat/desktop-selectable-themes` (PR #118).

## Decisions (reuse-first)

- **Localization — Adapt** the palette mechanism: one `ResourceDictionary` of `x:String` per language
  (`Strings/en.axaml`, `Strings/fr.axaml`), read by views via `DynamicResource` and by view models via an
  injected `ILocalizer` over the same dictionaries. Rejected: `.resx` + markup extension (needs a
  home-made indexer binding for live switch); `Jeek.Avalonia.Localization` (Avalonia 12 support unproven,
  second non-XAML pipeline).
- **Tabs — Build** a small tab bar (two `ItemsControl`s of `NavigationTabViewModel`, `Button.tab`).
  Rejected: `TabControl` (owns content hosting, rebuilds visual tree — LL-007 class); `TabStrip`
  (cannot split flow/utility groups).

## Behaviour

- `DesktopSettings(bool AnalyzeOnStartup = true, string Theme = Default, string? Language = null,
  bool OnboardingCompleted = false)`. Old files load (`Language` null, `OnboardingCompleted` false);
  unknown fields are ignored. Settings are written as `_settings with { ... }` — never rebuilt from a
  subset of fields — and an unchanged record is not written (idempotent).
- First run (`OnboardingCompleted == false`): Splash → Language → Usage → Scan (automatic) →
  Build recommendations → shell on Recommendations. Default language = OS UI culture (`fr*` → fr,
  else en). Completion is written once, when the build succeeds. Quitting midway restarts at Language.
  Scan failure: Retry / Go to Home (marks complete). Build failure: Retry / Back to Usage.
- Returning user: short splash → shell on Home. `AnalyzeOnStartup` unchanged.
- Clear local data deletes `settings.json` → next launch is a first run.

## Tabs and gating

| Tab | Enabled when |
|---|---|
| Home | always |
| Your PC, Usage profile | snapshot taken |
| Recommendations | recommendations built |
| Confirm | review prepared |
| Installation | queue not empty, installing, or report exists |
| Report | report exists |
| History, Settings (right) | always |

A tab is also enabled while it is the current step. Selecting a disabled tab is a no-op.

## Phases (each builds, passes format + tests, starts for 15 s)

1. **Infrastructure** — `ResourceDictionarySwapper`, `ThemeService` on it, `LocalizationService`
   (`AppLanguages`, `ILocalizer`), `Strings/{en,fr}.axaml`, settings record extension + `with` save,
   language row in Settings, `tests/AgenStart.Desktop.Tests` (settings compatibility, languages,
   string key parity) and CI wiring.
2. **Localize UI** — views via `DynamicResource`; worded `StringFormat`s moved to VM properties; VM
   status strings stored as keys and re-raised on `LanguageChanged`. Catalogue/engine text stays English.
3. **Tabs** — `NavigationTabViewModel`, `RelayCommand`, gating in `MainWindowViewModel`, header tab bar
   replacing the sidebar; History/Settings hosted in XAML.
4. **Onboarding** — `OnboardingViewModel` state machine (`IOnboardingActions`, `IOnboardingSettings`),
   `AppPhase { Splash, Onboarding, Shell }`, views under `Views/Onboarding/`, `UsageProfilesView`
   onboarding mode.
5. **Docs** — PASSATION (append only), CHANGELOG, RAIDER review on the PR.

## Risks

- Missing string key renders empty with no build error → key parity test.
- `DynamicResource` cannot feed `StringFormat` → formatted text lives in view models.
- `AutomationProperties.Name` and window title must be localized too.
- Startup crash classes (LL-008): unknown language must normalize to `en`, never to a missing URI.
- French labels must fit the tab bar at the 1040 px minimum width.
