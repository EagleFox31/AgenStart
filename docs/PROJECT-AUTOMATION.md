# GitHub Project automation

AgenStart automates repetitive GitHub Projects v2 metadata updates through GitHub Actions and the GraphQL API.

## Purpose

The automation reduces manual Project maintenance while keeping the workflow visible and version-controlled.

It can:

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

No Project node ID, field ID or option ID is committed to the repository.

---

## Required repository secret

Repository secret:

```text
PROJECT_TOKEN
```

The token is expected to be a Personal Access Token that can read/write the user-owned GitHub Project.

The secret must never be committed to the repository, printed in logs or placed in issue metadata.

---

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

The automation resolves the actual GraphQL IDs at runtime from these human-readable names.

If an optional field or option is absent, that field is skipped with a warning rather than guessed.

---

## Status transitions

Current lifecycle mapping:

```text
Issue opened/reopened     → Backlog
Draft PR opened/reopened  → In Progress
Non-draft PR opened       → Review
PR marked ready           → Review
PR merged                 → Done
Issue closed              → Done
```

Closing an unmerged Pull Request does not automatically move the Issue backwards because the correct state cannot be inferred safely.

---

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

The automation queries `closingIssuesReferences`; it does not scrape arbitrary `#123` text from descriptions.

---

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

`Status` is deliberately not accepted from issue metadata. It is owned by workflow state transitions.

---

## Title-prefix inference

Examples:

```text
[Engineering] → Engineering
[Feature]     → Feature
[UX]          → UX
[Security]    → Security
[Product]     → Product
[Bug]         → Bug
```

`[Foundation]` currently maps to `Engineering` for the `Work type` field; `Foundation` itself is a Phase, not a work type.

---

## Existing backlog overrides

The initial AgenStart issues predate embedded metadata, so `.github/project-config.json` contains explicit mappings for their Priority, Work type, Phase and Size.

These mappings are migration/bootstrap data rather than a long-term requirement.

New issues should prefer the embedded metadata block.

---

## Manual resync

From GitHub:

```text
Actions
→ Project automation
→ Run workflow
→ Issue number
```

The manual sync:

- adds the Issue if missing;
- reapplies Priority / Work type / Phase / Size;
- preserves the current Status when the Issue is already in the Project;
- assigns `Backlog` or `Done` only when the manual sync has to add a missing Issue.

This makes manual resync safe for Issues already in `In Progress` or `Review`.

---

## Security model

Pull Request lifecycle events use `pull_request_target`, not `pull_request`.

This is intentional because `PROJECT_TOKEN` is a privileged secret.

The workflow:

1. executes using the trusted workflow from the default branch;
2. checks out the trusted default branch explicitly;
3. never checks out PR head code;
4. runs only the automation script from the trusted default branch;
5. gives the built-in `GITHUB_TOKEN` read-only repository permissions;
6. uses `PROJECT_TOKEN` only for the Projects GraphQL calls.

Do **not** change the PR job to checkout contributor code while `PROJECT_TOKEN` is available.

---

## Failure philosophy

The automation does not guess missing Project configuration.

Examples:

- Project cannot be resolved → workflow fails clearly;
- token cannot access Project → workflow fails clearly;
- field exists but option is missing → warning and skip that field;
- PR has no closing Issue reference → no status mutation;
- unmerged PR closes → no backwards status guess.

The Project is workflow assistance, not the source of truth for application behavior.

---

## Future AppFactory reuse

The architecture intentionally separates:

```text
project-config.json
        +
project-automation.mjs
        +
project-automation.yml
```

so the same pattern can later be extracted into an AgenStudio AppFactory repository/action and reused by other products.
