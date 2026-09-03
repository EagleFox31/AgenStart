# GitHub Project automation

AgenStart automates repetitive GitHub Projects v2 metadata updates through the reusable **AppFactory Project Automation** GitHub Action.

The reusable engine lives in:

```text
EagleFox31/appfactory-project-automation
```

AgenStart keeps only its product-specific policy/configuration and the workflow triggers.

## Local files

```text
.github/project-config.json
.github/workflows/project-automation.yml
```

The workflow consumes:

```yaml
uses: EagleFox31/appfactory-project-automation@v1
```

The reusable action owns GraphQL Project discovery, item synchronization, field resolution, lifecycle transitions and manual Issue resync parsing.

## Purpose

The automation can:

- discover the Project by owner + title;
- add Issues to the Project;
- resolve Project field IDs dynamically;
- resolve single-select option IDs dynamically;
- populate `Status`, `Priority`, `Work type`, `Phase` and `Size`;
- infer `Work type` from title prefixes;
- use explicit metadata embedded in an Issue body;
- apply versioned overrides for the initial backlog;
- move linked Issues to `In Progress`, `Review` or `Done` based on Pull Request lifecycle;
- manually resync one Issue with `workflow_dispatch`.

No Project node ID, field ID or option ID is committed to AgenStart.

## Required repository secret

Repository secret:

```text
PROJECT_TOKEN
```

The token must be able to read/write the user-owned GitHub Project. It must never be committed, printed in logs or placed in issue metadata.

## Project contract

Configured in:

```text
.github/project-config.json
```

Expected Project title:

```text
AgenStart Product Development
```

Expected single-select fields:

```text
Status
Priority
Work type
Phase
Size
```

The reusable action resolves actual GraphQL IDs at runtime from these human-readable names.

If an optional field or option is absent, that field is skipped with a warning rather than guessed.

## Status transitions

```text
Issue opened/reopened     → Backlog
Draft PR opened/reopened  → In Progress
Non-draft PR opened       → Review
PR marked ready           → Review
PR merged                 → Done
Issue closed              → Done
```

Closing an unmerged Pull Request does not automatically move the Issue backwards because the correct state cannot be inferred safely.

## Linking a Pull Request to an Issue

PR status automation relies on GitHub's closing-issue relationship.

Use a supported closing keyword in the PR body, for example:

```text
Closes #11
```

or:

```text
Fixes #11
```

The action queries `closingIssuesReferences`; it does not scrape arbitrary `#123` text from descriptions.

## Issue metadata

For new issues, project metadata can be embedded without cluttering the rendered description:

```html
<!-- agenstart-project
priority: P1
workType: Engineering
phase: Foundation
size: M
-->
```

Supported keys:

```text
priority
workType
phase
size
```

Embedded metadata has highest precedence.

Resolution order:

```text
Title-prefix inference
        ↓
Versioned issue override
        ↓
Embedded issue metadata
```

`Status` is deliberately not accepted from issue metadata. It is owned by lifecycle state.

## Existing backlog overrides

The initial AgenStart issues predate embedded metadata, so `.github/project-config.json` contains explicit mappings for their Priority, Work type, Phase and Size.

These mappings are migration/bootstrap data rather than a long-term requirement. New issues should prefer the embedded metadata block.

## Manual resync

From GitHub:

```text
Actions
→ Project automation
→ Run workflow
→ Issue number
```

Accepted inputs include:

```text
15
#15
issue_number = 15
```

Manual resync is convergent: it adds the Issue if missing, reapplies configured metadata and aligns `Status` with the Issue state (`Backlog` when open, `Done` when closed).

## Security model

Pull Request lifecycle events use `pull_request_target`, not `pull_request`, because `PROJECT_TOKEN` is privileged.

The workflow:

1. executes from the trusted workflow on the default branch;
2. checks out the trusted default branch explicitly;
3. never checks out PR head code while `PROJECT_TOKEN` is available;
4. calls the centrally maintained AppFactory Action;
5. gives the built-in `GITHUB_TOKEN` read-only repository permissions;
6. passes `PROJECT_TOKEN` only to the Project synchronization Action.

Do **not** modify the PR job to execute contributor code while `PROJECT_TOKEN` is available.

## Failure philosophy

The automation fails closed rather than guessing Project configuration.

Examples:

- Project cannot be resolved → workflow fails clearly;
- token cannot access Project → workflow fails clearly;
- field exists but option is missing → warning and skip that field;
- PR has no closing Issue reference → no status mutation;
- unmerged PR closes → no backwards status guess.

The GitHub Project is workflow assistance, not the source of truth for application behavior.

## AppFactory reuse

AgenStart is the first consumer of the reusable AppFactory component. Other product repositories can use the same engine while maintaining their own `.github/project-config.json` and lightweight event workflow.
