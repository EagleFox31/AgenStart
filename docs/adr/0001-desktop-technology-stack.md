# ADR-0001: Desktop technology stack

- Status: Accepted
- Date: 2026-09-03
- Decision owners: AgenStudio / AgenStart

## Context

AgenStart is a local-first desktop setup assistant whose first release targets Windows, with a credible future expansion to macOS.

The product must combine:

- a premium, fluid desktop experience;
- strong operating-system integration;
- low operational complexity for a small engineering team;
- direct-download distribution from an AgenStudio website;
- a maintainable security boundary for privileged operations;
- package-provider abstraction, beginning with WinGet on Windows and potentially Homebrew/Cask on macOS;
- an architecture that can evolve without rewriting the product when a second operating system is added.

The principal candidates evaluated were:

1. WinUI 3 + .NET
2. Tauri 2 + React/TypeScript + Rust
3. Avalonia 12 + .NET

Electron was considered but rejected early because its Chromium-bundled runtime is not aligned with AgenStart's footprint and maintenance goals.

## Decision

AgenStart will use:

- **Avalonia 12** for the desktop UI framework;
- **C#** as the primary application language;
- **.NET 10 LTS** as the runtime and application platform;
- a modular architecture that keeps domain logic independent from the UI framework;
- platform-specific adapters behind explicit interfaces from the first release.

The initial target is Windows. macOS support is considered an architectural extension, not a fork of the product.

## Why Avalonia

### 1. One primary ecosystem

AgenStart can remain primarily inside the C#/.NET ecosystem for UI, domain logic, system integration, testing, logging, dependency injection, serialization, process management and platform services.

This avoids introducing React/TypeScript + Rust + npm + Cargo unless a future requirement demonstrates a clear need.

### 2. Strong fit for a system-oriented application

AgenStart's hardest problems are expected to be operating-system integration rather than page rendering:

- machine inventory;
- installed-application detection;
- package-manager orchestration;
- process execution;
- privilege boundaries;
- verification of installation state;
- failure recovery;
- platform-specific APIs.

.NET provides a mature environment for these concerns, while Avalonia keeps the application cross-platform.

### 3. Cross-platform path without rewriting the UI

WinUI 3 would provide excellent Windows integration but would lock the presentation layer to Windows.

Avalonia allows the same application/UI architecture to target Windows and macOS while platform-specific behaviour remains isolated behind adapters.

A future macOS implementation may use Homebrew/Cask and native macOS services while reusing the same domain model, recommendation engine, catalogue, workflow and much of the UI.

### 4. Native desktop rendering model

Avalonia is a desktop UI framework rather than a browser shell. This aligns with AgenStart's goal of feeling like a first-class desktop utility while remaining cross-platform.

### 5. Team and hiring pragmatism

C#/.NET has a broad professional developer market and allows AgenStudio to recruit general .NET engineers rather than requiring every desktop contributor to be proficient in both frontend web technologies and Rust.

### 6. AppFactory reuse

The decision supports reusable AgenStudio desktop capabilities such as:

- machine inventory;
- package-provider abstractions;
- elevation brokers;
- logging and diagnostics;
- update infrastructure;
- profile import/export;
- platform capability detection;
- release automation.

These can be packaged as reusable .NET libraries across future desktop products.

## Architecture constraints

This decision does not permit a monolithic Avalonia application.

The intended dependency direction is:

```text
AgenStart.UI (Avalonia)
        ↓
AgenStart.Application
        ↓
AgenStart.Core
        ↓
Platform abstractions
     ┌───────────────┐
     ↓               ↓
Windows           macOS (future)
```

Suggested initial projects:

```text
src/
├── AgenStart.App
├── AgenStart.Core
├── AgenStart.Application
├── AgenStart.Platform.Abstractions
├── AgenStart.Platform.Windows
├── AgenStart.PackageManagement
└── AgenStart.Contracts

tests/
├── AgenStart.Core.Tests
├── AgenStart.Application.Tests
└── AgenStart.Platform.Windows.Tests
```

The exact project boundaries may evolve, but the following rules are mandatory:

- Core/domain code must not depend on Avalonia.
- Core/domain code must not depend directly on WinGet.
- Platform-specific behaviour must sit behind explicit interfaces.
- Package managers must be represented as providers rather than hard-coded commands.
- The UI must not execute arbitrary shell commands.

## Package-provider strategy

Windows MVP:

```text
IPackageProvider
      ↓
WinGetProvider
```

Future macOS:

```text
IPackageProvider
      ↓
HomebrewProvider
```

