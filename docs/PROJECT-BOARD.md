# AgenStart — GitHub Project Configuration

The GitHub Project is the operational control plane for the AgenStart backlog. The README communicates direction; Issues describe units of work; the Project shows execution state.

## Project name

**AgenStart Product Development**

## Recommended views

### 1. Delivery Board
Primary Kanban view grouped by **Status**.

```text
Backlog → Ready → In Progress → Review → Validation → Done
```

### 2. Roadmap
Timeline/roadmap view grouped by **Phase**.

### 3. Current Work
Filtered view:

```text
Status != Done
AND Priority in (P0, P1)
```

### 4. Technical Foundation
Filtered view for architecture, engineering, security and quality work.

## Fields

### Status
- Backlog
- Ready
- In Progress
- Review
- Validation
- Done

### Priority
- P0 — Critical
- P1 — High
- P2 — Normal
- P3 — Later

### Type
- Product
- Feature
- Engineering
- UX
- Security
- Quality
- Documentation
- Bug

### Phase
- Phase 0 — Foundation
- Phase 1 — Machine Understanding
- Phase 2 — Catalogue & Recommendations
- Phase 3 — Installation Engine
- Phase 4 — Desktop MVP
- Phase 5 — Reproducible Setups
- Phase 6 — Release Hardening

### Effort
Keep estimates deliberately coarse:
- XS
- S
- M
- L
- XL

Anything larger than `XL` should normally be decomposed before becoming Ready.

## Initial ordering

Recommended first execution sequence:

1. Issue #1 — Select desktop stack / ADR-001
2. Issue #2 — Machine inventory boundary
3. Issue #11 — Security and privilege model
4. Issue #3 — Catalogue schema
5. Issue #10 — CI/test baseline
6. Issue #4 — Provider abstraction / WinGet adapter
7. Issue #5 — Installed application detection
8. Issue #6 — Recommendation engine
9. Issue #7 — Installation orchestrator
10. Issue #8 — Guided desktop MVP flow
11. Issue #9 — Reproducible setup profiles

This is not a strict waterfall. Independent work can overlap once its architectural dependencies are resolved.

## Ready policy

An Issue moves from **Backlog** to **Ready** only when it satisfies the AppFactory Definition of Ready in `docs/APPFACTORY.md`.

## Done policy

An Issue reaches **Done** only after implementation and verification are complete. A merged PR alone is not proof that product behaviour works.

## Recommended automation

When configuring GitHub Projects, automate where possible:

- newly added Issues → Backlog;
- PR opened → linked work can move to Review when appropriate;
- item reopened → return from Done;
- closed completed Issues → Done.

Keep automation simple enough that board state remains trustworthy.
