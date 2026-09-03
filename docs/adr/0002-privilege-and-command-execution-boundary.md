# ADR-0002: Privilege and command-execution boundary

- Status: Accepted
- Date: 2026-09-03
- Decision owners: AgenStudio / AgenStart
- Related: #11, ADR-0001

## Context

AgenStart is a desktop setup assistant that will inspect a machine, recommend software and invoke package-management operations. The product therefore sits close to a security boundary: a normal desktop UI can indirectly cause executables/installers to run and, in some cases, trigger Windows UAC.

The architecture must prevent a compromised UI, malformed catalogue entry or unexpected provider result from turning AgenStart into a generic privileged command runner.

The Windows MVP uses Avalonia 12 + .NET 10/C# per ADR-0001 and will initially use WinGet as its package provider.

## Decision

AgenStart adopts the following privilege and execution model.

### 1. Main application runs as standard user

`AgenStart.App` will run as `asInvoker`. Routine application startup must not require administrator privileges.

The UI, recommendation engine, catalogue, machine inventory and package-plan orchestration execute in the standard-user process.

### 2. WinGet is invoked non-elevated

The Windows MVP will invoke WinGet from the standard-user AgenStart process.

If a selected installer requires elevation, Windows/the installer owns the UAC prompt. AgenStart will not wrap normal WinGet installation inside an AgenStart-owned elevated broker.

This keeps the privileged surface smaller and preserves a platform-visible elevation boundary.

### 3. No shell-based command execution for providers

Package providers will use direct process execution (`ProcessStartInfo`) with `UseShellExecute = false` and discrete `ArgumentList` entries.

AgenStart will not construct provider operations through `cmd.exe`, arbitrary PowerShell commands or catalogue-supplied command strings.

### 4. Provider requests are typed

Application/domain code requests operations such as:

```text
Install(applicationId, scope)
```

The provider resolves the canonical application identity through the trusted catalogue into an exact provider package ID/source and constructs the approved CLI arguments internally.

No MVP API will expose arbitrary `ExtraArguments`/raw command-line passthrough.

### 5. Exact package identity and source are mandatory

AgenStart may install only curated package mappings.

WinGet execution uses an exact configured package identifier and exact allowed source. Fuzzy search is not part of the install path.

User-added/custom WinGet sources do not automatically become AgenStart-trusted sources.

### 6. Security-bypass provider options are prohibited

Normal AgenStart package execution will not generate options that bypass integrity/security controls or pass uncontrolled installer arguments, including:

- `--ignore-security-hash`;
- `--ignore-local-archive-malware-scan`;
- `--override`;
- `--custom`;
- local manifests supplied by untrusted input;
- arbitrary/custom sources;
- `--force` as default recovery;
- automatic reboot permission.

Introducing any equivalent capability requires a new security review.

### 7. Future AgenStart-owned elevation uses a separate one-shot helper

If future AgenStart features require direct privileged Windows changes, they must not elevate the main Avalonia application.

A separate minimal helper may be introduced with these constraints:

```text
AgenStart.App (standard user)
       ↓
validated typed request
       ↓
AgenStart.Elevated (one-shot)
       ↓
allow-listed privileged operation
```

The helper must not expose generic process, shell, PowerShell or arbitrary registry APIs. It validates the request again inside the elevated boundary and uses restricted IPC with explicit Windows access control.

The MVP does not introduce this helper until a concrete privileged AgenStart-owned operation requires it.

### 8. Catalogue is security-sensitive policy

Package mappings are treated as a trust boundary. The application ships with a known-good validated catalogue.

Future remote catalogue updates require authenticity/integrity verification, schema and semantic validation, atomic activation and last-known-good fallback before they may influence package execution.

TLS transport alone is not sufficient to define catalogue authenticity.

### 9. Fail closed

AgenStart refuses package execution when it cannot safely determine:

- trusted catalogue identity;
- allowed provider/source;
- exact provider package mapping;
- provider executable resolution;
- valid command plan;
- package policy state.

The application must not silently degrade to fuzzy search, custom sources, raw commands or security-bypass flags to make an installation succeed.

## Rationale

