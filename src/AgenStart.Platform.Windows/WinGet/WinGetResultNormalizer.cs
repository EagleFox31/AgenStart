using AgenStart.PackageManagement;

namespace AgenStart.Platform.Windows.WinGet;

public sealed record NormalizedWinGetResult(
    PackageOperationStatus Status,
    string DiagnosticCode);

public static class WinGetResultNormalizer
{
    private const int Success = 0;

    private static readonly IReadOnlyDictionary<int, NormalizedWinGetResult> KnownResults =
        new Dictionary<int, NormalizedWinGetResult>
        {
            [HResult(0x8A150061)] = new(PackageOperationStatus.AlreadyInstalled, "winget.package-already-installed"),
            [HResult(0x8A15010D)] = new(PackageOperationStatus.AlreadyInstalled, "winget.install-already-installed"),

            [HResult(0x8A150014)] = new(PackageOperationStatus.NotFound, "winget.no-applications-found"),
            [HResult(0x8A150016)] = new(PackageOperationStatus.Ambiguous, "winget.multiple-applications-found"),
            [HResult(0x8A150010)] = new(PackageOperationStatus.NoApplicableInstaller, "winget.no-applicable-installer"),

            [HResult(0x8A150012)] = new(PackageOperationStatus.SourceUnavailable, "winget.source-does-not-exist"),
            [HResult(0x8A150015)] = new(PackageOperationStatus.SourceUnavailable, "winget.no-sources-defined"),
            [HResult(0x8A150045)] = new(PackageOperationStatus.SourceUnavailable, "winget.source-open-failed"),
            [HResult(0x8A15004B)] = new(PackageOperationStatus.SourceUnavailable, "winget.failed-to-open-sources"),

            [HResult(0x8A150041)] = new(PackageOperationStatus.AgreementRequired, "winget.package-agreement-required"),
            [HResult(0x8A150046)] = new(PackageOperationStatus.AgreementRequired, "winget.source-agreement-required"),

            [HResult(0x8A150019)] = new(PackageOperationStatus.RequiresElevation, "winget.command-requires-admin"),

            [HResult(0x8A15001B)] = new(PackageOperationStatus.BlockedByPolicy, "winget.msstore-blocked-by-policy"),
            [HResult(0x8A15001C)] = new(PackageOperationStatus.BlockedByPolicy, "winget.msstore-app-blocked-by-policy"),
            [HResult(0x8A15003A)] = new(PackageOperationStatus.BlockedByPolicy, "winget.blocked-by-policy"),
            [HResult(0x8A15010F)] = new(PackageOperationStatus.BlockedByPolicy, "winget.install-blocked-by-policy"),

            [HResult(0x8A150011)] = new(PackageOperationStatus.IntegrityFailure, "winget.installer-hash-mismatch"),
            [HResult(0x8A15002D)] = new(PackageOperationStatus.IntegrityFailure, "winget.installer-security-check-failed"),
            [HResult(0x8A15003F)] = new(PackageOperationStatus.IntegrityFailure, "winget.source-integrity-failure"),
            [HResult(0x8A15005E)] = new(PackageOperationStatus.IntegrityFailure, "winget.pinned-certificate-mismatch"),
            [HResult(0x8A150060)] = new(PackageOperationStatus.IntegrityFailure, "winget.archive-scan-failed"),

            [HResult(0x8A150008)] = new(PackageOperationStatus.NetworkFailure, "winget.download-failed"),
            [HResult(0x8A15006D)] = new(PackageOperationStatus.NetworkFailure, "winget.service-unavailable"),
            [HResult(0x8A150107)] = new(PackageOperationStatus.NetworkFailure, "winget.install-no-network"),

            [HResult(0x8A150109)] = new(PackageOperationStatus.RebootRequired, "winget.reboot-required-to-finish"),
            [HResult(0x8A15010A)] = new(PackageOperationStatus.RebootRequired, "winget.reboot-required-for-install"),
            [HResult(0x8A15010B)] = new(PackageOperationStatus.RebootRequired, "winget.reboot-initiated"),

            [HResult(0x8A15010C)] = new(PackageOperationStatus.CancelledByUser, "winget.install-cancelled-by-user"),
            [HResult(0x8A150077)] = new(PackageOperationStatus.CancelledByUser, "winget.authentication-cancelled-by-user"),
            [HResult(0x8A150005)] = new(PackageOperationStatus.Cancelled, "winget.ctrl-signal-received"),
            [HResult(0x8A15006A)] = new(PackageOperationStatus.Cancelled, "winget.application-termination-received")
        };

    public static NormalizedWinGetResult Normalize(WinGetProcessResult processResult)
    {
        ArgumentNullException.ThrowIfNull(processResult);

        if (processResult.Cancelled)
        {
            return new NormalizedWinGetResult(PackageOperationStatus.Cancelled, "provider.cancelled");
        }

        if (processResult.TimedOut)
        {
            return new NormalizedWinGetResult(PackageOperationStatus.TimedOut, "provider.timed-out");
        }

        if (!processResult.Started || processResult.ExitCode is null)
        {
            return new NormalizedWinGetResult(PackageOperationStatus.ProviderUnavailable, "winget.process-start-failed");
        }

        if (processResult.ExitCode == Success)
        {
            return new NormalizedWinGetResult(PackageOperationStatus.Succeeded, "winget.success");
        }

        return KnownResults.TryGetValue(processResult.ExitCode.Value, out var result)
            ? result
            : new NormalizedWinGetResult(PackageOperationStatus.Failed, "winget.unmapped-error");
    }

    public static PackageResolutionStatus ToResolutionStatus(PackageOperationStatus status) => status switch
    {
        PackageOperationStatus.Succeeded => PackageResolutionStatus.Resolved,
        PackageOperationStatus.NotFound => PackageResolutionStatus.NotFound,
        PackageOperationStatus.Ambiguous => PackageResolutionStatus.Ambiguous,
        PackageOperationStatus.NoApplicableInstaller => PackageResolutionStatus.NoApplicableInstaller,
        PackageOperationStatus.SourceUnavailable or PackageOperationStatus.ProviderUnavailable => PackageResolutionStatus.SourceUnavailable,
        PackageOperationStatus.AgreementRequired => PackageResolutionStatus.AgreementRequired,
        PackageOperationStatus.BlockedByPolicy => PackageResolutionStatus.BlockedByPolicy,
        PackageOperationStatus.IntegrityFailure => PackageResolutionStatus.IntegrityFailure,
        PackageOperationStatus.Cancelled or PackageOperationStatus.CancelledByUser => PackageResolutionStatus.Cancelled,
        PackageOperationStatus.TimedOut => PackageResolutionStatus.TimedOut,
        _ => PackageResolutionStatus.Failed
    };

    private static int HResult(uint value) => unchecked((int)value);
}
