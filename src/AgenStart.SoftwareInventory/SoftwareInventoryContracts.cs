using AgenStart.PackageManagement;

namespace AgenStart.SoftwareInventory;

public enum InstalledSoftwareSourceKind
{
    Registry = 0,
    PackageProvider = 1
}

public enum InstalledSoftwareScope
{
    Unknown = 0,
    User = 1,
    Machine = 2
}

public enum InventorySourceState
{
    Complete = 0,
    Partial,
    Unavailable,
    Failed,
    TimedOut
}

public sealed record InventorySourceStatus(
    string SourceId,
    InventorySourceState State,
    string? DiagnosticCode = null,
    string? Message = null)
{
    public bool IsComplete => State == InventorySourceState.Complete;
}

public sealed record InstalledSoftwareRecord(
    InstalledSoftwareSourceKind SourceKind,
    string SourceId,
    string DisplayName,
    string? Publisher = null,
    string? Version = null,
    InstalledSoftwareScope Scope = InstalledSoftwareScope.Unknown,
    string? ProviderId = null,
    string? PackageId = null,
    string? PackageSource = null);

public sealed record InstalledSoftwareSnapshot(
    IReadOnlyList<InstalledSoftwareRecord> Records,
    IReadOnlyList<InventorySourceStatus> Sources,
    DateTimeOffset CapturedAtUtc);

public sealed record SoftwareDetectionTarget(
    string ApplicationId,
    string DisplayName,
    string Publisher,
    IReadOnlyList<ProviderPackageReference> ProviderPackages,
    IReadOnlyList<string> RegistryDisplayNames);

public enum SoftwarePresenceState
{
    Installed = 0,
    Missing,
    Unknown
}

public sealed record SoftwareStateDiagnostic(
    string Code,
    string Message);

public sealed record DetectedApplicationState(
    string ApplicationId,
    SoftwarePresenceState State,
    string? InstalledVersion,
    IReadOnlyList<InstalledSoftwareRecord> Evidence,
    IReadOnlyList<SoftwareStateDiagnostic> Diagnostics);

public sealed record SoftwareDetectionResult(
    IReadOnlyList<DetectedApplicationState> Applications,
    int UnmappedRecordCount);
