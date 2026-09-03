namespace AgenStart.PackageManagement;

public static class PackageProviderIds
{
    public const string WinGet = "winget";
    public const string Homebrew = "homebrew";
}

public enum PackageScope
{
    Default = 0,
    User = 1,
    Machine = 2
}

public sealed record ProviderPackageReference(
    string ProviderId,
    string PackageId,
    string Source,
    PackageScope ScopePreference = PackageScope.Default);

public sealed record PackageInstallRequest(
    string ApplicationId,
    ProviderPackageReference Package,
    bool Silent = true,
    bool AcceptPackageAgreements = false,
    bool AcceptSourceAgreements = false);

public enum PackageProviderAvailabilityStatus
{
    Available = 0,
    UnsupportedPlatform,
    NotInstalled,
    Unhealthy
}

public sealed record PackageProviderAvailability(
    PackageProviderAvailabilityStatus Status,
    string? Version = null,
    string? DiagnosticCode = null,
    string? Message = null)
{
    public bool IsAvailable => Status == PackageProviderAvailabilityStatus.Available;
}

public enum PackageResolutionStatus
{
    Resolved = 0,
    NotFound,
    Ambiguous,
    NoApplicableInstaller,
    SourceUnavailable,
    AgreementRequired,
    BlockedByPolicy,
    IntegrityFailure,
    Cancelled,
    TimedOut,
    ProviderUnavailable,
    Failed
}

public sealed record PackageResolutionResult(
    PackageResolutionStatus Status,
    ProviderPackageReference Package,
    int? NativeExitCode = null,
    string? DiagnosticCode = null,
    string? Message = null)
{
    public bool IsResolved => Status == PackageResolutionStatus.Resolved;
}

public enum PackageOperationStatus
{
    Succeeded = 0,
    AlreadyInstalled,
    NotFound,
    Ambiguous,
    NoApplicableInstaller,
    SourceUnavailable,
    AgreementRequired,
    RequiresElevation,
    BlockedByPolicy,
    IntegrityFailure,
    NetworkFailure,
    RebootRequired,
    CancelledByUser,
    Cancelled,
    TimedOut,
    ProviderUnavailable,
    Failed
}

public sealed record PackageOperationResult(
    PackageOperationStatus Status,
    string ApplicationId,
    ProviderPackageReference Package,
    int? NativeExitCode = null,
    string? DiagnosticCode = null,
    string? Message = null)
{
    public bool IsSuccess => Status is PackageOperationStatus.Succeeded or PackageOperationStatus.AlreadyInstalled;
}

public interface IPackageProvider
{
    string ProviderId { get; }

    Task<PackageProviderAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default);

    Task<PackageResolutionResult> ResolveAsync(
        ProviderPackageReference package,
        CancellationToken cancellationToken = default);

    Task<PackageOperationResult> InstallAsync(
        PackageInstallRequest request,
        CancellationToken cancellationToken = default);
}
