using AgenStart.PackageManagement;

namespace AgenStart.Uninstall;

/// <summary>What the vendor's uninstall returned, normalised (ADR-0004 §4).</summary>
public sealed record OfficialUninstallResult(
    PackageOperationStatus Status,
    int? ExitCode,
    string DiagnosticCode,
    bool Retryable = false,
    bool Executed = true)
{
    public bool IsSuccess => Status is PackageOperationStatus.Succeeded or PackageOperationStatus.RebootRequired;

    public bool RebootRequired => Status == PackageOperationStatus.RebootRequired;

    /// <summary>The step was refused before anything ran (validation, policy, missing elevation).</summary>
    public static OfficialUninstallResult NotExecuted(PackageOperationStatus status, string diagnosticCode) =>
        new(status, null, diagnosticCode, Executed: false);
}

/// <summary>
/// Runs the reviewed official step. The only execution port of the official uninstall: everything
/// else in the plan stays a description.
/// </summary>
public interface IOfficialUninstaller
{
    Task<OfficialUninstallResult> UninstallAsync(
        InstalledProgram program,
        OfficialUninstallStep step,
        CancellationToken cancellationToken = default);
}

/// <summary>Read-only: which processes hold the program's files right now (Restart Manager on Windows).</summary>
public interface IRunningProcessDetector
{
    IReadOnlyList<RunningProcess> Detect(InstalledProgram program);
}

/// <summary>
/// What the person agreed to about running processes. Closing asks each window to close; forcing
/// (ending a process that did not close) is a second, separate approval (ADR-0004 §5).
/// </summary>
public sealed record ProcessCloseConsent(bool CloseWindows, bool ForceAfterTimeout = false)
{
    public static ProcessCloseConsent None { get; } = new(false);
}

public sealed record ProcessCloseResult(IReadOnlyList<RunningProcess> Closed, IReadOnlyList<RunningProcess> StillRunning);

/// <summary>Closes processes. Implementations must refuse to act without consent.</summary>
public interface IRunningProcessCloser
{
    Task<ProcessCloseResult> CloseAsync(
        IReadOnlyList<RunningProcess> processes,
        ProcessCloseConsent consent,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Exit codes of vendor uninstallers (MSI conventions, which Inno and NSIS wrappers mostly follow).
/// 1618 has no <see cref="PackageOperationStatus"/> of its own: it is reported as a retryable failure.
/// </summary>
public static class UninstallExitCodes
{
    public const int Success = 0;
    public const int UserCancelled = 1602;
    public const int AnotherInstallInProgress = 1618;
    public const int UnknownProduct = 1605;
    public const int RebootInitiated = 1641;
    public const int RebootRequired = 3010;
    public const int OperationCancelled = 1223;
    public const int ElevationRequired = 740;

    public const string SucceededCode = "uninstall.succeeded";
    public const string RebootRequiredCode = "uninstall.reboot-required";
    public const string InProgressCode = "uninstall.another-install-in-progress";
    public const string CancelledByUserCode = "uninstall.cancelled-by-user";
    public const string NotInstalledCode = "uninstall.not-installed";
    public const string FailedCode = "uninstall.exit-code";
    public const string ChangedSinceReviewCode = "uninstall.changed-since-review";
    public const string ElevationRequiredCode = "uninstall.requires-elevation";
    public const string StartFailedCode = "uninstall.start-failed";
    public const string TimedOutCode = "uninstall.timed-out";
    public const string CancelledCode = "uninstall.cancelled";

    public static OfficialUninstallResult Normalize(int exitCode) => exitCode switch
    {
        Success => new(PackageOperationStatus.Succeeded, exitCode, SucceededCode),
        RebootRequired or RebootInitiated => new(PackageOperationStatus.RebootRequired, exitCode, RebootRequiredCode),
        AnotherInstallInProgress => new(PackageOperationStatus.Failed, exitCode, InProgressCode, Retryable: true),
        UserCancelled or OperationCancelled => new(PackageOperationStatus.CancelledByUser, exitCode, CancelledByUserCode),
        UnknownProduct => new(PackageOperationStatus.NotFound, exitCode, NotInstalledCode),
        _ => new(PackageOperationStatus.Failed, exitCode, FailedCode)
    };
}
