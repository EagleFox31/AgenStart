# AgenStart

> **A local-first Windows setup assistant that turns a fresh PC into a ready-to-work machine — intelligently, transparently, and reproducibly.**
>
> **BY AGENSTUDIO** · *Think sharp. Build what matters.*

AgenStart is being built to remove one of the most repetitive parts of owning, preparing, or deploying a Windows PC: figuring out what should be installed, finding trustworthy sources, installing everything one application at a time, and rebuilding the same setup again later.

The goal is not to create another software catalogue.

**AgenStart should understand the machine, understand the intended use, propose a coherent setup, let the user stay in control, and execute the setup safely.**

---

## Why AgenStart?

Setting up a Windows machine is still surprisingly manual.

A developer, student, trainer, creator, office worker, technician or small IT team often has to:

- inspect the machine manually;
- remember which applications are needed;
- search for official installers;
- avoid bundled or unsafe download sources;
- install applications one by one;
- repeat the exact same work on the next machine;
- remember what was installed and why.

AgenStart aims to turn that fragmented process into **one deliberate workflow**.

```text
Understand the PC
      ↓
Understand the user's goal
      ↓
Recommend a coherent setup
      ↓
Let the user review and approve
      ↓
Install through trusted providers
      ↓
Verify, report and save the setup
```

---

## Product vision

We want AgenStart to become a **trusted setup layer for Windows**.

A user should eventually be able to open AgenStart on a new or reinstalled computer and say, in effect:

> “This machine is for full-stack development, teaching and office work.”

AgenStart should determine what is already available, what is compatible with the machine, what is useful for that workload, what is unnecessary, and what can be installed safely.

The long-term ambition is bigger than batch installation. AgenStart is designed around the idea of **reproducible personal computing environments**: a setup should be understandable, exportable, repeatable and maintainable.

### What success looks like

AgenStart should make it possible to:

- prepare a new Windows PC in minutes instead of manually rebuilding a setup;
- receive recommendations based on **usage + machine capabilities**, not generic popularity;
- install selected applications in a controlled batch;
- avoid reinstalling software that is already present;
- reproduce a known setup on another machine;
- export and import setup profiles;
- keep a clear report of what succeeded, failed or was skipped;
- remain useful without requiring an AgenStudio account.

---

## Product principles

### Local first
Machine inspection and recommendation logic should run locally whenever possible.

### Explicit user control
Detection may be automatic. Installation is not. The user reviews the proposed setup before AgenStart changes the machine.

### Privacy by design
AgenStart does **not need personal files, browser history, passwords, MAC addresses or device serial numbers** to recommend software.

### Trusted installation paths
The preferred installation path is through trusted package providers such as **WinGet**, with carefully controlled fallbacks to official publisher sources when required.

### Explainable recommendations
A recommendation should have a reason. “Recommended because your profile is Development and Git is not installed” is useful. “Recommended by AI” is not enough.

### Reproducibility
A good setup is not a one-off event. Profiles, catalogues and installation results should be representable as versionable data.

### Failure is a first-class case
Installers fail. Networks disappear. Packages change. AgenStart should surface failures clearly, allow retries where safe, and never pretend an installation succeeded when it did not.

---

## Initial product scope

### Target platform
- Windows 10/11 x64
- desktop-first experience
- no mandatory cloud account

### Initial usage profiles
- Personal
- Development
- Business
- Creation
- Training / Education

### MVP capabilities
- local hardware and OS inventory;
- detection of already-installed applications;
- curated software catalogue;
- rule-based recommendation engine;
- compatibility and prerequisite checks;
- selection/review screen;
- installation queue;
- per-package progress and status;
- cancellation and safe retry where supported;
- final installation report;
- setup profile export/import;
- structured logs for diagnostics.

The initial catalogue is expected to contain roughly **40–60 carefully selected applications** rather than hundreds of poorly maintained entries.

---

## Architecture direction

```text
┌───────────────────────────────┐
│        Desktop Experience     │
├───────────────────────────────┤
│   Recommendation & Policies   │
├──────────────┬────────────────┤
│ PC Inventory │ Software State │
├──────────────┴────────────────┤
│ Catalogue & Provider Adapters │
├───────────────────────────────┤
│ Installation Orchestrator     │
├───────────────────────────────┤
│ Profiles · Reports · Logging  │
└───────────────────────────────┘
```

