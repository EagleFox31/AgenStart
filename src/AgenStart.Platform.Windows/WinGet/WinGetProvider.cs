using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AgenStart.PackageManagement;

namespace AgenStart.Platform.Windows.WinGet;

public sealed partial class WinGetProvider : IPreparablePackageProvider
{
    private static readonly TimeSpan AvailabilityTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ResolveTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(30);

    private readonly IWinGetExecutableLocator _locator;
    private readonly IWinGetProcessRunner _runner;
    private readonly string _preparationRoot;
    private readonly ConcurrentDictionary<string, WinGetPreparedPackage> _preparedPackages =
        new(StringComparer.OrdinalIgnoreCase);

    public WinGetProvider()
        : this(new WinGetExecutableLocator(), new WinGetProcessRunner())
    {
    }

    public WinGetProvider(
        IWinGetExecutableLocator locator,
        IWinGetProcessRunner runner,
        string? preparationRoot = null)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _preparationRoot = ResolvePreparationRoot(preparationRoot);
    }

    public string ProviderId => PackageProviderIds.WinGet;

    public bool CanPrepare(ProviderPackageReference package)
    {
        ArgumentNullException.ThrowIfNull(package);

        try
        {
            WinGetCommandBuilder.ValidateReference(package);
        }
        catch (ArgumentException)
        {
            return false;
        }

        // Store packages need account/licensing semantics that are intentionally left to WinGet.
        return string.Equals(package.Source, "winget", StringComparison.OrdinalIgnoreCase);
    }

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

    public async Task<PackagePreparationResult> PrepareAsync(
        PackageInstallRequest request,
        IProgress<PackagePreparationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!CanPrepare(request.Package))
        {
            return new PackagePreparationResult(
                PackagePreparationStatus.Unsupported,
                request.ApplicationId,
                request.Package,
                DiagnosticCode: "winget.preparation-not-supported",
                Message: "This trusted WinGet source is not eligible for local package preparation.");
        }

        var preparationDirectory = Path.Combine(_preparationRoot, Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(preparationDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new PackagePreparationResult(
                PackagePreparationStatus.Failed,
                request.ApplicationId,
                request.Package,
                DiagnosticCode: "winget.preparation-directory-unavailable",
                Message: "AgenStart could not create its local package preparation directory.");
        }

        WinGetCommand command;
        try
        {
            command = WinGetCommandBuilder.BuildDownload(request, preparationDirectory);
        }
        catch (ArgumentException exception)
        {
            TryDeleteDirectory(preparationDirectory);
            return new PackagePreparationResult(
                PackagePreparationStatus.Failed,
                request.ApplicationId,
                request.Package,
                DiagnosticCode: "winget.invalid-download-request",
                Message: exception.Message);
        }

        var executable = _locator.Resolve();
        if (!executable.Found || string.IsNullOrWhiteSpace(executable.Path))
        {
            TryDeleteDirectory(preparationDirectory);
            return new PackagePreparationResult(
                PackagePreparationStatus.ProviderUnavailable,
                request.ApplicationId,
                request.Package,
                DiagnosticCode: executable.DiagnosticCode,
                Message: executable.Message);
        }

        progress?.Report(new PackagePreparationProgress(
            BytesDownloaded: 0,
            Message: $"Downloading {request.ApplicationId} through WinGet."));

        var processResult = await _runner.RunAsync(
            executable.Path,
            command.Arguments,
            DownloadTimeout,
            cancellationToken).ConfigureAwait(false);
        var normalized = WinGetResultNormalizer.Normalize(processResult);

        if (normalized.Status != PackageOperationStatus.Succeeded)
        {
            TryDeleteDirectory(preparationDirectory);
            return new PackagePreparationResult(
                ToPreparationStatus(normalized.Status),
                request.ApplicationId,
                request.Package,
                DiagnosticCode: normalized.DiagnosticCode,
                Message: MessageFor(normalized.Status));
        }

        var inspection = WinGetPreparedPackageInspector.Inspect(
            preparationDirectory,
            request.Package.PackageId,
            request.Silent);

        if (inspection.Status != PackagePreparationStatus.Ready || inspection.Package is null)
        {
            TryDeleteDirectory(preparationDirectory);
            return new PackagePreparationResult(
                inspection.Status,
                request.ApplicationId,
                request.Package,
                BytesDownloaded: inspection.BytesDownloaded,
                DiagnosticCode: inspection.DiagnosticCode,
                Message: inspection.Message);
        }

        if (!_preparedPackages.TryAdd(inspection.Package.PreparationId, inspection.Package))
        {
            TryDeleteDirectory(preparationDirectory);
            return new PackagePreparationResult(
                PackagePreparationStatus.Failed,
                request.ApplicationId,
                request.Package,
                DiagnosticCode: "winget.preparation-id-collision",
                Message: "AgenStart could not register the prepared package safely.");
        }

        progress?.Report(new PackagePreparationProgress(
            BytesDownloaded: inspection.BytesDownloaded,
            BytesRequired: inspection.BytesDownloaded,
            Fraction: 1,
            Message: $"{request.ApplicationId} is ready to install."));

        return new PackagePreparationResult(
            PackagePreparationStatus.Ready,
            request.ApplicationId,
            request.Package,
            inspection.Package.PreparationId,
            inspection.BytesDownloaded,
            inspection.DiagnosticCode,
            inspection.Message);
    }

    public async Task<PackageOperationResult> InstallPreparedAsync(
        PackageInstallRequest request,
        PackagePreparationResult preparation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(preparation);

        if (!preparation.IsReady ||
            !string.Equals(preparation.ApplicationId, request.ApplicationId, StringComparison.OrdinalIgnoreCase) ||
            preparation.Package != request.Package ||
            !_preparedPackages.TryGetValue(preparation.PreparationId!, out var prepared))
        {
            return new PackageOperationResult(
                PackageOperationStatus.Failed,
                request.ApplicationId,
                request.Package,
                DiagnosticCode: "winget.preparation-invalid",
                Message: "The prepared package does not match the approved installation request.");
        }

        if (!WinGetPreparedPackageInspector.IsWithinRoot(prepared.RootDirectory, prepared.InstallerPath) ||
            !File.Exists(prepared.InstallerPath))
        {
            return new PackageOperationResult(
                PackageOperationStatus.IntegrityFailure,
                request.ApplicationId,
                request.Package,
                DiagnosticCode: "winget.prepared-installer-missing",
                Message: "The prepared installer is no longer available in AgenStart's package cache.");
        }

        if (!VerifyInstallerHash(prepared.InstallerPath, prepared.InstallerSha256))
        {
            return new PackageOperationResult(
                PackageOperationStatus.IntegrityFailure,
                request.ApplicationId,
                request.Package,
                DiagnosticCode: "winget.prepared-installer-hash-mismatch",
                Message: "The prepared installer changed after download and was blocked before execution.");
        }

        PreparedInstallerCommand command;
        try
        {
            command = prepared.CreateInstallCommand(request.Silent);
        }
        catch (InvalidOperationException)
        {
            return new PackageOperationResult(
                PackageOperationStatus.ProviderUnavailable,
                request.ApplicationId,
                request.Package,
                DiagnosticCode: "winget.prepared-installer-launch-unavailable",
                Message: "Windows could not provide a trusted local installer launch path.");
        }

        var processResult = await _runner.RunAsync(
            command.ExecutablePath,
            command.Arguments,
            InstallTimeout,
            cancellationToken).ConfigureAwait(false);

        var status = NormalizePreparedInstallerResult(processResult, prepared.SuccessExitCodes);
        return new PackageOperationResult(
            status,
            request.ApplicationId,
            request.Package,
            processResult.ExitCode,
            DiagnosticForPreparedInstaller(status),
            MessageForPreparedInstaller(status));
    }

    public Task ReleasePreparationAsync(
        PackagePreparationResult preparation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(preparation.PreparationId) ||
            !_preparedPackages.TryRemove(preparation.PreparationId, out var prepared))
        {
            return Task.CompletedTask;
        }

        TryDeleteDirectory(prepared.RootDirectory);
        return Task.CompletedTask;
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

    private static string ResolvePreparationRoot(string? configuredRoot)
    {
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            if (!Path.IsPathFullyQualified(configuredRoot))
            {
                throw new ArgumentException(
                    "Package preparation root must be an absolute path.",
                    nameof(configuredRoot));
            }

            return Path.GetFullPath(configuredRoot);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var baseDirectory = string.IsNullOrWhiteSpace(localAppData)
            ? Path.GetTempPath()
            : localAppData;
        return Path.GetFullPath(Path.Combine(baseDirectory, "AgenStart", "PackageCache"));
    }

    private static bool VerifyInstallerHash(string path, string expectedHash)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var actualHash = Convert.ToHexString(SHA256.HashData(stream));
            return string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static PackageOperationStatus NormalizePreparedInstallerResult(
        WinGetProcessResult result,
        IReadOnlySet<int> successExitCodes)
    {
        if (result.Cancelled)
        {
            return PackageOperationStatus.Cancelled;
        }

        if (result.TimedOut)
        {
            return PackageOperationStatus.TimedOut;
        }

        if (!result.Started || result.ExitCode is null)
        {
            return result.StartError?.Contains("elevation", StringComparison.OrdinalIgnoreCase) == true
                ? PackageOperationStatus.RequiresElevation
                : PackageOperationStatus.Failed;
        }

        if (result.ExitCode is 3010 or 1641)
        {
            return PackageOperationStatus.RebootRequired;
        }

        if (result.ExitCode == 1602)
        {
            return PackageOperationStatus.CancelledByUser;
        }

        return successExitCodes.Contains(result.ExitCode.Value)
            ? PackageOperationStatus.Succeeded
            : PackageOperationStatus.Failed;
    }

    private static PackagePreparationStatus ToPreparationStatus(PackageOperationStatus status) => status switch
    {
        PackageOperationStatus.SourceUnavailable => PackagePreparationStatus.SourceUnavailable,
        PackageOperationStatus.AgreementRequired => PackagePreparationStatus.AgreementRequired,
        PackageOperationStatus.BlockedByPolicy => PackagePreparationStatus.BlockedByPolicy,
        PackageOperationStatus.IntegrityFailure => PackagePreparationStatus.IntegrityFailure,
        PackageOperationStatus.NetworkFailure => PackagePreparationStatus.NetworkFailure,
        PackageOperationStatus.Cancelled or PackageOperationStatus.CancelledByUser => PackagePreparationStatus.Cancelled,
        PackageOperationStatus.TimedOut => PackagePreparationStatus.TimedOut,
        PackageOperationStatus.ProviderUnavailable => PackagePreparationStatus.ProviderUnavailable,
        _ => PackagePreparationStatus.Failed
    };

    private static string DiagnosticForPreparedInstaller(PackageOperationStatus status) => status switch
    {
        PackageOperationStatus.Succeeded => "winget.prepared-installer-success",
        PackageOperationStatus.RebootRequired => "winget.prepared-installer-reboot-required",
        PackageOperationStatus.CancelledByUser => "winget.prepared-installer-cancelled-by-user",
        PackageOperationStatus.Cancelled => "winget.prepared-installer-cancelled",
        PackageOperationStatus.TimedOut => "winget.prepared-installer-timeout",
        PackageOperationStatus.RequiresElevation => "winget.prepared-installer-requires-elevation",
        _ => "winget.prepared-installer-failed"
    };

    private static string MessageForPreparedInstaller(PackageOperationStatus status) => status switch
    {
        PackageOperationStatus.Succeeded => "The hash-verified prepared installer completed successfully.",
        PackageOperationStatus.RebootRequired => "The hash-verified prepared installer completed and requires a reboot.",
        PackageOperationStatus.CancelledByUser => "The prepared installer was cancelled by the user.",
        PackageOperationStatus.Cancelled => "The prepared installer was cancelled.",
        PackageOperationStatus.TimedOut => "The prepared installer exceeded its execution timeout.",
        PackageOperationStatus.RequiresElevation => "The prepared installer requires elevation; retry through the normal trusted WinGet path.",
        _ => "The prepared installer returned a non-success result."
    };

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Cache cleanup is best-effort.
        }
        catch (UnauthorizedAccessException)
        {
            // Cache cleanup is best-effort.
        }
    }

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