AgenStart is not a general-purpose package terminal. Its value is that users choose an intent/application while AgenStart turns that into a predictable, explainable and safe package operation.

A permanently elevated application would make every UI/plugin/catalogue bug more dangerous. A generic elevation broker would create a reusable local privilege primitive. A shell-based provider would increase quoting/injection complexity. Fuzzy package installation would weaken package identity.

The adopted model keeps the trusted computing base deliberately small:

```text
trusted catalogue
+ typed application/provider contracts
+ provider command builder
+ OS/package-manager security mechanisms
```

rather than:

```text
UI/catalogue string
→ shell
→ elevated process
→ arbitrary command
```

## Alternatives considered

### Run the whole AgenStart application as administrator

Rejected.

It simplifies privileged operations but unnecessarily gives the entire UI/application process elevated rights. The blast radius of bugs and compromised input would be materially larger.

### Always elevate an AgenStart broker before invoking WinGet

Rejected for the MVP.

WinGet can be invoked from a non-elevated process and installers that require elevation can trigger the normal Windows UAC path. Wrapping all installations in our own broker adds privileged IPC/code without a demonstrated need.

### Use PowerShell/cmd as the universal provider abstraction

Rejected.

A shell makes raw strings part of the security boundary, increases escaping/injection risk and encourages future features to bypass typed provider contracts.

### Store complete install commands in the software catalogue

Rejected.

The catalogue must describe trusted identities/policy, not executable instructions. Provider-specific code owns CLI construction.

### Permit custom sources and advanced WinGet flags for power users

Deferred/rejected for the MVP.

AgenStart's trust claim depends on curation. A future advanced/enterprise mode may support private sources only after explicit source trust, authentication and policy requirements are designed.

### Implement automatic global rollback

Rejected as a security/reliability guarantee.

Third-party Windows installers do not form a transactional system. AgenStart will record partial success, support retry/verification and only perform destructive uninstall/reversal with explicit supported behaviour and user intent.

## Consequences

### Positive

- smallest practical privileged surface for the Windows MVP;
- UAC remains visible and controlled by Windows/installers;
- catalogue compromise cannot directly inject arbitrary shell commands;
- package identity is deterministic at execution time;
- provider policy can be unit-tested without running real installers;
- future elevation remains possible without redesigning the entire application;
- architecture translates conceptually to future macOS support.

### Trade-offs

- some future Windows configuration features will require a separate helper design;
- advanced installer overrides/custom repositories are unavailable in the consumer MVP;
- provider code must explicitly model supported operations rather than passing raw flags through;
- AgenStart cannot guarantee atomic rollback across heterogeneous installers;
- executable/source validation adds implementation work but is mandatory.

## Implementation constraints for Issue #4

The first `WinGetProvider` must:

- run non-elevated;
- resolve WinGet through an explicit trusted resolution strategy;
- use direct process execution with `UseShellExecute = false`;
- construct command arguments through `ArgumentList`;
- install by exact package ID + configured source;
- reject unapproved sources/unsafe options;
- capture/normalize process results;
- support cancellation/timeouts safely;
- expose structured results rather than raw exit codes to the UI;
- never accept arbitrary command strings from the caller/catalogue.

A privileged helper is explicitly **out of scope** for Issue #4.

## Verification

This ADR is considered implemented when the provider/security test suite proves that:

- arbitrary catalogue strings cannot become executable commands;
- prohibited flags cannot be generated through normal provider requests;
- custom/unapproved sources fail closed;
- exact package/source binding is preserved;
- UAC cancellation/elevation denial becomes a normal structured failure;
- provider logs/results follow the redaction/normalization rules in `docs/security/security-model.md`.

## Revisit triggers

Revisit this ADR before introducing:

- AgenStart-owned privileged machine configuration;
- persistent service/daemon components;
- arbitrary script execution;
- private/custom package repositories;
- direct installer downloads outside provider trust;
- remote catalogue updates;
- self-update;
- macOS privileged operations.

Until such a trigger occurs, the accepted implementation direction is **standard-user AgenStart + non-elevated WinGet + OS-owned installer elevation + typed, allow-listed execution contracts**.