The implementation stack is **not considered a casual choice**. Major technical decisions will be documented through ADRs before the architecture becomes expensive to change.

---

## The AgenStudio AppFactory methodology

AgenStart is developed using our **AppFactory** approach: products are built from explicit, reusable engineering capabilities instead of accumulating ad-hoc code until something appears to work.

> **A feature is not finished because the UI works. It is finished when the product behaviour is specified, implemented, verified, observable and maintainable.**

Our loop:

1. **Discover** — define the real user problem, constraints, risks and success criteria.
2. **Specify** — turn ideas into issues, acceptance criteria, domain rules and technical decisions.
3. **Design** — define user flow, system boundaries, contracts and failure behaviour.
4. **Build** — implement small reviewable increments through branches and pull requests.
5. **Verify** — test happy paths, failure paths and OS-dependent behaviour.
6. **Ship** — automate builds, version releases and document meaningful changes.
7. **Observe** — capture actionable diagnostics without compromising privacy.
8. **Iterate** — feed real usage, maintenance lessons and defects back into the product system.

More detail: [`docs/APPFACTORY.md`](docs/APPFACTORY.md)

---

## Engineering standards

- `main` stays releasable;
- meaningful work is tracked with GitHub Issues;
- features and fixes use focused branches and pull requests;
- architecture-impacting choices get an ADR;
- new behaviour includes appropriate tests;
- CI validates proposed changes;
- releases are versioned and reproducible;
- dependencies and providers are treated as supply-chain boundaries;
- logs must help debugging without collecting unnecessary personal information;
- documentation evolves with implementation.

---

## Roadmap

### Phase 0 — Product foundation
Architecture, ADR process, repository conventions, catalogue schema, threat model and development stack.

### Phase 1 — Machine understanding
Windows inventory, installed-software detection and normalized capability models.

### Phase 2 — Catalogue & recommendations
Curated application catalogue, provider metadata, profiles, rules and recommendation explanations.

### Phase 3 — Installation engine
Provider adapters, queue orchestration, progress, cancellation, retries and post-install verification.

### Phase 4 — Desktop experience
Complete guided flow from onboarding to recommendations, approval, installation and report.

### Phase 5 — Reproducible setups
Profile export/import, reusable setup recipes and reliable re-execution on another compatible machine.

### Phase 6 — Release hardening
Packaging, CI/CD, signed releases where applicable, upgrade strategy, diagnostics and broader Windows testing.

Detailed working plan: [`docs/ROADMAP.md`](docs/ROADMAP.md)

---

## Backlog and project management

The README describes **where AgenStart is going**. It should not become the operational task tracker.

Use:
- **GitHub Issues** for concrete units of work;
- **GitHub Projects** for backlog, priority, status and roadmap views;
- **Pull Requests** for implementation and review;
- **ADRs** for durable technical decisions.

Recommended workflow:

```text
Backlog → Ready → In Progress → Review → Validation → Done
```

Priority:

```text
P0 Critical · P1 High · P2 Normal · P3 Later
```

Work types:

```text
Product · Feature · Engineering · UX · Security · Quality · Documentation · Bug
```

---

## Current status

**Stage: Product foundation / pre-MVP**

Next decisions:

1. choose and record the desktop technology stack;
2. define the machine inventory boundary;
3. define the software catalogue schema;
4. define provider abstraction and WinGet integration;
5. establish CI and test strategy;
6. prototype the end-to-end setup flow with a very small package set.

---

## What AgenStart is not

AgenStart is not intended to be:

- a cracked-software installer;
- an arbitrary script runner;
- an opaque “AI optimizer” that changes the computer without explanation;
- a replacement for enterprise device-management platforms;
- a catalogue filled with unverified third-party download links.

**Trust is part of the product.**

---

<p align="center">
  <strong>AgenStart</strong><br/>
  Prepare once. Understand everything. Reproduce anywhere.<br/><br/>
  <sub>Built by AgenStudio · Think sharp. Build what matters.</sub>
</p>
