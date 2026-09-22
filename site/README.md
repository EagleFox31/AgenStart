# AgenStart public site

Static, dependency-free landing page for AgenStart.

## Local preview

Run any static file server from this directory, for example:

```bash
python -m http.server 4173
```

Then open `http://localhost:4173`.

## Release download

The primary CTA resolves the latest published GitHub release at runtime and chooses the `win-x64.zip` asset when one is present. If GitHub's API is unavailable, the CTA falls back to the stable `/releases/latest` page.

## Deployment

The workflow `.github/workflows/landing-pages.yml` validates the site automatically on pull requests and pushes.

GitHub Pages must be enabled once in the repository with **Settings → Pages → Source: GitHub Actions**. After that one-time repository setting, run **Actions → AgenStart landing page → Run workflow** to publish the current `site/` directory.

Deployment is intentionally manual until Pages has been enabled so ordinary pushes do not fail because of repository-level Pages configuration.
