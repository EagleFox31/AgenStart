# AgenStart release baseline

AgenStart delegates product release orchestration to the reusable AppFactory workflow:

```text
EagleFox31/appfactory-project-automation/.github/workflows/release-dotnet-desktop.yml@v1
```

The product repository owns only the product-specific release inputs. AppFactory owns Release Please orchestration, semantic versioning, tagged release identity, deterministic .NET publishing, packaging and checksum generation.

## Release flow

1. Changes merge to `main` using Conventional Commit semantics (`feat:`, `fix:`, `perf:`, `docs:`, `refactor:`, `build:`, `ci:`, `test:`, `chore:`).
2. `.github/workflows/release.yml` calls the stable AppFactory `@v1` reusable workflow.
3. Release Please creates or updates the AgenStart Release PR and maintains `version.txt` plus `CHANGELOG.md`.
4. The Release PR must pass normal AgenStart CI before merge.
5. Merging the Release PR creates the semantic Git tag and GitHub Release.
6. AppFactory checks out the exact tagged release SHA on `windows-latest` and publishes `src/AgenStart.Desktop/AgenStart.Desktop.csproj` with .NET 10.
7. The release produces a self-contained Windows x64 ZIP and a SHA-256 checksum, and uploads both to the workflow run and GitHub Release.

Expected asset naming:

```text
AgenStart-v<version>-win-x64.zip
AgenStart-v<version>-win-x64.sha256.txt
```

## First release

The bootstrap commit carries a one-time Conventional Commit footer:

```text
Release-As: 0.1.0
```

That forces only the first AgenStart release to `v0.1.0`. The workflow itself does not contain a persistent `release-as` override, so later releases return to normal SemVer calculation.

## Versioning

Before `1.0.0`, AgenStart remains in rapid product evolution. Conventional Commit intent drives Release Please:

- `fix:`: bug fix release
- `feat:`: feature release
- `!` or `BREAKING CHANGE:`: breaking release

Do not manually tag normal releases and do not hand-edit a Release Please-generated version bump. `version.txt` and `CHANGELOG.md` are release-managed files after bootstrap.

## Token and repository settings

The reusable workflow accepts the optional repository secret `AGENSTART_RELEASE_TOKEN` and otherwise falls back to the workflow `GITHUB_TOKEN`.

For the `GITHUB_TOKEN` path, configure:

```text
Repository Settings
→ Actions
→ General
→ Workflow permissions
→ Read and write permissions
→ Allow GitHub Actions to create and approve pull requests
```

A dedicated PAT/GitHub App token stored as `AGENSTART_RELEASE_TOKEN` is preferred when Release Please-created pull requests must trigger ordinary pull-request CI automatically, because events produced by the built-in `GITHUB_TOKEN` can be suppressed by GitHub's recursion protection.

Never print or commit the release token.

## CI and reproducibility

The desktop build gate runs on `windows-latest`, restores with .NET 10, verifies formatting and builds Release with warnings treated as errors. Existing domain-specific test workflows remain path-filtered so normal PR feedback stays fast.

Release artifacts are reproducible from their tag because AppFactory builds from the exact release SHA rather than the moving `main` branch.
