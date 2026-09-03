# AgenStudio AppFactory — AgenStart

AppFactory is the product-engineering method used to build AgenStart as a maintainable system rather than a pile of features.

## Core rule

> A feature is complete only when its behaviour is specified, implemented, verified, observable and maintainable.

## Delivery loop

### 1. Discover
Clarify the user problem, target user, assumptions, constraints, risks and measurable success criteria.

### 2. Specify
Create a scoped issue with acceptance criteria. Define relevant domain rules before coding.

### 3. Design
Define UX flow, data contracts, module boundaries, permissions, failure states and security implications.

### 4. Build
Use focused branches. Keep increments small enough to review. Avoid mixing unrelated concerns in one PR.

### 5. Verify
Test domain logic, provider integrations and critical Windows behaviour. Failure paths are mandatory for installation workflows.

### 6. Ship
CI must produce repeatable results. Releases are versioned and changelog-worthy changes are documented.

### 7. Observe
Diagnostics must be useful but privacy-minimal. Never collect data just because it is technically available.

### 8. Iterate
Issues, support signals, failures and product usage feed future iterations.

---

## Reusable capability mindset

AgenStart should be composed from explicit capabilities:

- machine inventory;
- installed software discovery;
- software catalogue;
- recommendation engine;
- provider abstraction;
- installation orchestration;
- profile management;
- reporting;
- logging and diagnostics;
- packaging and update delivery.

Each capability should have a clear contract and should be replaceable without rewriting the entire product.

---

## Definition of Ready

An issue is **Ready** when:

- the problem is understandable;
- the expected behaviour is explicit;
- dependencies are known;
- acceptance criteria exist;
- major security or privilege questions are identified;
- the work is small enough to review meaningfully.

## Definition of Done

A feature is **Done** when:

- implementation is complete;
- relevant automated tests pass;
- failure behaviour is covered;
- logs and errors are actionable;
- documentation is updated when necessary;
- CI passes;
- the PR is reviewable and linked to its issue;
- no known blocker is being hidden behind the happy path.

---

## ADR policy

Create an Architecture Decision Record when a choice materially affects:

- technology stack;
- privilege model;
- local persistence;
- package provider architecture;
- update mechanism;
- security boundary;
- module contracts;
- long-lived dependency choices.

Use `docs/adr/NNNN-title.md` with **Context**, **Decision**, **Consequences** and **Alternatives considered**.

---

## Branch and PR discipline

Suggested branch prefixes:

```text
feat/
fix/
chore/
docs/
refactor/
test/
spike/
```

Every meaningful feature should travel through:

```text
Issue → Branch → Implementation → Verification → Pull Request → Review → Merge
```

`main` is not the experimentation branch. It represents the current trusted state of the product.

---

## AppFactory and velocity

AppFactory is not bureaucracy for its own sake. The objective is to make future work cheaper.

Reusable contracts, ADRs, CI, tests, catalogues and provider adapters create leverage: every new application profile, provider or installation workflow should become easier to ship than the previous one.
