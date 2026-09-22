# AgenStart

<p align="center">
  <img src="src/AgenStart.Desktop/Assets/agenstart-app-icon.png" alt="AgenStart icon" width="120" />
</p>

<p align="center">
  <strong>A local-first Windows setup assistant that turns a fresh PC into a ready-to-work machine.</strong><br/>
  <a href="https://agenstart.trigenys.com/">Website</a> · <a href="https://github.com/EagleFox31/AgenStart/releases/latest">Download for Windows · v0.4.0</a>
</p>

> **An AgenStudio project by [EagleFox31](https://github.com/EagleFox31)** · *Think sharp. Build what matters.*

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
- Creative
- Learning
- Gaming

Profiles are **multi-select**: AgenStart merges recommendations across the selected workloads instead of forcing a machine into one rigid category.

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

## Public site and Windows distribution

AgenStart has a public bilingual landing page at **https://agenstart.trigenys.com/**, deployed on Vercel.

The site is intentionally lightweight and dependency-free. It:

- supports **English and French** through an in-page language toggle;
- reuses the canonical AgenStart icon and bundled application logos instead of maintaining duplicate brand assets;
- resolves the latest published GitHub release at runtime;
- prefers the standalone **win-x64 EXE** artifact, falls back to the ZIP package, then to the stable GitHub release page;
- is deployed on **Vercel** from the repository, with `agenstart.trigenys.com` as its canonical production domain.

The current stable release is **v0.4.0**, published on **2026-09-22**, with a standalone Windows executable and SHA-256 checksum artifacts.

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

### Current implementation stack

- **C# / .NET 10** for the application and domain layers;
- **Avalonia UI 12.1.2** with XAML and compiled bindings for the Windows desktop experience;
- **WinGet** for trusted package discovery, preparation and installation;
- **Windows Registry + WinGet export** for installed-software inventory;
- **xUnit v3** and Microsoft .NET Test SDK for automated tests;
- a modular architecture split across Core, Application, Catalogue, Package Management, Windows Platform, Recommendations and Software Inventory projects.

The stack is now implemented and versioned. Future architecture-impacting changes will continue to be documented through ADRs.

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

**Stage: Functional prototype · v0.4.0 · released 2026-09-22**

The current application already includes:

- Windows machine and operating-system inventory;
- installed-software detection through WinGet and the Windows Registry;
- a curated catalogue with bundled local artwork for supported applications;
- **six multi-select usage profiles** — Personal, Development, Business, Creative, Learning and Gaming;
- explainable, merged recommendations with visible pipeline progress and compatibility checks;
- trusted WinGet package preparation and sequential installation;
- setup profile import/export, local history, settings and installation reports;
- a bilingual public landing page with direct latest-release resolution and Vercel deployment under `agenstart.trigenys.com`;
- continuous GitHub project-governance reconciliation, including GitHub App / OAuth-broker based synchronization;
- automated tests covering the application, catalogue, recommendation and Windows platform layers.

Next priorities:

1. sign Windows release artifacts and define the in-app upgrade strategy;
2. harden installation recovery, cancellation, retry and post-install verification;
3. expand and maintain the curated catalogue and provider metadata;
4. broaden release diagnostics and Windows compatibility testing;
5. keep the desktop app, landing page, README, roadmap and Project Registry aligned with each shipped release.

---

## What AgenStart is not

AgenStart is not intended to be:

- a cracked-software installer;
- an arbitrary script runner;
- an opaque “AI optimizer” that changes the computer without explanation;
- a replacement for enterprise device-management platforms;
- a catalogue filled with unverified third-party download links.

**Trust is part of the product.**

## License

AgenStart is proprietary software owned by **EagleFox31** and published under the **AgenStudio** brand. Public access to this repository does not grant permission to copy, modify, redistribute, deploy or commercially reuse the code.

Third-party components remain subject to their own licences. See [`LICENSE`](LICENSE) and [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md).

---

<p align="center">
  <strong>AgenStart</strong><br/>
  Prepare once. Understand everything. Reproduce anywhere.<br/><br/>
  <sub>An AgenStudio project by <a href="https://github.com/EagleFox31">EagleFox31</a> · Think sharp. Build what matters.</sub>
</p>
