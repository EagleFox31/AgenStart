# AgenStart — Working Roadmap

This roadmap converts the product vision into executable engineering phases.

## Phase 0 — Foundation

### Product & engineering
- Define personas and primary setup scenarios.
- Select desktop technology stack.
- Create ADR-001 for stack selection.
- Define repository conventions and branching model.
- Define logging and error model.
- Establish test strategy.
- Add CI baseline.

### Security
- Define privilege boundary.
- Document allowed system inspection data.
- Threat-model package execution and external providers.
- Define trusted-source policy.

### Catalogue
- Define application schema.
- Define provider schema.
- Define categories, tags, compatibility rules and recommendation reasons.

## Phase 1 — Machine understanding

- Read Windows version/build and architecture.
- Detect CPU, RAM, GPU and storage.
- Normalize capability model.
- Detect installed applications.
- Handle partial/unknown inventory safely.
- Add fixture-driven tests.

## Phase 2 — Catalogue & recommendations

- Build curated starter catalogue.
- Add WinGet identifiers.
- Add usage profiles.
- Implement deterministic recommendation rules.
- Explain every recommendation.
- Detect conflicts and unnecessary duplicates.
- Provide manual catalogue browsing.

## Phase 3 — Installation engine

- Create provider interface.
- Implement WinGet adapter.
- Implement command execution safety boundary.
- Add queue management.
- Stream progress/status.
- Add cancellation where supported.
- Implement retries with explicit policy.
- Verify post-install state.
- Generate structured report.

## Phase 4 — Desktop MVP

- Onboarding and privacy disclosure.
- Machine summary.
- Profile selection.
- Recommendations screen.
- Application review/selection.
- Installation progress.
- Failure/retry UX.
- Final report.

## Phase 5 — Reproducible setups

- Define portable profile format.
- Export setup profile.
- Import and validate profile.
- Compare desired vs current state.
- Re-run setup safely on another machine.
- Version profile schema.

## Phase 6 — Release hardening

- Windows installer/package.
- CI build artifacts.
- Release workflow.
- Code signing strategy.
- Upgrade strategy.
- Crash diagnostics strategy.
- Expanded Windows compatibility matrix.
- Documentation and contribution guide.

## MVP exit criteria

The MVP is ready when a clean supported Windows machine can:

1. launch AgenStart;
2. inspect its environment locally;
3. select a usage profile;
4. receive explainable recommendations;
5. approve a subset of applications;
6. install those applications through trusted providers;
7. see accurate success/failure state;
8. export a reusable setup profile.
