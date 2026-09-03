using AgenStart.PackageManagement;

namespace AgenStart.Application.Installation;

public sealed class InstallationOrchestrator
{
    private readonly IReadOnlyDictionary<string, IPackageProvider> _providers;
    private readonly IInstallationVerifier _verifier;
    private readonly TimeProvider _timeProvider;

    public InstallationOrchestrator(
        IEnumerable<IPackageProvider> providers,
        IInstallationVerifier verifier,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _timeProvider = timeProvider ?? TimeProvider.System;

        var providerMap = new Dictionary<string, IPackageProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);
            if (string.IsNullOrWhiteSpace(provider.ProviderId))
            {
                throw new ArgumentException("Package providers must expose a provider id.", nameof(providers));
            }

            if (!providerMap.TryAdd(provider.ProviderId.Trim(), provider))
            {
                throw new ArgumentException(
                    $"Package provider {provider.ProviderId} was registered more than once.",
                    nameof(providers));
            }
        }

        _providers = providerMap;
    }

    public InstallationSession CreateSession(IReadOnlyList<InstallationSelection> selections) =>
        new(selections, _timeProvider);

    public async Task<InstallationReport> RunAsync(
        InstallationSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.BeginRun();

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            session.CancellationToken,
            cancellationToken);
        var token = linkedCancellation.Token;
        var cancelled = false;

        foreach (var item in session.MutableItems)
        {
            if (item.State != InstallationQueueItemState.Queued)
            {
                continue;
            }

            if (token.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            cancelled = await ExecuteItemAsync(session, item, token).ConfigureAwait(false);
            if (cancelled)
            {
                break;
            }
        }

        if (cancelled || token.IsCancellationRequested)
        {
            session.MarkQueuedItemsCancelled(
                "installation.cancelled-before-start",
                "The item was cancelled before execution started.");
            session.CompleteRun(cancelled: true);
        }
        else
        {
            session.CompleteRun(cancelled: false);
        }

        return session.CreateReport();
    }

    public async Task<InstallationReport> RetryAsync(
        InstallationSession session,
        string applicationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            throw new ArgumentException("An application id is required.", nameof(applicationId));
        }

        session.PrepareRetry(applicationId.Trim());
        return await RunAsync(session, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> ExecuteItemAsync(
        InstallationSession session,
        InstallationSession.InstallationQueueItem item,
        CancellationToken cancellationToken)
    {
        session.MarkRunning(item);

        try
        {
            var preVerification = await _verifier
                .VerifyAsync(item.Selection.ApplicationId, cancellationToken)
                .ConfigureAwait(false);

            if (preVerification.Status == InstallationVerificationStatus.Verified)
            {
                session.MarkSucceeded(
                    item,
                    preVerification.InstalledVersion,
                    PackageOperationStatus.AlreadyInstalled,
                    requiresReboot: false,
                    $"{item.Selection.ApplicationId} is already installed; provider execution was skipped.");
                return false;
            }

            if (!_providers.TryGetValue(item.Selection.Package.ProviderId, out var provider))
            {
                session.MarkFailed(
                    item,
                    "installation.provider-not-registered",
                    $"Provider {item.Selection.Package.ProviderId} is not registered for this session.",
                    canRetry: false,
                    PackageOperationStatus.ProviderUnavailable);
                return false;
            }

            var availability = await provider
                .CheckAvailabilityAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!availability.IsAvailable)
            {
                session.MarkFailed(
                    item,
                    availability.DiagnosticCode ?? "installation.provider-unavailable",
                    availability.Message ?? $"Provider {provider.ProviderId} is unavailable.",
                    CanRetryAvailability(availability.Status),
                    PackageOperationStatus.ProviderUnavailable);
                return false;
            }

            var resolution = await provider
                .ResolveAsync(item.Selection.Package, cancellationToken)
                .ConfigureAwait(false);

            if (!resolution.IsResolved)
            {
                if (resolution.Status == PackageResolutionStatus.Cancelled)
                {
                    session.MarkCancelled(
                        item,
                        resolution.DiagnosticCode ?? "installation.resolution-cancelled",
                        resolution.Message ?? "Package resolution was cancelled.",
                        PackageOperationStatus.Cancelled);
                    return true;
                }

                session.MarkFailed(
                    item,
                    resolution.DiagnosticCode ?? "installation.package-resolution-failed",
                    resolution.Message ?? $"Package {item.Selection.Package.PackageId} could not be resolved.",
                    CanRetryResolution(resolution.Status),
                    MapResolutionStatus(resolution.Status));
                return false;
            }

            var operation = await provider
                .InstallAsync(
                    new PackageInstallRequest(
                        item.Selection.ApplicationId,
                        item.Selection.Package,
                        item.Selection.Silent,
                        item.Selection.AcceptPackageAgreements,
                        item.Selection.AcceptSourceAgreements),
                    cancellationToken)
                .ConfigureAwait(false);

            if (operation.Status is PackageOperationStatus.Cancelled or PackageOperationStatus.CancelledByUser)
            {
                session.MarkCancelled(
                    item,
                    operation.DiagnosticCode ?? "installation.cancelled",
                    operation.Message ?? "Package installation was cancelled.",
                    operation.Status);
                return true;
            }

            if (!IsVerifiableCompletion(operation.Status))
            {
                session.MarkFailed(
                    item,
                    operation.DiagnosticCode ?? "installation.provider-failed",
                    operation.Message ?? $"Provider installation failed with status {operation.Status}.",
                    CanRetryOperation(operation.Status),
                    operation.Status);
                return false;
            }

            var verification = await _verifier
                .VerifyAsync(item.Selection.ApplicationId, cancellationToken)
                .ConfigureAwait(false);

            if (verification.Status == InstallationVerificationStatus.Verified)
            {
                session.MarkSucceeded(
                    item,
                    verification.InstalledVersion,
                    operation.Status,
                    operation.Status == PackageOperationStatus.RebootRequired,
                    verification.Message);
                return false;
            }

            if (verification.Status == InstallationVerificationStatus.NotInstalled)
            {
                session.MarkFailed(
                    item,
                    verification.DiagnosticCode ?? "installation.verification-not-installed",
                    verification.Message ?? "The provider completed, but the application was not detected after installation.",
                    canRetry: true,
                    operation.Status);
                return false;
            }

            session.MarkFailed(
                item,
                verification.DiagnosticCode ?? "installation.verification-unknown",
                verification.Message ?? "Installation completed, but installed state could not be verified safely.",
                canRetry: false,
                operation.Status);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            session.MarkCancelled(
                item,
                "installation.cancelled",
                "Package installation was cancelled.",
                PackageOperationStatus.Cancelled);
            return true;
        }
        catch (Exception exception)
        {
            session.MarkFailed(
                item,
                "installation.unhandled-provider-error",
                $"The installation operation failed unexpectedly: {exception.GetType().Name}.",
                canRetry: false,
                PackageOperationStatus.Failed);
            return false;
        }
    }

    private static bool IsVerifiableCompletion(PackageOperationStatus status) =>
        status is PackageOperationStatus.Succeeded
            or PackageOperationStatus.AlreadyInstalled
            or PackageOperationStatus.RebootRequired;

    private static bool CanRetryAvailability(PackageProviderAvailabilityStatus status) =>
        status is PackageProviderAvailabilityStatus.NotInstalled
            or PackageProviderAvailabilityStatus.Unhealthy;

    private static bool CanRetryResolution(PackageResolutionStatus status) =>
        status is PackageResolutionStatus.SourceUnavailable
            or PackageResolutionStatus.TimedOut
            or PackageResolutionStatus.ProviderUnavailable
            or PackageResolutionStatus.Failed;

    private static bool CanRetryOperation(PackageOperationStatus status) =>
        status is PackageOperationStatus.NetworkFailure
            or PackageOperationStatus.SourceUnavailable
            or PackageOperationStatus.TimedOut
            or PackageOperationStatus.ProviderUnavailable
            or PackageOperationStatus.Failed;

    private static PackageOperationStatus? MapResolutionStatus(PackageResolutionStatus status) =>
        status switch
        {
            PackageResolutionStatus.NotFound => PackageOperationStatus.NotFound,
            PackageResolutionStatus.Ambiguous => PackageOperationStatus.Ambiguous,
            PackageResolutionStatus.NoApplicableInstaller => PackageOperationStatus.NoApplicableInstaller,
            PackageResolutionStatus.SourceUnavailable => PackageOperationStatus.SourceUnavailable,
            PackageResolutionStatus.AgreementRequired => PackageOperationStatus.AgreementRequired,
            PackageResolutionStatus.BlockedByPolicy => PackageOperationStatus.BlockedByPolicy,
            PackageResolutionStatus.IntegrityFailure => PackageOperationStatus.IntegrityFailure,
            PackageResolutionStatus.Cancelled => PackageOperationStatus.Cancelled,
            PackageResolutionStatus.TimedOut => PackageOperationStatus.TimedOut,
            PackageResolutionStatus.ProviderUnavailable => PackageOperationStatus.ProviderUnavailable,
            PackageResolutionStatus.Failed => PackageOperationStatus.Failed,
            _ => null
        };
}
