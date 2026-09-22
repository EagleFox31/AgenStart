# AgenStart public site

Static, dependency-free landing page for AgenStart.

## Local preview

Prepare the web copies of the canonical desktop logo assets, then run any static file server:

```bash
node site/build-assets.mjs
python -m http.server 4173 --directory site
```

Then open `http://localhost:4173`.

`site/assets/generated-logos/` is build output. The source of truth remains `src/AgenStart.Desktop/Assets/AppLogos/`; the landing does not maintain a second logo library.

## Release download

The primary CTA resolves the latest published GitHub release at runtime and chooses the `win-x64.zip` asset when one is present. If GitHub's API is unavailable, the CTA falls back to the stable `/releases/latest` page.

## Deployment

The workflow `.github/workflows/landing-pages.yml` validates the site automatically on pull requests and pushes. GitHub Pages uses **Settings → Pages → Source: GitHub Actions**.

Once Pages is enabled, every `main` push that changes `site/**`, the canonical `AppLogos` assets, or the landing workflow deploys the current `site/` directory automatically. The workflow can also be started manually with `workflow_dispatch` when a redeploy is needed without changing files.
