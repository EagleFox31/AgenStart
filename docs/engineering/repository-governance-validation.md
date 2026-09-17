# Repository Governance V1 validation

AgenStart is the primary real-world integration consumer for AppFactory Repository Governance V1. The validation is intentionally performed on the existing product repository rather than a disposable fixture.

## Candidate under test

- AppFactory commit: `b1631349800f85a29dc2948391745e2fc67c1eb6`
- policy preset: `solo`
- target: the repository's symbolic default branch
- initial workflow: manual `plan` / `apply`

The initial workflow was pinned to the exact pre-release candidate so the test could not change underneath a run.

## Continuous operation

After successful manual adoption, AgenStart opts into AppFactory continuous reconciliation from commit `1a5e2b3e996cff77c631235c6dfd42ca710eecb1`. The governance workflow:

- automatically applies approved governance config or workflow changes when they reach the default branch;
- runs daily at **03:17 UTC** to repair out-of-band drift;
- retains manual `plan` / `apply` for diagnostics;
- serializes governance writes through the shared reusable workflow;
- cannot enter Project automation, merge pull requests or create releases.

The reusable workflow and Action runtime are both pinned to the same immutable candidate until Repository Governance V1 is released, then they move to `@v1`.

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

### Attempt 1 — input-boundary failure, no mutation

The first live `plan` reached the Action but interpreted `governance-mode` as `off`. GitHub exposes that input as `INPUT_GOVERNANCE-MODE`; the candidate read the incorrect underscore form `INPUT_GOVERNANCE_MODE` and requested the unrelated Project token.

The run failed before governance preflight and left the repository with zero Rulesets. AppFactory corrected the platform-input adapter, added literal runtime-boundary tests and recorded the reusable lesson in `LESSON-2026-006`.

### Revised candidate

The corrected candidate passed the first convergence stages:

- read-only `plan` run `35164260192` succeeded and proposed one `CREATE` for the AppFactory-managed Ruleset;
- the repository still exposed zero Rulesets after that plan, proving zero mutation;
- first `apply` run `35164480545` created Ruleset `23570327` exactly once;
- the Ruleset targets `~DEFAULT_BRANCH`, blocks deletion and non-fast-forward updates, and requires pull requests with resolved review threads;
- second `apply` run `35164629329` reported `NO-OP` and `Applied: no changes were necessary`;
- the managed Ruleset retained the same id and unchanged update timestamp after the no-op;
- the release workflow completed successfully after the corrected candidate reached `main`;
- controlled issue #69 triggered Project automation successfully and entered the existing backlog lifecycle.

No unrelated Ruleset or classic branch protection existed before the test, and none was introduced by the plan. The controlled pull request closing issue #69 supplies the protected-branch and pull-request lifecycle evidence.

### Final checks

- controlled PR #70 merged through the protected default branch and automatically closed issue #69;
- Project automation succeeded for issue open, PR open, PR merge and issue close events;
- the release workflow remained successful after governance activation and did not auto-merge the release PR;
- a controlled approving-review drift changed the managed value from `0` to `1` without changing any other rule;
- repair run `35186267729` reported `UPDATE`, changed only `Required approving reviews: 1 -> 0`, and updated Ruleset `23570327` in place;
- final read-only plan run `35186516718` reported `NO-OP` / `No changes`;
- the repository retained one AppFactory-managed Ruleset, default branch `main`, release `v0.3.0`, the pre-existing product issue and the unmerged release PR;
- no AgenStart-specific source logic was added to AppFactory.

## Outcome

**PASS.** AgenStart validates Repository Governance V1 as a retroactive primary consumer: plan safety, single-resource creation, protected pull-request flow, Project/release non-regression, idempotent no-op behavior and minimal in-place drift repair all passed on the live repository.
