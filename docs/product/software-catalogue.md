# AgenStart Software Catalogue

## Purpose

The AgenStart catalogue is the curated product model that describes software AgenStart may recommend, install, verify and reproduce across supported operating systems.

It is intentionally **not** a copy of WinGet, Homebrew or any other provider catalogue.

AgenStart owns the product meaning of an application; package providers only describe how that application is acquired on a specific platform.

```text
AgenStart application
        │
        ├── product metadata
        ├── recommendation metadata
        ├── capability requirements
        ├── dependencies / conflicts
        ├── lifecycle / trust
        │
        └── provider mappings
              ├── Windows → WinGet
              └── macOS   → Homebrew/Cask (future)
```

This separation is a core architectural rule.

---

## Design goals

The catalogue must be:

- curated rather than automatically mirroring an external repository;
- versioned and schema-validated;
- independent from any single package manager;
- deterministic enough for recommendation tests;
- portable across Windows and future macOS support;
- safe when provider metadata becomes stale;
- explicit about compatibility, dependencies and conflicts;
- explainable to the user;
- free from executable shell fragments.

---

## Canonical application identity

Every application has one immutable AgenStart `id`.

Examples:

```text
visual-studio-code
git
firefox
vlc
obs-studio
7zip
powertoys
docker-desktop
```

The canonical ID is **not** a WinGet ID, Homebrew token, executable name or publisher identifier.

It belongs to AgenStart and remains stable even if a package provider changes.

Example:

```text
AgenStart id: visual-studio-code

Windows provider:
  winget → Microsoft.VisualStudioCode

macOS provider:
  homebrew-cask → visual-studio-code
```

This allows profiles, recommendation rules and exported setups to reference `visual-studio-code` without knowing how it is installed on the current OS.

Canonical IDs use lowercase kebab-case and must never be silently reused for a different product.

---

## Catalogue versions

The catalogue has two independent versions:

### `schemaVersion`
Version of the contract understood by AgenStart.

Example:

```json
"schemaVersion": "1.0.0"
```

Changing field semantics, required properties or structural compatibility requires a schema version change.

### `catalogueVersion`
Version of the curated content.

Example:

```json
"catalogueVersion": "2026.9.0"
```

Adding an application, updating a provider mapping or changing recommendation metadata increments the catalogue version without necessarily changing the schema.

The runtime must reject unsupported schema major versions rather than guessing.

---

## Application model

Each application contains the following product-level concerns.

### Identity and presentation

- `id`
- `name`
- `publisher`
- `description`
- `categories`
- `tags`

These fields belong to AgenStart and must not be populated dynamically from package-manager output at runtime.

Provider metadata may be used for validation, but it does not own the product display model.

---

## Lifecycle

Every application has an explicit lifecycle:

```text
active
  available for recommendation/installation

deprecated
  still understood but should not normally be recommended

blocked
  must not be installed through AgenStart
```

A deprecated or blocked entry may declare a `replacementId`.

Example:

```json
{
  "status": "deprecated",
  "replacementId": "new-product",
  "message": "Publisher replaced this product."
}
```

A blocked entry is a safety state. The runtime must fail closed even if the underlying provider can still install it.

---

## Recommendations

Recommendation metadata identifies how an app relates to an AgenStart profile.

Supported initial profiles:

```text
personal
development
business
creation
training
```

Recommendation levels:

```text
essential
recommended
optional
```

Every recommendation includes a stable `reasonKey` rather than hard-coded UI prose.

Example:

```json
{
  "profile": "development",
  "level": "essential",
  "reasonKey": "development.source-control"
}
```

The recommendation engine will combine this metadata with machine capability, installed-software state and future user preferences. The catalogue does not itself make the final decision.

---

## Capability requirements

An application can define `minimum` and `recommended` capability requirements.

Initial V1 vocabulary:

- minimum RAM;
- minimum free storage;
- supported CPU architectures;
- whether a GPU is required.

Example:

```json
{
  "minimum": {
    "minRamMiB": 4096,
    "minFreeStorageMiB": 1000,
    "architectures": ["x64", "arm64"]
  },
  "recommended": {
    "minRamMiB": 8192,
    "minFreeStorageMiB": 3000,
    "architectures": ["x64", "arm64"]
  }
}
```

Requirements must describe meaningful product constraints rather than attempting to duplicate every vendor specification.

Unknown machine values must not automatically be interpreted as compatible.

---

## Platform support

Platform support is separate from provider mappings.

This distinction matters because an application can support an OS even when AgenStart has not yet implemented an installation provider for it.

Statuses:

```text
supported
planned
unsupported
```

Example:

```json
[
  {
    "platform": "windows",
    "status": "supported",
    "architectures": ["x64", "arm64"]
  },
  {
    "platform": "macos",
    "status": "planned",
    "architectures": ["x64", "arm64"]
  }
]
```

---

## Provider mappings

Provider metadata is deliberately isolated from product metadata.

### Windows / WinGet

A WinGet mapping contains:

- platform;
- provider type;
- exact `packageId`;
- source (`winget` or `msstore`);
- optional installation-scope preference;
- optional installer-type preference.

Example:

```json
{
  "platform": "windows",
  "type": "winget",
  "packageId": "Microsoft.VisualStudioCode",
  "source": "winget",
  "scopePreference": "user"
}
```

AgenStart must install WinGet packages using an exact identifier, equivalent to the intent of:

```text
winget install --id <PackageIdentifier> -e --source <source>
```

Provider search results must never be treated as authoritative enough to install a fuzzy match.

WinGet itself models packages with a unique `PackageIdentifier`, supports architecture and installer-type selection, and can expose duplicate entries across sources; the AgenStart mapping therefore stores both package ID and source explicitly.

