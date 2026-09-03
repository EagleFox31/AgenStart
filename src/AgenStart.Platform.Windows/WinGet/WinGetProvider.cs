using System.Text.RegularExpressions;
using AgenStart.PackageManagement;

namespace AgenStart.Platform.Windows.WinGet;

public sealed partial class WinGetProvider : IPackageProvider
{
    private static readonly TimeSpan AvailabilityTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ResolveTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(30);

    private readonly IWinGetExecutableLocator _locator;
    private readonly IWinGetProcessRunner _runner;

    public WinGetProvider()
        : this(new WinGetExecutableLocator(), new WinGetProcessRunner())
    {
    }

    public WinGetProvider(
        IWinGetExecutableLocator locator,
        IWinGetProcessRunner runner)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public string ProviderId => PackageProviderIds.WinGet;

    public async Task<PackageProviderAvailability> CheckAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        var executable = _locator.Resolve();
        if (!executable.Found || string.IsNullOrWhiteSpace(executable.Path))
        {
            return new PackageProviderAvailability(
                OperatingSystem.IsWindows()
                    ? PackageProviderAvailabilityStatus.NotInstalled
                    : PackageProviderAvailabilityStatus.UnsupportedPlatform,
                DiagnosticCode: executable.DiagnosticCode,
                Message: executable.Message);
        }

        var processResult = await _runner.RunAsync(
            executable.Path,
            ["--version"],
            AvailabilityTimeout,
            cancellationToken).ConfigureAwait(false);

        if (processResult.Cancelled)
        {
            return new PackageProviderAvailability(
                PackageProviderAvailabilityStatus.Unhealthy,
                DiagnosticCode: "winget.availability-cancelled",
                Message: "WinGet availability check was cancelled.");
        }

        if (processResult.TimedOut)
        {
            return new PackageProviderAvailability(
                PackageProviderAvailabilityStatus.Unhealthy,
                DiagnosticCode: "winget.availability-timeout",
                Message: "WinGet did not respond before the availability timeout.");
        }

        if (!processResult.Started || processResult.ExitCode != 0)
        {
            return new PackageProviderAvailability(
                PackageProviderAvailabilityStatus.Unhealthy,
                DiagnosticCode: "winget.availability-failed",
                Message: "WinGet could not be executed successfully.");
        }

        var version = ParseVersion(processResult.StandardOutput);
        return new PackageProviderAvailability(
            PackageProviderAvailabilityStatus.Available,
            Version: version,
            DiagnosticCode: version is null ? "winget.version-unparsed" : null);
    }

    public async Task<PackageResolutionResult> ResolveAsync(
        ProviderPackageReference package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);

        WinGetCommand command;
        try
        {
            command = WinGetCommandBuilder.BuildResolve(package);
        }
        catch (ArgumentException exception)
        {
            return new PackageResolutionResult(
                PackageResolutionStatus.Failed,
                package,
                DiagnosticCode: "winget.invalid-package-reference",
                Message: exception.Message);
        }

        var executable = _locator.Resolve();
        if (!executable.Found || string.IsNullOrWhiteSpace(executable.Path))
        {
            return new PackageResolutionResult(
                PackageResolutionStatus.ProviderUnavailable,
                package,
                DiagnosticCode: executable.DiagnosticCode,
                Message: executable.Message);
        }

        var processResult = await _runner.RunAsync(
            executable.Path,
            command.Arguments,
            ResolveTimeout,
            cancellationToken).ConfigureAwait(false);

        var normalized = WinGetResultNormalizer.Normalize(processResult);
        return new PackageResolutionResult(
            WinGetResultNormalizer.ToResolutionStatus(normalized.Status),
            package,
            processResult.ExitCode,
            normalized.DiagnosticCode,
            MessageFor(normalized.Status));
    }

    public async Task<PackageOperationResult> InstallAsync(
        PackageInstallRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        WinGetCommand command;
        try
        {
            command = WinGetCommandBuilder.BuildInstall(request);
        }
        catch (ArgumentException exception)
        {
            return new PackageOperationResult(
                PackageOperationStatus.Failed,
                request.ApplicationId,
                request.Package,
                DiagnosticCode: "winget.invalid-install-request",
                Message: exception.Message);
        }

        var executable = _locator.Resolve();
        if (!executable.Found || string.IsNullOrWhiteSpace(executable.Path))
        {
            return new PackageOperationResult(
                PackageOperationStatus.ProviderUnavailable,
                request.ApplicationId,
                request.Package,
                DiagnosticCode: executable.DiagnosticCode,
                Message: executable.Message);
        }

        var processResult = await _runner.RunAsync(
            executable.Path,
            command.Arguments,
            InstallTimeout,
            cancellationToken).ConfigureAwait(false);

        var normalized = WinGetResultNormalizer.Normalize(processResult);
        return new PackageOperationResult(
            normalized.Status,
            request.ApplicationId,
            request.Package,
            processResult.ExitCode,
            normalized.DiagnosticCode,
            MessageFor(normalized.Status));
    }

    [GeneratedRegex(@"\bv?(\d+(?:\.\d+){1,3}(?:[-+][A-Za-z0-9.-]+)?)\b", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    private static string? ParseVersion(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var match = VersionPattern().Match(output);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string MessageFor(PackageOperationStatus status) => status switch
    {
        PackageOperationStatus.Succeeded => "WinGet completed successfully.",
        PackageOperationStatus.AlreadyInstalled => "The package is already installed.",
        PackageOperationStatus.NotFound => "The exact package mapping was not found in the configured source.",
        PackageOperationStatus.Ambiguous => "WinGet returned more than one package for an operation that requires an exact match.",
        PackageOperationStatus.NoApplicableInstaller => "The package exists but has no applicable installer for this machine.",
        PackageOperationStatus.SourceUnavailable => "The configured WinGet source is unavailable.",
        PackageOperationStatus.AgreementRequired => "A package or source agreement must be accepted explicitly before retrying.",
        PackageOperationStatus.RequiresElevation => "The operation requires elevation that WinGet could not obtain through the normal installer flow.",
        PackageOperationStatus.BlockedByPolicy => "Windows or organization policy blocked the package operation.",
        PackageOperationStatus.IntegrityFailure => "WinGet rejected the operation because an integrity or security check failed.",
        PackageOperationStatus.NetworkFailure => "The package operation failed because the network or provider service was unavailable.",
        PackageOperationStatus.RebootRequired => "The installer requires a reboot to continue or finish.",
        PackageOperationStatus.CancelledByUser => "The user cancelled the installer or authentication flow.",
        PackageOperationStatus.Cancelled => "The package operation was cancelled.",
        PackageOperationStatus.TimedOut => "The package operation exceeded its timeout.",
        PackageOperationStatus.ProviderUnavailable => "WinGet is unavailable for the current user session.",
        _ => "WinGet returned an unclassified failure."
    };
}
