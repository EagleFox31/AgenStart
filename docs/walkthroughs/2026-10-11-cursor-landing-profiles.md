# Walkthrough — Landing profiles from registry (2026-10-11, Cursor)

## Scope

ADR-0003 step 5: generate the public landing `#profiles` section and flow-step chips from `docs/product/profiles.json` via `site/build-assets.mjs`. Desktop frontend WIP not touched.

## Changed paths

- `site/build-assets.mjs` — writes marked regions in `site/index.html`
- `site/index.html` — generated active (8) and planned (5) cards; flow chips from the registry
- `site/app.js` — `data-en` / `data-fr` copy for generated nodes; retired Creation / Training walker strings
- `site/styles.css` — upcoming grid `auto-fit`; tighter flow chips
- `site/README.md`
- `.github/workflows/landing-pages.yml` — idempotence + marker checks + stale-name guard

## Evidence

- `node --check` on `app.js` and `build-assets.mjs`
- Two consecutive `node site/build-assets.mjs` runs: `fc` reports no difference on `index.html`
- Generated ids: personal, development, business, creative, learning, it-student, gaming, entertainment; planned family, data-ai, it-admin, cloud-devops, cybersecurity
- Flow chips use registry labels (Creative / Learning), not Creation / Training
- Browser EN/FR: eight chips + proof line; chips use `flex: 0 0 auto` so they do not shrink into circles

## Not done

- Generated logo/brand copies stay build output (`node site/build-assets.mjs`)