### macOS / Homebrew

Future macOS mappings use either:

```text
homebrew-formula
homebrew-cask
```

with an exact Homebrew token.

Example:

```json
{
  "platform": "macos",
  "type": "homebrew-cask",
  "token": "visual-studio-code"
}
```

Homebrew itself models OS/architecture requirements and dependencies, but AgenStart keeps its own normalized product-level constraints so recommendation logic does not depend on Homebrew internals.

---

## Dependencies

Dependencies reference **canonical AgenStart IDs only**.

Example:

```json
"dependencies": ["git"]
```

Provider package identifiers must never appear in the dependency graph.

Why:

```text
AgenStart.Core
      │
      └── canonical dependency graph
                    │
                    ▼
             platform provider
```

This prevents the core from becoming coupled to WinGet or Homebrew.

The initial catalogue should use dependencies sparingly. Package-manager-native dependencies remain the provider's responsibility unless AgenStart needs the dependency for product semantics.

---

## Conflicts

Conflicts also reference canonical AgenStart IDs.

Example:

```json
"conflicts": ["competing-product"]
```

A conflict means AgenStart should prevent or explicitly resolve a conflicting plan before execution.

Provider-level conflicts may still exist independently; these are execution concerns and must be surfaced by the provider adapter if encountered.

---

## Verification

Installation success must not be inferred from exit code alone.

Each application declares one or more verification strategies.

Initial strategies:

```text
provider-query
registry-display-name
executable-path
bundle-id            # macOS future
command-version
```

Example:

```json
{
  "platform": "windows",
  "type": "command-version",
  "value": "git --version"
}
```

Security rule: verification metadata is **not arbitrary shell execution**.

The runtime implementation must translate supported verification rule types into constrained operations. Catalogue text is never passed directly to a shell without a typed allowlisted interpreter.

For V1, `provider-query` should be preferred where reliable, with secondary verification only when needed.

---

## Trust and curation

A catalogue entry includes:

- official homepage;
- license label;
- trust classification;
- optional curator notes.

Initial trust classifications:

```text
official-publisher
trusted-community
```

AgenStart should prefer official publisher package mappings whenever available.

A catalogue entry must never include:

- arbitrary download mirrors;
- cracks or license bypasses;
- opaque shell scripts;
- user-provided executable URLs promoted automatically to trusted status.

Trust is part of the product contract.

---

## Validation rules

A catalogue is invalid when any of the following occurs:

- schema validation fails;
- canonical IDs are duplicated;
- an application references itself as a dependency or conflict;
- dependency/conflict IDs do not exist;
- two provider mappings for the same application collide ambiguously;
- an `active` application has no usable provider for a platform marked `supported`;
- a blocked application is selected for installation;
- a replacement ID does not exist;
- duplicate profile recommendations exist for the same app/profile pair;
- provider-specific values appear in canonical dependency/conflict fields;
- the schema major version is unsupported.

These cross-entry constraints are validated in application code in addition to JSON Schema validation.

---

## Failure behaviour

The catalogue must fail closed.

### Invalid whole catalogue

If the bundled catalogue does not pass structural validation at startup:

- AgenStart must not execute installations;
- diagnostics identify the validation failure;
- the UI can still explain that the catalogue is unavailable;
- arbitrary partial parsing is prohibited.

### Invalid individual external update

If a future remotely-updated catalogue fails validation:

- keep the last known-good validated catalogue;
- reject the invalid update;
- never merge unvalidated fragments into active state.

### Missing provider

If an application exists but has no provider for the current platform:

```text
Known product ≠ installable product
```

It may be displayed as unavailable/planned, but cannot enter the execution queue.

### Stale provider ID

If a provider mapping no longer resolves:

- mark the provider unavailable for that session;
- do not fuzzy-search and install a similarly named package;
- surface a catalogue/provider maintenance diagnostic.

---

## Runtime ownership

The catalogue layer should expose domain concepts such as:

```text
ApplicationDefinition
CanonicalApplicationId
PlatformSupport
ProviderReference
RecommendationMetadata
CapabilityRequirements
VerificationPolicy
ApplicationLifecycle
```

The core must never expose WinGet process arguments or Homebrew command strings as catalogue domain objects.

Provider adapters translate typed provider references into execution operations.

---

## Suggested repository structure

```text
src/
├── AgenStart.Core/
│   └── Catalogue/
│       ├── ApplicationDefinition.cs
│       ├── Catalogue.cs
│       ├── CatalogueValidator.cs
│       └── ...
│
├── AgenStart.Platform.Windows/
│   └── Packages/
│       └── WinGetProvider.cs
│
└── AgenStart.Platform.MacOS/
    └── Packages/
        └── HomebrewProvider.cs       # future

catalogue/
├── catalogue.json
└── software-catalogue.schema.json
```

The initial repository documentation includes a formal JSON Schema and representative fixtures before production code is introduced.

---

## Initial fixture set

The schema is exercised against representative applications covering different categories and complexity:

- Git
- Visual Studio Code
- Mozilla Firefox
- VLC
- 7-Zip
- OBS Studio
- Microsoft PowerToys
- Docker Desktop

These fixtures are examples for contract design and tests. They are not yet the production catalogue.

---

## Out of scope for this issue

This issue does not implement:

- provider execution;
- installed-software detection;
- remote catalogue delivery;
- catalogue cryptographic signing;
- recommendation scoring;
- UI catalogue browsing;
- licensing/subscription entitlement logic.

Those concerns build on this contract rather than being embedded into it.

---

## Architectural rule

> **AgenStart knows applications. Providers know packages.**

If a core feature needs to know that Visual Studio Code is `Microsoft.VisualStudioCode`, the abstraction has leaked.
