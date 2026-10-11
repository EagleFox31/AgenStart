# Research — WhatsApp, IT Student, Entertainment (2026-10-11)

## Questions

1. Can WhatsApp be added to the trusted Windows catalogue?
2. Should “étudiant IT” be a new profile or `learning` + `development`?
3. Can `entertainment` be activated under ADR-0003?

## Sources (accessed 2026-10-11)

| URL | Fact used |
|---|---|
| https://www.whatsapp.com/download/windows | Official Windows download is the Microsoft Store app. |
| https://apps.microsoft.com/detail/9nksqgp7f2nh | Store id `9NKSQGP7F2NH`, publisher WhatsApp Inc. |
| https://github.com/microsoft/winget-pkgs/issues/111741 | Community `WhatsApp.WhatsApp` package was removed; install via `msstore`. |
| https://api.github.com/repos/microsoft/winget-pkgs/contents/manifests/w/WhatsApp | 404 — no WhatsApp publisher tree on `winget` (2026-10-11). |
| https://support.microsoft.com/en-us/windows/experience/personalization/personalize-your-windows-experience-with-device-usage-settings | Windows Device usage intents: Development, Gaming, Family, Creativity, School, Entertainment, Business. No “IT student” intent. |
| https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/enum | Do not use enums for open sets (already in ADR-0003). |
| winget-pkgs API | Verified `XBMCFoundation.Kodi`, `Plex.Plex`, `Plex.Plexamp`, `Stremio.Stremio`, `Audacious.MediaPlayer` exist on `winget`. |

## Decision

- **WhatsApp: do not add.** Official identity is `msstore` / `9NKSQGP7F2NH`. `WinGetProvider` only treats `winget` as installable. Opening Store installs is a separate trust review, same class as Family Safety.
- **`it-student`: add as a new active profile.** Windows “School” is general education (`learning`). IT students need a lighter developer stack plus study tools without making Docker/Postgres essential. Distinct from planned `it-admin`.
- **`entertainment`: activate.** ADR-0003 required ≥6 curated `winget` apps with logos. Shipped: VLC, Spotify, Kodi, Plex, Plexamp, Stremio. Audacious skipped (no packaged logo).

Confidence: high on WhatsApp source block and entertainment package ids; medium on IT-student taxonomy (product slice, not a Windows intent).
