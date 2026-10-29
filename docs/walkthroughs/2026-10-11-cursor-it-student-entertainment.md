# Walkthrough — IT Student + Entertainment (+ WhatsApp no-go) (2026-10-11, Cursor)

## Scope

User asked for WhatsApp and two selectable profiles: “étudiant IT” and Entertainment. Frontend WIP (`MainWindow`, `InstallationView`, `ReportView`) not edited.

## Changed paths

- `docs/product/profiles.json` — `it-student` active; `entertainment` active
- `src/AgenStart.Core/Catalogue/ApplicationDefinition.cs` — `UserProfile.Entertainment`, `ItStudent` adapter bits
- `src/AgenStart.Core/Profiles/ProfileRegistry.cs` — LegacyFlags
- `docs/product/catalogue.fixtures.json` — 62 apps; entertainment + it-student rules
- `src/AgenStart.Desktop/Icons/AppIconService.cs` + `Assets/AppLogos/{kodi,plex,plexamp,stremio}.svg`
- `THIRD_PARTY_NOTICES.md`
- tests: catalogue, profile registry, recommendation engine/golden, setup serializer
- `tests/AgenStart.Recommendations.Tests/Golden/profiles-0.2.0.frozen.json` — isolates 0.2.0 goldens from live registry
- `docs/product/recommendation-guidelines.md`
- `docs/research/2026-10-11-whatsapp-it-student-entertainment.md`

## Evidence

- WhatsApp: official Store id `9NKSQGP7F2NH`; `WhatsApp.WhatsApp` 404 on winget-pkgs (2026-10-11). Not added.
- Entertainment WinGet ids verified on winget-pkgs (2026-10-11).
- Catalogue tests 26/26, recommendation tests 24/24 (goldens still frozen 0.2.0), Application tests 34/34.
- Desktop Release build 0 warnings / 0 errors; `--icon-smoke` exit 0.

## Not done

- WhatsApp / Family remain Store-blocked.
- Audacious skipped (no logo).
- MainWindow import still does not parse `entertainment` / `it-student` (LL-003). `UsageProfilesView` shows them from the registry.
- ADR-0003 step 4 (remove `UserProfile`) still open; new bits are temporary adapter entries.