A catalogue entry should use AgenStart's canonical application identity and map that identity to platform-specific provider identifiers.

Example conceptually:

```json
{
  "id": "vscode",
  "providers": {
    "windows": {
      "winget": "Microsoft.VisualStudioCode"
    },
    "macos": {
      "homebrew-cask": "visual-studio-code"
    }
  }
}
```

The domain layer must never treat WinGet or Homebrew identifiers as the application's canonical identity.

## Privilege model

The main AgenStart process must not run permanently as administrator.

Operations requiring elevation must be isolated behind a minimal privileged boundary.

Conceptually:

```text
AgenStart.App
standard user
      ↓
validated typed operation
      ↓
AgenStart elevated helper/broker
only when required
      ↓
OS privileged operation
```

The privileged component must accept structured, validated operations rather than arbitrary command strings.

Example:

```text
InstallPackage(packageId, provider, scope)
```

not:

```text
Execute("some arbitrary PowerShell command")
```

The detailed privilege design will be handled by a dedicated architecture/security decision.

## Distribution implications

AgenStart is expected to support direct download from an AgenStudio website.

Framework choice does not remove platform signing requirements:

- Windows direct distribution will require an appropriate code-signing strategy to minimize SmartScreen friction.
- macOS direct distribution will require Apple Developer ID signing and notarization.

Packaging, signing and update mechanisms will be covered by a separate ADR.

## Performance and footprint

Avalonia is not expected to produce the absolute smallest package among the candidates; Tauri is likely to retain an advantage in minimal installer footprint.

That advantage is accepted because AgenStart prioritizes:

1. system-integration maintainability;
2. architectural simplicity;
3. cross-platform continuity;
4. professional desktop performance;
5. team scalability.

Application size must still be measured and optimized during release hardening rather than assumed acceptable.

Native AOT may be evaluated later where compatible with required system APIs and libraries. It is not a mandatory MVP constraint.

## Alternatives considered

### Tauri 2 + React + TypeScript + Rust

**Strengths**

- very small application shell;
- excellent frontend design freedom;
- strong cross-platform story;
- strong security/capability model;
- strong ecosystem momentum;
- existing React/TypeScript skills can be reused.

**Why not selected**

AgenStart's core complexity is system integration. Tauri would introduce a second major application ecosystem and Rust expertise into a product where .NET already provides a strong fit for the operating-system and orchestration requirements.

Adding a .NET sidecar to Tauri was also evaluated and deferred because it would introduce three primary technology ecosystems (TypeScript, Rust and C#) before a demonstrated need justifies that complexity.

Tauri remains the primary alternative if future evidence shows that web-frontend reuse and minimal package size outweigh the benefits of a unified .NET stack.

### WinUI 3 + .NET

**Strengths**

- excellent Windows-native integration;
- first-class Fluent/Windows UX;
- mature .NET access to Windows APIs;
- strong fit for a permanently Windows-only system utility.

**Why not selected**

WinUI is Windows-specific. A credible macOS roadmap would require a second presentation stack or a significant UI migration. This creates avoidable product-level platform lock-in.

WinUI remains the preferred fallback if AgenStart is later formally scoped as Windows-only.

### Electron + React

Rejected because its bundled browser/runtime footprint and rapid runtime-maintenance cycle offer little strategic value for AgenStart compared with the other candidates.

## Consequences

### Positive

- one primary language and runtime for the application;
- strong .NET integration for Windows system work;
- cross-platform UI path;
- broad hiring pool;
- simpler build and dependency story than a multi-runtime architecture;
- reusable .NET AppFactory capabilities;
- future macOS support can be implemented through adapters instead of a product rewrite.

### Negative / trade-offs

- larger minimal footprint than a highly optimized Tauri application;
- less unrestricted styling freedom than browser/CSS-based UI;
- team must learn Avalonia/XAML patterns and establish an AgenStudio design system;
- macOS support still requires platform-specific integrations and testing;
- direct distribution still requires code signing and notarization work.

## Revisit triggers

This ADR must be reconsidered if any of the following becomes true:

- AgenStart becomes permanently Windows-only and deep Windows UX integration becomes the dominant requirement;
- the desired UI cannot be delivered acceptably in Avalonia without disproportionate engineering cost;
- installer/runtime footprint becomes a validated business blocker;
- Avalonia's platform support or maintenance direction no longer meets AgenStart requirements;
- a future AgenStudio desktop platform standard makes another stack materially cheaper to maintain.

Until one of these triggers occurs, **Avalonia 12 + .NET 10 LTS is the accepted desktop foundation for AgenStart**.
