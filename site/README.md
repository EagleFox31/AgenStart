# AgenStart public site

Static, dependency-free landing page for AgenStart.

## Local preview

Prepare the web copies of the canonical desktop logo assets, then run any static file server:

```bash
node site/build-assets.mjs
python -m http.server 4173 --directory site
```

Then open `http://localhost:4173`.

`site/assets/generated-logos/` and `site/assets/generated-brand/` are build output. Application artwork stays sourced from `src/AgenStart.Desktop/Assets/AppLogos/`, while the favicon reuses the canonical desktop app icon from `src/AgenStart.Desktop/Assets/agenstart-app-icon.*`; the landing does not maintain duplicate brand assets.

## Release download

The primary CTA resolves the latest published GitHub release at runtime and prefers the standalone `win-x64.exe` asset. It falls back to the ZIP asset, then to the stable `/releases/latest` page if release metadata is unavailable.

## Deployment

The primary production deployment is **Vercel**, with `https://agenstart.trigenys.com/` as the canonical public URL.

The repository-level `vercel.json` prepares the canonical landing assets with `node site/build-assets.mjs` and publishes the `site/` directory as a static site. Connect the GitHub repository to Vercel so pushes to `main` deploy automatically.

The existing `.github/workflows/landing-pages.yml` workflow can remain as a GitHub Pages mirror/fallback, but all canonical metadata points to the Trigenys domain.
