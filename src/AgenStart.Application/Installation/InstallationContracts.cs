using AgenStart.PackageManagement;

namespace AgenStart.Application.Installation;

public enum InstallationQueueItemState
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Skipped,
    Cancelled
}

public enum InstallationItemActivity
{
    Waiting = 0,
    Resolving,
    Downloading,
    Ready,
    Installing,
    Verifying,
    Completed,
    Failed,
    Skipped,
    Cancelled
}

public enum InstallationSessionState
{
    Ready,
    Running,
    Cancelling,
    Completed,
    Cancelled
}

public enum InstallationVerificationStatus
{
    Verified,
    NotInstalled,
    Unknown
}

public sealed record InstallationSelection(
    string ApplicationId,
    ProviderPackageReference Package,
    bool Approved,
    bool Silent = true,
    bool AcceptPackageAgreements = false,
    bool AcceptSourceAgreements = false);

public sealed record InstallationVerificationResult(
    InstallationVerificationStatus Status,
    string? InstalledVersion = null,
    string? DiagnosticCode = null,
    string? Message = null);

public interface IInstallationVerifier
{
    Task<InstallationVerificationResult> VerifyAsync(
        string applicationId,
        CancellationToken cancellationToken = default);
}

public sealed record InstallationItemSnapshot(
    int Sequence,
    string ApplicationId,
    ProviderPackageReference Package,
    InstallationQueueItemState State,
    InstallationItemActivity Activity,
    int AttemptCount,
    PackageOperationStatus? LastOperationStatus,
    string? DiagnosticCode,
    string? Message,
    string? InstalledVersion,
    bool CanRetry,
    bool RequiresReboot,
    long? BytesDownloaded,
    long? BytesRequired,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record InstallationProgressEvent(
    Guid SessionId,
    InstallationSessionState SessionState,
    InstallationItemSnapshot? Item,
    string Code,
    string Message,
    DateTimeOffset OccurredAtUtc);

public sealed record InstallationReport(
    Guid SessionId,
    InstallationSessionState State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<InstallationItemSnapshot> Items)
{
    public int SucceededCount => Items.Count(item => item.State == InstallationQueueItemState.Succeeded);
    public int FailedCount => Items.Count(item => item.State == InstallationQueueItemState.Failed);
    public int SkippedCount => Items.Count(item => item.State == InstallationQueueItemState.Skipped);
    public int CancelledCount => Items.Count(item => item.State == InstallationQueueItemState.Cancelled);
}
