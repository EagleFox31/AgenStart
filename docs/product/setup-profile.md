# Reproducible setup profile format

AgenStart setup profiles are portable, reviewable JSON documents that describe the **desired application set**, not a copy of a machine.

The current format is `agenstart.setup` schema version `1`.

## Design goals

- portable across compatible AgenStart installations;
- based on canonical AgenStart application IDs rather than machine-specific package state;
- safe to inspect before execution;
- strict validation before any installation planning;
- no hostname, username, serial number, MAC address, file path, account identity, hardware fingerprint or other sensitive machine identifier;
- current installed-software state is always re-evaluated on the destination machine;
- import never bypasses Review or installation approval.

## Example

```json
{
  "kind": "agenstart.setup",
  "schemaVersion": 1,
  "createdAtUtc": "2026-09-04T12:00:00+00:00",
  "profileId": "development",
  "applications": [
    {
      "applicationId": "git",
      "reason": "Essential for source control."
    },
    {
      "applicationId": "visual-studio-code",
      "reason": "Recommended for development."
    }
  ],
  "metadata": {
    "name": "Development setup",
    "agenStartVersion": "0.1.0-alpha"
  }
}
```

## Import pipeline

```text
setup JSON
    │
    ▼
strict JSON parser
    │
    ▼
schema/version validation
    │
    ▼
canonical application IDs
    │
    ▼
current AgenStart catalogue
    │
    ├── unknown/unsupported IDs → actionable validation result
    │
    ▼
installed-software inventory on destination PC
    │
    ▼
desired vs current comparison
    │
    ├── already installed → visible, skipped
    │
    └── missing + supported → proposed for installation
    │
    ▼
Review screen
    │
    ▼
explicit user approval
```

The imported file therefore never contains executable commands or arbitrary provider arguments. Package-provider identities are resolved from the destination machine's trusted AgenStart catalogue.

## Validation rules

- maximum document size: 256 KB;
- `kind` must be `agenstart.setup`;
- only schema version `1` is accepted by the current implementation;
- at least one and at most 200 applications;
- canonical application IDs must be lowercase portable identifiers and unique case-insensitively;
- unknown JSON fields are rejected rather than silently ignored;
- recommendation context is optional and capped at 512 characters;
- malformed, corrupted or unsupported documents produce validation errors and cannot proceed to installation.

JSON Schema: [`setup-profile.schema.json`](./setup-profile.schema.json).

## Privacy boundary

The setup profile contract intentionally has no field for machine inventory. Do not add fields for:

- hostname/device name;
- username or account identity;
- serial numbers;
- MAC addresses;
- disk/BIOS identifiers;
- personal file names or paths;
- full environment or process lists;
- credentials, tokens or secrets;
- hardware-derived fingerprints.

If recommendation context later needs capability information, it must use coarse, non-identifying categories and receive a separate privacy review.

## Versioning

`schemaVersion` is mandatory. Unsupported future versions fail closed with a clear diagnostic instead of being interpreted as version 1.

A future schema version must document migration/compatibility behavior before it is accepted by the importer.
