# Repository Governance V1 validation

AgenStart is the primary real-world integration consumer for AppFactory Repository Governance V1. The validation is intentionally performed on the existing product repository rather than a disposable fixture.

## Candidate under test

- AppFactory commit: `eac14d9e6f1a4cc15959949eb6c234912cfa0c0a`
- policy preset: `solo`
- target: the repository's symbolic default branch
- workflow: manual `plan` / `apply`

The workflow is pinned to the exact pre-release candidate so the test cannot change underneath a run. It must move to `EagleFox31/appfactory-project-automation@v1` only after V1 is released.

## Baseline before governance

Captured on 2026-09-16 before enabling the integration:

- default branch: `main`
- repository Rulesets: none
- open pull requests: none
- latest release: `v0.3.0`
- latest Project automation runs: successful
- latest desktop build and test runs: successful
- latest release workflow runs: successful
- existing Project workflow remains configured with `PROJECT_TOKEN`
- existing release workflow remains configured with `AGENSTART_RELEASE_TOKEN`

Repository governance uses the separate `APPFACTORY_GOVERNANCE_TOKEN`; it does not reuse or replace either existing credential.

## Controlled validation sequence

1. Merge the integration through the normal pull-request flow.
2. Run **Repository governance** with `governance_mode=plan` and retain the read-only plan output.
3. Confirm the plan proposes exactly one AppFactory-managed Ruleset and no unrelated mutation.
4. Run `apply` once.
5. Confirm pull requests are required and default-branch deletion and force pushes are blocked.
6. Run `apply` again and confirm `NO-OP` / no write.
7. Exercise a controlled issue and pull request; confirm Project state transitions and CI remain operational.
8. Introduce one AppFactory-owned Ruleset drift, run `apply`, and confirm an in-place minimal update rather than recreation.
9. Run `plan` again and confirm convergence.
10. Confirm unrelated repository settings, workflows, issues and releases remain unchanged.

## Result

Pending execution after this integration reaches the default branch.
