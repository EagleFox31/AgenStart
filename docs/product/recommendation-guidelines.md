# AgenStart Recommendation Guidelines

## Product rule

AgenStart recommends software; it does not behave like a generic download directory.

The user should understand three things from every recommendation card without already knowing the product:

1. what the app does in plain language;
2. how strongly AgenStart recommends it;
3. why it matches the selected uses and this PC.

## Usage profiles

A setup can select one or more of six usage profiles:

- Personal / Everyday
- Business / Work
- Study / Learning
- Development
- Creative
- Gaming

Profiles are additive. AgenStart merges matching catalogue rules, deduplicates applications by canonical AgenStart ID, and keeps the strongest recommendation level when an app matches multiple selected profiles.

Legacy `creation` and `training` values remain readable as aliases of `creative` and `learning`.

## Recommendation levels

- **Essential** — a core tool for the selected use; eligible for default selection.
- **Recommended** — a strong fit; eligible for default selection.
- **Gem** — a useful, less-obvious tool worth discovering; never selected automatically.
- **Optional** — relevant only when the user specifically wants that capability; never selected automatically.

Installed-state, compatibility, lifecycle and conflict checks can always downgrade or block selection.

These four levels are ranking semantics, not four mandatory visual badges. The UI intentionally exposes fewer flags than the engine has internal states.

## Plain-language descriptions

Every catalogue application must have a concise description that answers “what is this for?” without marketing copy or unexplained technical jargon.

Runtime validation currently caps the description at 180 characters.

Good:

> Finds files and folders on your PC by name almost instantly, much faster than normal Windows search.

Avoid:

> The world's leading next-generation productivity experience.

## Multi-profile behavior

For `Development + Gaming`, for example:

1. evaluate the app against both profiles;
2. create only one recommendation row for a canonical app;
3. retain the strongest matching level;
4. keep all matching profile reasons for explainability;
5. apply machine compatibility and installed-software state;
6. never automatically select `Gem` or `Optional` items.

## Catalogue philosophy

AgenStart owns a curated product catalogue rather than mirroring Softonic, Uptodown, WinGet or another directory.

External catalogues, trend pages and community sources may later contribute **discovery signals**, but discovery does not grant install trust.

For Windows, installable entries must resolve through an exact trusted WinGet/MS Store identity. AgenStart must not install fuzzy search results or arbitrary mirror URLs.

## Discovery candidates

Good catalogue candidates include:

- widely useful essentials;
- strong profile-specific tools;
- lightweight utilities that solve a concrete Windows pain point;
- privacy-friendly or offline-first alternatives;
- respected open-source tools;
- “gems” that materially improve a workflow without being obvious to a new user.

Popularity alone is not sufficient.

## UI guidance

Recommendation cards should show, in this order:

- app icon or safe fallback;
- app name;
- plain-language purpose;
- a status flag only when it adds useful information;
- selection control;
- optional “Why AgenStart recommends this” explanation when space allows.

Keep the recommendation surface visually quiet. The visible flag vocabulary is deliberately limited to four categories:

- **Installed** — the app is already on this PC;
- **Recommended** — a strong recommendation;
- **Gem** — a less-obvious discovery worth considering;
- **Attention** — compatibility, availability, inventory or conflict needs review.

`Essential` is communicated through ranking and default selection instead of another badge. `Optional` has no badge. Detailed technical states remain available to the product logic and explanatory copy, but they must not create a rainbow of separate pills.

Color must not be the only carrier of meaning: every visible flag also uses text and an icon. Recommended uses AgenStart teal, Installed uses success green, Gem uses a restrained warm accent, and Attention uses warning amber.

Recommendation-build progress has one owner and one presentation. Show one stage label, one percentage and one progress bar; never duplicate the same stage/percentage in nested progress presenters.

The recommendation UI must remain functional if artwork is missing or corrupt. Icons are cosmetic and must never be allowed to break recommendation or installation logic.

## Selection discipline

The catalogue can be much larger than the recommendation surface. The product should eventually rank a broad candidate pool down to a concise set rather than dumping the full catalogue on the user.

Target direction:

- roughly 15–20 candidate apps available per profile;
- normally 6–10 strong recommendations shown for a single profile;
- multi-profile results deduplicated and ranked;
- roughly 12 visible recommendations as a practical default target;
- only a small number of strong `Essential` / `Recommended` items preselected.

The exact cap should be introduced together with ranking/scoring so useful gems are not accidentally hidden by a naive `Take(12)` rule.
